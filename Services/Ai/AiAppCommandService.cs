using System;
using System.Text;

namespace PDF_umbenenen.Services.Ai
{
    internal sealed class AiCommandResult
    {
        public bool Executed { get; set; }

        public string Message { get; set; }
    }

    internal static class AiAppCommandService
    {
        public static AiCommandResult TryExecute(string userMessage, WatermarkSettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(userMessage))
                return NotExecuted();

            var lower = userMessage.ToLowerInvariant();
            if (!ContainsAny(lower, "wasserzeichen", "auftragsnummer", "position", "verschieb", "setz", "ändere", "aendere", "move"))
                return NotExecuted();

            if (ContainsAny(lower, "auftragsnummer"))
            {
                var orderPosition = ParseCornerPosition(lower);
                if (orderPosition.HasValue)
                {
                    settings.OrderPosition = orderPosition.Value;
                    return Executed(
                        "Auftragsnummer-Position geändert auf: " + settings.OrderPositionDisplayName + ".\r\n" +
                        "Wichtig: Erneut „Sortieren & umbenennen“ ausführen, damit die PDFs neu geschrieben werden.");
                }
            }

            if (ContainsAny(lower, "wasserzeichen", "menge", "lieferdatum", "lieferort", "daten", "stückzahl", "stueckzahl", "baugruppe", "zeichnung"))
            {
                if (ContainsAny(lower, "intelligent", "automatisch", "ki ", " smart", "smart", "pdf analys"))
                {
                    if (ContainsAny(lower, "aus", "deaktiv", "manuell"))
                    {
                        settings.UseSmartPlacement = true;
                        return Executed(
                            "Intelligente Platzierung bleibt immer aktiv und kann nicht ausgeschaltet werden.");
                    }

                    settings.UseSmartPlacement = true;
                    return Executed(
                        "Intelligente Wasserzeichen-Platzierung aktiviert.\r\n" +
                        "Regeln: Stückzahl neben Zeichnungsnr., Lieferort oben Mitte, BG unten links, Auftragsnr. oben rechts.\r\n" +
                        "Erneut „Sortieren & umbenennen“ ausführen.");
                }

                var dataPosition = ParseDataPosition(lower);
                if (dataPosition.HasValue)
                {
                    settings.DataPosition = dataPosition.Value;
                    return Executed(
                        "Wasserzeichen-Position (Menge/Datum/Ort) geändert auf: " + settings.DataPositionDisplayName + ".\r\n" +
                        "Wichtig: Erneut „Sortieren & umbenennen“ ausführen, damit die PDFs neu geschrieben werden.");
                }
            }

            if (ContainsAny(lower, "position", "wo", "standard"))
            {
                var sb = new StringBuilder();
                sb.AppendLine("Aktuelle Einstellung:");
                sb.AppendLine("• Intelligente Platzierung: immer an");
                sb.AppendLine("• Daten (Fallback): " + settings.DataPositionDisplayName);
                sb.AppendLine("• Auftragsnummer: Oben rechts (fest)");
                return Executed(sb.ToString().Trim());
            }

            return NotExecuted();
        }

        private static WatermarkDataPosition? ParseDataPosition(string lower)
        {
            var vertical = ContainsAny(lower, "senkrecht", "mitte");
            var left = ContainsAny(lower, "links", "linken", "left");
            var right = ContainsAny(lower, "rechts", "rechten", "right");
            var top = ContainsAny(lower, "oben", "ober", "top");
            var bottom = ContainsAny(lower, "unten", "unter", "bottom");

            if (left && vertical)
                return WatermarkDataPosition.LeftMiddleVertical;
            if (right && vertical)
                return WatermarkDataPosition.RightMiddleVertical;
            if (top && left)
                return WatermarkDataPosition.TopLeft;
            if (top && right)
                return WatermarkDataPosition.TopRight;
            if (bottom && left)
                return WatermarkDataPosition.BottomLeft;
            if (bottom && right)
                return WatermarkDataPosition.BottomRight;

            if (left)
                return WatermarkDataPosition.LeftMiddleVertical;
            if (right)
                return WatermarkDataPosition.RightMiddleVertical;
            if (top)
                return WatermarkDataPosition.TopLeft;
            if (bottom)
                return WatermarkDataPosition.BottomRight;

            return null;
        }

        private static OrderNumberPosition? ParseCornerPosition(string lower)
        {
            var left = ContainsAny(lower, "links", "linken");
            var right = ContainsAny(lower, "rechts", "rechten");
            var top = ContainsAny(lower, "oben", "ober");
            var bottom = ContainsAny(lower, "unten", "unter");

            if (top && left)
                return OrderNumberPosition.TopLeft;
            if (top && right)
                return OrderNumberPosition.TopRight;
            if (bottom && left)
                return OrderNumberPosition.BottomLeft;
            if (bottom && right)
                return OrderNumberPosition.BottomRight;

            if (left)
                return OrderNumberPosition.TopLeft;
            if (right)
                return OrderNumberPosition.TopRight;
            if (top)
                return OrderNumberPosition.TopRight;
            if (bottom)
                return OrderNumberPosition.BottomRight;

            return null;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            foreach (var value in values)
            {
                if (text.Contains(value))
                    return true;
            }

            return false;
        }

        private static AiCommandResult Executed(string message)
        {
            return new AiCommandResult { Executed = true, Message = message };
        }

        private static AiCommandResult NotExecuted()
        {
            return new AiCommandResult { Executed = false };
        }
    }
}
