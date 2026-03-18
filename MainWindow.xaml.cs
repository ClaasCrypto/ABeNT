using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ABeNT.Model;
using ABeNT.Services;
using ABeNT.ViewModel;

namespace ABeNT
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                DataContext = new MainViewModel();
                Loaded += MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Initialisieren des Fensters:\n\n{ex.Message}\n\n{ex.StackTrace}", 
                    "Kritischer Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.LoadSettings();
            ChkBefund.IsChecked = settings.IncludeBefund;
            ChkDiagnosen.IsChecked = settings.IncludeDiagnosen;
            ChkIcd10.IsChecked = settings.SuggestIcd10;
            ChkIcd10.IsEnabled = settings.IncludeDiagnosen;
            ChkTherapie.IsChecked = settings.IncludeTherapie;
            LoadSubjectForms();
            LoadDocumentations();
            UpdateKeyStatus();
            UpdateTokenEstimate();
        }

        private void ChkReportSection_Changed(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.LoadSettings();
            settings.IncludeBefund = ChkBefund.IsChecked ?? false;
            settings.IncludeDiagnosen = ChkDiagnosen.IsChecked ?? false;
            settings.SuggestIcd10 = ChkIcd10.IsChecked ?? false;
            settings.IncludeTherapie = ChkTherapie.IsChecked ?? false;
            ChkIcd10.IsEnabled = settings.IncludeDiagnosen;
            if (!settings.IncludeDiagnosen) ChkIcd10.IsChecked = false;
            SettingsService.SaveSettings(settings);
            UpdateTokenEstimate();
        }

        private void UpdateTokenEstimate()
        {
            if (TxtTokenEstimate == null) return;
            try
            {
                var formId = (CmbSubjectForm.SelectedItem as Model.SubjectForm)?.Id;
                bool includeBefund = ChkBefund.IsChecked ?? false;
                bool includeDiagnosen = ChkDiagnosen.IsChecked ?? false;
                bool includeIcd10 = ChkIcd10.IsChecked ?? false;
                bool includeTherapie = ChkTherapie.IsChecked ?? false;

                string fullPrompt = OutputFormsService.BuildSystemPromptFromConfig(
                    formId, "Neutral", true, true, true, true, "Neupatient");
                int fullTotal = RoundToNearest(fullPrompt.Length / 4 + EstimateOutputTokens(true, true, true, true), 50);

                string currentPrompt = OutputFormsService.BuildSystemPromptFromConfig(
                    formId, "Neutral", includeBefund, includeDiagnosen, includeTherapie, includeIcd10, "Neupatient");
                int currentTotal = RoundToNearest(currentPrompt.Length / 4 + EstimateOutputTokens(includeBefund, includeDiagnosen, includeIcd10, includeTherapie), 50);

                int saved = fullTotal - currentTotal;
                TxtTokenEstimate.Text = saved > 0
                    ? $"⚡ ≈ {currentTotal:N0} Tokens  (↓{saved:N0} gespart)"
                    : $"⚡ ≈ {currentTotal:N0} Tokens";
            }
            catch
            {
                TxtTokenEstimate.Text = string.Empty;
            }
        }

        private static int EstimateOutputTokens(bool includeBefund, bool includeDiagnosen, bool includeIcd10, bool includeTherapie)
        {
            int tokens = 300; // Anamnese
            if (includeBefund) tokens += 250;
            if (includeDiagnosen) tokens += includeIcd10 ? 200 : 100;
            if (includeTherapie) tokens += 150;
            return tokens;
        }

        private static int RoundToNearest(int value, int nearest)
        {
            return (int)(Math.Round((double)value / nearest) * nearest);
        }

        private void UpdateKeyStatus()
        {
            if (TxtKeyStatus == null) return;
            var settings = SettingsService.LoadSettings();
            bool deepgramOk = !string.IsNullOrWhiteSpace(settings.DeepgramApiKey);
            string llmKey = settings.SelectedLlm switch
            {
                "ChatGPT" => settings.OpenAiApiKey ?? "",
                "Gemini" => settings.GeminiApiKey ?? "",
                "Mistral" => settings.MistralApiKey ?? "",
                _ => settings.ClaudeApiKey ?? ""
            };
            bool llmOk = !string.IsNullOrWhiteSpace(llmKey);
            string d = deepgramOk ? "✓" : "–";
            string l = llmOk ? "✓" : "–";
            TxtKeyStatus.Text = $"API-Keys: Deepgram {d}   LLM ({settings.SelectedLlm ?? "Claude"}) {l}";
        }

        private void LoadSubjectForms()
        {
            try
            {
                var forms = Services.OutputFormsService.GetForms();
                CmbSubjectForm.ItemsSource = forms;
                if (forms.Count > 0)
                {
                    var settings = SettingsService.LoadSettings();
                    var lastId = (settings.LastSelectedFormId ?? string.Empty).Trim();
                    var preferred = !string.IsNullOrEmpty(lastId)
                        ? forms.FirstOrDefault(f => string.Equals(f.Id, lastId, StringComparison.OrdinalIgnoreCase))
                        : forms.FirstOrDefault(f => string.Equals(f.Id, "allgemeinmedizin", StringComparison.OrdinalIgnoreCase));
                    if (preferred != null)
                        CmbSubjectForm.SelectedItem = preferred;
                    else
                        CmbSubjectForm.SelectedIndex = 0;
                }
                CmbSubjectForm.SelectionChanged -= CmbSubjectForm_SelectionChanged;
                CmbSubjectForm.SelectionChanged += CmbSubjectForm_SelectionChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der Berichtsformulare: {ex.Message}");
            }
        }

        private void CmbSubjectForm_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSubjectForm?.SelectedItem is Model.SubjectForm form)
            {
                var settings = SettingsService.LoadSettings();
                settings.LastSelectedFormId = form.Id ?? string.Empty;
                SettingsService.SaveSettings(settings);
            }
            UpdateTokenEstimate();
        }

        private void BtnOpenRecorder_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.LoadSettings();
            var options = new RecorderReportOptions
            {
                SelectedSttProvider = settings.SelectedSttProvider ?? "Deepgram",
                DeepgramApiKey = settings.DeepgramApiKey ?? string.Empty,
                AzureSpeechKey = settings.AzureSpeechKey ?? string.Empty,
                AzureSpeechRegion = settings.AzureSpeechRegion ?? "westeurope",
                CustomSttEndpoint = settings.CustomSttEndpoint ?? string.Empty,
                CustomSttApiKey = settings.CustomSttApiKey ?? string.Empty,
                SelectedLlm = settings.SelectedLlm ?? "Claude",
                OpenAiApiKey = settings.OpenAiApiKey ?? string.Empty,
                GeminiApiKey = settings.GeminiApiKey ?? string.Empty,
                ClaudeApiKey = settings.ClaudeApiKey ?? string.Empty,
                MistralApiKey = settings.MistralApiKey ?? string.Empty,
                Gender = "Neutral",
                IncludeBefund = ChkBefund.IsChecked ?? false,
                IncludeDiagnosen = ChkDiagnosen.IsChecked ?? false,
                IncludeTherapie = ChkTherapie.IsChecked ?? false,
                IncludeIcd10 = ChkIcd10.IsChecked ?? false,
                FormId = (CmbSubjectForm.SelectedItem as Model.SubjectForm)?.Id
            };
            var recorder = new RecorderWindow
            {
                Owner = this,
                ReportOptions = options
            };
            if (recorder.ShowDialog() == true && !string.IsNullOrEmpty(recorder.ResultReportText))
            {
                ParseAndDisplayResult(recorder.ResultReportText);
                SaveDocumentation(recorder.ResultReportText);
                LoadDocumentations();
            }
        }

        private void BtnManageForms_Click(object sender, RoutedEventArgs e)
        {
            var currentFormId = (CmbSubjectForm.SelectedItem as Model.SubjectForm)?.Id;
            var formsWindow = new OutputFormsWindow(currentFormId) { Owner = this };
            formsWindow.ShowDialog();
            LoadSubjectForms();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = this
            };
            
            settingsWindow.ShowDialog();
            UpdateKeyStatus();
        }

        private void LstNotes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Notiz-Aktionen aktivieren/deaktivieren basierend auf Auswahl
            bool hasSelection = LstNotes.SelectedItem != null;
            BtnCopyNote.IsEnabled = hasSelection;
            BtnExportNote.IsEnabled = hasSelection;
            BtnDeleteNote.IsEnabled = hasSelection;

            // Lade Inhalt der ausgewählten Dokumentation
            if (hasSelection && LstNotes.SelectedValue is string filePath && File.Exists(filePath))
            {
                try
                {
                    string content = File.ReadAllText(filePath, Encoding.UTF8);
                    ParseAndDisplayResult(content);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der Dokumentation: {ex.Message}");
                }
            }
        }

        private void BtnCopyNote_Click(object sender, RoutedEventArgs e)
        {
            if (LstNotes.SelectedValue is string filePath && File.Exists(filePath))
            {
                try
                {
                    string content = File.ReadAllText(filePath, Encoding.UTF8);
                    Clipboard.SetText(content);
                    MessageBox.Show("Dokumentation in Zwischenablage kopiert.", 
                        "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Kopieren: {ex.Message}", 
                        "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportNote_Click(object sender, RoutedEventArgs e)
        {
            if (LstNotes.SelectedValue is string filePath && File.Exists(filePath))
            {
                try
                {
                    // Öffne Datei-Explorer mit ausgewählter Datei
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Öffnen: {ex.Message}", 
                        "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (LstNotes.SelectedValue is string filePath && File.Exists(filePath))
            {
                var result = MessageBox.Show(
                    $"Möchten Sie diese Dokumentation wirklich löschen?\n\n{Path.GetFileName(filePath)}",
                    "Löschen bestätigen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        File.Delete(filePath);
                        LoadDocumentations(); // Aktualisiere Liste
                        ParseAndDisplayResult(""); // Leere Anzeige
                        MessageBox.Show("Dokumentation gelöscht.", 
                            "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Fehler beim Löschen: {ex.Message}", 
                            "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void SaveDocumentation(string content)
        {
            try
            {
                // Erstelle Dokumentationen-Ordner
                string documentsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                    "ABeNT", 
                    "Dokumentationen");
                Directory.CreateDirectory(documentsPath);

                // Erstelle Dateiname mit Zeitstempel
                string fileName = $"ABeNT_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string filePath = Path.Combine(documentsPath, fileName);

                // Speichere Datei
                File.WriteAllText(filePath, content, Encoding.UTF8);

                // Aktualisiere Liste
                LoadDocumentations();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern der Dokumentation: {ex.Message}");
            }
        }

        private void LoadDocumentations()
        {
            try
            {
                string documentsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                    "ABeNT", 
                    "Dokumentationen");

                if (!Directory.Exists(documentsPath))
                {
                    LstNotes.ItemsSource = new List<string>();
                    return;
                }

                // Berichte nur 48 Stunden aufbewahren – ältere löschen
                var cutoff = DateTime.Now.AddHours(-48);
                foreach (string f in Directory.GetFiles(documentsPath, "ABeNT_*.txt"))
                {
                    try
                    {
                        if (File.GetCreationTime(f) < cutoff)
                            File.Delete(f);
                    }
                    catch { /* ignore */ }
                }

                // Lade alle TXT-Dateien, sortiert nach Datum (neueste zuerst)
                var files = Directory.GetFiles(documentsPath, "ABeNT_*.txt")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Select(f => new
                    {
                        FilePath = f,
                        DisplayName = $"{Path.GetFileNameWithoutExtension(f).Replace("ABeNT_", "")} - {File.GetCreationTime(f):dd.MM.yyyy HH:mm}"
                    })
                    .ToList();

                LstNotes.ItemsSource = files;
                LstNotes.DisplayMemberPath = "DisplayName";
                LstNotes.SelectedValuePath = "FilePath";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der Dokumentationen: {ex.Message}");
                LstNotes.ItemsSource = new List<string>();
            }
        }

        /// <summary>
        /// Displays a report result in the regular result area (Anamnese, Befund, Diagnosen).
        /// Used e.g. when running the example transcript test from the forms window.
        /// </summary>
        public void DisplayReportResult(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return;
            ParseAndDisplayResult(fullText);
            SaveDocumentation(fullText);
            LoadDocumentations();
            Activate();
        }

        /// <summary>
        /// Sets the selected form (Fachgebiet) in the main window by form Id.
        /// Used so that after a test from the forms window, the main window shows the same form that was tested.
        /// </summary>
        public void SetSelectedFormId(string? formId)
        {
            if (string.IsNullOrWhiteSpace(formId)) return;
            var forms = Services.OutputFormsService.GetForms();
            var form = forms.FirstOrDefault(f => string.Equals(f.Id, formId, StringComparison.OrdinalIgnoreCase));
            if (form == null) return;
            CmbSubjectForm.SelectedItem = form;
            var settings = SettingsService.LoadSettings();
            settings.LastSelectedFormId = form.Id ?? string.Empty;
            SettingsService.SaveSettings(settings);
        }

        private void ParseAndDisplayResult(string fullText)
        {
            TxtAnamnese.Text = "";
            TxtBefund.Text = "";
            TxtDiagnose.Text = "";
            TxtTherapie.Text = "";

            if (string.IsNullOrWhiteSpace(fullText))
            {
                return;
            }

            var patternA = @"\[ABSCHNITT:A\]\s*(.*?)(?=\[ABSCHNITT:(?:Be|T|N)\]|$)";
            var patternBe = @"\[ABSCHNITT:Be\]\s*(.*?)(?=\[ABSCHNITT:(?:T|N)\]|$)";
            var patternT = @"\[ABSCHNITT:T\]\s*(.*?)(?=\[ABSCHNITT:N\]|$)";
            var patternN = @"\[ABSCHNITT:N\]\s*(.*?)$";

            var matchA = Regex.Match(fullText, patternA, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchA.Success && matchA.Groups.Count > 1)
            {
                TxtAnamnese.Text = matchA.Groups[1].Value.Trim();
            }

            var matchBe = Regex.Match(fullText, patternBe, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchBe.Success && matchBe.Groups.Count > 1)
            {
                TxtBefund.Text = matchBe.Groups[1].Value.Trim();
            }

            var matchT = Regex.Match(fullText, patternT, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchT.Success && matchT.Groups.Count > 1)
            {
                TxtTherapie.Text = matchT.Groups[1].Value.Trim();
            }

            var matchN = Regex.Match(fullText, patternN, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchN.Success && matchN.Groups.Count > 1)
            {
                TxtDiagnose.Text = matchN.Groups[1].Value.Trim();
            }
        }

        private async void BtnCopyA_Click(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardWithFeedback(TxtAnamnese.Text, BtnCopyA);
        }

        private async void BtnCopyBe_Click(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardWithFeedback(TxtBefund.Text, BtnCopyBe);
        }

        private async void BtnCopyN_Click(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardWithFeedback(TxtDiagnose.Text, BtnCopyN);
        }

        private async void BtnCopyT_Click(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardWithFeedback(TxtTherapie.Text, BtnCopyT);
        }

        private async Task CopyToClipboardWithFeedback(string text, Button button)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Kein Text zum Kopieren vorhanden.", 
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Clipboard.SetText(text);
                
                // Feedback: Button-Text kurz ändern
                string originalText = button.Content.ToString() ?? "Kopieren";
                button.Content = "Kopiert!";
                button.IsEnabled = false;
                
                await Task.Delay(1500);
                
                button.Content = originalText;
                button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Kopieren: {ex.Message}", 
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───── Diktat-Funktionen ─────

        private void BtnDictateA_Click(object sender, RoutedEventArgs e) => OpenDictationWindow("A", TxtAnamnese);
        private void BtnDictateBe_Click(object sender, RoutedEventArgs e) => OpenDictationWindow("Be", TxtBefund);
        private void BtnDictateT_Click(object sender, RoutedEventArgs e) => OpenDictationWindow("T", TxtTherapie);
        private void BtnDictateN_Click(object sender, RoutedEventArgs e) => OpenDictationWindow("N", TxtDiagnose);

        private void OpenDictationWindow(string section, TextBox targetBox)
        {
            var formId = (CmbSubjectForm.SelectedItem as SubjectForm)?.Id;
            var dlg = new DictationWindow
            {
                Owner = this,
                Section = section,
                FormId = formId
            };

            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText))
                return;

            if (dlg.ShouldReplace)
            {
                targetBox.Text = dlg.ResultText;
            }
            else
            {
                string existing = targetBox.Text?.Trim() ?? string.Empty;
                targetBox.Text = string.IsNullOrEmpty(existing)
                    ? dlg.ResultText
                    : $"{existing}\n{dlg.ResultText}";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}
