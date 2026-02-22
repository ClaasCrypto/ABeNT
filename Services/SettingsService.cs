using System;
using System.IO;
using Newtonsoft.Json;

namespace ABeNT.Services
{
    public class AppSettings
    {
        public string DeepgramApiKey { get; set; } = string.Empty;
        /// <summary>Selected LLM: "ChatGPT", "Gemini", or "Claude".</summary>
        public string SelectedLlm { get; set; } = "Claude";
        public string OpenAiApiKey { get; set; } = string.Empty;
        public string GeminiApiKey { get; set; } = string.Empty;
        public string ClaudeApiKey { get; set; } = string.Empty;
        /// <summary>Bericht erstellen: Befund einbeziehen (wie bei letzter Aufzeichnung).</summary>
        public bool IncludeBefund { get; set; } = true;
        /// <summary>Bericht erstellen: Therapie einbeziehen (wie bei letzter Aufzeichnung).</summary>
        public bool IncludeTherapie { get; set; } = true;
        /// <summary>Bericht erstellen: Diagnosen nach ICD-10 (wie bei letzter Aufzeichnung).</summary>
        public bool SuggestIcd10 { get; set; } = false;
        /// <summary>Zuletzt gewähltes Mikrofon (Anzeigename) – wird im Recorder wiederhergestellt.</summary>
        public string SelectedMicrophoneDeviceName { get; set; } = string.Empty;
        /// <summary>Id des zuletzt gewählten Berichtsformulars – wird beim Start wiederhergestellt.</summary>
        public string LastSelectedFormId { get; set; } = string.Empty;
    }

    public class SettingsService
    {
        private static string GetSettingsPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "ABeNT");
            Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "settings.json");
        }

        public static AppSettings LoadSettings()
        {
            string settingsPath = GetSettingsPath();
            
            if (!File.Exists(settingsPath))
            {
                return new AppSettings();
            }

            try
            {
                string json = File.ReadAllText(settingsPath);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            string settingsPath = GetSettingsPath();
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(settingsPath, json);
        }
    }
}
