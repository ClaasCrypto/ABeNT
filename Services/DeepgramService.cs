using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ABeNT.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ABeNT.Services
{
    public class DeepgramService : ISttService
    {
        private readonly HttpClient _httpClient;

        public DeepgramService()
        {
            _httpClient = new HttpClient();
        }

        public Task<List<TranscriptSegment>> TranscribeAudioAsync(string filePath, RecorderReportOptions options)
        {
            return TranscribeAudioAsync(filePath, options.DeepgramApiKey);
        }

        public async Task<List<TranscriptSegment>> TranscribeAudioAsync(string filePath, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException("Deepgram API Key ist erforderlich.");
            }

            // Dateiprüfung: Existenz und Größe
            if (!File.Exists(filePath))
            {
                MessageBox.Show($"Audio-Datei nicht gefunden: {filePath}", 
                    "Dateifehler", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new FileNotFoundException($"Audio-Datei nicht gefunden: {filePath}");
            }

            FileInfo fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                string errorMsg = $"Audiodatei ist leer oder fehlt.\nDatei: {filePath}\nGröße: {fileInfo.Length} Bytes";
                MessageBox.Show(errorMsg, "Dateifehler", MessageBoxButton.OK, MessageBoxImage.Error);
                throw new Exception("Audiodatei ist leer oder fehlt.");
            }

            // Query Parameter
            var queryParams = new StringBuilder();
            queryParams.Append("?model=nova-2");
            queryParams.Append("&language=de");
            queryParams.Append("&diarize=true");
            queryParams.Append("&smart_format=true");
            queryParams.Append("&keywords=Patient:2&keywords=Arzt:2&keywords=Schmerzen:1&keywords=Befund:1");

            string url = $"https://api.deepgram.com/v1/listen{queryParams}";

            // Setze Authorization Header
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Token", apiKey);

            try
            {
                // Deepgram erwartet die Datei direkt als Body, nicht als Multipart
                // Lade Audio-Datei als Byte-Array
                byte[] audioBytes = await File.ReadAllBytesAsync(filePath);
                using var content = new ByteArrayContent(audioBytes);
                
                // WICHTIG: Setze den Content-Type Header explizit!
                content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

                // Sende Request mit Datei direkt als Body
                var response = await _httpClient.PostAsync(url, content);
                
                // Lese Antwort als String
                string responseJson = await response.Content.ReadAsStringAsync();
                
                // Prüfe Status-Code
                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = $"Deepgram API Fehler:\nStatus: {response.StatusCode} ({(int)response.StatusCode})\n\nAntwort:\n{responseJson}";
                    MessageBox.Show(errorMsg, "API Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    throw new HttpRequestException($"Deepgram API Fehler: {response.StatusCode} - {responseJson}");
                }
                
                // Parse und zurückgeben (ohne Debug-MessageBox)
                return ParseDeepgramResponse(responseJson);
            }
            finally
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
        }

        private List<TranscriptSegment> ParseDeepgramResponse(string json)
        {
            var segments = new List<TranscriptSegment>();
            
            try
            {
                var root = JObject.Parse(json);
                
                // Navigiere durch die Hierarchie: results -> channels[0] -> alternatives[0]
                var channels = root["results"]?["channels"] as JArray;
                if (channels == null || channels.Count == 0)
                {
                    MessageBox.Show("JSON Struktur nicht erkannt: 'channels' fehlt oder ist leer.\n\nJSON:\n" + 
                        (json.Length > 500 ? json.Substring(0, 500) + "..." : json), 
                        "Parsing Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    throw new Exception("JSON Struktur nicht erkannt: 'channels' fehlt oder ist leer.");
                }
                
                var alternatives = channels[0]?["alternatives"] as JArray;
                if (alternatives == null || alternatives.Count == 0)
                {
                    MessageBox.Show("JSON Struktur nicht erkannt: 'alternatives' fehlt oder ist leer.\n\nJSON:\n" + 
                        (json.Length > 500 ? json.Substring(0, 500) + "..." : json), 
                        "Parsing Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    throw new Exception("JSON Struktur nicht erkannt: 'alternatives' fehlt oder ist leer.");
                }
                
                var results = alternatives[0];
                if (results == null)
                {
                    MessageBox.Show("JSON Struktur nicht erkannt: 'alternatives[0]' ist null.\n\nJSON:\n" + 
                        (json.Length > 500 ? json.Substring(0, 500) + "..." : json), 
                        "Parsing Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    throw new Exception("JSON Struktur nicht erkannt: 'alternatives[0]' ist null.");
                }

                // Prüfe zuerst, ob überhaupt Sprache erkannt wurde
                var transcript = results["transcript"]?.ToString() ?? "";
                var confidence = results["confidence"]?.Value<double>() ?? 0.0;
                
                if (string.IsNullOrWhiteSpace(transcript) && confidence == 0.0)
                {
                    // Keine Sprache erkannt - gebe leere Liste zurück ohne Fehlermeldung
                    // (Die UI zeigt dann "Keine Transkription erhalten" an)
                    return segments;
                }

                // Versuche zuerst "paragraphs" zu nutzen (strukturierter)
                var paragraphsObj = results["paragraphs"];
                JArray? paragraphs = null;
                
                // paragraphs kann ein Objekt mit "paragraphs" Array sein
                if (paragraphsObj != null)
                {
                    if (paragraphsObj["paragraphs"] is JArray paraArray)
                    {
                        paragraphs = paraArray;
                    }
                    else if (paragraphsObj is JArray directArray)
                    {
                        paragraphs = directArray;
                    }
                }
                
                if (paragraphs != null && paragraphs.Count > 0)
                {
                    foreach (var paragraph in paragraphs)
                    {
                        var speaker = paragraph["speaker"]?.ToString() ?? "unknown";
                        var sentences = paragraph["sentences"] as JArray;
                        
                        if (sentences != null && sentences.Count > 0)
                        {
                            var text = string.Join(" ", sentences.Select(s => s["text"]?.ToString() ?? ""));
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                segments.Add(new TranscriptSegment
                                {
                                    Speaker = speaker,
                                    Text = text.Trim()
                                });
                            }
                        }
                    }
                }
                
                // Wenn paragraphs leer ist, versuche "words"
                if (segments.Count == 0)
                {
                    var words = results["words"] as JArray;
                    if (words != null && words.Count > 0)
                    {
                        string? currentSpeaker = null;
                        var currentWords = new List<string>();

                        foreach (var word in words)
                        {
                            var speaker = word["speaker"]?.ToString();
                            var wordText = word["word"]?.ToString()?.Trim();

                            if (string.IsNullOrWhiteSpace(wordText))
                                continue;

                            if (speaker != currentSpeaker && currentWords.Count > 0)
                            {
                                // Neuer Speaker - speichere bisherige Wörter
                                segments.Add(new TranscriptSegment
                                {
                                    Speaker = currentSpeaker ?? "unknown",
                                    Text = string.Join(" ", currentWords)
                                });
                                currentWords.Clear();
                            }

                            currentSpeaker = speaker;
                            currentWords.Add(wordText);
                        }

                        // Letztes Segment hinzufügen
                        if (currentWords.Count > 0)
                        {
                            segments.Add(new TranscriptSegment
                            {
                                Speaker = currentSpeaker ?? "unknown",
                                Text = string.Join(" ", currentWords)
                            });
                        }
                    }
                }
                
                // Wenn immer noch keine Segmente gefunden wurden
                if (segments.Count == 0)
                {
                    // Prüfe, ob transcript vorhanden ist (auch wenn leer)
                    if (!string.IsNullOrWhiteSpace(transcript))
                    {
                        // Es gibt einen transcript, aber keine strukturierten Segmente
                        // Erstelle ein Segment aus dem transcript
                        segments.Add(new TranscriptSegment
                        {
                            Speaker = "unknown",
                            Text = transcript
                        });
                    }
                    else
                    {
                        // Keine Segmente und kein transcript - wahrscheinlich keine Sprache erkannt
                        // Das wurde schon oben behandelt, aber falls wir hier ankommen:
                        return segments; // Leere Liste zurückgeben
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("JSON Struktur nicht erkannt"))
                {
                    throw; // Re-throw unsere eigenen Fehler
                }
                throw new Exception($"Fehler beim Parsen der Deepgram-Antwort: {ex.Message}", ex);
            }

            return segments;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
