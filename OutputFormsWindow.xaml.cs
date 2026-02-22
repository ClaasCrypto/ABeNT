using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            TxtPromptT.Text = form.SectionPrompts?.T ?? string.Empty;
            BtnRestoreDefault.Visibility = OutputFormsService.IsStandardForm(form.Id) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            string newId = "formular_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var newForm = new SubjectForm
            {
                Id = newId,
                DisplayName = "Neues Formular",
                Description = string.Empty,
                SectionPrompts = new AbentSectionPrompts()
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
                TxtId.IsReadOnly = false; // Bei Neu darf Id noch geändert werden (einmalig)
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
                    TxtPromptT.Text = form.SectionPrompts?.T ?? string.Empty;
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
            string id = (TxtId.Text ?? string.Empty).Trim();
            string displayName = (TxtDisplayName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(displayName))
            {
                MessageBox.Show("Id und Anzeigename müssen ausgefüllt sein.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            id = NormalizeId(id);
            string? description = (TxtDescription.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(description)) description = null;

            var sectionPrompts = new AbentSectionPrompts
            {
                A = TxtPromptA.Text ?? string.Empty,
                Be = TxtPromptBe.Text ?? string.Empty,
                N = TxtPromptN.Text ?? string.Empty,
                T = TxtPromptT.Text ?? string.Empty,
                Icd10 = (LstForms.SelectedItem is SubjectForm current) ? (current.SectionPrompts?.Icd10 ?? string.Empty) : string.Empty
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
                    MessageBox.Show("Formular hinzugefügt.", "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (idChanged)
                {
                    OutputFormsService.RemoveForm(_editingFormId);
                    OutputFormsService.AddForm(form);
                    MessageBox.Show("Formular unter neuer Id gespeichert.", "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    OutputFormsService.UpdateForm(form);
                    MessageBox.Show("Formular gespeichert.", "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                _editingFormId = id;
                TxtId.IsReadOnly = true;
                RefreshFormList();
                int idx = _forms.FindIndex(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) LstForms.SelectedIndex = idx;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string NormalizeId(string id)
        {
            id = id.Trim().ToLowerInvariant();
            id = Regex.Replace(id, @"[^a-z0-9_\-]", "_");
            id = Regex.Replace(id, @"_+", "_").Trim('_');
            return string.IsNullOrEmpty(id) ? "formular" : id;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
