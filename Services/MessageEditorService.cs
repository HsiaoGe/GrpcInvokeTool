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
        /// 从空树开始，根据JSON内容动态添加字段，正确还原用户删除的字段。
        /// </summary>
        public MessageFieldNode BuildEditorTreeFromJson(MessageDescriptor descriptor, string json)
        {
            // 使用空树而不是完整树，这样JSON中没有的字段就不会被添加
            var root = BuildEditorTree(descriptor);

            try
            {
                using var doc = JsonDocument.Parse(json);
                ApplyJsonToNodeFromEmpty(root, doc.RootElement, descriptor);
            }
            catch
            {
                // If parsing fails, return the default full tree
                return BuildFullEditorTree(descriptor);
            }

            return root;
        }

        /// <summary>
        /// 从空树开始应用JSON，动态添加字段
        /// </summary>
        private void ApplyJsonToNodeFromEmpty(MessageFieldNode node, JsonElement element, MessageDescriptor? descriptor = null)
        {
            var targetDescriptor = descriptor ?? node.MessageDescriptor;
            if (targetDescriptor == null) return;

            element = ExpandWellKnownJson(element, targetDescriptor);
            if (element.ValueKind != JsonValueKind.Object) return;

            var processedOneofs = new HashSet<OneofDescriptor>();

            foreach (var prop in element.EnumerateObject())
            {
                // 查找字段描述符
                var field = targetDescriptor.Fields.InFieldNumberOrder()
                    .FirstOrDefault(f => f.Name == prop.Name);

                if (field == null) continue;

                // 处理synthetic oneof (proto3 optional)
                if (field.ContainingOneof != null && IsSyntheticOneof(field.ContainingOneof))
                {
                    var fieldNode = CreateFieldNode(field);
                    fieldNode.IsOptional = true;
                    fieldNode.IsSyntheticOneof = true;
                    fieldNode.IsActive = true;
                    ApplyScalarOrMessageValue(fieldNode, prop.Value);
                    node.Children.Add(fieldNode);
                    fieldNode.Parent = node;
                    continue;
                }

                // 处理真正的oneof
                if (field.ContainingOneof != null && !IsSyntheticOneof(field.ContainingOneof))
                {
                    if (processedOneofs.Contains(field.ContainingOneof)) continue;
                    processedOneofs.Add(field.ContainingOneof);

                    // 检查是否已经存在同名的oneof组
                    var existingOneof = node.Children.FirstOrDefault(c => 
                        c.IsOneofGroup && c.OneofDescriptor == field.ContainingOneof);
                    
                    if (existingOneof != null)
                    {
                        // 已存在，更新选中的选项
                        var selectedOption_oneof = existingOneof.OneofOptions.FirstOrDefault(o => o.FieldName == prop.Name);
                        if (selectedOption_oneof != null)
                        {
                            existingOneof.SelectedOneofField = selectedOption_oneof;
                            existingOneof.Children.Clear();
                            // 不调用AddChildWithDefaults，直接创建空节点并应用JSON
                            var optionNode = CreateFieldNode(selectedOption_oneof.FieldDescriptor!);
                            optionNode.IsActive = true;
                            existingOneof.Children.Add(optionNode);
                            optionNode.Parent = existingOneof;
                            ApplyScalarOrMessageValue(optionNode, prop.Value);
                        }
                        continue;
                    }

                    // 创建oneof组节点
                    var oneofNode = CreateOneofGroupNode(field.ContainingOneof);
                    
                    // 找到JSON中实际存在的oneof选项
                    var selectedOption = oneofNode.OneofOptions.FirstOrDefault(o => o.FieldName == prop.Name);
                    if (selectedOption != null)
                    {
                        oneofNode.SelectedOneofField = selectedOption;
                        oneofNode.Children.Clear();
                        // 不调用AddChildWithDefaults，直接创建空节点并应用JSON
                        var optionNode = CreateFieldNode(selectedOption.FieldDescriptor!);
                        optionNode.IsActive = true;
                        oneofNode.Children.Add(optionNode);
                        optionNode.Parent = oneofNode;
                        ApplyScalarOrMessageValue(optionNode, prop.Value);
                    }
                    
                    node.Children.Add(oneofNode);
                    oneofNode.Parent = node;
                    continue;
                }

                // 处理普通字段
                // 检查是否已经存在同名的字段
                var existingField = node.Children.FirstOrDefault(c => c.FieldName == prop.Name);
                if (existingField != null)
                {
                    // 已存在，更新值
                    ApplyScalarOrMessageValue(existingField, prop.Value);
                    continue;
                }

                var childNode = CreateFieldNode(field);
                childNode.IsActive = true;
                
                // 处理map
                if (field.IsMap)
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        int idx = 0;
                        foreach (var entry in prop.Value.EnumerateObject())
                        {
                            var mapEntry = CreateMapEntryNode(field, entry.Name, idx++);
                            var valueNode = mapEntry.Children.FirstOrDefault(c => c.FieldName == "value");
                            if (valueNode != null)
                            {
                                ApplyScalarOrMessageValue(valueNode, entry.Value);
                            }
                            childNode.Children.Add(mapEntry);
                            mapEntry.Parent = childNode;
                        }
                    }
                }
                // 处理repeated
                else if (field.IsRepeated)
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        int idx = 0;
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            var repeatedItem = CreateRepeatedItemNode(field, idx++);
                            ApplyScalarOrMessageValue(repeatedItem, item);
                            childNode.Children.Add(repeatedItem);
                            repeatedItem.Parent = childNode;
                        }
                    }
                }
                // 处理message
                else if (field.FieldType == FieldType.Message)
                {
                    ApplyScalarOrMessageValue(childNode, prop.Value);
                }
                // 处理enum和scalar
                else
                {
                    ApplyScalarOrMessageValue(childNode, prop.Value);
                }

                node.Children.Add(childNode);
                childNode.Parent = node;
            }
        }

        /// <summary>
        /// 应用scalar或message值到节点
        /// </summary>
        private void ApplyScalarOrMessageValue(MessageFieldNode node, JsonElement element)
        {
            if (node.IsMessage)
            {
                ApplyJsonToNodeFromEmpty(node, element, node.FieldDescriptor?.MessageType);
            }
            else if (node.IsEnum)
            {
                node.StringValue = element.ToString();
            }
            else
            {
                ApplyScalarValue(node, element);
            }
        }

        private static JsonElement ExpandWellKnownJson(JsonElement element, MessageDescriptor descriptor)
        {
            // The editor uses descriptor fields; ProtoJSON uses plain objects, arrays and values.
            string fieldName;
            switch (descriptor.FullName)
            {
                case "google.protobuf.Struct":
                    fieldName = "fields";
                    break;
                case "google.protobuf.ListValue":
                    fieldName = "values";
                    break;
                case "google.protobuf.Value":
                    fieldName = element.ValueKind switch
                    {
                        JsonValueKind.Null => "null_value",
                        JsonValueKind.Number => "number_value",
                        JsonValueKind.String => "string_value",
                        JsonValueKind.True or JsonValueKind.False => "bool_value",
                        JsonValueKind.Object => "struct_value",
                        JsonValueKind.Array => "list_value",
                        _ => throw new InvalidOperationException("Unsupported JSON value.")
                    };
                    if (element.ValueKind == JsonValueKind.Null)
                        return JsonSerializer.SerializeToElement(new Dictionary<string, object>
                        {
                            [fieldName] = "NULL_VALUE"
                        });
                    break;
                default:
                    return element;
            }

            return JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>
            {
                [fieldName] = element
            });
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
                switch (node.MessageDescriptor?.FullName)
                {
                    case "google.protobuf.Struct":
                        var fields = node.Children.FirstOrDefault(c => c.IsActive && c.FieldName == "fields");
                        return fields == null ? new Dictionary<string, object?>() : MapToDict(fields);
                    case "google.protobuf.ListValue":
                        var values = node.Children.FirstOrDefault(c => c.IsActive && c.FieldName == "values");
                        return values == null ? new List<object?>() : RepeatedToList(values);
                    case "google.protobuf.Value":
                        var kind = node.Children.FirstOrDefault(c => c.IsActive && c.IsOneofGroup);
                        var value = kind?.Children.FirstOrDefault(c =>
                            c.IsActive && c.FieldName == kind.SelectedOneofField?.FieldName);
                        if (value == null || value.FieldName == "null_value") return null;
                        return value.IsMessage ? NodeToDict(value) : ConvertScalarValue(value);
                }

                var dict = new Dictionary<string, object?>();
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
                            if (activeOneofChild != null && activeOneofChild.IsActive)
                            {
                                if (activeOneofChild.IsMessage)
                                {
                                    dict[activeOneofChild.FieldName] = NodeToDict(activeOneofChild);
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
                        dict[child.FieldName] = MapToDict(child);
                    }
                    else if (child.IsRepeated && !child.IsMap)
                    {
                        dict[child.FieldName] = RepeatedToList(child);
                    }
                    else if (child.IsMessage)
                    {
                        dict[child.FieldName] = NodeToDict(child);
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

        private Dictionary<string, object?> MapToDict(MessageFieldNode node)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var entry in node.Children.Where(c => c.IsActive))
            {
                var key = entry.Children.FirstOrDefault(c => c.IsActive && c.FieldName == "key");
                var value = entry.Children.FirstOrDefault(c => c.IsActive && c.FieldName == "value");
                if (key == null || value == null) continue;
                dict[key.StringValue] = value.IsMessage ? NodeToDict(value)
                    : value.IsEnum ? value.StringValue : ConvertScalarValue(value);
            }
            return dict;
        }

        private List<object?> RepeatedToList(MessageFieldNode node)
        {
            return node.Children.Where(c => c.IsActive)
                .Select(item => item.IsMessage ? NodeToDict(item)
                    : item.IsEnum ? item.StringValue : ConvertScalarValue(item))
                .ToList();
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
