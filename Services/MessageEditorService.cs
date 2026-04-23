using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpcInvokeTool.Models;
using System.Text.Json;

namespace GrpcInvokeTool.Services
{
    public class MessageEditorService
    {
        /// <summary>
        /// Build an editable node tree from a message descriptor.
        /// Initially all fields are inactive (not added). User adds fields via "+" button.
        /// </summary>
        public MessageFieldNode BuildEditorTree(MessageDescriptor descriptor)
        {
            var root = new MessageFieldNode
            {
                FieldName = descriptor.Name,
                Kind = FieldNodeKind.Root,
                MessageDescriptor = descriptor,
                DisplayType = descriptor.Name,
                AllAvailableFields = descriptor.Fields.InFieldNumberOrder().ToList(),
            };

            return root;
        }

        /// <summary>
        /// Build a full editor tree with all fields pre-populated (for template / history loading).
        /// </summary>
        public MessageFieldNode BuildFullEditorTree(MessageDescriptor descriptor)
        {
            var root = BuildEditorTree(descriptor);
            var processedOneofs = new HashSet<OneofDescriptor>();

            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                // Skip fields that are part of a synthetic oneof (optional fields with _ prefix)
                // These will be handled as regular optional fields
                if (field.ContainingOneof != null)
                {
                    // Check if this is a synthetic oneof (proto3 optional fields)
                    if (IsSyntheticOneof(field.ContainingOneof))
                    {
                        // Treat as optional field - add directly to parent
                        var node = CreateFieldNode(field);
                        node.IsOptional = true;
                        node.IsSyntheticOneof = true;
                        SetDefaultValue(node);
                        root.Children.Add(node);
                        node.Parent = root;
                        continue;
                    }

                    if (processedOneofs.Contains(field.ContainingOneof))
                        continue;
                    processedOneofs.Add(field.ContainingOneof);

                    var oneofNode = CreateOneofGroupNode(field.ContainingOneof);
                    // Auto-select first option
                    if (oneofNode.OneofOptions.Count > 0)
                    {
                        oneofNode.SelectedOneofField = oneofNode.OneofOptions[0];
                        AddChildWithDefaults(oneofNode, oneofNode.OneofOptions[0]);
                    }
                    root.Children.Add(oneofNode);
                    oneofNode.Parent = root;
                }
                else
                {
                    var node = CreateFieldNode(field);
                    SetDefaultValue(node);
                    root.Children.Add(node);
                    node.Parent = root;

                    // Auto-populate sub-fields for non-optional message fields
                    PopulateMessageChildren(node);
                }
            }

            return root;
        }

