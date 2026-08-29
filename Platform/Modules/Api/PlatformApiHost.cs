using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Api
{
    internal sealed class PlatformApiHost : IPlatformApi, ILocalApiHost, IPluginApiHost, IImportApi, IExportApi
    {
        private readonly IAiOrchestrator _orchestrator;
        private readonly IKnowledgeStore _knowledge;
        private readonly IPlatformLogger _logger;
        private readonly ConcurrentDictionary<string, Func<IDictionary<string, string>, string>> _plugins =
            new ConcurrentDictionary<string, Func<IDictionary<string, string>, string>>(StringComparer.OrdinalIgnoreCase);

        private HttpListener _listener;
        private CancellationTokenSource _cts;

        public PlatformApiHost(IAiOrchestrator orchestrator, IKnowledgeStore knowledge, IPlatformLogger logger)
        {
            _orchestrator = orchestrator;
            _knowledge = knowledge;
            _logger = logger;
        }

        public bool IsRunning { get; private set; }

        public void Start(int port = 8765)
        {
            if (IsRunning)
                return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            _listener.Start();
            IsRunning = true;
            Task.Run(() => ListenLoop(_cts.Token));
            _logger.Info("api", "Local REST API gestartet", new Dictionary<string, string> { ["port"] = port.ToString() });
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
            }
            finally
            {
                IsRunning = false;
            }
        }

        public string Invoke(string method, IDictionary<string, string> parameters)
        {
            parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            switch ((method ?? string.Empty).ToLowerInvariant())
            {
                case "analyze":
                    return AnalyzeSync(parameters.TryGetValue("file", out var file) ? file : string.Empty);
                case "status":
                    return "ok";
                default:
                    return "unknown-method";
            }
        }

        public void RegisterPlugin(string pluginId, Func<IDictionary<string, string>, string> handler)
        {
            if (!string.IsNullOrWhiteSpace(pluginId) && handler != null)
                _plugins[pluginId] = handler;
        }

        public string InvokePlugin(string pluginId, IDictionary<string, string> parameters)
        {
            return _plugins.TryGetValue(pluginId, out var handler)
                ? handler(parameters ?? new Dictionary<string, string>())
                : "plugin-not-found";
        }

        public int ImportKnowledge(string filePath)
        {
            if (!File.Exists(filePath))
                return 0;

            var count = 0;
            foreach (var line in File.ReadAllLines(filePath))
            {
                var parts = line.Split(new[] { '\t', ';', '|' }, 3);
                if (parts.Length < 3)
                    continue;

                _knowledge.Upsert(new KnowledgeEntry
                {
                    Category = parts[0].Trim(),
                    Key = parts[1].Trim(),
                    Value = parts[2].Trim()
                });
                count++;
            }

            return count;
        }

        public int ImportRules(string filePath)
        {
            return ImportKnowledge(filePath);
        }

        public string ExportKnowledge(string targetFolder)
        {
            Directory.CreateDirectory(targetFolder);
            var path = Path.Combine(targetFolder, "knowledge-export.txt");
            var lines = _knowledge.Search(null, null)
                .Select(e => e.Category + "\t" + e.Key + "\t" + e.Value);
            File.WriteAllLines(path, lines, Encoding.UTF8);
            return path;
        }

        public string ExportAuditLog(string targetFolder)
        {
            Directory.CreateDirectory(targetFolder);
            var source = Path.Combine(AppInfo.UserDataDirectory, "Logs", "Platform", "audit.log");
            var target = Path.Combine(targetFolder, "audit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            if (File.Exists(source))
                File.Copy(source, target, true);
            return target;
        }

        private async Task ListenLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext context = null;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                    await HandleRequest(context).ConfigureAwait(false);
                }
                catch when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    context?.Response?.Close();
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
            string body;

            try
            {
                if (path == "api/analyze")
                {
                    var file = request.QueryString["file"];
                    body = AnalyzeSync(file);
                }
                else if (path == "api/status")
                {
                    body = "{\"status\":\"ok\",\"offline\":true}";
                }
                else if (path == "api/export/knowledge")
                {
                    var folder = Path.Combine(AppInfo.UserDataDirectory, "Exports");
                    body = ExportKnowledge(folder);
                }
                else
                {
                    body = "{\"error\":\"not-found\"}";
                    response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                body = "{\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}";
                response.StatusCode = 500;
            }

            var buffer = Encoding.UTF8.GetBytes(body ?? string.Empty);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            response.OutputStream.Close();
        }

        private string AnalyzeSync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return "{\"success\":false,\"error\":\"file-not-found\"}";

            var analysis = _orchestrator.AnalyzeDocumentAsync(filePath).GetAwaiter().GetResult();
            var rules = _orchestrator.EvaluateRulesAsync(analysis).GetAwaiter().GetResult();
            var decision = _orchestrator.DecideAsync(analysis, rules).GetAwaiter().GetResult();

            return "{\"success\":true,\"decision\":\"" + (decision?.Decision ?? string.Empty)
                   + "\",\"confidence\":" + (decision?.Confidence.ToString("0.###") ?? "0") + "}";
        }
    }
}
