using System.Windows;
using FluidDecks.UI.ViewModels;

namespace FluidDecks.UI.Windows
{
    /// <summary>
    /// The first-run onboarding screen. Introduces the user to basic concepts
    /// and ensures initial configuration is acknowledged.
    /// </summary>
    public partial class WelcomeWindow : Wpf.Ui.Controls.FluentWindow
    {
        public WelcomeWindow()
        {
            InitializeComponent();
        }

        private void GetStarted_Click(object sender, RoutedEventArgs e)
        {
            var mainVM = Application.Current?.MainWindow?.DataContext as MainViewModel;
            if (mainVM?.AppConfigManager != null)
            {
                // Mark first run as complete and save atomically
                mainVM.AppConfigManager.CurrentConfig.IsFirstRun = false;
                mainVM.AppConfigManager.SaveConfig();
            }

            this.Close();
        }
    }
}
