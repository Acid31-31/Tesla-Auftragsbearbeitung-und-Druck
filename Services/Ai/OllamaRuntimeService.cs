using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace PDF_umbenenen.Services.Ai
{
    /// <summary>
    /// Prüft Ollama automatisch und startet den lokalen Server bei Bedarf (ohne Benutzeraktion).
    /// </summary>
    internal static class OllamaRuntimeService
    {
        private const int ApiTimeoutSeconds = 4;
        private const int StartupWaitSeconds = 45;
        private const int EnsureCacheSeconds = 20;

        private static readonly object StartLock = new object();
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds)
        };

        private static DateTime _lastEnsureUtc = DateTime.MinValue;
        private static bool _lastEnsureSuccess;

        public static bool EnsureRunning(string baseUrl)
        {
            var normalizedUrl = NormalizeBaseUrl(baseUrl);
            if ((DateTime.UtcNow - _lastEnsureUtc).TotalSeconds < EnsureCacheSeconds && _lastEnsureSuccess)
                return true;

            lock (StartLock)
            {
                if (IsApiReachable(normalizedUrl))
                {
                    RememberSuccess();
                    return true;
                }

                TryStartServeProcess();

                for (var i = 0; i < StartupWaitSeconds; i++)
                {
                    Thread.Sleep(1000);
                    if (IsApiReachable(normalizedUrl))
                    {
                        RememberSuccess();
                        return true;
                    }
                }

                _lastEnsureSuccess = false;
                _lastEnsureUtc = DateTime.UtcNow;
                return false;
            }
        }

        public static bool IsApiReachable(string baseUrl)
        {
            try
            {
                var url = NormalizeBaseUrl(baseUrl) + "/api/tags";
                using (var response = HttpClient.GetAsync(url).GetAwaiter().GetResult())
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void TryStartServeProcess()
        {
            var exe = FindOllamaExecutable();
            if (string.IsNullOrWhiteSpace(exe))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch
            {
            }
        }

        private static string FindOllamaExecutable()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            var candidates = new[]
            {
                Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"),
                Path.Combine(programFiles, "Ollama", "ollama.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static void RememberSuccess()
        {
            _lastEnsureSuccess = true;
            _lastEnsureUtc = DateTime.UtcNow;
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return "http://localhost:11434";

            return baseUrl.Trim().TrimEnd('/');
        }
    }
}
