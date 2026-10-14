using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Services.Ai;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Portable Ollama im Ordner AI_DOK-V01 (Nebenordner zur EXE, USB-tauglich).
    /// </summary>
    internal static class OllamaPortableService
    {
        private static readonly object StartLock = new object();
        private static Process _serverProcess;
        private static string _lastError = string.Empty;

        public const string OllamaExeName = "ollama.exe";
        public const string ModelsSubFolder = "models";
        public const string HomeSubFolder = "home";

        public static string LastError => _lastError ?? string.Empty;

        public static string ResolveKiDirectory(AiSettings settings)
        {
            // Immer zuerst neben der EXE suchen (USB/Desktop-Kopie) – gespeicherte Pfade koennen veraltet sein.
            var found = AppInfo.TryFindPortableAiDirectory();
            if (!string.IsNullOrWhiteSpace(found))
                return found;

            if (settings != null && !string.IsNullOrWhiteSpace(settings.OllamaPortableDirectory))
            {
                var configured = AppInfo.NormalizePortableKiPath(settings.OllamaPortableDirectory);
                if (AppInfo.HasPortableAiLayout(configured))
                    return configured;
            }

            return AppInfo.GetDefaultPortableKiDirectory();
        }

        public static string GetOllamaExecutablePath(AiSettings settings)
        {
            var root = ResolveKiDirectory(settings);
            var direct = Path.Combine(root, OllamaExeName);
            if (File.Exists(direct))
                return direct;

            var nested = Path.Combine(root, "ollama", OllamaExeName);
            if (File.Exists(nested))
                return nested;

            return direct;
        }

        public static string GetModelsDirectory(AiSettings settings)
        {
            return Path.Combine(ResolveKiDirectory(settings), ModelsSubFolder);
        }

        public static string GetHomeDirectory(AiSettings settings)
        {
            return Path.Combine(ResolveKiDirectory(settings), HomeSubFolder);
        }

        public static string TryReadVersion(AiSettings settings = null)
        {
            var exe = GetOllamaExecutablePath(settings);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return null;

            try
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrWhiteSpace(fileVersion.FileVersion))
                {
                    var fileMatch = Regex.Match(fileVersion.FileVersion, @"\d+\.\d+\.\d+");
                    if (fileMatch.Success)
                        return fileMatch.Value;
                }
            }
            catch
            {
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--version",
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                ApplyPortableEnvironment(psi, settings ?? new AiSettings());

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        return null;

                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(4000))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    var text = (stdout + " " + stderr) ?? string.Empty;
                    var match = Regex.Match(text, @"(\d+\.\d+\.\d+)");
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            catch
            {
            }

            return null;
        }

        public static void StopServer()
        {
            StopAllOllamaProcesses();
        }

        public static bool IsPortableLayoutReady(AiSettings settings)
        {
            return File.Exists(GetOllamaExecutablePath(settings));
        }

        public const string DefaultTextModel = "tinyllama";
        public const string DefaultVisionModel = "moondream";

        private static readonly string[] PreferredVisionModels =
        {
            "moondream",
            "llava",
            "minicpm-v",
            "qwen2.5vl",
            "qwen2-vl",
            "bakllava",
            "llama3.2-vision"
        };

        public static bool IsPortableAiComplete(AiSettings settings)
        {
            if (!IsPortableLayoutReady(settings))
                return false;

            var model = settings?.OllamaModel;
            if (string.IsNullOrWhiteSpace(model))
                model = DefaultTextModel;

            return HasPortableModel(settings, model);
        }

        public static string FindInstalledVisionModel(AiSettings settings)
        {
            foreach (var name in PreferredVisionModels)
            {
                if (HasPortableModel(settings, name))
                    return name;
            }

            try
            {
                var library = Path.Combine(
                    GetModelsDirectory(settings),
                    "manifests",
                    "registry.ollama.ai",
                    "library");
                if (!Directory.Exists(library))
                    return null;

                foreach (var dir in Directory.GetDirectories(library))
                {
                    var name = Path.GetFileName(dir);
                    if (!OpenAiCompatibleBackend.LooksLikeVisionModel(name))
                        continue;

                    foreach (var file in Directory.GetFiles(dir))
                    {
                        var tag = Path.GetFileName(file);
                        if (string.IsNullOrWhiteSpace(tag))
                            continue;
                        if (string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase))
                            return name;
                        return name + ":" + tag;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static string GetVisionModuleDisplayName(AiSettings settings = null)
        {
            var installed = FindInstalledVisionModel(settings ?? new AiSettings());
            var model = string.IsNullOrWhiteSpace(installed) ? DefaultVisionModel : installed;
            return "Ollama · " + model;
        }

        public static string ReadInstalledVisionDigest(AiSettings settings)
        {
            var model = FindInstalledVisionModel(settings);
            if (string.IsNullOrWhiteSpace(model))
                model = DefaultVisionModel;

            var path = GetModelManifestPath(settings, model);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            try
            {
                var json = File.ReadAllText(path);
                var match = Regex.Match(json, @"sha256:([a-fA-F0-9]{64})");
                if (match.Success)
                    return match.Groups[1].Value.ToLowerInvariant();
            }
            catch
            {
            }

            return string.Empty;
        }

        public static string GetModelManifestPath(AiSettings settings, string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return null;

            var parts = model.Split(new[] { ':' }, 2);
            var name = parts[0];
            var tag = parts.Length > 1 ? parts[1] : "latest";
            return Path.Combine(
                GetModelsDirectory(settings),
                "manifests",
                "registry.ollama.ai",
                "library",
                name,
                tag);
        }

        public static bool IsAllowedVisionPullName(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return false;

            var name = model.Trim().Split(new[] { ':' }, 2)[0];
            foreach (var allowed in PreferredVisionModels)
            {
                if (string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static async Task PullVisionModelAsync(
            string model,
            IProgress<PDF_umbenenen.Models.UpdateProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            _lastError = string.Empty;
            var work = AiSettingsStore.Load() ?? new AiSettings();
            work.OllamaUsePortableFolder = true;
            var target = string.IsNullOrWhiteSpace(model) ? DefaultVisionModel : model.Trim();
            if (!IsAllowedVisionPullName(target))
                throw new InvalidOperationException("Unbekanntes Vision-Modul: " + target);

            if (!IsPortableLayoutReady(work))
                throw new InvalidOperationException("KI-Ordner AI_DOK-V01 fehlt neben DOK.");

            EnsureDirectoryLayout(work);
            ReportPull(progress, 4, "Ollama wird gestartet…");
            if (!await EnsureServerRunningAsync(work, cancellationToken, requireLocalModel: false).ConfigureAwait(false))
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(_lastError) ? "Ollama konnte nicht gestartet werden." : _lastError);

            var exe = GetOllamaExecutablePath(work);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "pull " + target,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ResolveKiDirectory(work),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            ApplyPortableEnvironment(psi, work);

            ReportPull(progress, 8, "Zeichnungs-Vision-Modul wird geladen (" + target + ")…");
            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new InvalidOperationException("ollama pull konnte nicht gestartet werden.");

                var percent = 8;
                DataReceivedEventHandler onData = (sender, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data))
                        return;

                    var match = Regex.Match(e.Data, @"(\d{1,3})\s*%");
                    if (match.Success)
                    {
                        int parsed;
                        if (int.TryParse(match.Groups[1].Value, out parsed))
                            percent = Math.Max(8, Math.Min(95, parsed));
                    }

                    var line = e.Data.Trim();
                    if (line.Length > 80)
                        line = line.Substring(0, 80) + "…";
                    ReportPull(progress, percent, line);
                };

                process.OutputDataReceived += onData;
                process.ErrorDataReceived += onData;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(400))
                {
                    if (!cancellationToken.IsCancellationRequested)
                        continue;

                    try { if (!process.HasExited) process.Kill(); } catch { }
                    throw new OperationCanceledException();
                }

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Vision-Update fehlgeschlagen (ollama pull, Code " + process.ExitCode + ").");
            }

            ReportPull(progress, 100, "Zeichnungs-Vision-Modul ist aktuell.");
        }

        private static void ReportPull(
            IProgress<PDF_umbenenen.Models.UpdateProgressInfo> progress,
            int percent,
            string message)
        {
            if (progress == null)
                return;
            progress.Report(new PDF_umbenenen.Models.UpdateProgressInfo(percent, message));
        }

        public static async Task<bool> EnsureVisionRuntimeAsync(AiSettings settings, CancellationToken cancellationToken)
        {
            _lastError = string.Empty;
            var work = settings ?? new AiSettings();
            work.OllamaUsePortableFolder = true;

            if (!IsPortableLayoutReady(work))
            {
                _lastError = "KI-Ordner AI_DOK-V01 fehlt neben DOK.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(FindInstalledVisionModel(work)))
            {
                _lastError = "Zeichnungs-Vision-Modell fehlt in AI_DOK-V01.";
                return false;
            }

            return await EnsureServerRunningAsync(work, cancellationToken).ConfigureAwait(false);
        }

        public static bool HasPortableModel(AiSettings settings, string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return false;

            var modelsDir = GetModelsDirectory(settings);
            var parts = model.Split(new[] { ':' }, 2);
            var name = parts[0];
            var tag = parts.Length > 1 ? parts[1] : "latest";
            var manifest = Path.Combine(
                modelsDir,
                "manifests",
                "registry.ollama.ai",
                "library",
                name,
                tag);

            return File.Exists(manifest);
        }

        public static void EnsureDirectoryLayout(AiSettings settings)
        {
            var root = ResolveKiDirectory(settings);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(GetModelsDirectory(settings));
            Directory.CreateDirectory(GetHomeDirectory(settings));
        }

        /// <summary>
        /// Nach Modell-Crash (0xc0000005 / Verbindungsabbruch) Server hart neu starten.
        /// </summary>
        public static async Task<bool> RestartServerAsync(AiSettings settings, CancellationToken cancellationToken)
        {
            _lastError = string.Empty;
            StopAllOllamaProcesses();
            await Task.Delay(800, cancellationToken).ConfigureAwait(false);
            return await EnsureServerRunningAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<bool> EnsureServerRunningAsync(
            AiSettings settings,
            CancellationToken cancellationToken,
            bool requireLocalModel = true)
        {
            _lastError = string.Empty;
            if (settings == null || !settings.OllamaUsePortableFolder)
                return false;

            if (!IsPortableLayoutReady(settings))
            {
                _lastError = "Portable KI nicht gefunden: " + GetOllamaExecutablePath(settings);
                return false;
            }

            // Konfiguriertes Modell fehlt: tinyllama reicht als stabiler Fallback.
            if (requireLocalModel
                && !IsPortableAiComplete(settings)
                && !HasPortableModel(settings, DefaultTextModel)
                && string.IsNullOrWhiteSpace(FindInstalledVisionModel(settings)))
            {
                _lastError = "Modell fehlt im KI-Ordner – AI_DOK-V01 vollständig mitkopieren (kein Download nötig).";
                return false;
            }

            if (await IsServerRespondingAsync(cancellationToken).ConfigureAwait(false))
                return true;

            // Immer fremde Ollama-Instanzen beenden – sonst sieht die App andere Modelle.
            if (!IsOurPortableServerRunning())
            {
                StopAllOllamaProcesses();
                await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
            }
            else if (await IsServerRespondingAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
            else
            {
                StopAllOllamaProcesses();
                await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
            }

            lock (StartLock)
            {
                if (IsOurPortableServerRunning())
                {
                    // Prozess lebt, Antwort prüfen außerhalb des Locks
                }
                else
                {
                    try
                    {
                        EnsureDirectoryLayout(settings);
                        var exe = GetOllamaExecutablePath(settings);
                        var kiDir = Path.GetDirectoryName(exe) ?? ResolveKiDirectory(settings);
                        var psi = new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = "serve",
                            WorkingDirectory = kiDir,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = false,
                            RedirectStandardError = false
                        };

                        ApplyPortableEnvironment(psi, settings);
                        _serverProcess = Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        return false;
                    }
                }
            }

            for (var i = 0; i < 90; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                if (await IsServerRespondingAsync(cancellationToken).ConfigureAwait(false))
                    return true;
            }

            _lastError = "Ollama startet zu langsam – bitte AI-starten.bat im Ordner "
                         + ResolveKiDirectory(settings) + " ausführen und erneut testen.";
            return false;
        }

        private static void ApplyPortableEnvironment(ProcessStartInfo psi, AiSettings settings)
        {
            psi.EnvironmentVariables["OLLAMA_MODELS"] = GetModelsDirectory(settings);
            psi.EnvironmentVariables["OLLAMA_HOME"] = GetHomeDirectory(settings);
            psi.EnvironmentVariables["OLLAMA_HOST"] = "127.0.0.1:11434";
            psi.EnvironmentVariables["OLLAMA_NUM_PARALLEL"] = "1";
            if (HasNvidiaGpu())
            {
                psi.EnvironmentVariables.Remove("OLLAMA_NUM_GPU");
                psi.EnvironmentVariables.Remove("OLLAMA_NO_GPU");
                psi.EnvironmentVariables.Remove("CUDA_VISIBLE_DEVICES");
            }
            else
            {
                psi.EnvironmentVariables["OLLAMA_NUM_GPU"] = "0";
                psi.EnvironmentVariables["OLLAMA_NO_GPU"] = "1";
                psi.EnvironmentVariables["CUDA_VISIBLE_DEVICES"] = "-1";
            }
        }

        internal static bool HasNvidiaGpu()
        {
            try
            {
                return File.Exists(@"C:\Windows\System32\nvidia-smi.exe")
                       || File.Exists(@"C:\Windows\System32\nvcuda.dll")
                       || File.Exists(@"C:\Windows\System32\nvml.dll");
            }
            catch
            {
                return false;
            }
        }

        private static bool IsOurPortableServerRunning()
        {
            try
            {
                return _serverProcess != null && !_serverProcess.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void StopAllOllamaProcesses()
        {
            try
            {
                if (_serverProcess != null)
                {
                    try
                    {
                        if (!_serverProcess.HasExited)
                            _serverProcess.Kill();
                    }
                    catch
                    {
                    }

                    _serverProcess = null;
                }

                foreach (var process in Process.GetProcessesByName("ollama"))
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
            }
        }

        private static async Task<bool> IsServerRespondingAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(3));
                    var response = await client.GetAsync(OllamaUrlHelper.DefaultBaseUrl + "/api/tags", cts.Token)
                        .ConfigureAwait(false);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
