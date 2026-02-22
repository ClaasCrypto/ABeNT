using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ABeNT.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ABeNT.Services
{
    public class LlmService
    {
        private readonly HttpClient _httpClient;

        public LlmService()
        {
            _httpClient = new HttpClient();
        }

        /// <summary>Generate ABeNT report using the LLM selected in options. Supports ChatGPT, Gemini, Claude.</summary>
        public async Task<string> GenerateAbentReportAsync(
            string rawTranscript,
            RecorderReportOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            string apiKey = options.GetLlmApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"API Key für {options.SelectedLlm} ist erforderlich. Bitte in den Einstellungen eintragen.");
            if (string.IsNullOrWhiteSpace(rawTranscript))
                throw new ArgumentException("Transkript ist leer.");

            string systemPrompt = !string.IsNullOrWhiteSpace(options.FormId)
                ? OutputFormsService.BuildSystemPromptFromConfig(options.FormId, options.Gender, options.IncludeBefund, options.IncludeTherapie, options.IncludeIcd10)
                : BuildSystemPrompt(options.Gender, options.IncludeBefund, options.IncludeTherapie, options.IncludeIcd10);
            string userMessage = $"Transkript:\n\n{rawTranscript}";

            return options.SelectedLlm switch
            {
                "ChatGPT" => await CallOpenAiAsync(systemPrompt, userMessage, options.OpenAiApiKey ?? "", cancellationToken),
                "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, options.GeminiApiKey ?? "", cancellationToken),
                _ => await CallClaudeAsync(systemPrompt, userMessage, options.ClaudeApiKey ?? "", cancellationToken)
            };
        }

        private async Task<string> CallOpenAiAsync(string systemPrompt, string userMessage, string apiKey, CancellationToken cancellationToken)
        {
            var body = new
            {
                model = "gpt-4o",
                max_tokens = 4096,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", "Bearer " + apiKey.Trim());
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"OpenAI API Fehler ({response.StatusCode}): {json}");
            return ParseOpenAiResponse(json);
        }

        private static string ParseOpenAiResponse(string json)
        {
            var root = JObject.Parse(json);
            var text = root["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                throw new Exception("Keine Antwort von OpenAI erhalten.");
            return text.Trim();
        }

        private async Task<string> CallGeminiAsync(string systemPrompt, string userMessage, string apiKey, CancellationToken cancellationToken)
        {
            var body = new
            {
                contents = new[] { new { parts = new[] { new { text = userMessage } } } },
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                generationConfig = new { maxOutputTokens = 4096 }
            };
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={Uri.EscapeDataString(apiKey.Trim())}";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Gemini API Fehler ({response.StatusCode}): {json}");
            return ParseGeminiResponse(json);
        }

        private static string ParseGeminiResponse(string json)
        {
            var root = JObject.Parse(json);
            var text = root["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                throw new Exception("Keine Antwort von Gemini erhalten.");
            return text.Trim();
        }

        private async Task<string> CallClaudeAsync(string systemPrompt, string userMessage, string apiKey, CancellationToken cancellationToken)
        {
            if (!apiKey.Trim().StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Ungültiges Format für Claude API Key. Der Key sollte mit 'sk-ant-' beginnen.");
            string[] modelNames = new[]
            {
                "claude-sonnet-4-5-20250929",
                "claude-sonnet-4-20250514",
                "claude-opus-4-5-20251101",
                "claude-haiku-4-5-20251001"
            };
            Exception? lastException = null;
            foreach (string modelName in modelNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var requestBody = new
                    {
                        model = modelName,
                        max_tokens = 4096,
                        system = systemPrompt,
                        messages = new[] { new { role = "user", content = userMessage } }
                    };
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")
                    };
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    string responseJson = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        if (responseJson.Contains("not_found_error") && responseJson.Contains("model"))
                        {
                            lastException = new HttpRequestException($"Modell '{modelName}' nicht gefunden.");
                            continue;
                        }
                        throw new HttpRequestException($"Anthropic API Fehler ({response.StatusCode}): {responseJson}");
                    }
                    return ParseClaudeResponse(responseJson);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    if (ex.Message.Contains("not_found_error") || ex.Message.Contains("nicht gefunden"))
                    {
                        lastException = ex;
                        continue;
                    }
                    throw;
                }
            }
            throw new Exception($"Keines der Claude-Modelle funktioniert. Letzter Fehler: {lastException?.Message}");
        }

        private string BuildSystemPrompt(string gender, bool includeBefund, bool includeTherapie, bool includeIcd10)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Du bist ein präziser medizinischer Dokumentations-Assistent.");
            prompt.AppendLine("\nGLOBALE REGELN:");
            prompt.AppendLine("1. Nutze für den INHALT reinen Text (kein Markdown wie **Fett**).");
            prompt.AppendLine("2. Nutze KEINE automatischen Nummerierungen (1., 2.).");
            prompt.AppendLine("3. Trenne Haupt-Abschnitte NUR mit den Markern **A**, **Be**, **N**, **T**, **ICD-10**.");
            prompt.AppendLine("4. ABKÜRZUNGEN: Kürze Seitenangaben IMMER ab: 're.' (rechts), 'li.' (links), 'bds.' (beidseits). Nutze gängige medizinische Kürzel (z.B. 'o.B.', 'Z.n.', 'V.a.').");
            prompt.AppendLine("\nSprecher-Erkennung:");
            prompt.AppendLine("Analysiere das Gespräch. Der Sprecher, der Fragen stellt und Anweisungen gibt, ist der ARZT. Der andere ist der PATIENT. Tausche die Rollen logisch, falls die Labels vertauscht scheinen.");
            prompt.AppendLine("\nGeschlecht:");
            if (gender == "Männlich")
                prompt.AppendLine("Nutze 'Der Patient', 'er', 'sein'.");
            else if (gender == "Weiblich")
                prompt.AppendLine("Nutze 'Die Patientin', 'sie', 'ihr'.");
            else
                prompt.AppendLine("Nutze 'Patient', neutrale Formulierungen ohne Pronomen.");

            bool includeN = includeBefund || includeTherapie || includeIcd10;
            prompt.AppendLine("\nOutput-Struktur:");
            prompt.AppendLine("Erstelle **A** (Anamnese) immer.");
            if (includeBefund) prompt.AppendLine("Erstelle **Be** (Befund).");
            if (includeN) prompt.AppendLine("Erstelle **N** (Diagnose/Name)" + (includeIcd10 ? " mit ICD-10-Codes in Klammern." : "."));
            if (includeTherapie) prompt.AppendLine("Erstelle **T** (Therapie).");

            prompt.AppendLine("\nSTRUKTURVORGABEN:");
            prompt.AppendLine("\n**A**");
            prompt.AppendLine("[ANWEISUNG:");
            prompt.AppendLine("Nutze PATIENTENSPRACHE (Laienbegriffe) im NOMINALSTIL.");
            prompt.AppendLine("Formatierung: Trenne einzelne Sätze/Aussagen durch einen PUNKT (.).");
            prompt.AppendLine("");
            prompt.AppendLine("WICHTIG: Nur tatsächlich im Gespräch genannte Informationen ausgeben. Keine Platzhalter wie 'k.A.', 'keine Angabe' oder 'Keine Vorgeschichte'. Kategorien, zu denen nichts genannt wurde, komplett weglassen.");
            prompt.AppendLine("");
            prompt.AppendLine("Mögliche Kategorien (nur ausgeben, wenn im Gespräch erwähnt): Vorstellungsgrund, Vorgeschichte am Organ, Begleitumstände, Nebendiagnosen/chronische Erkrankungen, aktuelle Medikamente, frühere Operationen, Allergien/Unverträglichkeiten, Sozialanamnese.");
            prompt.AppendLine("]");

            if (includeBefund)
            {
                prompt.AppendLine("\n**Be**");
                prompt.AppendLine("[ANWEISUNG:");
                prompt.AppendLine("Nutze strikte ÄRZTLICHE FACHSPRACHE.");
                prompt.AppendLine("");
                prompt.AppendLine("Struktur:");
                prompt.AppendLine("Für jedes Organ einen Block:");
                prompt.AppendLine("'Befund [Organ] [Seite (re./li./bds.)]:'");
                prompt.AppendLine("Darunter die Befunde (Inspektion, Palpation, Funktion, Tests).");
                prompt.AppendLine("Formatierung: Trenne einzelne Angaben innerhalb eines Organs mit KOMMA (,). Ende den Block mit einem PUNKT (.).");
                prompt.AppendLine("Mache eine LEERZEILE zwischen verschiedenen Organ-Blöcken.");
                prompt.AppendLine("");
                prompt.AppendLine("Vitalparameter: Nur wenn Werte genannt (Format: 'RR 120/80 mmHg').");
                prompt.AppendLine("WICHTIG: Wenn gar keine Untersuchung: 'Keine Untersuchungsergebnisse dokumentiert'.");
                prompt.AppendLine("]");
            }
            if (includeN)
            {
                prompt.AppendLine("\n**N**");
                prompt.AppendLine("[ANWEISUNG:");
                prompt.AppendLine("Liste der Diagnosen (Fachsprache).");
                prompt.AppendLine("- Seitenangabe PFLICHT (re./li./bds.).");
                prompt.AppendLine("- Kennzeichne Unsicherheiten (V.a., D.D.).");
                prompt.AppendLine("- Jede Diagnose in eine neue Zeile.");
                if (includeIcd10)
                    prompt.AppendLine("- Bei jeder Diagnose den passenden ICD-10-Code in Klammern angeben, z.B. Lumbago (M54.5).");
                prompt.AppendLine("]");
            }
            if (includeTherapie)
            {
                prompt.AppendLine("\n**T**");
                prompt.AppendLine("[ANWEISUNG:");
                prompt.AppendLine("Halte dich strikt an diese Reihenfolge:");
                prompt.AppendLine("");
                prompt.AppendLine("1. Zuerst: Geplante Behandlungen, Verordnungen (Heilmittel/Hilfsmittel), AU und Procedere.");
                prompt.AppendLine("   Formatierung: Liste diese Punkte hintereinander auf, getrennt durch KOMMA (,).");
                prompt.AppendLine("");
                prompt.AppendLine("2. Danach: Füge eine LEERZEILE ein und schreibe exakt: 'Medikation:'");
                prompt.AppendLine("");
                prompt.AppendLine("3. Darunter:");
                prompt.AppendLine("   - WICHTIG: Jedes Medikament MUSS in eine NEUE ZEILE! Niemals mehrere Medikamente in einer Zeile!");
                prompt.AppendLine("   - Format: Wirkstoff/Name + Stärke + Schema (z.B. '1-0-1', 'tgl.', 'wchtl.', bei Tropfen 'ggt').");
                prompt.AppendLine("   - Beispiel:");
                prompt.AppendLine("     Medikation:");
                prompt.AppendLine("     Ibuprofen 400mg 1-0-1");
                prompt.AppendLine("     Paracetamol 500mg tgl.");
                prompt.AppendLine("     (Jedes Medikament in eigener Zeile!)");
                prompt.AppendLine("]");
            }

            prompt.AppendLine("\nSchreibe keinen Text vor dem ersten Marker.");
            return prompt.ToString();
        }

        private string ParseClaudeResponse(string json)
        {
            try
            {
                var root = JObject.Parse(json);
                var content = root["content"] as JArray;
                
                if (content != null && content.Count > 0)
                {
                    var textContent = content[0]?["text"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(textContent))
                    {
                        return textContent.Trim();
                    }
                }

                throw new Exception("Keine Antwort von Claude erhalten.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Fehler beim Parsen der Claude-Antwort: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
