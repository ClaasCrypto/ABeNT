using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ABeNT.Services;

namespace ABeNT
{
    public partial class SettingsWindow : Window
    {
        private string _currentLlm = "Claude";
        private string _openAiKey = "";
        private string _geminiKey = "";
        private string _claudeKey = "";
        private string _mistralKey = "";
        private string _currentSttProvider = "Deepgram";
        private string? _pendingDownloadUrl;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            TxtCurrentVersion.Text = $"v{UpdateService.GetCurrentVersion().ToString(3)}";
            LoadSettings();
        }

        private void LnkReadme_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string readmePath = Path.GetFullPath(Path.Combine(baseDir, "README.md"));
            if (!File.Exists(readmePath))
                readmePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "README.md"));
            if (File.Exists(readmePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = readmePath, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"README konnte nicht geöffnet werden: {ex.Message}", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("README.md wurde im Anwendungsordner nicht gefunden. Bitte im Projektordner nachsehen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
                _mistralKey = settings.MistralApiKey ?? "";
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

                if (CmbLlm != null && CmbLlm.Items.Count >= 4)
                {
                    CmbLlm.SelectionChanged -= CmbLlm_SelectionChanged;
                    CmbLlm.SelectedIndex = _currentLlm switch
                    {
                        "ChatGPT" => 0,
                        "Gemini" => 1,
                        "Mistral" => 3,
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
                "Mistral" => _mistralKey,
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
                "Mistral" => "Mistral API Key:",
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
                case "Mistral":
                    _mistralKey = text;
                    break;
                default:
                    _claudeKey = text;
                    break;
            }
        }

        private void CmbLlm_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLlm == null || CmbLlm.SelectedIndex < 0 || CmbLlm.SelectedIndex > 3) return;
            StoreCurrentKeyFromTextBox();
            _currentLlm = CmbLlm.SelectedIndex switch
            {
                0 => "ChatGPT",
                1 => "Gemini",
                3 => "Mistral",
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
                settings.AzureSpeechKey = (TxtAzureKey?.Text ?? "").Trim();
                settings.AzureSpeechRegion = (TxtAzureRegion?.Text ?? "westeurope").Trim();
                settings.CustomSttEndpoint = TxtCustomEndpoint?.Text ?? "";
                settings.CustomSttApiKey = TxtCustomApiKey?.Text ?? "";
                settings.SelectedLlm = _currentLlm;
                settings.OpenAiApiKey = _openAiKey;
                settings.GeminiApiKey = _geminiKey;
                settings.ClaudeApiKey = _claudeKey;
                settings.MistralApiKey = _mistralKey;
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

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Text = "Prüfe...";
            PanelUpdateAvailable.Visibility = Visibility.Collapsed;

            try
            {
                var result = await UpdateService.CheckForUpdateAsync();
                if (result == null)
                {
                    TxtUpdateStatus.Text = "Server nicht erreichbar.";
                    return;
                }

                var (available, tagName, downloadUrl, body) = result.Value;
                if (!available)
                {
                    TxtUpdateStatus.Text = "Aktuelle Version ist auf dem neuesten Stand.";
                    return;
                }

                TxtUpdateStatus.Text = "";
                _pendingDownloadUrl = downloadUrl;
                TxtUpdateInfo.Text = $"Neue Version verfügbar: {tagName}";
                PanelUpdateAvailable.Visibility = Visibility.Visible;

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    BtnInstallUpdate.IsEnabled = false;
                    BtnInstallUpdate.Content = "Kein Download verfügbar";
                }
            }
            catch (Exception ex)
            {
                TxtUpdateStatus.Text = $"Fehler: {ex.Message}";
            }
            finally
            {
                BtnCheckUpdate.IsEnabled = true;
            }
        }

        private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_pendingDownloadUrl)) return;

            BtnInstallUpdate.IsEnabled = false;
            BtnInstallUpdate.Content = "Download läuft...";
            ProgressUpdate.Visibility = Visibility.Visible;
            ProgressUpdate.Value = 0;

            try
            {
                await UpdateService.DownloadAndInstallAsync(_pendingDownloadUrl, progress =>
                {
                    Dispatcher.Invoke(() => ProgressUpdate.Value = progress);
                });

                MessageBox.Show(
                    "Update heruntergeladen. Die App wird jetzt neu gestartet.",
                    "Update", MessageBoxButton.OK, MessageBoxImage.Information);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnInstallUpdate.IsEnabled = true;
                BtnInstallUpdate.Content = "Update herunterladen und installieren";
                ProgressUpdate.Visibility = Visibility.Collapsed;
            }
        }
    }
}
