using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Google.Protobuf.Reflection;
using GrpcInvokeTool.Models;
using GrpcInvokeTool.ViewModels;

namespace GrpcInvokeTool
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (e.NewValue is GrpcMethodInfo method)
            {
                vm.SelectedMethod = method;
            }
            else if (e.NewValue is GrpcServiceInfo service)
            {
                vm.SelectedService = service;
                vm.SelectedMethod = null;
            }
        }

        /// <summary>
        /// "+" button click for adding a field to root node (first-level field).
        /// </summary>
        private void AddRootFieldButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (vm.RequestRootNode == null) return;

            // Use the same logic as AddFieldButton_Click but with RequestRootNode as parent
            var available = vm.RequestRootNode.AvailableFieldsToAdd.ToList();
            if (available.Count == 0) return;

            var menu = new ContextMenu { Style = (Style)FindResource("FieldContextMenuStyle") };

            foreach (var field in available)
            {
                var item = new MenuItem
                {
                    Header = $"{field.Name} ({GetFieldTypeLabel(field)})",
                    Tag = (vm.RequestRootNode, field),
                    Foreground = FindResource("PrimaryTextBrush") as System.Windows.Media.Brush,
                };
                item.Click += AddFieldMenuItem_Click;
                menu.Items.Add(item);
            }

            menu.PlacementTarget = sender as Button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;

            e.Handled = true;
        }

        /// <summary>
        /// "+" button click for adding a field to a message/root node.
        /// Shows a context menu with available fields.
        /// </summary>
        private void AddFieldButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not MessageFieldNode node) return;
            if (DataContext is not MainViewModel vm) return;

            var available = node.AvailableFieldsToAdd.ToList();
            if (available.Count == 0) return;

            var menu = new ContextMenu { Style = (Style)FindResource("FieldContextMenuStyle") };

            foreach (var field in available)
            {
                var item = new MenuItem
                {
                    Header = $"{field.Name} ({GetFieldTypeLabel(field)})",
                    Tag = (node, field),
                    Foreground = FindResource("PrimaryTextBrush") as System.Windows.Media.Brush,
                };
                item.Click += AddFieldMenuItem_Click;
                menu.Items.Add(item);
            }

            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;

            e.Handled = true;
        }

        private void AddFieldMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            if (item.Tag is not (MessageFieldNode parent, FieldDescriptor field)) return;
            if (DataContext is not MainViewModel vm) return;

            vm.AddFieldToParent(parent, field);
        }

        /// <summary>
        /// "+" button click for adding a repeated/map item.
        /// </summary>
        private void AddRepeatedItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not MessageFieldNode node) return;
            if (DataContext is not MainViewModel vm) return;

            if (node.IsMap)
            {
                vm.AddMapEntry(node);
            }
            else if (node.IsRepeated)
            {
                vm.AddRepeatedItem(node);
            }

            e.Handled = true;
        }

        /// <summary>
        /// "x" delete button click.
        /// </summary>
        private void DeleteFieldButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not MessageFieldNode node) return;
            if (DataContext is not MainViewModel vm) return;

            vm.DeleteFieldCommand.Execute(node);
            e.Handled = true;
        }

        /// <summary>
        /// Right-click delete menu item.
        /// </summary>
        private void DeleteFieldMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            if (menuItem.CommandParameter is not MessageFieldNode node) return;
            if (DataContext is not MainViewModel vm) return;

            vm.DeleteFieldCommand.Execute(node);
        }

        /// <summary>
        /// Oneof selector change.
        /// </summary>
        private void OneofSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo) return;
            if (combo.Tag is not MessageFieldNode oneofGroup) return;
            if (combo.SelectedItem is not MessageFieldNode selectedOption) return;
            if (DataContext is not MainViewModel vm) return;

            vm.SelectOneofOption(oneofGroup, selectedOption);
            e.Handled = true;
        }

        /// <summary>
        /// Field value changed - sync JSON.
        /// </summary>
        private void FieldValue_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            vm.SyncJsonFromTree();
        }

        private void FieldValue_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            vm.SyncJsonFromTree();
        }

        private string GetFieldTypeLabel(FieldDescriptor field)
        {
            if (field.IsMap)
            {
                var mapFields = field.MessageType.Fields.InFieldNumberOrder();
                return $"map<{GetSimpleType(mapFields[0])}, {GetSimpleType(mapFields[1])}>";
            }
            if (field.IsRepeated) return $"repeated {GetSimpleType(field)}";
            return GetSimpleType(field);
        }

        private string GetSimpleType(FieldDescriptor field)
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
    }
}
