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

        public static string BuildChatRequestWithImages(
            string model,
            string systemPrompt,
            string userPrompt,
            IList<string> base64Images,
            bool jsonFormat)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(Escape(model)).Append("\",\"stream\":false");
            if (jsonFormat)
                sb.Append(",\"format\":\"json\"");
            sb.Append(",\"messages\":[");

            var first = true;
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":\"").Append(Escape(systemPrompt)).Append("\"}");
                first = false;
            }

            if (!first)
                sb.Append(',');
            sb.Append("{\"role\":\"user\",\"content\":\"").Append(Escape(userPrompt)).Append("\",\"images\":[");
            if (base64Images != null)
            {
                for (var i = 0; i < base64Images.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append('"').Append(base64Images[i] ?? string.Empty).Append('"');
                }
            }

            sb.Append("]}]}");
            return sb.ToString();
        }

        public static string ExtractResponseField(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            var messageContent = Regex.Match(
                json,
                "\"message\"\\s*:\\s*\\{[\\s\\S]*?\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.Singleline);
            if (messageContent.Success)
                return Unescape(messageContent.Groups[1].Value);

            var responseMatch = Regex.Match(json, "\"response\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (responseMatch.Success)
                return Unescape(responseMatch.Groups[1].Value);

            var contentMatch = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
            if (contentMatch.Success)
                return Unescape(contentMatch.Groups[1].Value);

            return json.Trim();
        }

        /// <summary>
        /// moondream liefert mit format=json oft unbrauchbare verschachtelte Strukturen.
        /// </summary>
        public static bool ShouldUseStrictJsonFormat(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return false;

            var model = modelName.Trim();
            if (model.IndexOf("moondream", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return true;
        }

        public static string StripMarkdownCodeFence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var trimmed = text.Trim();
            var fenced = Regex.Match(
                trimmed,
                "```(?:json)?\\s*([\\s\\S]*?)```",
                RegexOptions.IgnoreCase);
            if (fenced.Success)
                return fenced.Groups[1].Value.Trim();

            return trimmed;
        }

        public static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var source = StripMarkdownCodeFence(text);
            var start = source.IndexOf('{');
            if (start < 0)
                return string.Empty;

            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = start; i < source.Length; i++)
            {
                var c = source[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return string.Empty;
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

        public static bool GetJsonBool(string json, string key, bool defaultValue)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return defaultValue;

            return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetJsonNullableDouble(string json, string key, out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            var nullMatch = Regex.Match(
                json,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*null",
                RegexOptions.IgnoreCase);
            if (nullMatch.Success)
                return true;

            var numberMatch = Regex.Match(
                json,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)",
                RegexOptions.IgnoreCase);
            if (!numberMatch.Success)
                return false;

            double parsed;
            if (!double.TryParse(numberMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return false;

            value = parsed;
            return true;
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
