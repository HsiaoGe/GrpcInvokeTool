using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Google.Protobuf.Reflection;

namespace GrpcInvokeTool.Models
{
    public enum FieldNodeKind
    {
        Root,           // Root message node
        Field,          // Normal field
        RepeatedItem,   // An item inside a repeated field
        MapEntry,       // A key-value entry inside a map
        OneofGroup,     // Oneof container
        OneofOption,    // A field inside oneof
    }

    public partial class MessageFieldNode : ObservableObject
    {
        // Identity
        public string FieldName { get; set; } = string.Empty;
        public int FieldNumber { get; set; }
        public FieldNodeKind Kind { get; set; } = FieldNodeKind.Field;
        public FieldDescriptor? FieldDescriptor { get; set; }
        public MessageDescriptor? MessageDescriptor { get; set; }

        // Type info
        public string DisplayType { get; set; } = string.Empty;
        public FieldType ProtoFieldType { get; set; }
        public bool IsRepeated { get; set; }
        public bool IsMap { get; set; }
        public bool IsEnum => ProtoFieldType == FieldType.Enum;
        public bool IsMessage => ProtoFieldType == FieldType.Message;
        public bool IsOneofGroup { get; set; }
        public bool IsOptional { get; set; }
        public bool IsSyntheticOneof { get; set; }

        // Value editing
        [ObservableProperty]
        private string _stringValue = string.Empty;

        partial void OnStringValueChanged(string value)
        {
            if (ProtoFieldType == FieldType.Bool)
            {
                var b = value?.ToLower() == "true";
                if (_boolValue != b) _boolValue = b;
            }
        }

        [ObservableProperty]
        private bool _boolValue;

        partial void OnBoolValueChanged(bool value)
        {
            StringValue = value ? "true" : "false";
        }

        [ObservableProperty]
        private MessageFieldNode? _selectedOneofField;

        // Whether this node is "active" (visible/present in the message)
        [ObservableProperty]
        private bool _isActive = true;

        // Children
        public ObservableCollection<MessageFieldNode> Children { get; set; } = new();

        // Parent reference for delete operations
        public MessageFieldNode? Parent { get; set; }

        // Available fields to add (for root / message / oneof nodes)
        public List<FieldDescriptor> AllAvailableFields { get; set; } = new();

        // Enum values (for enum fields)
        public List<string> EnumValues { get; set; } = new();

        // Oneof fields (for oneof group)
        public List<MessageFieldNode> OneofOptions { get; set; } = new();

        // Oneof descriptor
        public OneofDescriptor? OneofDescriptor { get; set; }

        // Computed display
        public bool HasChildren => Children.Count > 0;
        // Only Root node gets the + button in front of the name
        // Message fields get the + button after the colon (ShowInlineAddButton)
        public bool CanAddFields => Kind == FieldNodeKind.Root
            && !IsRepeated && !IsMap;
        public bool CanAddRepeatedItem => (IsRepeated || IsMap);

        // For repeated (non-map) fields: show + button after colon (add repeated item)
        public bool ShowInlineAddButtonForRepeated => IsRepeated && !IsMap && !IsOneofGroup
            && Kind != FieldNodeKind.Root;

        // For scalar oneof options: show editable value when selected
        public bool IsEditableValue => !IsMessage && !IsRepeated && !IsMap && !IsOneofGroup && !IsEnum
            && ProtoFieldType != FieldType.Bool
            && Kind != FieldNodeKind.Root
            && Kind != FieldNodeKind.MapEntry;

        public bool IsBoolValue => ProtoFieldType == FieldType.Bool && !IsMessage && !IsRepeated && !IsMap && !IsOneofGroup
            && Kind != FieldNodeKind.Root
            && Kind != FieldNodeKind.MapEntry;

        public bool ShowOneofSelector => IsOneofGroup;
        public bool IsDeletable => Kind is FieldNodeKind.Field or FieldNodeKind.RepeatedItem
            or FieldNodeKind.MapEntry or FieldNodeKind.OneofGroup;

        // For message type fields: show + button after colon (add child field)
        public bool ShowInlineAddButtonForMessage => IsMessage && !IsRepeated && !IsMap && !IsOneofGroup
            && Kind != FieldNodeKind.Root;

        // For map type fields: show + button after colon (add map entry)
        public bool ShowInlineAddButtonForMap => IsMap && !IsOneofGroup
            && Kind != FieldNodeKind.Root;

        // For Root node: show + button after colon (add field to message)
        public bool ShowInlineAddButtonForRoot => Kind == FieldNodeKind.Root;

        // Map entries' key/value children should not show editable input boxes
        public bool IsMapEntryChild => Parent != null && Parent.Kind == FieldNodeKind.MapEntry;

        // For available fields dropdown
        public IEnumerable<FieldDescriptor> AvailableFieldsToAdd
        {
            get
            {
                if (AllAvailableFields == null || AllAvailableFields.Count == 0)
                    return Enumerable.Empty<FieldDescriptor>();

                var existingFieldNames = Children
                    .Where(c => c.Kind != FieldNodeKind.RepeatedItem && c.Kind != FieldNodeKind.MapEntry)
                    .Select(c => c.FieldName)
                    .ToHashSet();

                return AllAvailableFields.Where(f => !existingFieldNames.Contains(f.Name));
            }
        }

        // For oneof group: which oneof field is selected
        public IEnumerable<MessageFieldNode> AvailableOneofOptions
        {
            get
            {
                if (!IsOneofGroup) return Enumerable.Empty<MessageFieldNode>();
                return OneofOptions;
            }
        }

        public string TypeBadge
        {
            get
            {
                if (Kind == FieldNodeKind.MapEntry) return "";  // Don't show type badge for map entries
                if (IsMap) return "map";
                if (IsRepeated) return "repeated";
                if (IsOneofGroup) return "oneof";
                if (IsEnum) return "enum";
                if (IsMessage) return "msg";
                return DisplayType;
            }
        }
    }
}
