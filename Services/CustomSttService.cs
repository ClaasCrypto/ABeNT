using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using ABeNT.Model;
using Newtonsoft.Json.Linq;

namespace ABeNT.Services
{
    /// <summary>
    /// STT service for a user-hosted API endpoint.
    /// Expected JSON response: { "text": "..." } or { "segments": [{ "speaker": "0", "text": "..." }] }
    /// Audio is POSTed as binary body (audio/wav). API key sent via Authorization: Bearer header.
    /// </summary>
    public class CustomSttService : ISttService
    {
        private readonly HttpClient _httpClient;

        public CustomSttService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public async Task<List<TranscriptSegment>> TranscribeAudioAsync(string filePath, RecorderReportOptions options)
        {
            string endpoint = options.CustomSttEndpoint;
            string apiKey = options.CustomSttApiKey;

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Custom STT Endpoint ist erforderlich.");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Audio-Datei nicht gefunden: {filePath}");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
                throw new Exception("Audiodatei ist leer oder fehlt.");

            byte[] audioBytes = await File.ReadAllBytesAsync(filePath);
            using var content = new ByteArrayContent(audioBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = content;

            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = $"Custom STT API Fehler:\nStatus: {response.StatusCode} ({(int)response.StatusCode})\n\nAntwort:\n{responseJson}";
                MessageBox.Show(errorMsg, "API Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new HttpRequestException($"Custom STT API Fehler: {response.StatusCode} - {responseJson}");
            }

            return ParseCustomResponse(responseJson);
        }

        private static List<TranscriptSegment> ParseCustomResponse(string json)
        {
            var segments = new List<TranscriptSegment>();

            try
            {
                var root = JObject.Parse(json);

                var segsArray = root["segments"] as JArray;
                if (segsArray != null && segsArray.Count > 0)
                {
                    foreach (var seg in segsArray)
                    {
                        string speaker = seg["speaker"]?.ToString() ?? "0";
                        string text = seg["text"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            segments.Add(new TranscriptSegment { Speaker = speaker, Text = text });
                        }
                    }
                    return segments;
                }

                string? plainText = root["text"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(plainText))
                {
                    segments.Add(new TranscriptSegment { Speaker = "0", Text = plainText });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Fehler beim Parsen der Custom-STT-Antwort: {ex.Message}", ex);
            }

            return segments;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
