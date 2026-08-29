using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Logging
{
    internal sealed class PlatformLogger : Contracts.IPlatformLogger
    {
        private readonly string _logDirectory;
        private readonly object _writeLock = new object();

        public PlatformLogger()
        {
            _logDirectory = Path.Combine(AppInfo.UserDataDirectory, "Logs", "Platform");
            Directory.CreateDirectory(_logDirectory);
        }

        public void Info(string category, string message, IDictionary<string, string> data = null)
            => Write("info", category, message, null, data);

        public void Warning(string category, string message, IDictionary<string, string> data = null)
            => Write("warning", category, message, null, data);

        public void Error(string category, string message, Exception ex = null, IDictionary<string, string> data = null)
            => Write("error", category, message, ex, data);

        public void Audit(AiDecisionRecord record)
        {
            if (record == null)
                return;

            Write(
                "audit",
                "decision",
                record.Decision,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["decisionId"] = record.DecisionId,
                    ["confidence"] = record.Confidence.ToString("0.###"),
                    ["provider"] = record.ProviderId ?? string.Empty,
                    ["rationale"] = record.Rationale ?? string.Empty
                });
        }

        public void Performance(string operation, TimeSpan duration, IDictionary<string, string> data = null)
        {
            var payload = data != null
                ? new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            payload["durationMs"] = ((int)duration.TotalMilliseconds).ToString();
            Write("performance", operation, operation, null, payload);
        }

        public void UserActivity(string action, string subject = null, IDictionary<string, string> data = null)
        {
            var payload = data != null
                ? new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(subject))
                payload["subject"] = subject;

            Write("user-activity", action ?? "action", action ?? string.Empty, null, payload);
        }

        private void Write(string level, string category, string message, Exception ex, IDictionary<string, string> data)
        {
            var line = DateTime.UtcNow.ToString("o")
                + " [" + level + "] "
                + category + ": "
                + (message ?? string.Empty);

            if (ex != null)
                line += " | " + ex.GetType().Name + ": " + ex.Message;

            if (data != null)
            {
                foreach (var pair in data)
                    line += " | " + pair.Key + "=" + pair.Value;
            }

            lock (_writeLock)
            {
                File.AppendAllText(Path.Combine(_logDirectory, level + ".log"), line + Environment.NewLine);
            }
        }
    }
}