        /// <summary>
        /// Build editor tree from existing JSON (history restore).
        /// </summary>
        public MessageFieldNode BuildEditorTreeFromJson(MessageDescriptor descriptor, string json)
        {
            var root = BuildFullEditorTree(descriptor);

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    ApplyJsonToNode(root, doc.RootElement);
                }
            }
            catch
            {
                // If parsing fails, return the default full tree
            }

            return root;
        }

        private void ApplyJsonToNode(MessageFieldNode node, JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in element.EnumerateObject())
            {
                // Find child by field name
                var child = node.Children.FirstOrDefault(c => c.FieldName == prop.Name);
                if (child == null) continue;

                if (child.IsOneofGroup)
                {
                    // Find which oneof option is set
                    foreach (var option in child.OneofOptions)
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object && option.IsMessage)
                        {
                            // Check if this option's fields exist in the json
                            var hasField = false;
                            foreach (var subProp in prop.Value.EnumerateObject())
                            {
                                if (option.AllAvailableFields.Any(f => f.Name == subProp.Name))
                                {
                                    hasField = true;
                                    break;
                                }
                            }
                            if (hasField || prop.Value.ValueKind != JsonValueKind.Object)
                            {
                                child.SelectedOneofField = option;
                                // Clear other oneof children and add this one
                                child.Children.Clear();
                                AddChildWithDefaults(child, option);
                                if (prop.Value.ValueKind == JsonValueKind.Object)
                                {
                                    ApplyJsonToNode(option, prop.Value);
                                }
                                break;
                            }
                        }
                        else if (prop.Value.ValueKind != JsonValueKind.Object)
                        {
                            // Scalar oneof option
                            child.SelectedOneofField = option;
                            child.Children.Clear();
                            AddChildWithDefaults(child, option);
                            ApplyScalarValue(option, prop.Value);
                            break;
                        }
                    }
                }
                else if (child.IsMap)
                {
                    child.Children.Clear();
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        var mapFields = child.FieldDescriptor!.MessageType.Fields.InFieldNumberOrder();
                        var valueField = mapFields[1];
                        var keyField = mapFields[0];
                        int idx = 0;
                        foreach (var entry in prop.Value.EnumerateObject())
                        {
                            var mapEntry = CreateMapEntryNode(child.FieldDescriptor, entry.Name, idx++);
                            if (entry.Value.ValueKind == JsonValueKind.Object && valueField.FieldType == FieldType.Message)
                            {
                                ApplyJsonValueToNode(mapEntry.Children.FirstOrDefault(c => c.FieldName == "value"), entry.Value);
                            }
                            else
                            {
                                var valueNode = mapEntry.Children.FirstOrDefault(c => c.FieldName == "value");
                                if (valueNode != null) valueNode.StringValue = entry.Value.ToString();
                            }
                            child.Children.Add(mapEntry);
                            mapEntry.Parent = child;
                        }
                    }
                }
                else if (child.IsRepeated && !child.IsMap)
                {
                    child.Children.Clear();
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            var repeatedItem = CreateRepeatedItemNode(child.FieldDescriptor!, idx++);
                            if (child.IsMessage && item.ValueKind == JsonValueKind.Object)
                            {
                                ApplyJsonToNode(repeatedItem, item);
                            }
                            else if (child.IsEnum)
                            {
                                repeatedItem.StringValue = item.ToString();
                            }
                            else
                            {
                                repeatedItem.StringValue = item.ToString();
                            }
                            child.Children.Add(repeatedItem);
                            repeatedItem.Parent = child;
                        }
                    }
                }
                else if (child.IsMessage)
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        ApplyJsonToNode(child, prop.Value);
                    }
                }
                else if (child.IsEnum)
                {
                    child.StringValue = prop.Value.ToString();
                }
                else
                {
                    ApplyScalarValue(child, prop.Value);
                }
            }
        }

        private void ApplyJsonValueToNode(MessageFieldNode? node, JsonElement element)
        {
            if (node == null) return;
            if (node.IsMessage && element.ValueKind == JsonValueKind.Object)
            {
                ApplyJsonToNode(node, element);
            }
            else
            {
                node.StringValue = element.ToString();
            }
        }

        private void ApplyScalarValue(MessageFieldNode node, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.True:
                case JsonValueKind.False:
                    node.BoolValue = element.GetBoolean();
                    node.StringValue = element.GetBoolean().ToString().ToLower();
                    break;
                case JsonValueKind.Number:
                    node.StringValue = element.GetRawText();
                    break;
                case JsonValueKind.String:
                    node.StringValue = element.GetString() ?? "";
                    break;
                default:
                    node.StringValue = element.ToString();
                    break;
            }
        }

        /// <summary>
        /// Serialize the editor tree to a JSON string that can be parsed by JsonParser.
        /// </summary>
        public string SerializeToJson(MessageFieldNode root)
        {
            var dict = NodeToDict(root);
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(dict, options);
        }

        private object? NodeToDict(MessageFieldNode node)
        {
            if (node.Kind == FieldNodeKind.Root || (node.IsMessage && !node.IsRepeated && !node.IsMap))
            {
                var dict = new Dictionary<string, object>();
                foreach (var child in node.Children)
                {
                    if (!child.IsActive) continue;

                    if (child.IsOneofGroup)
                    {
                        var selectedOption = child.SelectedOneofField;
                        if (selectedOption != null)
                        {
                            // Find the active oneof child in Children
                            var activeOneofChild = child.Children.FirstOrDefault(c => c.FieldName == selectedOption.FieldName);
                            if (activeOneofChild != null)
                            {
                                if (activeOneofChild.IsMessage)
                                {
                                    dict[activeOneofChild.FieldName] = NodeToDict(activeOneofChild) ?? new Dictionary<string, object>();
                                }
                                else if (activeOneofChild.IsEnum)
                                {
                                    dict[activeOneofChild.FieldName] = activeOneofChild.StringValue;
                                }
                                else
                                {
                                    dict[activeOneofChild.FieldName] = ConvertScalarValue(activeOneofChild);
                                }
                            }
                        }
                    }
                    else if (child.IsMap)
                    {
                        var mapDict = new Dictionary<string, object>();
                        foreach (var entry in child.Children.OfType<MessageFieldNode>())
                        {
                            var keyNode = entry.Children.FirstOrDefault(c => c.FieldName == "key");
                            var valueNode = entry.Children.FirstOrDefault(c => c.FieldName == "value");
                            if (keyNode != null && valueNode != null)
                            {
                                var key = keyNode.StringValue;
                                if (valueNode.IsMessage)
                                {
                                    mapDict[key] = NodeToDict(valueNode) ?? new Dictionary<string, object>();
                                }
                                else if (valueNode.IsEnum)
                                {
                                    mapDict[key] = valueNode.StringValue;
                                }
                                else
                                {
                                    mapDict[key] = ConvertScalarValue(valueNode);
                                }
                            }
                        }
                        dict[child.FieldName] = mapDict;
                    }
                    else if (child.IsRepeated && !child.IsMap)
                    {
                        var list = new List<object>();
                        foreach (var item in child.Children)
                        {
                            if (child.IsMessage)
                            {
                                list.Add(NodeToDict(item) ?? new Dictionary<string, object>());
                            }
                            else if (child.IsEnum)
                            {
                                list.Add(item.StringValue);
                            }
                            else
                            {
                                list.Add(ConvertScalarValue(item));
                            }
                        }
                        dict[child.FieldName] = list;
                    }
                    else if (child.IsMessage)
                    {
                        dict[child.FieldName] = NodeToDict(child) ?? new Dictionary<string, object>();
                    }
                    else if (child.IsEnum)
                    {
                        dict[child.FieldName] = child.StringValue;
                    }
                    else
                    {
                        dict[child.FieldName] = ConvertScalarValue(child);
                    }
                }
                return dict;
            }
            return null;
        }

        private object ConvertScalarValue(MessageFieldNode node)
        {
            if (node.ProtoFieldType == FieldType.Bool)
            {
                if (bool.TryParse(node.StringValue, out var b)) return b;
                return node.BoolValue;
            }
            if (node.ProtoFieldType == FieldType.Double || node.ProtoFieldType == FieldType.Float)
            {
                if (double.TryParse(node.StringValue, out var d)) return d;
                return 0.0;
            }
            if (node.ProtoFieldType is FieldType.Int64 or FieldType.SFixed64 or FieldType.SInt64)
            {
                if (long.TryParse(node.StringValue, out var l)) return l;
                return 0L;
            }
            if (node.ProtoFieldType is FieldType.UInt64 or FieldType.Fixed64)
            {
                if (ulong.TryParse(node.StringValue, out var ul)) return ul;
                return 0UL;
            }
            if (node.ProtoFieldType is FieldType.Int32 or FieldType.SFixed32 or FieldType.SInt32)
            {
                if (int.TryParse(node.StringValue, out var i)) return i;
                return 0;
            }
            if (node.ProtoFieldType is FieldType.UInt32 or FieldType.Fixed32)
            {
                if (uint.TryParse(node.StringValue, out var u)) return u;
                return 0U;
            }
            // Default: return string
            return node.StringValue;
        }

        #region Node Creation

        public MessageFieldNode CreateFieldNode(FieldDescriptor field)
        {
            // Detect proto3 optional fields (synthetic oneof with _ prefix)
            var isOptional = field.ContainingOneof != null && IsSyntheticOneof(field.ContainingOneof);

            var node = new MessageFieldNode
            {
                FieldName = field.Name,
                FieldNumber = field.FieldNumber,
                Kind = FieldNodeKind.Field,
                FieldDescriptor = field,
                DisplayType = GetFieldTypeDisplay(field),
                ProtoFieldType = field.IsMap ? FieldType.Message : field.FieldType,
                IsRepeated = field.IsRepeated && !field.IsMap,
                IsMap = field.IsMap,
                IsActive = true,
                IsOptional = isOptional,
                IsSyntheticOneof = isOptional,
            };

            // Enum values
            if (field.FieldType == FieldType.Enum)
            {
                node.EnumValues = field.EnumType.Values.Select(v => v.Name).ToList();
                node.StringValue = node.EnumValues.FirstOrDefault() ?? "";
            }

            // Map: store sub-descriptors for key/value
            if (field.IsMap)
            {
                node.AllAvailableFields = new List<FieldDescriptor>();
                var mapFields = field.MessageType.Fields.InFieldNumberOrder();
                var keyField = mapFields[0];
                var valueField = mapFields[1];
                // Map entries are created dynamically via CreateMapEntryNode
            }

            // Message children: populate available fields (but don't auto-add children)
            if (field.FieldType == FieldType.Message && !field.IsMap)
            {
                node.MessageDescriptor = field.MessageType;
                node.AllAvailableFields = field.MessageType.Fields.InFieldNumberOrder().ToList();
            }

            return node;
        }

        public MessageFieldNode CreateOneofGroupNode(OneofDescriptor oneof)
        {
            var node = new MessageFieldNode
            {
                FieldName = oneof.Name,
                Kind = FieldNodeKind.OneofGroup,
                DisplayType = "oneof",
                OneofDescriptor = oneof,
                IsOneofGroup = true,
                IsActive = true,
            };

            foreach (var field in oneof.Fields)
            {
                var option = CreateFieldNode(field);
                option.Kind = FieldNodeKind.OneofOption;
                option.IsActive = false;
                node.OneofOptions.Add(option);
                option.Parent = node;
            }

            return node;
        }

        public MessageFieldNode CreateRepeatedItemNode(FieldDescriptor field, int index)
        {
            var node = new MessageFieldNode
            {
                FieldName = $"[{index}]",
                Kind = FieldNodeKind.RepeatedItem,
                FieldDescriptor = field,
                DisplayType = field.FieldType == FieldType.Message ? field.MessageType.Name : GetScalarTypeDisplay(field),
                ProtoFieldType = field.FieldType,
                IsRepeated = false,
                IsMap = false,
                IsActive = true,
            };

            if (field.FieldType == FieldType.Message)
            {
                node.MessageDescriptor = field.MessageType;
                node.AllAvailableFields = field.MessageType.Fields.InFieldNumberOrder().ToList();
            }

            if (field.FieldType == FieldType.Enum)
            {
                node.EnumValues = field.EnumType.Values.Select(v => v.Name).ToList();
                node.StringValue = node.EnumValues.FirstOrDefault() ?? "";
            }

            return node;
        }

        public MessageFieldNode CreateMapEntryNode(FieldDescriptor mapField, string key, int index)
        {
            var mapFields = mapField.MessageType.Fields.InFieldNumberOrder();
            var keyField = mapFields[0];
            var valueField = mapFields[1];

            var entry = new MessageFieldNode
            {
                FieldName = string.IsNullOrEmpty(key) ? $"[{index}]" : $"\"{key}\"",
                Kind = FieldNodeKind.MapEntry,
                FieldDescriptor = mapField,
                DisplayType = "",  // Don't show "entry" type for map entries
                IsActive = true,
            };

            var keyNode = new MessageFieldNode
            {
                FieldName = "key",
                DisplayType = GetScalarTypeDisplay(keyField),
                ProtoFieldType = keyField.FieldType,
                Kind = FieldNodeKind.Field,
                StringValue = key,
                IsActive = true,
            };

            // Check if value has oneof fields
            if (valueField.FieldType == FieldType.Message && valueField.MessageType != null)
            {
                var hasRealOneof = false;
                var processedOneofs = new HashSet<OneofDescriptor>();
                foreach (var f in valueField.MessageType.Fields.InFieldNumberOrder())
                {
                    if (f.ContainingOneof != null && !IsSyntheticOneof(f.ContainingOneof))
                    {
                        hasRealOneof = true;
                        break;
                    }
                }

                if (hasRealOneof)
                {
                    // Value is a message with oneof - create a oneof group inside value
                    var valueNode = new MessageFieldNode
                    {
                        FieldName = "value",
                        DisplayType = valueField.MessageType.Name,
                        ProtoFieldType = FieldType.Message,
                        Kind = FieldNodeKind.Field,
                        IsActive = true,
                        IsOneofGroup = false,
                    };
                    valueNode.MessageDescriptor = valueField.MessageType;
                    valueNode.AllAvailableFields = valueField.MessageType.Fields.InFieldNumberOrder().ToList();

                    // Add oneof groups and regular fields for value
                    processedOneofs.Clear();
                    foreach (var f in valueField.MessageType.Fields.InFieldNumberOrder())
                    {
                        if (f.ContainingOneof != null)
                        {
                            if (IsSyntheticOneof(f.ContainingOneof))
                            {
                                // Synthetic oneof (proto3 optional): treat as optional field
                                var subField = CreateFieldNode(f);
                                subField.IsOptional = true;
                                subField.IsSyntheticOneof = true;
                                SetDefaultValue(subField);
                                valueNode.Children.Add(subField);
                                subField.Parent = valueNode;
                                continue;
                            }

                            if (processedOneofs.Contains(f.ContainingOneof)) continue;
                            processedOneofs.Add(f.ContainingOneof);

                            var oneofNode = CreateOneofGroupNode(f.ContainingOneof);
                            if (oneofNode.OneofOptions.Count > 0)
                            {
                                oneofNode.SelectedOneofField = oneofNode.OneofOptions[0];
                                AddChildWithDefaults(oneofNode, oneofNode.OneofOptions[0]);
                            }
                            valueNode.Children.Add(oneofNode);
                            oneofNode.Parent = valueNode;
                        }
                        else
                        {
                            var subField = CreateFieldNode(f);
                            SetDefaultValue(subField);
                            valueNode.Children.Add(subField);
                            subField.Parent = valueNode;
                        }
                    }

                    entry.Children.Add(keyNode);
                    keyNode.Parent = entry;
                    entry.Children.Add(valueNode);
                    valueNode.Parent = entry;
                    return entry;
                }
            }

            // Default: value is a simple scalar, enum, or message without oneof
            var defaultvalueNode = new MessageFieldNode
            {
                FieldName = "value",
                DisplayType = valueField.FieldType == FieldType.Message ? valueField.MessageType?.Name ?? "message" : GetScalarTypeDisplay(valueField),
                ProtoFieldType = valueField.FieldType,
                Kind = FieldNodeKind.Field,
                IsActive = true,
            };

            if (valueField.FieldType == FieldType.Message && valueField.MessageType != null)
            {
                defaultvalueNode.MessageDescriptor = valueField.MessageType;
                defaultvalueNode.AllAvailableFields = valueField.MessageType.Fields.InFieldNumberOrder().ToList();
            }

            if (valueField.FieldType == FieldType.Enum)
            {
                defaultvalueNode.EnumValues = valueField.EnumType.Values.Select(v => v.Name).ToList();
                defaultvalueNode.StringValue = defaultvalueNode.EnumValues.FirstOrDefault() ?? "";
            }

            entry.Children.Add(keyNode);
            keyNode.Parent = entry;
            entry.Children.Add(defaultvalueNode);
            defaultvalueNode.Parent = entry;

            return entry;
        }

        public void AddChildWithDefaults(MessageFieldNode parent, MessageFieldNode fieldTemplate)
        {
            var node = new MessageFieldNode
            {
                FieldName = fieldTemplate.FieldName,
                FieldNumber = fieldTemplate.FieldNumber,
                Kind = fieldTemplate.Kind,
                FieldDescriptor = fieldTemplate.FieldDescriptor,
                DisplayType = fieldTemplate.DisplayType,
                ProtoFieldType = fieldTemplate.ProtoFieldType,
                IsRepeated = fieldTemplate.IsRepeated,
                IsMap = fieldTemplate.IsMap,
                IsActive = true,
                MessageDescriptor = fieldTemplate.MessageDescriptor,
                AllAvailableFields = fieldTemplate.AllAvailableFields,
                EnumValues = fieldTemplate.EnumValues,
                OneofDescriptor = fieldTemplate.OneofDescriptor,
                IsOptional = fieldTemplate.IsOptional,
                IsSyntheticOneof = fieldTemplate.IsSyntheticOneof,
            };

            SetDefaultValue(node);
            parent.Children.Add(node);
            node.Parent = parent;

            // Auto-populate all sub-fields for message type nodes (not just oneof children)
            PopulateMessageChildren(node);
        }

        /// <summary>
        /// Populate all sub-fields for a message type node, including handling nested oneofs.
        /// Only non-optional message fields are auto-expanded; optional ones show a + button.
        /// </summary>
        private void PopulateMessageChildren(MessageFieldNode node)
        {
            if (!node.IsMessage || node.MessageDescriptor == null) return;
            if (node.IsRepeated || node.IsMap) return;
            // Skip auto-populate for optional fields - user adds via + button
            if (node.IsOptional) return;

            var processedOneofs = new HashSet<OneofDescriptor>();
            foreach (var field in node.MessageDescriptor.Fields.InFieldNumberOrder())
            {
                if (field.ContainingOneof != null)
                {
                    if (IsSyntheticOneof(field.ContainingOneof))
                    {
                        // Synthetic oneof (proto3 optional): treat as optional field
                        var subField = CreateFieldNode(field);
                        subField.IsOptional = true;
                        subField.IsSyntheticOneof = true;
                        SetDefaultValue(subField);
                        node.Children.Add(subField);
                        subField.Parent = node;
                        continue;
                    }

                    if (processedOneofs.Contains(field.ContainingOneof)) continue;
                    processedOneofs.Add(field.ContainingOneof);

                    var subOneofNode = CreateOneofGroupNode(field.ContainingOneof);
                    if (subOneofNode.OneofOptions.Count > 0)
                    {
                        subOneofNode.SelectedOneofField = subOneofNode.OneofOptions[0];
                        AddChildWithDefaults(subOneofNode, subOneofNode.OneofOptions[0]);
                    }
                    node.Children.Add(subOneofNode);
                    subOneofNode.Parent = node;
                }
                else
                {
                    var subField = CreateFieldNode(field);
                    SetDefaultValue(subField);
                    node.Children.Add(subField);
                    subField.Parent = node;

                    // Recursively auto-populate non-optional message sub-fields
                    PopulateMessageChildren(subField);
                }
            }
        }

        public void SetDefaultValue(MessageFieldNode node)
        {
            if (node.IsEnum && node.EnumValues.Count > 0)
            {
                node.StringValue = node.EnumValues[0];
            }
            else if (node.ProtoFieldType == FieldType.Bool)
            {
                node.BoolValue = false;
                node.StringValue = "false";
            }
            else if (node.ProtoFieldType == FieldType.String)
            {
                node.StringValue = "";
            }
            else if (node.ProtoFieldType == FieldType.Message)
            {
                node.StringValue = "";
            }
            else
            {
                node.StringValue = "0";
            }
        }

        /// <summary>
        /// Check if a oneof is a synthetic oneof (proto3 optional fields).
        /// Synthetic oneofs have fields that start with '_' and contain only one field.
        /// </summary>
        private bool IsSyntheticOneof(OneofDescriptor oneof)
        {
            // Synthetic oneofs: only contain one field, field name starts with '_'
            if (oneof.Fields.Count != 1)
                return false;

            var field = oneof.Fields[0];
            return field.Name.StartsWith("_") || oneof.Name.StartsWith("_");
        }

        #endregion

        #region Display Helpers

        private string GetFieldTypeDisplay(FieldDescriptor field)
        {
            var prefix = field.IsRepeated && !field.IsMap ? "repeated " : "";
            if (field.IsMap)
            {
                var mapFields = field.MessageType.Fields.InFieldNumberOrder();
                return $"map<{GetScalarTypeDisplay(mapFields[0])}, {GetScalarTypeDisplay(mapFields[1])}>";
            }
            return $"{prefix}{GetScalarTypeDisplay(field)}";
        }

        private string GetScalarTypeDisplay(FieldDescriptor field)
        {
            return field.FieldType switch
            {
                FieldType.Double => "double",
                FieldType.Float => "float",
                FieldType.Int64 => "int64",
                FieldType.UInt64 => "uint64",
                FieldType.Int32 => "int32",
                FieldType.Fixed64 => "fixed64",
                FieldType.Fixed32 => "fixed32",
                FieldType.Bool => "bool",
                FieldType.String => "string",
                FieldType.Bytes => "bytes",
                FieldType.UInt32 => "uint32",
                FieldType.SFixed32 => "sfixed32",
                FieldType.SFixed64 => "sfixed64",
                FieldType.SInt32 => "sint32",
                FieldType.SInt64 => "sint64",
                FieldType.Enum => field.EnumType.Name,
                FieldType.Message => field.MessageType.Name,
                _ => field.FieldType.ToString()
            };
        }

        #endregion

        #region ParseMessage (delegate)

        public IMessage? ParseMessage(string json, Type messageType)
        {
            try
            {
                var parser = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));
                var parseMethod = typeof(JsonParser).GetMethods()
                    .First(m => m.Name == "Parse" && m.IsGenericMethod)
                    .MakeGenericMethod(messageType);
                return (IMessage?)parseMethod.Invoke(parser, new object[] { json });
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
