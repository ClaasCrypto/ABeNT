namespace ABeNT.Model
{
    /// <summary>
    /// Options passed from the dashboard to the recorder for report generation after Finish.
    /// </summary>
    public class RecorderReportOptions
    {
        public string SelectedSttProvider { get; set; } = "Deepgram";
        public string DeepgramApiKey { get; set; } = string.Empty;
        public string AzureSpeechKey { get; set; } = string.Empty;
        public string AzureSpeechRegion { get; set; } = "westeurope";
        public string CustomSttEndpoint { get; set; } = string.Empty;
        public string CustomSttApiKey { get; set; } = string.Empty;
        /// <summary>Selected LLM: "ChatGPT", "Gemini", "Claude", or "Mistral".</summary>
        public string SelectedLlm { get; set; } = "Claude";
        public string OpenAiApiKey { get; set; } = string.Empty;
        public string GeminiApiKey { get; set; } = string.Empty;
        public string ClaudeApiKey { get; set; } = string.Empty;
        public string MistralApiKey { get; set; } = string.Empty;
        public string Gender { get; set; } = "Neutral";
        public bool IncludeBefund { get; set; } = true;
        public bool IncludeDiagnosen { get; set; } = true;
        public bool IncludeTherapie { get; set; } = true;
        public bool IncludeIcd10 { get; set; }
        public string? FormId { get; set; }

        /// <summary>Recording mode: "Neupatient" or "Kontrolltermin".</summary>
        public string RecordingMode { get; set; } = "Neupatient";

        /// <summary>Returns the API key for the currently selected LLM.</summary>
        public string GetLlmApiKey()
        {
            return SelectedLlm switch
            {
                "ChatGPT" => OpenAiApiKey ?? string.Empty,
                "Gemini" => GeminiApiKey ?? string.Empty,
                "Mistral" => MistralApiKey ?? string.Empty,
                _ => ClaudeApiKey ?? string.Empty
            };
        }
    }
}
