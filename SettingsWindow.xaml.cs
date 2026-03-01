using System;
using System.Windows;
using System.Windows.Controls;
using ABeNT.Services;

namespace ABeNT
{
    public partial class SettingsWindow : Window
    {
        private string _currentLlm = "Claude";
        private string _openAiKey = "";
        private string _geminiKey = "";
        private string _claudeKey = "";
        private string _currentSttProvider = "Deepgram";

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                AppSettings? settings = SettingsService.LoadSettings();
                if (settings == null)
                    settings = new AppSettings();

                _openAiKey = settings.OpenAiApiKey ?? "";
                _geminiKey = settings.GeminiApiKey ?? "";
                _claudeKey = settings.ClaudeApiKey ?? "";
                _currentLlm = settings.SelectedLlm ?? "Claude";
                _currentSttProvider = settings.SelectedSttProvider ?? "Deepgram";

                TxtDeepgramKey.Text = settings.DeepgramApiKey ?? "";
                TxtAzureKey.Text = settings.AzureSpeechKey ?? "";
                TxtAzureRegion.Text = string.IsNullOrWhiteSpace(settings.AzureSpeechRegion) ? "westeurope" : settings.AzureSpeechRegion;
                TxtCustomEndpoint.Text = settings.CustomSttEndpoint ?? "";
                TxtCustomApiKey.Text = settings.CustomSttApiKey ?? "";

                ApplyLlmKeyToTextBox();
                UpdateLlmKeyLabel();

                CmbSttProvider.SelectionChanged -= CmbSttProvider_SelectionChanged;
                CmbSttProvider.SelectedIndex = _currentSttProvider switch
                {
                    "Azure" => 1,
                    "Custom" => 2,
                    _ => 0
                };
                UpdateSttPanels();
                CmbSttProvider.SelectionChanged += CmbSttProvider_SelectionChanged;

                if (CmbLlm != null && CmbLlm.Items.Count >= 3)
                {
                    CmbLlm.SelectionChanged -= CmbLlm_SelectionChanged;
                    CmbLlm.SelectedIndex = _currentLlm switch
                    {
                        "ChatGPT" => 0,
                        "Gemini" => 1,
                        _ => 2
                    };
                    CmbLlm.SelectionChanged += CmbLlm_SelectionChanged;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Einstellungen konnten nicht geladen werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CmbSttProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSttProvider == null || CmbSttProvider.SelectedIndex < 0) return;
            _currentSttProvider = CmbSttProvider.SelectedIndex switch
            {
                1 => "Azure",
                2 => "Custom",
                _ => "Deepgram"
            };
            UpdateSttPanels();
        }

        private void UpdateSttPanels()
        {
            PanelDeepgram.Visibility = _currentSttProvider == "Deepgram" ? Visibility.Visible : Visibility.Collapsed;
            PanelAzure.Visibility = _currentSttProvider == "Azure" ? Visibility.Visible : Visibility.Collapsed;
            PanelCustom.Visibility = _currentSttProvider == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyLlmKeyToTextBox()
        {
            if (TxtLlmKey == null) return;
            TxtLlmKey.Text = _currentLlm switch
            {
                "ChatGPT" => _openAiKey,
                "Gemini" => _geminiKey,
                _ => _claudeKey
            };
        }

        private void UpdateLlmKeyLabel()
        {
            if (TxtLlmKeyLabel == null) return;
            TxtLlmKeyLabel.Text = _currentLlm switch
            {
                "ChatGPT" => "OpenAI API Key:",
                "Gemini" => "Google Gemini API Key:",
                _ => "Anthropic Claude API Key:"
            };
        }

        private void StoreCurrentKeyFromTextBox()
        {
            string text = TxtLlmKey?.Text ?? "";
            switch (_currentLlm)
            {
                case "ChatGPT":
                    _openAiKey = text;
                    break;
                case "Gemini":
                    _geminiKey = text;
                    break;
                default:
                    _claudeKey = text;
                    break;
            }
        }

        private void CmbLlm_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLlm == null || CmbLlm.SelectedIndex < 0 || CmbLlm.SelectedIndex > 2) return;
            StoreCurrentKeyFromTextBox();
            _currentLlm = CmbLlm.SelectedIndex switch
            {
                0 => "ChatGPT",
                1 => "Gemini",
                _ => "Claude"
            };
            ApplyLlmKeyToTextBox();
            UpdateLlmKeyLabel();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StoreCurrentKeyFromTextBox();
                var settings = SettingsService.LoadSettings();
                settings.SelectedSttProvider = _currentSttProvider;
                settings.DeepgramApiKey = TxtDeepgramKey?.Text ?? "";
                settings.AzureSpeechKey = TxtAzureKey?.Text ?? "";
                settings.AzureSpeechRegion = TxtAzureRegion?.Text ?? "westeurope";
                settings.CustomSttEndpoint = TxtCustomEndpoint?.Text ?? "";
                settings.CustomSttApiKey = TxtCustomApiKey?.Text ?? "";
                settings.SelectedLlm = _currentLlm;
                settings.OpenAiApiKey = _openAiKey;
                settings.GeminiApiKey = _geminiKey;
                settings.ClaudeApiKey = _claudeKey;
                SettingsService.SaveSettings(settings);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
