using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ABeNT.Model;
using ABeNT.Services;

namespace ABeNT
{
    public partial class RecorderWindow : Window
    {
        private AudioService? _audioService;
        private ISttService? _sttService;
        private LlmService? _llmService;
        private DispatcherTimer? _recordingTimer;
        private DateTime _recordingStartTime;
        private readonly List<string> _audioSegments = new List<string>();
        private bool _isPaused;
        private CancellationTokenSource? _cancelSource;

        /// <summary>Report options (API keys, gender, form, etc.) set by MainWindow before ShowDialog.</summary>
        public RecorderReportOptions? ReportOptions { get; set; }

        /// <summary>After ShowDialog, if DialogResult == true, contains the generated report text.</summary>
        public string? ResultReportText { get; private set; }

        public RecorderWindow()
        {
            InitializeComponent();
            _audioService = new AudioService();
            _audioService.OnAudioLevelChanged += AudioService_OnAudioLevelChanged;
            _llmService = new LlmService();
            Loaded += RecorderWindow_Loaded;
            Closed += RecorderWindow_Closed;
        }

        private void RecorderWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMicrophones();
            try
            {
                _audioService?.StartMonitoring();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Monitoring start: {ex.Message}");
            }
        }

        private void RecorderWindow_Closed(object? sender, EventArgs e)
        {
            _recordingTimer?.Stop();
            if (_audioService != null && !_audioService.IsRecording)
                _audioService.StopMonitoring();
            SaveCurrentMicrophoneToSettings();
            _audioService?.Dispose();
            _sttService?.Dispose();
            _llmService?.Dispose();
        }

        private void SaveCurrentMicrophoneToSettings()
        {
            if (CmbMicrophones?.SelectedIndex >= 0 && CmbMicrophones.ItemsSource is System.Collections.IList list)
            {
                int idx = CmbMicrophones.SelectedIndex;
                if (idx < list.Count)
                {
                    var settings = SettingsService.LoadSettings();
                    settings.SelectedMicrophoneDeviceName = list[idx]?.ToString() ?? string.Empty;
                    SettingsService.SaveSettings(settings);
                }
            }
        }

        private void LoadMicrophones()
        {
            try
            {
                var devices = _audioService?.GetInputDevices() ?? new List<string>();
                CmbMicrophones.ItemsSource = devices;
                if (devices.Count == 0) return;

                var settings = SettingsService.LoadSettings();
                var savedName = (settings.SelectedMicrophoneDeviceName ?? string.Empty).Trim();
                int index = string.IsNullOrEmpty(savedName) ? -1 : devices.IndexOf(savedName);
                if (index < 0) index = 0;
                CmbMicrophones.SelectedIndex = index;
                _audioService!.SelectedDeviceIndex = index;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load microphones: {ex.Message}");
            }
        }

