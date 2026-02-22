using System;
using System.Windows;
using System.Windows.Threading;

namespace ABeNT
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Globaler Exception-Handler
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Unbehandelte Exception:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", 
                "Kritischer Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Unbehandelte Exception:\n\n{ex.Message}\n\n{ex.StackTrace}", 
                    "Kritischer Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
