using System.Windows;
using System.Windows.Input;

namespace GrpcInvokeTool
{
    public partial class DetailDialog : Window
    {
        public DetailDialog(string title, string infoContent, string jsonContent)
        {
            InitializeComponent();
            DataContext = new DetailDialogViewModel
            {
                Title = title,
                InfoContent = infoContent,
                JsonContent = jsonContent
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }

    public class DetailDialogViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string InfoContent { get; set; } = string.Empty;
        public string JsonContent { get; set; } = string.Empty;
    }
}