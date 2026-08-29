using System;
using System.IO;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Rules
{
    /// <summary>
    /// Geschäftslogik ohne KI: Sortierung, Zuordnung, Validierung.
    /// </summary>
    internal sealed class BusinessRuleEngineModule : IRuleEngine
    {
        public RuleEvaluationResult Evaluate(DocumentAnalysisResult analysis)
        {
            if (analysis?.Fields == null)
                return NoMatch();

            if (analysis.Fields.IsAssembly)
            {
                return Match(
                    "assembly.no-material-watermark",
                    "skip-material",
                    100,
                    "Baugruppe: kein Material-Wasserzeichen",
                    BuildTarget(analysis, "Baugruppen"));
            }

            if (analysis.Classification?.Kind == DocumentKind.Invoice)
            {
                return Match(
                    "invoice.archive",
                    "move-to-folder",
                    90,
                    "Rechnung archivieren",
                    BuildTarget(analysis, "Rechnungen"),
                    BuildFileName(analysis, "RE"));
            }

            if (analysis.Classification?.Kind == DocumentKind.DeliveryNote)
            {
                return Match(
                    "delivery-note.archive",
                    "move-to-folder",
                    85,
                    "Lieferschein archivieren",
                    BuildTarget(analysis, "Lieferscheine"),
                    BuildFileName(analysis, "LS"));
            }

            if (analysis.Classification?.Kind == DocumentKind.PurchaseOrder)
            {
                return Match(
                    "purchase-order.archive",
                    "move-to-folder",
                    85,
                    "Bestellung archivieren",
                    BuildTarget(analysis, "Bestellungen"),
                    BuildFileName(analysis, "PO"));
            }

            if (analysis.Classification?.Kind == DocumentKind.MaterialCertificate)
            {
                return Match(
                    "material-certificate.archive",
                    "move-to-folder",
                    80,
                    "Materialzeugnis archivieren",
                    BuildTarget(analysis, "Materialzeugnisse"),
                    BuildFileName(analysis, "MZ"));
            }

            if (!string.IsNullOrWhiteSpace(analysis.Fields.DrawingNumber)
                && !string.IsNullOrWhiteSpace(analysis.Fields.MaterialThickness))
            {
                return Match(
                    "drawing.material.ok",
                    "apply-material-watermark",
                    80,
                    "Material und Dicke vorhanden",
                    BuildTarget(analysis, "Zeichnungen"),
                    SanitizeFileName(analysis.Fields.DrawingNumber) + ".pdf");
            }

            if (analysis.Classification?.Kind == DocumentKind.TechnicalDrawing)
            {
                return Match(
                    "drawing.needs-review",
                    "flag-review",
                    50,
                    "Zeichnung ohne vollständige Materialdaten",
                    BuildTarget(analysis, "Prüfung"),
                    BuildFileName(analysis, "ZW"));
            }

            return NoMatch();
        }

        private static RuleEvaluationResult NoMatch()
            => new RuleEvaluationResult { Matched = false, Priority = 0, RuleId = "none" };

        private static RuleEvaluationResult Match(
            string ruleId,
            string action,
            int priority,
            string message,
            string targetFolder = null,
            string targetFileName = null)
        {
            return new RuleEvaluationResult
            {
                Matched = true,
                RuleId = ruleId,
                Action = action,
                Priority = priority,
                Message = message,
                TargetFolder = targetFolder,
                TargetFileName = targetFileName
            };
        }

        private static string BuildTarget(DocumentAnalysisResult analysis, string subfolder)
        {
            var customer = analysis.Fields?.Customer;
            var project = analysis.Fields?.Project;
            var basePath = AppInfo.UserDataDirectory;
            if (!string.IsNullOrWhiteSpace(customer))
                basePath = Path.Combine(basePath, "Ablage", SanitizePath(customer));
            if (!string.IsNullOrWhiteSpace(project))
                basePath = Path.Combine(basePath, SanitizePath(project));
            return Path.Combine(basePath, subfolder);
        }

        private static string BuildFileName(DocumentAnalysisResult analysis, string prefix)
        {
            var drawing = analysis.Fields?.DrawingNumber;
            var revision = analysis.Fields?.Revision;
            var name = !string.IsNullOrWhiteSpace(drawing)
                ? drawing
                : Path.GetFileNameWithoutExtension(analysis.Document?.FilePath);
            if (!string.IsNullOrWhiteSpace(revision))
                name += "_Rev" + revision;
            return prefix + "_" + SanitizeFileName(name) + ".pdf";
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Dokument";

            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value.Trim();
        }

        private static string SanitizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Allgemein";

            foreach (var c in Path.GetInvalidPathChars())
                value = value.Replace(c, '_');

            return value.Trim();
        }
    }
}
