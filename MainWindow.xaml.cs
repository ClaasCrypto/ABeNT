using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
            ChkIcd10.IsChecked = settings.SuggestIcd10;
            LoadSubjectForms();
            LoadDocumentations();
            UpdateKeyStatus();
        }

        private void ChkReportSection_Changed(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.LoadSettings();
            settings.IncludeBefund = ChkBefund.IsChecked ?? false;
            settings.SuggestIcd10 = ChkIcd10.IsChecked ?? false;
            SettingsService.SaveSettings(settings);
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
                Gender = "Neutral",
                IncludeBefund = ChkBefund.IsChecked ?? false,
                IncludeTherapie = false,
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
            var formsWindow = new OutputFormsWindow { Owner = this };
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

        private void ParseAndDisplayResult(string fullText)
        {
            TxtAnamnese.Text = "";
            TxtBefund.Text = "";
            TxtDiagnose.Text = "";

            if (string.IsNullOrWhiteSpace(fullText))
            {
                return;
            }

            var patternA = @"\*\*A\*\*\s*[-–]?\s*(.*?)(?=\*\*Be\*\*|\*\*N\*\*|\*\*T\*\*|\*\*ICD-10\*\*|$)";
            var patternBe = @"\*\*Be\*\*\s*[-–]?\s*(.*?)(?=\*\*N\*\*|\*\*T\*\*|\*\*ICD-10\*\*|$)";
            var patternN = @"\*\*N\*\*\s*[-–]?\s*(.*?)(?=\*\*T\*\*|\*\*ICD-10\*\*|$)";
            var patternIcd = @"\*\*ICD-10\*\*\s*[-–]?\s*(.*?)$";

            var matchA = Regex.Match(fullText, patternA, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchA.Success && matchA.Groups.Count > 1)
            {
                TxtAnamnese.Text = StripAnamneseHeaders(CleanText(matchA.Groups[1].Value));
            }

            var matchBe = Regex.Match(fullText, patternBe, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchBe.Success && matchBe.Groups.Count > 1)
            {
                TxtBefund.Text = CleanText(matchBe.Groups[1].Value);
            }

            // Diagnosen und ICD-10 in einer Karte zusammenführen
            string diagnosen = "";
            var matchN = Regex.Match(fullText, patternN, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchN.Success && matchN.Groups.Count > 1)
                diagnosen = CleanText(matchN.Groups[1].Value);

            var matchIcd = Regex.Match(fullText, patternIcd, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchIcd.Success && matchIcd.Groups.Count > 1)
            {
                string icdText = CleanText(matchIcd.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(icdText))
                    diagnosen = MergeDiagnosenWithIcd(diagnosen, icdText);
            }

            TxtDiagnose.Text = diagnosen;
        }

        /// <summary>
        /// Merges the N (diagnoses) and ICD-10 blocks into one display:
        /// each diagnosis line gets its ICD-10 code appended in parentheses.
        /// </summary>
        private static string MergeDiagnosenWithIcd(string diagnosen, string icd10)
        {
            if (string.IsNullOrWhiteSpace(diagnosen))
                return icd10;
            if (string.IsNullOrWhiteSpace(icd10))
                return diagnosen;

            var diagLines = diagnosen.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var icdLines = icd10.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            var icdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in icdLines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                // ICD-10 lines: "M54.5 Lumbago" or "M54.5G Lumbago re." — extract code
                var m = Regex.Match(trimmed, @"^([A-Z]\d{2}(?:\.\d[\d\-]*)?[GVAZ]?(?:\s*[RLB])?)\s+(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string code = m.Groups[1].Value.Trim();
                    string diagText = m.Groups[2].Value.Trim();
                    icdMap[diagText] = code;
                }
            }

            var result = new List<string>();
            foreach (string line in diagLines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                string bestCode = "";
                int bestLen = 0;
                foreach (var kv in icdMap)
                {
                    if (trimmed.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        if (kv.Key.Length > bestLen)
                        {
                            bestLen = kv.Key.Length;
                            bestCode = kv.Value;
                        }
                    }
                }

                if (string.IsNullOrEmpty(bestCode) && icdMap.Count > 0)
                {
                    // Positional fallback: match by line index
                    int idx = Array.IndexOf(diagLines, line);
                    if (idx >= 0 && idx < icdLines.Length)
                    {
                        var m = Regex.Match(icdLines[idx].Trim(), @"^([A-Z]\d{2}(?:\.\d[\d\-]*)?[GVAZ]?(?:\s*[RLB])?)\s+", RegexOptions.IgnoreCase);
                        if (m.Success)
                            bestCode = m.Groups[1].Value.Trim();
                    }
                }

                result.Add(string.IsNullOrEmpty(bestCode) ? trimmed : $"{trimmed} ({bestCode})");
            }

            return string.Join(Environment.NewLine, result);
        }

        /// <summary>Entfernt die Zeile "ANAMNESE" und die Überschrift "Jetziges Leiden:" aus dem Anamnese-Text (nur eine gültige Variante).</summary>
        private static string StripAnamneseHeaders(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new List<string>();
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (string.Equals(t, "ANAMNESE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (t.StartsWith("Jetziges Leiden:", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = t.Length > 15 ? t.Substring(15).Trim() : "";
                    if (!string.IsNullOrEmpty(rest)) result.Add(rest);
                    continue;
                }
                result.Add(line);
            }
            string joined = string.Join(Environment.NewLine, result).Trim();
            joined = joined.TrimStart(':', ' ').TrimStart('\r', '\n');
            return joined;
        }

        private string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            text = text.Trim();
            // Mehrfache Leerzeichen/Tabs pro Zeile reduzieren, Zeilenumbrüche beibehalten (für Anamnese-Kurzansicht)
            text = Regex.Replace(text, @"[ \t]+", " ");
            // Mehrfache Zeilenumbrüche auf maximal zwei reduzieren
            text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");
            return text;
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}
