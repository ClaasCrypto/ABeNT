using System.Windows;

namespace ABeNT
{
    public partial class UniversalPromptWindow : Window
    {
        public UniversalPromptWindow()
        {
            InitializeComponent();
        }

        public string UniversalPromptText
        {
            get => TxtUniversalPrompt.Text ?? string.Empty;
            set => TxtUniversalPrompt.Text = value ?? string.Empty;
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

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
