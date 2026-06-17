using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PDF_umbenenen.Services.Ai
{
    internal static class SimpleJsonHelper
    {
        public static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        public static string BuildGenerateRequest(string model, string prompt, bool jsonFormat)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"model\":\"").Append(Escape(model)).Append("\",");
            sb.Append("\"prompt\":\"").Append(Escape(prompt)).Append("\",");
            sb.Append("\"stream\":false");
            if (jsonFormat)
                sb.Append(",\"format\":\"json\"");
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildChatRequest(string model, IList<(string role, string content)> messages)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(Escape(model)).Append("\",\"stream\":false,\"messages\":[");
            for (var i = 0; i < messages.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append("{\"role\":\"").Append(Escape(messages[i].role)).Append("\",");
                sb.Append("\"content\":\"").Append(Escape(messages[i].content)).Append("\"}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        public static string ExtractResponseField(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            var responseMatch = Regex.Match(json, "\"response\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (responseMatch.Success)
                return Unescape(responseMatch.Groups[1].Value);

            var contentMatch = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (contentMatch.Success)
                return Unescape(contentMatch.Groups[1].Value);

            return json.Trim();
        }

        public static string GetJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return match.Success ? Unescape(match.Groups[1].Value) : string.Empty;
        }

        public static double GetJsonDouble(string json, string key, double defaultValue)
        {
            var match = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)");
            if (!match.Success)
                return defaultValue;

            double value;
            return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        public static int GetJsonInt(string json, string key, int defaultValue)
        {
            var match = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9]+)");
            if (!match.Success)
                return defaultValue;

            int value;
            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
    }
}
