using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ABeNT.Services;

namespace ABeNT
{
    public partial class GeneratePromptsDialog : Window
    {
        public string GeneratedA { get; private set; } = string.Empty;
        public string GeneratedBe { get; private set; } = string.Empty;
        public string GeneratedN { get; private set; } = string.Empty;
        public string GeneratedIcd10 { get; private set; } = string.Empty;
        public string Fachgebiet { get; private set; } = string.Empty;
        public string Untersuchung { get; private set; } = string.Empty;
        public bool AnamneseIncluded { get; private set; } = true;

        private CancellationTokenSource? _cts;

        public GeneratePromptsDialog()
        {
            InitializeComponent();
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            string fach = (TxtFachgebiet.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fach))
            {
                MessageBox.Show("Bitte ein Fachgebiet eingeben.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = SettingsService.LoadSettings();
            string llm = settings.SelectedLlm ?? "Claude";
            string apiKey = llm switch
            {
                "ChatGPT" => settings.OpenAiApiKey ?? "",
                "Gemini" => settings.GeminiApiKey ?? "",
                "Mistral" => settings.MistralApiKey ?? "",
                _ => settings.ClaudeApiKey ?? ""
            };

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show($"Kein API Key für {llm} hinterlegt. Bitte zuerst in den Einstellungen eintragen.",
                    "API Key fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string untersuchung = (TxtUntersuchung.Text ?? "").Trim();
            bool includeAnamnese = ChkAnamnese.IsChecked ?? true;

            BtnGenerate.IsEnabled = false;
            BtnCancel.Content = "Abbrechen";
            TxtStatus.Text = $"Generiere Prompts via {llm}...";

            _cts = new CancellationTokenSource();

            try
            {
                string metaPrompt = PromptGeneratorService.BuildMetaPrompt(fach, untersuchung, includeAnamnese);

                var llmService = new LlmService();
                string response = await llmService.GenerateRawAsync(
                    metaPrompt, $"Erstelle die Prompt-Module für: {fach}", llm, apiKey, _cts.Token);

                ParseResponse(response, includeAnamnese);

                Fachgebiet = fach;
                Untersuchung = untersuchung;
                AnamneseIncluded = includeAnamnese;

                TxtStatus.Text = "Prompts erfolgreich generiert.";
                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Abgebrochen.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "";
                MessageBox.Show($"Fehler bei der Generierung:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnGenerate.IsEnabled = true;
                BtnCancel.Content = "Schließen";
                _cts = null;
            }
        }

        private void ParseResponse(string response, bool includeAnamnese)
        {
            GeneratedA = includeAnamnese ? ExtractSection(response, "===A===", "===Be===") : "";
            GeneratedBe = ExtractSection(response, "===Be===", "===N===");
            GeneratedN = ExtractSection(response, "===N===", "===END===");
            GeneratedIcd10 = string.Empty;

            if (string.IsNullOrWhiteSpace(GeneratedBe) && string.IsNullOrWhiteSpace(GeneratedN))
                throw new Exception("LLM-Antwort konnte nicht geparst werden (Delimiter ===A===, ===Be===, ===N===, ===END=== nicht gefunden). Bitte erneut versuchen.");
        }

        private static string ExtractSection(string text, string startMarker, string endMarker)
        {
            int start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;
            start += startMarker.Length;

            int end = text.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = text.Length;

            return text.Substring(start, end - start).Trim();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                return;
            }
            DialogResult = false;
            Close();
        }
    }
}
