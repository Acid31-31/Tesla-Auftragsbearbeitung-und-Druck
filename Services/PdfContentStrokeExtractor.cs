using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PDF_umbenenen.Services
{
    /// <summary>
    /// Extrahiert Stroke-Rechtecke und Liniensegmente aus PDF-Content-Streams (Vektorgraphik).
    /// </summary>
    internal static class PdfContentStrokeExtractor
    {
        private const float MinRectSizePt = 1.5f;
        private const float LineThicknessPt = 1.2f;
        private const int MaxStrokeRects = 400;

        public static List<PdfTextRect> ExtractStrokeRects(string pdfPath, int pageNumber, float pageWidth, float pageHeight)
        {
            var result = new List<PdfTextRect>();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath) || !PdfWatermarkService.IsAvailable())
                return result;

            object reader = null;
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "iTextSharp", StringComparison.OrdinalIgnoreCase));
                if (assembly == null)
                    return result;

                var readerType = assembly.GetType("iTextSharp.text.pdf.PdfReader");
                if (readerType == null)
                    return result;

                reader = Activator.CreateInstance(readerType, pdfPath);
                var getPageContent = readerType.GetMethod("GetPageContent", new[] { typeof(int) });
                if (getPageContent == null)
                    return result;

                var content = getPageContent.Invoke(reader, new object[] { pageNumber }) as byte[];
                if (content == null || content.Length == 0)
                    return result;

                ParseContentStream(content, pageWidth, pageHeight, result);
            }
            catch
            {
            }
            finally
            {
                if (reader != null)
                {
                    try
                    {
                        reader.GetType().GetMethod("Close")?.Invoke(reader, null);
                    }
                    catch
                    {
                    }
                }
            }

            if (result.Count > MaxStrokeRects)
            {
                return result
                    .OrderByDescending(r => r.Width * r.Height)
                    .Take(MaxStrokeRects)
                    .ToList();
            }

            return result;
        }

        private static void ParseContentStream(byte[] content, float pageWidth, float pageHeight, List<PdfTextRect> target)
        {
            var numbers = new List<float>(8);
            float? curX = null;
            float? curY = null;

            foreach (var token in Tokenize(content))
            {
                if (token.IsNumber)
                {
                    numbers.Add(token.Number);
                    continue;
                }

                var op = token.Text;
                if (op == "re" && numbers.Count >= 4)
                {
                    var h = numbers[numbers.Count - 1];
                    var w = numbers[numbers.Count - 2];
                    var y = numbers[numbers.Count - 3];
                    var x = numbers[numbers.Count - 4];
                    numbers.RemoveRange(numbers.Count - 4, 4);
                    AddRect(target, x, y, w, h, pageWidth, pageHeight);
                    continue;
                }

                if (op == "l" && numbers.Count >= 2)
                {
                    var y = numbers[numbers.Count - 1];
                    var x = numbers[numbers.Count - 2];
                    numbers.RemoveRange(numbers.Count - 2, 2);
                    if (curX.HasValue && curY.HasValue)
                        AddLine(target, curX.Value, curY.Value, x, y, pageWidth, pageHeight);
                    curX = x;
                    curY = y;
                    continue;
                }

                if (op == "m" && numbers.Count >= 2)
                {
                    curY = numbers[numbers.Count - 1];
                    curX = numbers[numbers.Count - 2];
                    numbers.RemoveRange(numbers.Count - 2, 2);
                    continue;
                }

                if (op == "c" && numbers.Count >= 6)
                {
                    curY = numbers[numbers.Count - 1];
                    curX = numbers[numbers.Count - 2];
                    numbers.RemoveRange(numbers.Count - 6, 6);
                    continue;
                }

                if (op == "S" || op == "s" || op == "f" || op == "F" || op == "B" || op == "b" || op == "n")
                {
                    numbers.Clear();
                }
            }
        }

        private static void AddRect(
            List<PdfTextRect> target,
            float x,
            float y,
            float w,
            float h,
            float pageWidth,
            float pageHeight)
        {
            if (Math.Abs(w) < MinRectSizePt || Math.Abs(h) < MinRectSizePt)
                return;

            var left = Math.Min(x, x + w);
            var right = Math.Max(x, x + w);
            var bottom = Math.Min(y, y + h);
            var top = Math.Max(y, y + h);

            if (right < 0f || left > pageWidth || top < 0f || bottom > pageHeight)
                return;

            target.Add(new PdfTextRect
            {
                Text = "__stroke_rect__",
                Left = left,
                Right = right,
                Bottom = bottom,
                Top = top
            });
        }

        private static void AddLine(
            List<PdfTextRect> target,
            float x1,
            float y1,
            float x2,
            float y2,
            float pageWidth,
            float pageHeight)
        {
            var left = Math.Min(x1, x2) - LineThicknessPt;
            var right = Math.Max(x1, x2) + LineThicknessPt;
            var bottom = Math.Min(y1, y2) - LineThicknessPt;
            var top = Math.Max(y1, y2) + LineThicknessPt;

            if (right - left < MinRectSizePt && top - bottom < MinRectSizePt)
                return;

            if (right < 0f || left > pageWidth || top < 0f || bottom > pageHeight)
                return;

            target.Add(new PdfTextRect
            {
                Text = "__stroke_line__",
                Left = left,
                Right = right,
                Bottom = bottom,
                Top = top
            });
        }

        private struct ContentToken
        {
            public bool IsNumber;
            public float Number;
            public string Text;
        }

        private static IEnumerable<ContentToken> Tokenize(byte[] content)
        {
            var i = 0;
            while (i < content.Length)
            {
                while (i < content.Length && IsWhitespace(content[i]))
                    i++;

                if (i >= content.Length)
                    yield break;

                if (content[i] == '%')
                {
                    while (i < content.Length && content[i] != '\n' && content[i] != '\r')
                        i++;
                    continue;
                }

                if (content[i] == '(')
                {
                    i++;
                    while (i < content.Length)
                    {
                        if (content[i] == '\\' && i + 1 < content.Length)
                        {
                            i += 2;
                            continue;
                        }

                        if (content[i] == ')')
                        {
                            i++;
                            break;
                        }

                        i++;
                    }

                    continue;
                }

                if (content[i] == '<')
                {
                    i++;
                    while (i < content.Length && content[i] != '>')
                        i++;
                    if (i < content.Length)
                        i++;
                    continue;
                }

                var start = i;
                while (i < content.Length && !IsDelimiter(content[i]))
                    i++;

                var raw = System.Text.Encoding.ASCII.GetString(content, start, i - start).Trim();
                if (raw.Length == 0)
                    continue;

                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    yield return new ContentToken { IsNumber = true, Number = number };
                }
                else
                {
                    yield return new ContentToken { Text = raw };
                }
            }
        }

        private static bool IsWhitespace(byte b)
        {
            return b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;
        }

        private static bool IsDelimiter(byte b)
        {
            return IsWhitespace(b) || b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '/' || b == '%';
        }
    }
}
