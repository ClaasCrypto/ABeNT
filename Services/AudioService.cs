using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ABeNT.Services
{
    public class AudioService
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private string? _outputFilePath;
        private bool _isRecording;
        private int _selectedDeviceIndex = 0; // Standard: Erstes Mikrofon
        private DateTime _recordingStartTime;
        private long _bytesRecorded = 0;
        
        // AGC (Automatic Gain Control) – normalisiert Lautstärke unabhängig vom Mikrofon
        private float _currentGain = 1.0f;
        private const float AgcTargetRms = 0.08f;
        private const float AgcMaxGain = 15.0f;
        private const float AgcMinGain = 0.1f;
        private const float AgcAttackCoeff = 0.05f;
        private const float AgcReleaseCoeff = 0.002f;
        private const float AgcNoiseGate = 0.002f;

        // Erzwungenes Format: 16kHz, 16-bit, Mono (Industriestandard für STT)
        private static readonly WaveFormat RecordingFormat = new WaveFormat(16000, 16, 1);

        public event Action<float>? OnAudioLevelChanged;

        public bool IsMonitoring => _waveIn != null;
        public bool IsRecording => _isRecording;

        public List<string> GetInputDevices()
        {
            var devices = new List<string>();
            int deviceCount = WaveInEvent.DeviceCount;
            
            for (int i = 0; i < deviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                devices.Add(capabilities.ProductName);
            }
            
            return devices;
        }

        public int SelectedDeviceIndex
        {
            get => _selectedDeviceIndex;
            set
            {
                if (_selectedDeviceIndex != value)
                {
                    bool wasMonitoring = IsMonitoring;
                    if (wasMonitoring)
                    {
                        StopMonitoring();
                    }
                    _selectedDeviceIndex = value;
                    if (wasMonitoring)
                    {
                        StartMonitoring();
                    }
                }
            }
        }

        public void StartMonitoring()
        {
            if (IsMonitoring)
            {
                return; // Bereits aktiv
            }

            // Initialisiere WaveInEvent OHNE erzwungenes Format
            // NAudio verwendet dann die native Sample-Rate des Mikrofons
            _waveIn = new WaveInEvent
            {
                DeviceNumber = _selectedDeviceIndex
                // WaveFormat wird automatisch vom Gerät übernommen
            };

            // Abonniere DataAvailable Event
            _waveIn.DataAvailable += WaveIn_DataAvailable;

            // Starte Monitoring
            _waveIn.StartRecording();
        }

        public void StopMonitoring()
        {
            if (!IsMonitoring)
            {
                return;
            }

            // Stoppe nur, wenn nicht gerade aufgenommen wird
            if (!_isRecording)
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveIn = null;
            }
        }

        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            // IMMER: Berechne Pegel und feuere Event (für Visualisierung)
            float maxSample = 0f;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                // Konvertiere 16-Bit Sample zu float (-1.0 bis 1.0)
                short sample = BitConverter.ToInt16(e.Buffer, i);
                float normalizedSample = sample / 32768f;
                float absSample = Math.Abs(normalizedSample);
                
                if (absSample > maxSample)
                {
                    maxSample = absSample;
                }
            }

            // Normalisiere auf 0-100 (RMS-ähnlich, aber mit Peak)
            float level = maxSample * 100f;
            
            // Verstärke für bessere Sichtbarkeit im UI (Faktor 10)
            float amplifiedLevel = level * 10f;
            
            // Begrenze auf Maximum 100
            float finalLevel = Math.Min(100f, amplifiedLevel);

            // Feuere Event (wird vom Audio-Thread aufgerufen)
            OnAudioLevelChanged?.Invoke(finalLevel);

            // NUR WENN aufgenommen wird: Schreibe in Datei mit Verstärkung
            if (_isRecording && _writer != null)
            {
                byte[] amplifiedBuffer = ApplyAgc(e.Buffer, e.BytesRecorded);
                _writer.Write(amplifiedBuffer, 0, amplifiedBuffer.Length);
                _bytesRecorded += e.BytesRecorded;

                // Flush alle 5 Sekunden (ca. 16000 samples/sec * 2 bytes * 5 sec = 160000 bytes)
                // Oder einfacher: alle ~80KB (ca. 5 Sekunden bei 16kHz)
                if (_bytesRecorded % 160000 < e.BytesRecorded)
                {
                    _writer.Flush();
                }
            }
        }

        public void BeginRecordingToFile()
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("Aufnahme läuft bereits.");
            }

            if (!IsMonitoring)
            {
                throw new InvalidOperationException("Monitoring muss zuerst gestartet werden.");
            }

            if (_waveIn == null)
            {
                throw new InvalidOperationException("WaveInEvent ist null - Monitoring wurde nicht korrekt gestartet.");
            }

            try
            {
                // Erstelle temporäre WAV-Datei mit eindeutigem Namen
                string tempDir = Path.Combine(Path.GetTempPath(), "ABeNT");
                Directory.CreateDirectory(tempDir);
                
                // Dynamischer Dateiname mit Timestamp
                string fileName = $"Aufnahme_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
                _outputFilePath = Path.Combine(tempDir, fileName);

                // Reset Zähler und AGC
                _bytesRecorded = 0;
                _currentGain = 1.0f;
                _recordingStartTime = DateTime.Now;

                // Erstelle WaveFileWriter mit dem tatsächlichen Format des Mikrofons
                // WICHTIG: Verwende das Format, das das Mikrofon tatsächlich liefert
                var waveFormat = _waveIn.WaveFormat;
                _writer = new WaveFileWriter(_outputFilePath, waveFormat);
                _isRecording = true;
                
                System.Diagnostics.Debug.WriteLine($"Aufnahme gestartet: {_outputFilePath}");
                System.Diagnostics.Debug.WriteLine($"Format: {waveFormat.SampleRate}Hz, {waveFormat.BitsPerSample}-bit, {waveFormat.Channels} Channel(s)");
            }
            catch (Exception ex)
            {
                // Cleanup bei Fehler
                _writer?.Dispose();
                _writer = null;
                _outputFilePath = null;
                _isRecording = false;
                
                System.Diagnostics.Debug.WriteLine($"Fehler beim Starten der Aufnahme: {ex.Message}");
                throw new InvalidOperationException($"Fehler beim Starten der Aufnahme: {ex.Message}", ex);
            }
        }

        public async Task<string?> FinishRecordingFromFileAsync()
        {
            try
            {
                if (!_isRecording)
                {
                    System.Diagnostics.Debug.WriteLine("FinishRecordingFromFileAsync: Keine aktive Aufnahme.");
                    return null;
                }

                string? filePath = _outputFilePath;

                // Prüfe ob Writer existiert
                if (_writer == null)
                {
                    throw new InvalidOperationException("WaveFileWriter ist null - Aufnahme wurde möglicherweise nicht korrekt gestartet.");
                }

                // Setze Recording-Flag zurück
                _isRecording = false;

                // Sauberes Stoppen
                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Fehler beim Schließen des Writers: {ex.Message}");
                    // Weiter mit Dateiprüfung, auch wenn Writer-Fehler auftritt
                }
                finally
                {
                    _writer = null;
                }

                _bytesRecorded = 0;

                // WICHTIG: Warte 500ms, damit das Dateisystem die Datei freigibt
                await Task.Delay(500);

                // Prüfe ob Dateipfad vorhanden ist
                if (string.IsNullOrEmpty(filePath))
                {
                    throw new InvalidOperationException("Dateipfad ist leer - Aufnahme wurde möglicherweise nicht korrekt gestartet.");
                }

                // Prüfe ob Datei existiert
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Audiodatei wurde nicht gefunden: {filePath}");
                }

                // Prüfe Dateigröße
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 1024) // Kleiner als 1KB
                {
                    throw new Exception($"Audiodatei ist zu klein/leer - Aufnahme fehlgeschlagen. Dateigröße: {fileInfo.Length} Bytes. Datei: {filePath}");
                }
                
                // Debug: Zeige Format-Informationen
                if (_waveIn != null)
                {
                    var format = _waveIn.WaveFormat;
                    System.Diagnostics.Debug.WriteLine($"WAV-Format: {format.SampleRate}Hz, {format.BitsPerSample}-bit, {format.Channels} Channel(s)");
                    System.Diagnostics.Debug.WriteLine($"Dateigröße: {fileInfo.Length} Bytes ({fileInfo.Length / 1024} KB)");
                    System.Diagnostics.Debug.WriteLine($"Geschätzte Dauer: {fileInfo.Length / (format.SampleRate * format.BitsPerSample / 8 * format.Channels)} Sekunden");
                }

                // Setze _outputFilePath erst NACH erfolgreicher Prüfung zurück
                _outputFilePath = null;

                return filePath;
            }
            catch (Exception)
            {
                // Stelle sicher, dass Flags zurückgesetzt werden, auch bei Fehler
                _isRecording = false;
                _writer?.Dispose();
                _writer = null;
                _outputFilePath = null;
                _bytesRecorded = 0;
                
                // Re-throw für Behandlung in MainWindow
                throw;
            }
        }

        private float CalculateRms(byte[] buffer, int bytesRecorded)
        {
            double sumOfSquares = 0;
            int sampleCount = bytesRecorded / 2;
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                float normalized = sample / 32768f;
                sumOfSquares += normalized * normalized;
            }
            return (float)Math.Sqrt(sumOfSquares / Math.Max(1, sampleCount));
        }

        private byte[] ApplyAgc(byte[] buffer, int bytesRecorded)
        {
            float rms = CalculateRms(buffer, bytesRecorded);

            if (rms > AgcNoiseGate)
            {
                float desiredGain = AgcTargetRms / rms;
                desiredGain = Math.Clamp(desiredGain, AgcMinGain, AgcMaxGain);

                float coeff = desiredGain < _currentGain ? AgcAttackCoeff : AgcReleaseCoeff;
                _currentGain += coeff * (desiredGain - _currentGain);
            }

            byte[] result = new byte[bytesRecorded];
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                float amplified = sample * _currentGain;
                amplified = Math.Clamp(amplified, -32768f, 32767f);
                BitConverter.GetBytes((short)amplified).CopyTo(result, i);
            }

            return result;
        }

        public void Dispose()
        {
            if (_isRecording)
            {
                // Synchron beenden (für Dispose)
                _isRecording = false;
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
            StopMonitoring();
        }
    }
}
