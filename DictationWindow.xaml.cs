using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ABeNT.Services;

namespace ABeNT
{
    public partial class DictationWindow : Window
    {
        private AudioService? _audioService;
        private DictationService? _dictationService;
        private DispatcherTimer? _recordingTimer;
        private DispatcherTimer? _deviceCheckTimer;
        private List<string>? _lastDeviceList;
        private DateTime _recordingStartTime;
        private System.Threading.CancellationTokenSource? _cts;

        /// <summary>Sektion: "A", "Be", "T", "N".</summary>
        public string Section { get; set; } = "A";

        /// <summary>Formular-Id für Diktat-Prompt-Routing.</summary>
        public string? FormId { get; set; }

        /// <summary>Nach ShowDialog: formatierter Diktattext.</summary>
        public string? ResultText { get; private set; }

        /// <summary>True = Karteninhalt ersetzen, False = anfügen.</summary>
        public bool ShouldReplace { get; private set; }

        public DictationWindow()
        {
            InitializeComponent();
            Loaded += DictationWindow_Loaded;
            Closed += DictationWindow_Closed;
        }

        private void DictationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Title = $"Diktat – {GetSectionDisplayName(Section)}";
            TxtSectionTitle.Text = $"{GetSectionDisplayName(Section)} diktieren";

            _audioService = new AudioService();
            _audioService.OnAudioLevelChanged += level =>
                Dispatcher.BeginInvoke(new Action(() => PbAudioLevel.Value = level), DispatcherPriority.Background);

            LoadMicrophones();
            _lastDeviceList = (CmbMicrophones.ItemsSource as System.Collections.IList)?.Cast<string>().ToList();
            TryStartMonitoringAfterLoad();
            StartDeviceCheckTimer();
        }

        private void TryStartMonitoringAfterLoad()
        {
            try
            {
                if (_audioService != null && !_audioService.IsMonitoring
                    && CmbMicrophones?.ItemsSource is System.Collections.IList list && list.Count > 0)
                    _audioService.StartMonitoring();
            }
            catch { /* Monitoring-Fehler nicht blockierend */ }
        }

        private void StartDeviceCheckTimer()
        {
            _deviceCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _deviceCheckTimer.Tick += DeviceCheckTimer_Tick;
            _deviceCheckTimer.Start();
        }

