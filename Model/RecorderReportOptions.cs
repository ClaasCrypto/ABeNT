namespace ABeNT.Model
{
    /// <summary>
    /// Options passed from the dashboard to the recorder for report generation after Finish.
    /// </summary>
    public class RecorderReportOptions
    {
        public string DeepgramApiKey { get; set; } = string.Empty;
        /// <summary>Selected LLM: "ChatGPT", "Gemini", or "Claude".</summary>
        public string SelectedLlm { get; set; } = "Claude";
        public string OpenAiApiKey { get; set; } = string.Empty;
        public string GeminiApiKey { get; set; } = string.Empty;
        public string ClaudeApiKey { get; set; } = string.Empty;
        public string Gender { get; set; } = "Neutral";
        public bool IncludeBefund { get; set; } = true;
        public bool IncludeTherapie { get; set; } = true;
        public bool IncludeIcd10 { get; set; }
        public string? FormId { get; set; }

        /// <summary>Returns the API key for the currently selected LLM.</summary>
        public string GetLlmApiKey()
        {
            return SelectedLlm switch
            {
                "ChatGPT" => OpenAiApiKey ?? string.Empty,
                "Gemini" => GeminiApiKey ?? string.Empty,
                _ => ClaudeApiKey ?? string.Empty
            };
        }
    }
}
