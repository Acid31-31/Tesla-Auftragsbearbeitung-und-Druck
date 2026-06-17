using System;
using System.Text;

namespace PDF_umbenenen.Services.Ai
{
    internal static class AiTextHelper
    {
        public static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            return builder.ToString();
        }

        public static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
                return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b))
                return a.Length;

            var n = a.Length;
            var m = b.Length;
            var d = new int[n + 1, m + 1];
            for (var i = 0; i <= n; i++)
                d[i, 0] = i;
            for (var j = 0; j <= m; j++)
                d[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        public static double SimilarityScore(string a, string b)
        {
            a = NormalizeKey(a);
            b = NormalizeKey(b);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;

            if (a == b)
                return 1.0;

            if (a.Contains(b) || b.Contains(a))
                return 0.85;

            var maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0)
                return 0;

            var distance = LevenshteinDistance(a, b);
            return Math.Max(0, 1.0 - (double)distance / maxLen);
        }
    }
}
