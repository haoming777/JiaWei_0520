using System.Windows;
using System.Windows.Media;

namespace CameraSwitchTool
{
    /// <summary>
    /// 扁平化消息对话框：替代系统 MessageBox，与整体界面风格一致。
    /// </summary>
    public partial class DialogWindow : Window
    {
        public enum DialogIconKind
        {
            Info,
            Question,
            Warning,
            Error
        }

        /// <summary>用户选择结果：true = 主按钮（确定/立即重启等）</summary>
        public bool Result { get; private set; }

        public DialogWindow(DialogIconKind icon, string title, string message, string detail,
            string primaryText, string secondaryText)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;
            if (string.IsNullOrEmpty(detail))
            {
                DetailText.Visibility = Visibility.Collapsed;
            }
            else
            {
                DetailText.Text = detail;
                DetailText.Visibility = Visibility.Visible;
            }

            PrimaryButton.Content = primaryText;
            if (string.IsNullOrEmpty(secondaryText))
            {
                SecondaryButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                SecondaryButton.Content = secondaryText;
            }

            switch (icon)
            {
                case DialogIconKind.Warning:
                    IconBorder.Background = BrushFromHex("#F59E0B");
                    IconGlyph.Text = "!";
                    break;
                case DialogIconKind.Error:
                    IconBorder.Background = BrushFromHex("#EF4444");
                    IconGlyph.Text = "!";
                    break;
                case DialogIconKind.Question:
                    IconBorder.Background = BrushFromHex("#0078D4");
                    IconGlyph.Text = "?";
                    break;
                default:
                    IconBorder.Background = BrushFromHex("#0078D4");
                    IconGlyph.Text = "i";
                    break;
            }

            Loaded += (s, e) => PrimaryButton.Focus();
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        /// <summary>
        /// 以模态方式显示对话框并返回用户选择。
        /// </summary>
        public static bool Show(Window owner, DialogIconKind icon, string title, string message,
            string detail, string primaryText, string secondaryText)
        {
            var dialog = new DialogWindow(icon, title, message, detail, primaryText, secondaryText);
            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            dialog.ShowDialog();
            return dialog.Result;
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}
