using System;
using System.Collections.Generic;
using System.Linq;
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
        private string _loadedIcd10 = ""; // preserved for backward compat, not used when building prompt
        private readonly LlmService _llmService = new LlmService();
        private readonly string? _initialFormId;

        public OutputFormsWindow() : this(null)
        {
        }

        /// <param name="initialFormId">Formular-ID aus der Sprechstunde; wird beim Öffnen in der Liste selektiert.</param>
        public OutputFormsWindow(string? initialFormId)
        {
            _initialFormId = string.IsNullOrWhiteSpace(initialFormId) ? null : initialFormId.Trim();
            InitializeComponent();
            Loaded += OutputFormsWindow_Loaded;
        }

        private void OutputFormsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshFormList();
            SelectFormInList(_initialFormId);
        }

        private void RefreshFormList()
        {
            _forms = OutputFormsService.GetForms();
            LstForms.ItemsSource = null;
            LstForms.ItemsSource = _forms;
        }

        /// <summary>Wählt das Formular mit der angegebenen Id; sonst den ersten Eintrag. Leere Liste → keine Auswahl.</summary>
        private void SelectFormInList(string? formId)
        {
            if (_forms.Count == 0)
            {
                LstForms.SelectedIndex = -1;
                return;
            }

            if (!string.IsNullOrEmpty(formId))
            {
                int idx = _forms.FindIndex(f => string.Equals(f.Id, formId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    LstForms.SelectedIndex = idx;
                    if (LstForms.SelectedItem != null)
                        LstForms.ScrollIntoView(LstForms.SelectedItem);
                    return;
                }
            }

            LstForms.SelectedIndex = 0;
            if (LstForms.SelectedItem != null)
                LstForms.ScrollIntoView(LstForms.SelectedItem);
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
            TxtDisplayName.Text = form.DisplayName;
            TxtDescription.Text = form.Description ?? string.Empty;
            TxtPromptA.Text = form.SectionPrompts?.A ?? string.Empty;
            TxtPromptBe.Text = form.SectionPrompts?.Be ?? string.Empty;
            TxtPromptN.Text = form.SectionPrompts?.N ?? string.Empty;
            TxtPromptT.Text = form.SectionPrompts?.T ?? string.Empty;
            _loadedIcd10 = form.SectionPrompts?.Icd10 ?? string.Empty;
            BtnRestoreDefault.Visibility = OutputFormsService.IsStandardForm(form.Id) ? Visibility.Visible : Visibility.Collapsed;
            UpdateSectionStatusIndicators(form);
        }

        private void UpdateSectionStatusIndicators(SubjectForm form)
        {
            bool isStandard = OutputFormsService.IsStandardForm(form.Id);
            if (!isStandard)
            {
                TxtStatusA.Text = string.Empty;
                TxtStatusBe.Text = string.Empty;
                TxtStatusT.Text = string.Empty;
                TxtStatusN.Text = string.Empty;
                return;
            }
            var p = form.SectionPrompts;
            SetSectionStatus(TxtStatusA, p?.ACustomized ?? false);
            SetSectionStatus(TxtStatusBe, p?.BeCustomized ?? false);
            SetSectionStatus(TxtStatusT, p?.TCustomized ?? false);
            SetSectionStatus(TxtStatusN, p?.NCustomized ?? false);
        }

        private static void SetSectionStatus(System.Windows.Controls.TextBlock indicator, bool isCustomized)
        {
            if (isCustomized)
            {
                indicator.Text = "Angepasst";
                indicator.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F072AB"));
            }
            else
            {
                indicator.Text = "Standard";
                indicator.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#558FC4"));
            }
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

            string newId = OutputFormsService.SanitizeIdForFile(displayName);
            if (_forms.Any(f => string.Equals(f.Id, newId, StringComparison.OrdinalIgnoreCase)))
                newId += "_" + DateTime.Now.ToString("yyyyMMddHHmmss");
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
                _editingFormId = newId;
                RefreshFormList();
                SelectFormInList(newId);
                PanelFormDetail.Visibility = Visibility.Visible;
                BtnRemove.IsEnabled = true;
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
                LstForms.SelectedIndex = -1;
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
                $"Standardvorlage für \"{TxtDisplayName.Text}\" wiederherstellen? Alle Änderungen an diesem Formular gehen verloren.",
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
                    TxtPromptT.Text = form.SectionPrompts?.T ?? string.Empty;
                    _loadedIcd10 = form.SectionPrompts?.Icd10 ?? string.Empty;
                    UpdateSectionStatusIndicators(form);
                }
                RefreshFormList();
                SelectFormInList(_editingFormId);
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
            string displayName = (TxtDisplayName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(displayName))
            {
                MessageBox.Show("Bitte einen Namen eingeben.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            string id = OutputFormsService.SanitizeIdForFile(displayName);
            string? description = (TxtDescription.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(description)) description = null;

            var sectionPrompts = new AbentSectionPrompts
            {
                A = TxtPromptA.Text ?? string.Empty,
                Be = TxtPromptBe.Text ?? string.Empty,
                N = TxtPromptN.Text ?? string.Empty,
                T = TxtPromptT.Text ?? string.Empty,
                Icd10 = _loadedIcd10,
                PromptVersion = OutputFormsService.CurrentPromptVersion
            };

            var defaults = OutputFormsService.GetDefaultSectionPrompts(id);
            if (defaults != null)
            {
                sectionPrompts.ACustomized = !string.Equals(sectionPrompts.A.Trim(), defaults.A.Trim(), StringComparison.Ordinal);
                sectionPrompts.BeCustomized = !string.Equals(sectionPrompts.Be.Trim(), defaults.Be.Trim(), StringComparison.Ordinal);
                sectionPrompts.TCustomized = !string.Equals(sectionPrompts.T.Trim(), defaults.T.Trim(), StringComparison.Ordinal);
                sectionPrompts.NCustomized = !string.Equals(sectionPrompts.N.Trim(), defaults.N.Trim(), StringComparison.Ordinal);
            }
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
                string successMessage;
                if (_editingFormId == null || !_forms.Any(f => string.Equals(f.Id, _editingFormId, StringComparison.OrdinalIgnoreCase)))
                {
                    OutputFormsService.AddForm(form);
                    successMessage = "Formular hinzugefügt.";
                }
                else if (idChanged)
                {
                    OutputFormsService.RemoveForm(_editingFormId);
                    OutputFormsService.AddForm(form);
                    successMessage = "Formular umbenannt und gespeichert.";
                }
                else
                {
                    OutputFormsService.UpdateForm(form);
                    successMessage = "Formular gespeichert.";
                }

                _editingFormId = id;
                RefreshFormList();
                SelectFormInList(id);

                if (showSuccessMessage)
                    MessageBox.Show(successMessage, "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
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
                IncludeBefund = !string.IsNullOrWhiteSpace(TxtPromptBe.Text),
                IncludeDiagnosen = !string.IsNullOrWhiteSpace(TxtPromptN.Text),
                IncludeTherapie = !string.IsNullOrWhiteSpace(TxtPromptT.Text),
                IncludeIcd10 = true,
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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
