using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ABeNT.Services
{
    public static class UpdateService
    {
        private const string GitHubRepo = "ClaasCrypto/ABeNT";
        private const string ReleasesApi = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

        public static Version GetCurrentVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            return ver ?? new Version(1, 0, 0);
        }

        public static async Task<(bool available, string tagName, string downloadUrl, string body)?> CheckForUpdateAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ABeNT-Updater/1.0");
            http.Timeout = TimeSpan.FromSeconds(15);

            var response = await http.GetAsync(ReleasesApi);
            if (!response.IsSuccessStatusCode) return null;

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var tagName = json["tag_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(tagName)) return null;

            var remoteVersion = ParseVersion(tagName);
            if (remoteVersion == null) return null;

            var current = GetCurrentVersion();
            bool available = remoteVersion > current;

            string downloadUrl = "";
            var assets = json["assets"] as JArray;
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    var name = asset["name"]?.ToString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset["browser_download_url"]?.ToString() ?? "";
                        break;
                    }
                }
            }

            var body = json["body"]?.ToString() ?? "";
            return (available, tagName!, downloadUrl, body);
        }

        public static async Task DownloadAndInstallAsync(string downloadUrl, Action<int>? progressCallback = null)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ABeNT-Updater/1.0");
            http.Timeout = TimeSpan.FromMinutes(5);

            var tempDir = Path.Combine(Path.GetTempPath(), "ABeNT_Update");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "update.zip");

            using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                long downloaded = 0;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    downloaded += read;
                    if (totalBytes > 0)
                        progressCallback?.Invoke((int)(downloaded * 100 / totalBytes));
                }
            }

            var extractDir = Path.Combine(tempDir, "extracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, true);

            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var batchPath = Path.Combine(tempDir, "update.bat");

            var batchContent = $"""
                @echo off
                echo Warte auf Beenden von ABeNT...
                timeout /t 2 /nobreak >nul
                :waitloop
                tasklist /FI "IMAGENAME eq ABeNT.exe" 2>NUL | find /I "ABeNT.exe" >NUL
                if not errorlevel 1 (
                    timeout /t 1 /nobreak >nul
                    goto waitloop
                )
                echo Kopiere Update-Dateien...
                xcopy /E /Y /Q "{extractDir}\*" "{appDir}"
                echo Update abgeschlossen. Starte ABeNT...
                start "" "{Path.Combine(appDir, "ABeNT.exe")}"
                rmdir /S /Q "{tempDir}"
                """;

            File.WriteAllText(batchPath, batchContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static Version? ParseVersion(string tag)
        {
            var match = Regex.Match(tag, @"(\d+)\.(\d+)\.(\d+)");
            if (!match.Success) return null;
            return new Version(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value));
        }
    }
}
