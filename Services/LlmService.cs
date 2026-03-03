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

        /// <summary>Generate ABeNT report using the LLM selected in options. Supports ChatGPT, Gemini, Claude, Mistral.</summary>
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
                ? OutputFormsService.BuildSystemPromptFromConfig(options.FormId, options.Gender, options.IncludeBefund, options.IncludeTherapie, options.IncludeIcd10, options.RecordingMode)
                : BuildSystemPrompt(options.Gender, options.IncludeBefund, options.IncludeTherapie, options.IncludeIcd10, options.RecordingMode);
            string userMessage = $"Transkript:\n\n{rawTranscript}";

            return options.SelectedLlm switch
            {
                "ChatGPT" => await CallOpenAiAsync(systemPrompt, userMessage, options.OpenAiApiKey ?? "", cancellationToken),
                "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, options.GeminiApiKey ?? "", cancellationToken),
                "Mistral" => await CallMistralAsync(systemPrompt, userMessage, options.MistralApiKey ?? "", cancellationToken),
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

        private async Task<string> CallMistralAsync(string systemPrompt, string userMessage, string apiKey, CancellationToken cancellationToken)
        {
            var body = new
            {
                model = "mistral-large-latest",
                max_tokens = 4096,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions")
            {
                Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", "Bearer " + apiKey.Trim());
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Mistral API Fehler ({response.StatusCode}): {json}");
            return ParseOpenAiResponse(json);
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

        /// <summary>Send a raw system+user prompt to the selected LLM and return the response text.</summary>
        public async Task<string> GenerateRawAsync(
            string systemPrompt, string userMessage, string llmProvider, string apiKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"API Key für {llmProvider} ist erforderlich.");

            return llmProvider switch
            {
                "ChatGPT" => await CallOpenAiAsync(systemPrompt, userMessage, apiKey, cancellationToken),
                "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, apiKey, cancellationToken),
                _ => await CallClaudeAsync(systemPrompt, userMessage, apiKey, cancellationToken)
            };
        }

        private static string BuildSystemPrompt(string gender, bool includeBefund, bool includeTherapie, bool includeIcd10, string recordingMode = "Neupatient")
        {
            return OutputFormsService.BuildSystemPromptFromConfig(null, gender, includeBefund, includeTherapie, includeIcd10, recordingMode);
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
