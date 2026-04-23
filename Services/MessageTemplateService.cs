using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using GrpcInvokeTool.Models;

namespace GrpcInvokeTool.Services
{
    public class MessageTemplateService
    {
        public List<FieldTreeNode> BuildFieldTree(IMessage message)
        {
            return BuildFieldTree(message.Descriptor, new HashSet<string>());
        }

        public List<FieldTreeNode> BuildFieldTree(MessageDescriptor descriptor)
        {
            return BuildFieldTree(descriptor, new HashSet<string>());
        }

        private List<FieldTreeNode> BuildFieldTree(MessageDescriptor descriptor, HashSet<string> visited)
        {
            var nodes = new List<FieldTreeNode>();
            var processedOneofs = new HashSet<OneofDescriptor>();

            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                // For oneof fields, create a group node for the first field in each oneof
                if (field.ContainingOneof != null)
                {
                    if (processedOneofs.Contains(field.ContainingOneof))
                        continue; // Skip subsequent oneof fields, already handled

                    processedOneofs.Add(field.ContainingOneof);
                    var oneofNode = CreateOneofGroupNode(field.ContainingOneof, visited);
                    nodes.Add(oneofNode);
                }
                else
                {
                    var node = CreateFieldNode(field, visited);
                    nodes.Add(node);
                }
            }

            return nodes;
        }

        private FieldTreeNode CreateOneofGroupNode(OneofDescriptor oneof, HashSet<string> visited)
        {
            var node = new FieldTreeNode
            {
                FieldName = oneof.Name,
                DisplayType = $"oneof",
                FieldNumber = 0,
                Description = $"oneof - only one of:",
            };

            foreach (var field in oneof.Fields)
            {
                var childNode = CreateFieldNode(field, visited);
                childNode.Description = $"(oneof option)";
                node.Children.Add(childNode);
            }

            return node;
        }

        private FieldTreeNode CreateFieldNode(FieldDescriptor field, HashSet<string> visited)
        {
            var node = new FieldTreeNode
            {
                FieldName = field.Name,
                FieldNumber = field.FieldNumber,
                FieldDescriptor = field,
                DisplayType = GetFieldTypeDisplay(field),
            };

            // Add enum values hint
            if (field.FieldType == FieldType.Enum)
            {
                var enumValues = string.Join(", ", field.EnumType.Values.Select(v => v.Name));
                node.Description = $"enum values: [{enumValues}]";
            }

            // Add children for nested messages
            if (field.IsMap)
            {
                var mapFields = field.MessageType.Fields.InFieldNumberOrder();
                var keyNode = new FieldTreeNode
                {
                    FieldName = "key",
                    DisplayType = GetProtoTypeDisplay(mapFields[0]),
                    FieldNumber = 1,
                };
                var valueNode = new FieldTreeNode
                {
                    FieldName = "value",
                    DisplayType = GetProtoTypeDisplay(mapFields[1]),
                    FieldNumber = 2,
                };

                // If value is a message type, add its children (with cycle detection)
                if (mapFields[1].FieldType == FieldType.Message)
                {
                    var valueTypeName = mapFields[1].MessageType.FullName;
                    if (!visited.Contains(valueTypeName))
                    {
                        visited.Add(valueTypeName);
                        foreach (var child in BuildFieldTree(mapFields[1].MessageType, visited))
                        {
                            valueNode.Children.Add(child);
                        }
                        visited.Remove(valueTypeName);
                    }
                }

                node.Children.Add(keyNode);
                node.Children.Add(valueNode);
            }
            else if (field.FieldType == FieldType.Message && !field.IsMap)
            {
                var childVisited = new HashSet<string>(visited);
                var typeName = field.MessageType.FullName;

                if (!visited.Contains(typeName))
                {
                    childVisited.Add(typeName);
                    foreach (var childField in field.MessageType.Fields.InFieldNumberOrder())
                    {
                        node.Children.Add(CreateFieldNode(childField, childVisited));
                    }
                }
            }

            return node;
        }

        private string GetFieldTypeDisplay(FieldDescriptor field)
        {
            var prefix = field.IsRepeated ? "repeated " : "";
            if (field.IsMap)
            {
                var mapFields = field.MessageType.Fields.InFieldNumberOrder();
                return $"map<{GetProtoTypeDisplay(mapFields[0])}, {GetProtoTypeDisplay(mapFields[1])}>";
            }
            return $"{prefix}{GetProtoTypeDisplay(field)}";
        }

