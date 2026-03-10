using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ABeNT.Model;

namespace ABeNT.Services
{
    /// <summary>
    /// Diktat-Service: Aufnahme → STT → optionale LLM-Formatierung.
    /// Verwendet AudioService für Aufnahme, SttServiceFactory für Transkription,
    /// LlmService für Formatierung.
    /// </summary>
    public class DictationService : IDisposable
    {
        private AudioService? _audioService;
        private readonly LlmService _llmService = new LlmService();
        private bool _isRecording;
        private bool _disposed;

        public bool IsRecording => _isRecording;

        public event Action<float>? OnAudioLevelChanged;

        /// <summary>Startet Monitoring und Aufnahme auf dem gespeicherten Mikrofon.</summary>
        public void StartRecording()
        {
            if (_isRecording)
                throw new InvalidOperationException("Diktat läuft bereits.");

            _audioService?.Dispose();
            _audioService = new AudioService();
            _audioService.OnAudioLevelChanged += level => OnAudioLevelChanged?.Invoke(level);

            var settings = SettingsService.LoadSettings();
            var devices = _audioService.GetInputDevices();
            if (devices.Count == 0)
                throw new InvalidOperationException("Kein Mikrofon gefunden.");

            var savedName = (settings.SelectedMicrophoneDeviceName ?? string.Empty).Trim();
            int index = string.IsNullOrEmpty(savedName) ? 0 : devices.IndexOf(savedName);
            if (index < 0) index = 0;

            _audioService.SelectedDeviceIndex = index;

            _audioService.StartMonitoring();
            _audioService.BeginRecordingToFile();
            _isRecording = true;
        }

        /// <summary>
        /// Stoppt Aufnahme, transkribiert per STT, formatiert per LLM und gibt den fertigen Text zurück.
        /// </summary>
        /// <param name="sectionName">Name der Sektion (z.B. "Therapie") für den LLM-Formatierungsprompt.</param>
        /// <param name="sectionPrompt">Der Fachmodul-Prompt der Sektion (z.B. Therapie-Prompt). Wenn leer, wird rohes Transkript zurückgegeben.</param>
        /// <param name="cancellationToken">Abbruch-Token.</param>
        public async Task<string> StopAndTranscribeAsync(string sectionName, string sectionPrompt, CancellationToken cancellationToken = default)
        {
            if (!_isRecording || _audioService == null)
                throw new InvalidOperationException("Keine aktive Aufnahme.");

            _isRecording = false;
            string? wavPath = null;

            try
            {
                wavPath = await _audioService.FinishRecordingFromFileAsync();
                _audioService.StopMonitoring();

                if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
                    return string.Empty;

                var fi = new FileInfo(wavPath);
                if (fi.Length < 1024)
                    return string.Empty;

                var settings = SettingsService.LoadSettings();
                var options = BuildSttOptions(settings);

                using var sttService = SttServiceFactory.Create(options.SelectedSttProvider);
                var segments = await sttService.TranscribeAudioAsync(wavPath, options);

                if (segments == null || segments.Count == 0)
                    return string.Empty;

                string rawTranscript = string.Join(" ", segments.Select(s => s.Text));

                if (string.IsNullOrWhiteSpace(sectionPrompt))
                    return rawTranscript;

                cancellationToken.ThrowIfCancellationRequested();

                string systemPrompt = OutputFormsService.BuildDictationPrompt(sectionPrompt);
                string userMessage = $"Diktat:\n\n{rawTranscript}";

                string apiKey = options.GetLlmApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                    return rawTranscript;

                string formatted = await _llmService.GenerateRawAsync(
                    systemPrompt, userMessage, settings.SelectedLlm ?? "Claude", apiKey, cancellationToken);

                return string.IsNullOrWhiteSpace(formatted) ? rawTranscript : formatted.Trim();
            }
            finally
            {
                if (!string.IsNullOrEmpty(wavPath))
                {
                    try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
                }
            }
        }

        /// <summary>Bricht eine laufende Aufnahme ab ohne Transkription.</summary>
        public async Task CancelAsync()
        {
            if (!_isRecording || _audioService == null) return;
            _isRecording = false;
            try
            {
                string? wavPath = await _audioService.FinishRecordingFromFileAsync();
                _audioService.StopMonitoring();
                if (!string.IsNullOrEmpty(wavPath))
                {
                    try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
                }
            }
            catch { }
        }

        private static RecorderReportOptions BuildSttOptions(AppSettings settings)
        {
            return new RecorderReportOptions
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
                MistralApiKey = settings.MistralApiKey ?? string.Empty
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _audioService?.Dispose();
            _llmService?.Dispose();
        }
    }
}
