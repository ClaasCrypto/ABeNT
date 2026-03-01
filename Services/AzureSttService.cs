using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using ABeNT.Model;
using Newtonsoft.Json.Linq;

namespace ABeNT.Services
{
    public class AzureSttService : ISttService
    {
        private readonly HttpClient _httpClient;

        public AzureSttService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<List<TranscriptSegment>> TranscribeAudioAsync(string filePath, RecorderReportOptions options)
        {
            string speechKey = options.AzureSpeechKey;
            string region = options.AzureSpeechRegion;

            if (string.IsNullOrWhiteSpace(speechKey))
                throw new ArgumentException("Azure Speech Key ist erforderlich.");
            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Azure Speech Region ist erforderlich.");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Audio-Datei nicht gefunden: {filePath}");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
                throw new Exception("Audiodatei ist leer oder fehlt.");

            string url = $"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1"
                       + "?language=de-DE&format=detailed";

            byte[] audioBytes = await File.ReadAllBytesAsync(filePath);
            using var content = new ByteArrayContent(audioBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Add("Ocp-Apim-Subscription-Key", speechKey);

            var response = await _httpClient.SendAsync(request);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = $"Azure Speech API Fehler:\nStatus: {response.StatusCode} ({(int)response.StatusCode})\n\nAntwort:\n{responseJson}";
                MessageBox.Show(errorMsg, "API Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new HttpRequestException($"Azure Speech API Fehler: {response.StatusCode} - {responseJson}");
            }

            return ParseAzureResponse(responseJson);
        }

        private static List<TranscriptSegment> ParseAzureResponse(string json)
        {
            var segments = new List<TranscriptSegment>();

            try
            {
                var root = JObject.Parse(json);
                string status = root["RecognitionStatus"]?.ToString() ?? "";

                if (status != "Success")
                    return segments;

                var nBest = root["NBest"] as JArray;
                string text;
                if (nBest != null && nBest.Count > 0)
                    text = nBest[0]?["Display"]?.ToString() ?? "";
                else
                    text = root["DisplayText"]?.ToString() ?? "";

                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(new TranscriptSegment
                    {
                        Speaker = "0",
                        Text = text.Trim()
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Fehler beim Parsen der Azure-Antwort: {ex.Message}", ex);
            }

            return segments;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
