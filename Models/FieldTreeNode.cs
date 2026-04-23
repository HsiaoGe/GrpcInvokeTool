using System.Collections.Generic;
using Google.Protobuf.Reflection;

namespace GrpcInvokeTool.Models
{
    public class FieldTreeNode
    {
        public string FieldName { get; set; } = string.Empty;

        public string DisplayType { get; set; } = string.Empty;

        public int FieldNumber { get; set; }

        public string Description { get; set; } = string.Empty;

        public FieldDescriptor? FieldDescriptor { get; set; }

        public List<FieldTreeNode> Children { get; set; } = new();

        public bool HasChildren => Children.Count > 0;

        public bool HasDescription => !string.IsNullOrEmpty(Description);

        public string DisplayText => string.IsNullOrEmpty(DisplayType)
            ? FieldName
            : $"{FieldName} : {DisplayType}";
    }
}
