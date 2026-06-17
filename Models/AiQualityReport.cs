using System.Collections.Generic;

namespace PDF_umbenenen.Models
{
    public sealed class AiQualityReport
    {
        public List<string> MissingPdfsForExcel { get; } = new List<string>();

        public List<string> UnmatchedPdfs { get; } = new List<string>();

        public List<string> AmbiguousMatches { get; } = new List<string>();

        public List<string> FieldMismatches { get; } = new List<string>();

        public List<AiMatchSuggestion> UncertainMatches { get; } = new List<AiMatchSuggestion>();

        public List<string> InfoMessages { get; } = new List<string>();

        public bool HasIssues =>
            MissingPdfsForExcel.Count > 0
            || UnmatchedPdfs.Count > 0
            || AmbiguousMatches.Count > 0
            || FieldMismatches.Count > 0
            || UncertainMatches.Count > 0;

        public string ToDisplayText()
        {
            var lines = new List<string> { "── KI-Qualitätsprüfung ──" };

            if (!HasIssues)
            {
                lines.Add("Keine Probleme erkannt.");
            }
            else
            {
                AppendSection(lines, "Excel-Zeilen ohne PDF", MissingPdfsForExcel);
                AppendSection(lines, "PDFs ohne Excel-Treffer", UnmatchedPdfs);
                AppendSection(lines, "Mehrdeutige Zuordnungen", AmbiguousMatches);
                AppendSection(lines, "Abweichungen Excel/PDF", FieldMismatches);

                if (UncertainMatches.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Unsichere KI-Zuordnungen:");
                    foreach (var m in UncertainMatches)
                    {
                        lines.Add(string.Format("  {0} -> Zeile {1} ({2:P0}): {3}",
                            m.PdfDisplayName, m.ExcelRowIndex, m.Confidence, m.Reason));
                    }
                }
            }

            foreach (var info in InfoMessages)
            {
                lines.Add(info);
            }

            lines.Add("────────────────────────");
            return string.Join(System.Environment.NewLine, lines);
        }

        private static void AppendSection(List<string> lines, string title, List<string> items)
        {
            if (items.Count == 0)
                return;

            lines.Add("");
            lines.Add(title + ":");
            foreach (var item in items)
                lines.Add("  • " + item);
        }
    }
}