        private void CmbMicrophones_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbMicrophones.SelectedIndex < 0 || _audioService == null) return;
            if (_audioService.IsRecording)
            {
                CmbMicrophones.SelectionChanged -= CmbMicrophones_SelectionChanged;
                CmbMicrophones.SelectedIndex = _audioService.SelectedDeviceIndex;
                CmbMicrophones.SelectionChanged += CmbMicrophones_SelectionChanged;
                return;
            }
            int idx = CmbMicrophones.SelectedIndex;
            _audioService.SelectedDeviceIndex = idx;
            if (CmbMicrophones.ItemsSource is System.Collections.IList list && idx >= 0 && idx < list.Count)
            {
                var settings = SettingsService.LoadSettings();
                settings.SelectedMicrophoneDeviceName = list[idx]?.ToString() ?? string.Empty;
                SettingsService.SaveSettings(settings);
            }
            if (_audioService.IsMonitoring)
            {
                _audioService.StopMonitoring();
                _audioService.StartMonitoring();
            }
        }

        private void AudioService_OnAudioLevelChanged(float level)
        {
            Dispatcher.Invoke(() => PbAudioLevel.Value = level);
        }

        private void BtnStartNeu_Click(object sender, RoutedEventArgs e)
        {
            if (ReportOptions != null)
                ReportOptions.RecordingMode = "Neupatient";
            StartRecordingFromButton();
        }

        private void BtnStartKontrolle_Click(object sender, RoutedEventArgs e)
        {
            if (ReportOptions != null)
                ReportOptions.RecordingMode = "Kontrolltermin";
            StartRecordingFromButton();
        }

        private void StartRecordingFromButton()
        {
            if (ReportOptions != null)
                ReportOptions.Gender = (CmbGender.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Neutral";
            PanelStartButtons.Visibility = Visibility.Collapsed;
            PanelRecordingButtons.Visibility = Visibility.Visible;
            CmbGender.IsEnabled = false;
            StartOrResumeRecording();
        }

        private void StartOrResumeRecording()
        {
            try
            {
                if (!_audioService?.IsMonitoring ?? true)
                    _audioService?.StartMonitoring();
                _audioService?.BeginRecordingToFile();
                _recordingStartTime = DateTime.Now;
                _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _recordingTimer.Tick += RecordingTimer_Tick;
                _recordingTimer.Start();
                _isPaused = false;
                BtnPauseResume.IsEnabled = true;
                BtnPauseResume.Content = "Pause";
                BtnFinish.IsEnabled = true;
                CmbMicrophones.IsEnabled = false;
                TxtStatus.Text = "Aufnahme läuft...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Starten der Aufnahme: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                CmbMicrophones.IsEnabled = true;
            }
        }

        private void RecordingTimer_Tick(object? sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - _recordingStartTime;
            TxtStatus.Text = $"Aufnahme: {elapsed:mm\\:ss}";
        }

        private async void BtnPauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaused)
            {
                StartOrResumeRecording();
                return;
            }
            try
            {
                _recordingTimer?.Stop();
                _recordingTimer = null;
                string? path = await _audioService!.FinishRecordingFromFileAsync();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    if (fi.Length >= 1024)
                        _audioSegments.Add(path);
                }
                _isPaused = true;
                BtnPauseResume.Content = "Fortsetzen";
                TxtStatus.Text = "Pausiert – Fortsetzen oder Beenden & Auswerten.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Pausieren: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetAfterCancelOrError()
        {
            BtnStop.Visibility = Visibility.Collapsed;
            PanelRecordingButtons.Visibility = Visibility.Collapsed;
            PanelStartButtons.Visibility = Visibility.Visible;
            CmbGender.IsEnabled = true;
            BtnFinish.IsEnabled = true;
            BtnPauseResume.IsEnabled = _audioSegments.Count > 0;
            TxtStatus.Text = "Bereit.";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _cancelSource?.Cancel();
            BtnStop.IsEnabled = false;
        }

        private async void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _recordingTimer?.Stop();
                _recordingTimer = null;
                BtnPauseResume.IsEnabled = false;
                BtnFinish.IsEnabled = false;
                CmbMicrophones.IsEnabled = true;

                if (_audioService != null && _audioService.IsRecording)
                {
                    string? path = await _audioService.FinishRecordingFromFileAsync();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var fi = new FileInfo(path);
                        if (fi.Length >= 1024)
                            _audioSegments.Add(path);
                    }
                }

                if (_audioSegments.Count == 0)
                {
                    MessageBox.Show("Keine Aufnahme vorhanden. Bitte zuerst aufnehmen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                    PanelRecordingButtons.Visibility = Visibility.Collapsed;
                    PanelStartButtons.Visibility = Visibility.Visible;
                    TxtStatus.Text = "Bereit – Aufnahme starten.";
                    return;
                }

                var options = ReportOptions;
                if (options == null)
                {
                    MessageBox.Show("Einstellungen fehlen.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ResetAfterCancelOrError();
                    return;
                }

                string? sttError = ValidateSttSettings(options);
                if (sttError != null)
                {
                    MessageBox.Show(sttError, "STT-Einstellung fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ResetAfterCancelOrError();
                    return;
                }

                if (string.IsNullOrWhiteSpace(options.GetLlmApiKey()))
                {
                    MessageBox.Show($"Bitte API Key für {options.SelectedLlm} in den Einstellungen eintragen.", "API Key fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ResetAfterCancelOrError();
                    return;
                }

                _sttService?.Dispose();
                _sttService = SttServiceFactory.Create(options.SelectedSttProvider);

                _cancelSource = new CancellationTokenSource();
                var ct = _cancelSource.Token;
                BtnStop.Visibility = Visibility.Visible;
                BtnStop.IsEnabled = true;

                TxtStatus.Text = "Transkription...";
                var allSegments = new List<TranscriptSegment>();
                foreach (string filePath in _audioSegments)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var segments = await _sttService.TranscribeAudioAsync(filePath, options);
                        allSegments.AddRange(segments);
                    }
                    catch
                    {
                    }
                }

                if (allSegments.Count == 0)
                {
                    ResultReportText = "Keine Transkription erhalten (alle Segmente übersprungen oder fehlgeschlagen).";
                    CleanupWavFiles();
                    DialogResult = true;
                    Close();
                    return;
                }

                string rawTranscript = string.Join("\n\n", allSegments.Select(s => $"{s.Speaker}: {s.Text}"));

                TxtStatus.Text = "Generiere ABeNT-Dokumentation...";
                string abentText;
                try
                {
                    abentText = await _llmService!.GenerateAbentReportAsync(rawTranscript, options, ct);
                }
                catch (OperationCanceledException)
                {
                    ResetAfterCancelOrError();
                    return;
                }

                ResultReportText = abentText;
                CleanupWavFiles();
                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                ResetAfterCancelOrError();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetAfterCancelOrError();
            }
        }

        private void CleanupWavFiles()
        {
            foreach (string path in _audioSegments)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch { /* ignore */ }
            }
        }

        private static string? ValidateSttSettings(RecorderReportOptions opts)
        {
            return opts.SelectedSttProvider switch
            {
                "Azure" when string.IsNullOrWhiteSpace(opts.AzureSpeechKey)
                    => "Bitte Azure Speech Key in den Einstellungen eintragen.",
                "Azure" when string.IsNullOrWhiteSpace(opts.AzureSpeechRegion)
                    => "Bitte Azure Region in den Einstellungen eintragen.",
                "Custom" when string.IsNullOrWhiteSpace(opts.CustomSttEndpoint)
                    => "Bitte Custom STT Endpoint in den Einstellungen eintragen.",
                "Deepgram" when string.IsNullOrWhiteSpace(opts.DeepgramApiKey)
                    => "Bitte Deepgram API Key in den Einstellungen eintragen.",
                _ => null
            };
        }
    }
}