        private void DeviceCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (_audioService == null) return;
            var current = _audioService.GetInputDevices();
            if (!DeviceListChanged(current, _lastDeviceList)) return;
            _lastDeviceList = new List<string>(current);
            LoadMicrophones();
            TryStartMonitoringAfterLoad();
            SaveCurrentMicrophoneToSettings();
        }

        private static bool DeviceListChanged(System.Collections.Generic.IList<string>? current, System.Collections.Generic.IList<string>? last)
        {
            if (current == null && last == null) return false;
            if (current == null || last == null) return true;
            if (current.Count != last.Count) return true;
            return !current.SequenceEqual(last);
        }

        private void DictationWindow_Closed(object? sender, EventArgs e)
        {
            if (_deviceCheckTimer != null)
            {
                _deviceCheckTimer.Tick -= DeviceCheckTimer_Tick;
                _deviceCheckTimer.Stop();
                _deviceCheckTimer = null;
            }
            _recordingTimer?.Stop();
            _recordingTimer = null;
            _cts?.Cancel();
            if (_audioService != null && !_audioService.IsRecording)
                _audioService.StopMonitoring();
            SaveCurrentMicrophoneToSettings();
            _audioService?.Dispose();
            _dictationService?.Dispose();
        }

        private void LoadMicrophones()
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

        private void CmbMicrophones_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbMicrophones.SelectedIndex < 0 || _audioService == null) return;
            if (_audioService.IsRecording)
            {
                CmbMicrophones.SelectionChanged -= CmbMicrophones_SelectionChanged;
                CmbMicrophones.SelectedIndex = _audioService.SelectedDeviceIndex;
                CmbMicrophones.SelectionChanged += CmbMicrophones_SelectionChanged;
                return;
            }
            _audioService.SelectedDeviceIndex = CmbMicrophones.SelectedIndex;
            SaveCurrentMicrophoneToSettings();
            if (_audioService.IsMonitoring)
            {
                _audioService.StopMonitoring();
                _audioService.StartMonitoring();
            }
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

        private async void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_dictationService?.IsRecording == true)
            {
                await StopAndProcessAsync();
                return;
            }
            StartRecording();
        }

        private void StartRecording()
        {
            try
            {
                _dictationService?.Dispose();
                _dictationService = new DictationService();
                _cts = new System.Threading.CancellationTokenSource();

                _dictationService.StartRecording();

                BtnRecord.Content = "\u25A0 Aufnahme stoppen";
                CmbMicrophones.IsEnabled = false;
                TxtResult.Text = string.Empty;
                HideResultButtons();

                _recordingStartTime = DateTime.Now;
                _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _recordingTimer.Tick += (_, _) =>
                {
                    var elapsed = DateTime.Now - _recordingStartTime;
                    TxtStatus.Text = $"\u25CF Aufnahme: {elapsed:mm\\:ss}";
                };
                _recordingTimer.Start();
                TxtStatus.Text = "\u25CF Aufnahme: 00:00";
            }
            catch (Exception ex)
            {
                ResetToReady();
                MessageBox.Show($"Aufnahme konnte nicht gestartet werden: {ex.Message}",
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task StopAndProcessAsync()
        {
            if (_dictationService == null) return;

            _recordingTimer?.Stop();
            _recordingTimer = null;
            BtnRecord.IsEnabled = false;
            TxtStatus.Text = "Transkription + Formatierung...";

            try
            {
                string sectionPrompt = OutputFormsService.GetDictationSectionPrompt(FormId, Section);
                string result = await _dictationService.StopAndTranscribeAsync(
                    Section, sectionPrompt, _cts?.Token ?? default);

                if (!string.IsNullOrWhiteSpace(result))
                {
                    TxtResult.Text = result;
                    TxtStatus.Text = "Diktat fertig – Übernehmen oder Anfügen.";
                    ShowResultButtons();
                }
                else
                {
                    TxtStatus.Text = "Kein Text erkannt. Erneut versuchen.";
                    ResetToReady();
                }
            }
            catch (OperationCanceledException)
            {
                ResetToReady();
            }
            catch (Exception ex)
            {
                ResetToReady();
                MessageBox.Show($"Fehler bei der Diktat-Verarbeitung: {ex.Message}",
                    "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNewDictation_Click(object sender, RoutedEventArgs e)
        {
            TxtResult.Text = string.Empty;
            HideResultButtons();
            ResetToReady();
        }

        private void BtnReplace_Click(object sender, RoutedEventArgs e)
        {
            ResultText = TxtResult.Text?.Trim();
            ShouldReplace = true;
            DialogResult = true;
            Close();
        }

        private void BtnAppend_Click(object sender, RoutedEventArgs e)
        {
            ResultText = TxtResult.Text?.Trim();
            ShouldReplace = false;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }

        private void ShowResultButtons()
        {
            BtnReplace.Visibility = Visibility.Visible;
            BtnAppend.Visibility = Visibility.Visible;
            BtnNewDictation.Visibility = Visibility.Visible;
            BtnRecord.Content = "● Aufnahme starten";
            BtnRecord.IsEnabled = false;
            CmbMicrophones.IsEnabled = true;
        }

        private void HideResultButtons()
        {
            BtnReplace.Visibility = Visibility.Collapsed;
            BtnAppend.Visibility = Visibility.Collapsed;
            BtnNewDictation.Visibility = Visibility.Collapsed;
        }

        private void ResetToReady()
        {
            BtnRecord.Content = "● Aufnahme starten";
            BtnRecord.IsEnabled = true;
            CmbMicrophones.IsEnabled = true;
            TxtStatus.Text = "Bereit – Aufnahme starten.";
        }

        private static string GetSectionDisplayName(string section) => section switch
        {
            "A" => "Anamnese",
            "Be" => "Befund",
            "T" => "Therapie",
            "N" => "Diagnosen",
            _ => "Diktat"
        };
    }
}
