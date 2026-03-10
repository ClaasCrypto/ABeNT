using System.Windows;
using System.Windows.Media;

namespace ABeNT
{
    public partial class UniversalPromptWindow : Window
    {
        public UniversalPromptWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateStatus();
        }

        public string UniversalPromptText
        {
            get => TxtUniversalPrompt.Text ?? string.Empty;
            set => TxtUniversalPrompt.Text = value ?? string.Empty;
        }

        private void UpdateStatus()
        {
            bool customized = Services.OutputFormsService.IsUniversalPromptCustomized();
            if (customized)
            {
                TxtUniversalStatus.Text = "Angepasst";
                TxtUniversalStatus.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#F072AB"));
                BtnRestoreDefault.Visibility = Visibility.Visible;
            }
            else
            {
                TxtUniversalStatus.Text = "Standard";
                TxtUniversalStatus.Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#558FC4"));
                BtnRestoreDefault.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Möchten Sie den Universal-Prompt speichern?",
                "Speichern",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            result = MessageBox.Show(
                "Letzte Bestätigung: Änderungen am Universal-Prompt betreffen alle Berichtsformulare.\n\nWirklich speichern?",
                "Doppelte Bestätigung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            Services.OutputFormsService.SetUniversalPrompt(TxtUniversalPrompt.Text ?? string.Empty);
            MessageBox.Show("Universal-Prompt wurde gespeichert.", "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Universal-Prompt auf den Code-Standard zurücksetzen?\n\nAlle individuellen Anpassungen gehen verloren.",
                "Standard wiederherstellen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            Services.OutputFormsService.RestoreDefaultUniversalPrompt();
            TxtUniversalPrompt.Text = Services.OutputFormsService.GetUniversalPrompt();
            UpdateStatus();
            MessageBox.Show("Standard wiederhergestellt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
