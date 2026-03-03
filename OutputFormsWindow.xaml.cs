using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ABeNT.Model;
using ABeNT.Services;

namespace ABeNT
{
    public partial class OutputFormsWindow : Window
    {
        private List<SubjectForm> _forms = new List<SubjectForm>();
        private string? _editingFormId; // null = new form, not yet saved
        private readonly LlmService _llmService = new LlmService();

        public OutputFormsWindow()
        {
            InitializeComponent();
            Loaded += OutputFormsWindow_Loaded;
        }

        private void OutputFormsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshFormList();
        }

        private void RefreshFormList()
        {
            _forms = OutputFormsService.GetForms();
            LstForms.ItemsSource = null;
            LstForms.ItemsSource = _forms;
            if (_forms.Count > 0 && LstForms.SelectedIndex < 0)
                LstForms.SelectedIndex = 0;
        }

        private void BtnUniversalPrompt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new UniversalPromptWindow
            {
                Owner = this,
                UniversalPromptText = OutputFormsService.GetUniversalPrompt()
            };
            dlg.ShowDialog();
        }

        private void LstForms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstForms.SelectedItem is not SubjectForm form)
            {
                PanelFormDetail.Visibility = Visibility.Collapsed;
                BtnRemove.IsEnabled = false;
                return;
            }
            BtnRemove.IsEnabled = true;
            PanelFormDetail.Visibility = Visibility.Visible;
            _editingFormId = form.Id;
            TxtId.Text = form.Id;
            TxtId.IsReadOnly = true; // Id nicht ändern (Eindeutigkeit)
            TxtDisplayName.Text = form.DisplayName;
            TxtDescription.Text = form.Description ?? string.Empty;
            TxtPromptA.Text = form.SectionPrompts?.A ?? string.Empty;
            TxtPromptBe.Text = form.SectionPrompts?.Be ?? string.Empty;
            TxtPromptN.Text = form.SectionPrompts?.N ?? string.Empty;
            TxtPromptIcd10.Text = form.SectionPrompts?.Icd10 ?? string.Empty;
            BtnRestoreDefault.Visibility = OutputFormsService.IsStandardForm(form.Id) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new GeneratePromptsDialog { Owner = this };
            bool generated = dlg.ShowDialog() == true;

            string displayName;
            string description = string.Empty;
            var prompts = new AbentSectionPrompts();

            if (generated)
            {
                displayName = string.IsNullOrWhiteSpace(dlg.Untersuchung)
                    ? dlg.Fachgebiet
                    : $"{dlg.Fachgebiet} – {dlg.Untersuchung}";
                if (!dlg.AnamneseIncluded)
                    description = "Kein Anamnese-Block.";
                prompts.A = dlg.GeneratedA;
                prompts.Be = dlg.GeneratedBe;
                prompts.N = dlg.GeneratedN;
                prompts.Icd10 = dlg.GeneratedIcd10;
            }
            else
            {
                displayName = "Neues Formular";
            }

            string newId = NormalizeId(displayName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            var newForm = new SubjectForm
            {
                Id = newId,
                DisplayName = displayName,
                Description = description,
                SectionPrompts = prompts
            };
            try
            {
                OutputFormsService.AddForm(newForm);
                RefreshFormList();
                var index = _forms.FindIndex(f => f.Id == newId);
                if (index >= 0)
                    LstForms.SelectedIndex = index;
                _editingFormId = newId;
                PanelFormDetail.Visibility = Visibility.Visible;
                BtnRemove.IsEnabled = true;
                TxtId.IsReadOnly = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (LstForms.SelectedItem is not SubjectForm form)
                return;
            var result = MessageBox.Show(
                $"Berichtsformular \"{form.DisplayName}\" wirklich entfernen?",
                "Entfernen bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                OutputFormsService.RemoveForm(form.Id);
                RefreshFormList();
                PanelFormDetail.Visibility = Visibility.Collapsed;
                BtnRemove.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_editingFormId)) return;
            var result = MessageBox.Show(
                $"Standardvorlage für \"{_editingFormId}\" wiederherstellen? Alle Änderungen an diesem Formular gehen verloren.",
                "Standard wiederherstellen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                OutputFormsService.RestoreDefaultForm(_editingFormId);
                var form = OutputFormsService.GetForm(_editingFormId);
                if (form != null)
                {
                    TxtDisplayName.Text = form.DisplayName;
                    TxtDescription.Text = form.Description ?? string.Empty;
                    TxtPromptA.Text = form.SectionPrompts?.A ?? string.Empty;
                    TxtPromptBe.Text = form.SectionPrompts?.Be ?? string.Empty;
                    TxtPromptN.Text = form.SectionPrompts?.N ?? string.Empty;
                    TxtPromptIcd10.Text = form.SectionPrompts?.Icd10 ?? string.Empty;
                }
                RefreshFormList();
                MessageBox.Show("Standard wiederhergestellt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveForm_Click(object sender, RoutedEventArgs e)
        {
            TrySaveForm(showSuccessMessage: true);
        }

        /// <summary>Saves the current form. Returns true on success. Optionally shows a success message.</summary>
        private bool TrySaveForm(bool showSuccessMessage)
        {
            string id = (TxtId.Text ?? string.Empty).Trim();
            string displayName = (TxtDisplayName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(displayName))
            {
                MessageBox.Show("Id und Anzeigename müssen ausgefüllt sein.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            id = NormalizeId(id);
            string? description = (TxtDescription.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(description)) description = null;

            var sectionPrompts = new AbentSectionPrompts
            {
                A = TxtPromptA.Text ?? string.Empty,
                Be = TxtPromptBe.Text ?? string.Empty,
                N = TxtPromptN.Text ?? string.Empty,
                T = string.Empty,
                Icd10 = TxtPromptIcd10.Text ?? string.Empty
            };
            var form = new SubjectForm
            {
                Id = id,
                DisplayName = displayName,
                Description = description,
                SectionPrompts = sectionPrompts
            };

            try
            {
                bool idChanged = _editingFormId != null && !string.Equals(_editingFormId, id, StringComparison.OrdinalIgnoreCase);
                if (_editingFormId == null || !_forms.Any(f => string.Equals(f.Id, _editingFormId, StringComparison.OrdinalIgnoreCase)))
                {
                    OutputFormsService.AddForm(form);
                    if (showSuccessMessage) MessageBox.Show("Formular hinzugefügt.", "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (idChanged)
                {
                    OutputFormsService.RemoveForm(_editingFormId);
                    OutputFormsService.AddForm(form);
                    if (showSuccessMessage) MessageBox.Show("Formular unter neuer Id gespeichert.", "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    OutputFormsService.UpdateForm(form);
                    if (showSuccessMessage) MessageBox.Show("Formular gespeichert.", "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                _editingFormId = id;
                TxtId.IsReadOnly = true;
                RefreshFormList();
                int idx = _forms.FindIndex(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) LstForms.SelectedIndex = idx;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async void BtnSaveAndTest_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveForm(showSuccessMessage: false))
                return;

            string transcript = ExampleTranscriptService.GetDefaultTranscript();
            var settings = SettingsService.LoadSettings();
            var form = LstForms.SelectedItem as SubjectForm;
            var options = new RecorderReportOptions
            {
                SelectedLlm = settings.SelectedLlm ?? "Claude",
                OpenAiApiKey = settings.OpenAiApiKey ?? string.Empty,
                GeminiApiKey = settings.GeminiApiKey ?? string.Empty,
                ClaudeApiKey = settings.ClaudeApiKey ?? string.Empty,
                MistralApiKey = settings.MistralApiKey ?? string.Empty,
                Gender = "Männlich",
                IncludeBefund = settings.IncludeBefund,
                IncludeTherapie = settings.IncludeTherapie,
                IncludeIcd10 = settings.SuggestIcd10,
                RecordingMode = "Neupatient",
                FormId = form?.Id
            };

            string? apiKey = options.GetLlmApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show($"Bitte tragen Sie in den Einstellungen einen API-Key für {options.SelectedLlm} ein.", "API-Key fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnSaveAndTest.IsEnabled = false;
            try
            {
                string result = await _llmService.GenerateAbentReportAsync(transcript, options, CancellationToken.None);
                var mainWindow = Owner as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.DisplayReportResult(result);
                    mainWindow.SetSelectedFormId(form?.Id);
                }
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler beim Test", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSaveAndTest.IsEnabled = true;
            }
        }

        private static string NormalizeId(string id)
        {
            id = id.Trim().ToLowerInvariant();
            id = Regex.Replace(id, @"[^a-z0-9_\-]", "_");
            id = Regex.Replace(id, @"_+", "_").Trim('_');
            return string.IsNullOrEmpty(id) ? "formular" : id;
        }

        private void BtnGeneratePrompts_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new GeneratePromptsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                TxtPromptA.Text = dlg.GeneratedA;
                TxtPromptBe.Text = dlg.GeneratedBe;
                TxtPromptN.Text = dlg.GeneratedN;
                TxtPromptIcd10.Text = dlg.GeneratedIcd10;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
