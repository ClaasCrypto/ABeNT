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
        private string _fullAnamneseText = "";

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
            ChkTherapie.IsChecked = settings.IncludeTherapie;
            ChkIcd10.IsChecked = settings.SuggestIcd10;
            LoadSubjectForms();
            LoadDocumentations();
            UpdateKeyStatus();
        }

        private void ChkReportSection_Changed(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.LoadSettings();
            settings.IncludeBefund = ChkBefund.IsChecked ?? false;
            settings.IncludeTherapie = ChkTherapie.IsChecked ?? false;
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
                DeepgramApiKey = settings.DeepgramApiKey ?? string.Empty,
                SelectedLlm = settings.SelectedLlm ?? "Claude",
                OpenAiApiKey = settings.OpenAiApiKey ?? string.Empty,
                GeminiApiKey = settings.GeminiApiKey ?? string.Empty,
                ClaudeApiKey = settings.ClaudeApiKey ?? string.Empty,
                Gender = (CmbGender.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Neutral",
                IncludeBefund = ChkBefund.IsChecked ?? false,
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
            // Leere alle Felder zuerst
            _fullAnamneseText = "";
            TxtAnamnese.Text = "";
            TxtBefund.Text = "";
            TxtDiagnose.Text = "";
            TxtTherapie.Text = "";
            TxtIcd.Text = "";
            BorderIcd.Visibility = Visibility.Collapsed;
            if (ChkErstanamnese != null)
            {
                ChkErstanamnese.IsEnabled = false;
                ChkErstanamnese.IsChecked = false;
            }

            if (string.IsNullOrWhiteSpace(fullText))
            {
                return;
            }

            // Regex-Patterns für A, Be, N, T (ICD-10 erscheint nur in N als Code in Klammern, keine eigene Sektion)
            var patternA = @"\*\*A\*\*\s*[-–]?\s*(.*?)(?=\*\*Be\*\*|\*\*N\*\*|\*\*T\*\*|$)";
            var patternBe = @"\*\*Be\*\*\s*[-–]?\s*(.*?)(?=\*\*N\*\*|\*\*T\*\*|$)";
            var patternN = @"\*\*N\*\*\s*[-–]?\s*(.*?)(?=\*\*T\*\*|$)";
            var patternT = @"\*\*T\*\*\s*[-–]?\s*(.*?)(?=\*\*ICD-10\*\*|$)";

            // Extrahiere Anamnese (ohne "ANAMNESE"/"Jetziges Leiden:" – nur eine gültige Variante)
            var matchA = Regex.Match(fullText, patternA, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchA.Success && matchA.Groups.Count > 1)
            {
                _fullAnamneseText = StripAnamneseHeaders(CleanText(matchA.Groups[1].Value));
                if (ChkErstanamnese != null)
                {
                    ChkErstanamnese.IsEnabled = true;
                    UpdateAnamneseDisplay();
                }
                else
                {
                    TxtAnamnese.Text = _fullAnamneseText;
                }
            }

            // Extrahiere Befund
            var matchBe = Regex.Match(fullText, patternBe, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchBe.Success && matchBe.Groups.Count > 1)
            {
                TxtBefund.Text = CleanText(matchBe.Groups[1].Value);
            }

            // Extrahiere Diagnose
            var matchN = Regex.Match(fullText, patternN, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchN.Success && matchN.Groups.Count > 1)
            {
                TxtDiagnose.Text = CleanText(matchN.Groups[1].Value);
            }

            // Extrahiere Therapie (ohne ICD-10 in der Karte)
            var matchT = Regex.Match(fullText, patternT, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchT.Success && matchT.Groups.Count > 1)
            {
                string therapie = CleanText(matchT.Groups[1].Value);
                TxtTherapie.Text = StripIcd10FromTherapie(therapie);
            }
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

        /// <summary>Entfernt ICD-10-Bereich und alle Diagnose-Codes aus dem Therapie-Text (Überschrift **ICD-10**, Codes in Klammern oder eigenständig).</summary>
        private static string StripIcd10FromTherapie(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var icdCodeInLine = new Regex(@"\s*\([A-Z][0-9]{2}(\.[0-9\-]+)?\)\s*");
            var standaloneIcdLine = new Regex(@"^\s*[A-Z][0-9]{2}(\.[0-9\-]+)?\s*$", RegexOptions.Multiline);
            text = icdCodeInLine.Replace(text, " ");
            text = standaloneIcdLine.Replace(text, "");
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var filtered = new List<string>();
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (string.Equals(t, "**ICD-10**", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(t) && filtered.Count > 0 && filtered[filtered.Count - 1].Trim().Length == 0) continue;
                filtered.Add(line);
            }
            text = string.Join(Environment.NewLine, filtered);
            return Regex.Replace(text, @"[ \t]+", " ").Trim();
        }

        private void UpdateAnamneseDisplay()
        {
            if (TxtAnamnese == null) return;
            TxtAnamnese.Text = (ChkErstanamnese?.IsChecked == true)
                ? _fullAnamneseText
                : GetShortAnamnese(_fullAnamneseText);
        }

        private void ChkErstanamnese_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAnamneseDisplay();
        }

        /// <summary>Liefert den Anamnese-Text nur bis vor die erste Rubrik (Vorerkrankungen, Dauermedikation, Allergien, vegetative Anamnese, Noxen/Sozialanamnese).</summary>
        private static string GetShortAnamnese(string full)
        {
            if (string.IsNullOrWhiteSpace(full)) return full;
            var lines = full.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sectionStarts = new[] { "Vorerkrankungen", "Dauermedikation", "Allergien", "Vegetative Anamnese", "Noxen", "Sozialanamnese" };
            int cutIndex = lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (sectionStarts.Any(s => trimmed.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                {
                    cutIndex = i;
                    break;
                }
            }
            return string.Join(Environment.NewLine, lines.Take(cutIndex)).TrimEnd();
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

        private async void BtnCopyT_Click(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardWithFeedback(TxtTherapie.Text, BtnCopyT);
        }

        private async void BtnCopyIcd_Click(object sender, RoutedEventArgs e)
        {
            await CopyToClipboardWithFeedback(TxtIcd.Text, BtnCopyIcd);
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
