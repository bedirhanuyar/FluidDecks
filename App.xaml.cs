using System;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using FluidDecks.Core.Logging;

namespace FluidDecks;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Logger.Log("FluidDecks application starting...", "INFO");

        // Catch exceptions from UI thread
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        // Catch exceptions from background threads
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Catch exceptions from unobserved tasks
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Log("A fatal UI exception occurred.", "CRITICAL_ERROR", e.Exception);
        MessageBox.Show($"An unexpected UI error occurred:\n{e.Exception.Message}\n\nDetails have been saved to the log file.", "FluidDecks Crashed", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // Attempt to keep app alive to prevent silent flashing
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger.Log("A fatal background exception occurred.", "CRITICAL_ERROR", ex);
            MessageBox.Show($"An unexpected background error occurred:\n{ex.Message}\n\nDetails have been saved to the log file.", "FluidDecks Crashed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Log("An unobserved task exception occurred.", "ERROR", e.Exception);
        e.SetObserved();
    }
}
