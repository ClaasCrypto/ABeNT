using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ABeNT.Services
{
    public class AudioService : IDisposable
    {
        private WasapiCapture? _capture;
        private WaveFileWriter? _writer;
        private string? _outputFilePath;
        private bool _isRecording;
        private int _selectedDeviceIndex = 0;
        private DateTime _recordingStartTime;
        private long _bytesRecorded = 0;
        private bool _isFloat;
        private int _bytesPerSample;

        private List<MMDevice> _mmDevices = new List<MMDevice>();

        // AGC (Automatic Gain Control)
        private float _currentGain = 1.0f;
        private const float AgcTargetRms = 0.1f;
        private const float AgcMaxGain = 50.0f;
        private const float AgcMinGain = 0.1f;
        private const float AgcAttackCoeff = 0.05f;
        private const float AgcReleaseCoeff = 0.005f;
        private const float AgcNoiseGate = 0.001f;

        public event Action<float>? OnAudioLevelChanged;

        public bool IsMonitoring => _capture != null;
        public bool IsRecording => _isRecording;

        public List<string> GetInputDevices()
        {
            _mmDevices.Clear();
            var names = new List<string>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var device in endpoints)
                {
                    _mmDevices.Add(device);
                    names.Add(device.FriendlyName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WASAPI enumerate: {ex.Message}");
            }


            return names;
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
            if (IsMonitoring) return;

            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _mmDevices.Count)
                throw new InvalidOperationException("Kein gültiges Mikrofon ausgewählt.");

            var device = _mmDevices[_selectedDeviceIndex];
            _capture = new WasapiCapture(device);
            _capture.DataAvailable += Capture_DataAvailable;

            var fmt = _capture.WaveFormat;
            _isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;
            _bytesPerSample = fmt.BitsPerSample / 8;

            _capture.StartRecording();
        }

        public void StopMonitoring()
        {
            if (!IsMonitoring) return;
            if (!_isRecording)
            {
                _capture?.StopRecording();
                _capture?.Dispose();
                _capture = null;
            }
        }

        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            int step = _bytesPerSample * (_capture?.WaveFormat.Channels ?? 1);
            if (step == 0) return;

            float maxSample = 0f;
            for (int i = 0; i + _bytesPerSample <= e.BytesRecorded; i += step)
            {
                float sample = ReadSample(e.Buffer, i);
                float absSample = Math.Abs(sample);
                if (absSample > maxSample) maxSample = absSample;
            }

            float finalLevel = Math.Min(100f, maxSample * 1000f);
            OnAudioLevelChanged?.Invoke(finalLevel);

            if (_isRecording && _writer != null)
            {
                byte[] processed = ApplyAgc(e.Buffer, e.BytesRecorded);
                _writer.Write(processed, 0, processed.Length);
                _bytesRecorded += processed.Length;

                if (_bytesRecorded % 160000 < processed.Length)
                {
                    _writer.Flush();
                }
            }
        }

        private float ReadSample(byte[] buffer, int offset)
        {
            if (_isFloat)
                return BitConverter.ToSingle(buffer, offset);
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        }

        public void BeginRecordingToFile()
        {
            if (_isRecording)
                throw new InvalidOperationException("Aufnahme läuft bereits.");
            if (!IsMonitoring || _capture == null)
                throw new InvalidOperationException("Monitoring muss zuerst gestartet werden.");

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ABeNT");
                Directory.CreateDirectory(tempDir);
                string fileName = $"Aufnahme_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
                _outputFilePath = Path.Combine(tempDir, fileName);

                _bytesRecorded = 0;
                _currentGain = 1.0f;
                _recordingStartTime = DateTime.Now;

                var waveFormat = _capture.WaveFormat;
                _writer = new WaveFileWriter(_outputFilePath, waveFormat);
                _isRecording = true;

                System.Diagnostics.Debug.WriteLine($"Aufnahme gestartet: {_outputFilePath}");
                System.Diagnostics.Debug.WriteLine($"Format: {waveFormat.SampleRate}Hz, {waveFormat.BitsPerSample}-bit, {waveFormat.Channels} Channel(s), Encoding={waveFormat.Encoding}");
            }
            catch (Exception ex)
            {
                _writer?.Dispose();
                _writer = null;
                _outputFilePath = null;
                _isRecording = false;
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

                if (_writer == null)
                    throw new InvalidOperationException("WaveFileWriter ist null.");

                _isRecording = false;

                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Fehler beim Schließen des Writers: {ex.Message}");
                }
                finally
                {
                    _writer = null;
                }

                _bytesRecorded = 0;
                await Task.Delay(500);

                if (string.IsNullOrEmpty(filePath))
                    throw new InvalidOperationException("Dateipfad ist leer.");
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Audiodatei nicht gefunden: {filePath}");

                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 1024)
                    throw new Exception($"Audiodatei zu klein ({fileInfo.Length} Bytes): {filePath}");

                _outputFilePath = null;
                return filePath;
            }
            catch (Exception)
            {
                _isRecording = false;
                _writer?.Dispose();
                _writer = null;
                _outputFilePath = null;
                _bytesRecorded = 0;
                throw;
            }
        }

        private float CalculateRms(byte[] buffer, int bytesRecorded)
        {
            int step = _bytesPerSample * (_capture?.WaveFormat.Channels ?? 1);
            if (step == 0) return 0f;
            double sumOfSquares = 0;
            int sampleCount = 0;
            for (int i = 0; i + _bytesPerSample <= bytesRecorded; i += step)
            {
                float sample = ReadSample(buffer, i);
                sumOfSquares += sample * sample;
                sampleCount++;
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
            if (_isFloat)
            {
                for (int i = 0; i + 4 <= bytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(buffer, i);
                    float amplified = Math.Clamp(sample * _currentGain, -1f, 1f);
                    BitConverter.GetBytes(amplified).CopyTo(result, i);
                }
            }
            else
            {
                for (int i = 0; i + 2 <= bytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    float amplified = Math.Clamp(sample * _currentGain, -32768f, 32767f);
                    BitConverter.GetBytes((short)amplified).CopyTo(result, i);
                }
            }

            return result;
        }

        public void Dispose()
        {
            if (_isRecording)
            {
                _isRecording = false;
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
            StopMonitoring();
        }
    }
}
