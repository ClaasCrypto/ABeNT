using System;
using System.IO;
using Newtonsoft.Json;

namespace ABeNT.Services
{
    public class AppSettings
    {
        /// <summary>STT provider: "Deepgram", "Azure", or "Custom".</summary>
        public string SelectedSttProvider { get; set; } = "Deepgram";
        public string DeepgramApiKey { get; set; } = string.Empty;
        public string AzureSpeechKey { get; set; } = string.Empty;
        public string AzureSpeechRegion { get; set; } = "westeurope";
        public string CustomSttEndpoint { get; set; } = string.Empty;
        public string CustomSttApiKey { get; set; } = string.Empty;
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
        /// <summary>True once keys have been DPAPI-encrypted. Used for auto-migration of plain-text keys.</summary>
        public bool KeysEncrypted { get; set; }
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
                return new AppSettings();

            AppSettings settings;
            try
            {
                string json = File.ReadAllText(settingsPath);
                settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }

            if (settings.KeysEncrypted)
            {
                DecryptKeys(settings);
            }
            else if (HasAnyKey(settings))
            {
                EncryptKeys(settings);
                settings.KeysEncrypted = true;
                WriteToDisk(settings);
                DecryptKeys(settings);
            }

            return settings;
        }

        public static void SaveSettings(AppSettings settings)
        {
            EncryptKeys(settings);
            settings.KeysEncrypted = true;
            WriteToDisk(settings);
            DecryptKeys(settings);
        }

        private static void WriteToDisk(AppSettings settings)
        {
            string settingsPath = GetSettingsPath();
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(settingsPath, json);
        }

        private static void EncryptKeys(AppSettings s)
        {
            s.DeepgramApiKey = CryptoHelper.Encrypt(s.DeepgramApiKey);
            s.AzureSpeechKey = CryptoHelper.Encrypt(s.AzureSpeechKey);
            s.CustomSttApiKey = CryptoHelper.Encrypt(s.CustomSttApiKey);
            s.OpenAiApiKey = CryptoHelper.Encrypt(s.OpenAiApiKey);
            s.GeminiApiKey = CryptoHelper.Encrypt(s.GeminiApiKey);
            s.ClaudeApiKey = CryptoHelper.Encrypt(s.ClaudeApiKey);
        }

        private static void DecryptKeys(AppSettings s)
        {
            s.DeepgramApiKey = CryptoHelper.Decrypt(s.DeepgramApiKey);
            s.AzureSpeechKey = CryptoHelper.Decrypt(s.AzureSpeechKey);
            s.CustomSttApiKey = CryptoHelper.Decrypt(s.CustomSttApiKey);
            s.OpenAiApiKey = CryptoHelper.Decrypt(s.OpenAiApiKey);
            s.GeminiApiKey = CryptoHelper.Decrypt(s.GeminiApiKey);
            s.ClaudeApiKey = CryptoHelper.Decrypt(s.ClaudeApiKey);
        }

        private static bool HasAnyKey(AppSettings s)
        {
            return !string.IsNullOrEmpty(s.DeepgramApiKey)
                || !string.IsNullOrEmpty(s.AzureSpeechKey)
                || !string.IsNullOrEmpty(s.CustomSttApiKey)
                || !string.IsNullOrEmpty(s.OpenAiApiKey)
                || !string.IsNullOrEmpty(s.GeminiApiKey)
                || !string.IsNullOrEmpty(s.ClaudeApiKey);
        }
    }
}
