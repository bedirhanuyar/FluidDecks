using System.Windows;

namespace FluidDecks.UI.Windows
{
    public partial class InputDialog : Wpf.Ui.Controls.FluentWindow
    {
        public string InputText { get; private set; }

        public InputDialog(string question, string defaultAnswer = "")
        {
            InitializeComponent();
            MessageText.Text = question;
            InputTextBox.Text = defaultAnswer;
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