        private string GetProtoTypeDisplay(FieldDescriptor field)
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
                FieldType.Message => field.MessageType.Name,
                FieldType.Bytes => "bytes",
                FieldType.UInt32 => "uint32",
                FieldType.SFixed32 => "sfixed32",
                FieldType.SFixed64 => "sfixed64",
                FieldType.SInt32 => "sint32",
                FieldType.SInt64 => "sint64",
                FieldType.Enum => field.EnumType.Name,
                _ => field.FieldType.ToString()
            };
        }

        public string GenerateJsonTemplate(IMessage message)
        {
            return GenerateJsonTemplate(message.Descriptor);
        }

        public string GenerateJsonTemplate(MessageDescriptor descriptor)
        {
            var dict = BuildTemplateDict(descriptor, new HashSet<string>());
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(dict, options);
        }

        private object BuildTemplateDict(MessageDescriptor descriptor, HashSet<string> visited)
        {
            var dict = new Dictionary<string, object>();
            var processedOneofs = new HashSet<OneofDescriptor>();

            foreach (var field in descriptor.Fields.InFieldNumberOrder())
            {
                // For oneof: only fill the first field
                if (field.ContainingOneof != null)
                {
                    if (processedOneofs.Contains(field.ContainingOneof))
                        continue; // Skip other oneof fields

                    processedOneofs.Add(field.ContainingOneof);
                    // Only include the first field of the oneof with default value
                    AddFieldToDict(dict, field, visited);
                }
                else
                {
                    AddFieldToDict(dict, field, visited);
                }
            }

            return dict;
        }

        private void AddFieldToDict(Dictionary<string, object> dict, FieldDescriptor field, HashSet<string> visited)
        {
            if (field.IsMap)
            {
                dict[field.Name] = new Dictionary<string, object>();
            }
            else if (field.IsRepeated)
            {
                var itemTemplate = field.FieldType == FieldType.Message
                    ? BuildTemplateDictForItem(field.MessageType, visited)
                    : GetDefaultValue(field);
                dict[field.Name] = new List<object> { itemTemplate };
            }
            else if (field.FieldType == FieldType.Message)
            {
                var typeName = field.MessageType.FullName;
                if (visited.Contains(typeName))
                {
                    dict[field.Name] = new Dictionary<string, object> { { "// circular_ref", typeName } };
                }
                else
                {
                    dict[field.Name] = BuildTemplateDict(field.MessageType, visited);
                }
            }
            else
            {
                dict[field.Name] = GetDefaultValue(field);
            }
        }

        private object BuildTemplateDictForItem(MessageDescriptor messageType, HashSet<string> visited)
        {
            var typeName = messageType.FullName;
            if (visited.Contains(typeName))
            {
                return new Dictionary<string, object> { { "// circular_ref", typeName } };
            }

            var childVisited = new HashSet<string>(visited) { typeName };
            return BuildTemplateDict(messageType, childVisited);
        }

        private object GetDefaultValue(FieldDescriptor field)
        {
            // For enum types, use the first enum value name as string
            if (field.FieldType == FieldType.Enum)
            {
                var enumType = field.EnumType;
                if (enumType.Values.Count > 0)
                {
                    // Return first defined enum value name
                    return enumType.Values[0].Name;
                }
                return enumType.Name; // fallback
            }

            return field.FieldType switch
            {
                FieldType.Double => 0.0,
                FieldType.Float => 0.0f,
                FieldType.Int64 => 0L,
                FieldType.UInt64 => 0UL,
                FieldType.Int32 => 0,
                FieldType.Fixed64 => 0UL,
                FieldType.Fixed32 => 0U,
                FieldType.Bool => false,
                FieldType.String => "",
                FieldType.Bytes => "",
                FieldType.UInt32 => 0U,
                FieldType.SFixed32 => 0,
                FieldType.SFixed64 => 0L,
                FieldType.SInt32 => 0,
                FieldType.SInt64 => 0L,
                _ => ""
            };
        }

        public (bool Success, string Error) ValidateJson(string json)
        {
            try
            {
                JsonDocument.Parse(json);
                return (true, string.Empty);
            }
            catch (JsonException ex)
            {
                return (false, $"JSON error: {ex.Message}");
            }
        }

        public string FormatJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var options = new JsonSerializerOptions { WriteIndented = true };
                return JsonSerializer.Serialize(doc.RootElement, options);
            }
            catch
            {
                return json;
            }
        }

        public IMessage? ParseMessage(string json, Type messageType)
        {
            try
            {
                // Google.Protobuf's JsonParser natively supports enum names as strings
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
    }
}
