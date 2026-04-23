using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using GrpcInvokeTool.Models;

namespace GrpcInvokeTool
{
    public partial class SelectGroupDialog : Window
    {
        public CallGroup? SelectedGroup { get; private set; }
        public bool CreateNewGroup { get; private set; }

        private readonly ObservableCollection<CallGroup> _groups;

        public SelectGroupDialog(ObservableCollection<CallGroup> groups)
        {
            InitializeComponent();
            _groups = groups;
            GroupsListBox.ItemsSource = groups;
            GroupsListBox.SelectionChanged += GroupsListBox_SelectionChanged;
        }

        private void GroupsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            AddButton.IsEnabled = GroupsListBox.SelectedItem != null;
        }

        private void GroupsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GroupsListBox.SelectedItem is CallGroup group)
            {
                SelectedGroup = group;
                CreateNewGroup = false;
                DialogResult = true;
                Close();
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            SelectedGroup = GroupsListBox.SelectedItem as CallGroup;
            CreateNewGroup = false;
            DialogResult = true;
            Close();
        }

        private void NewGroup_Click(object sender, RoutedEventArgs e)
        {
            SelectedGroup = null;
            CreateNewGroup = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}