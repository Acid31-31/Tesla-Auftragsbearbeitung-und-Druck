using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PDF_umbenenen.Services.Ai
{
    internal static class AiHelpKnowledge
    {
        private sealed class HelpEntry
        {
            public string[] Keywords { get; set; }
            public string Answer { get; set; }
            public int Priority { get; set; }
        }

        private static readonly HelpEntry[] Entries =
        {
            new HelpEntry
            {
                Priority = 100,
                Keywords = new[] { "manuell", "von hand", "selbst", "eingeben", "eintippen", "setzen" },
                Answer =
                    "Wasserzeichen – was geht manuell?\r\n\r\n" +
                    "Manuell in der App können Sie:\r\n" +
                    "• Wasserzeichen An/Aus schalten (Button im Bereich „Wasserzeichen“)\r\n" +
                    "• Die Auftragsnummer im Textfeld eintragen – sie wird auf jede PDF-Seite gedruckt\r\n" +
                    "• Positionen wählen (Dropdown „Daten-Position“ / „Auftragsnr.-Pos.“)\r\n" +
                    "• Intelligente Platzierung aktivieren – KI analysiert die PDF und setzt:\r\n" +
                    "  Stückzahl neben Zeichnungsnr., Lieferort oben Mitte, BG unten links, Auftragsnr. oben rechts\r\n" +
                    "• Im KI-Chat z. B. „Intelligente Wasserzeichen-Platzierung an“\r\n\r\n" +
                    "Nach Positionsänderung: erneut „Sortieren & umbenennen“ ausführen!\r\n\r\n" +
                    "Nicht manuell pro PDF einstellbar (kommt automatisch aus Excel):\r\n" +
                    "• Menge → Spalte „Requested Amount“\r\n" +
                    "• Lieferdatum → „Requested Delivery Date“\r\n" +
                    "• Lieferort → „Requested Delivery Location“\r\n\r\n" +
                    "Andere Werte? → Excel anpassen, dann erneut „Sortieren & umbenennen“.\r\n" +
                    "Ohne App-Wasserzeichen? → Schalter auf Aus, danach PDF extern bearbeiten (z. B. Acrobat)."
            },
            new HelpEntry
            {
                Priority = 95,
                Keywords = new[] { "position", "verschieb", "oben links", "oben rechts", "unten links", "unten rechts", "rechts mitte", "links mitte" },
                Answer =
                    "Wasserzeichen-Position (intelligent):\r\n\r\n" +
                    "1. Schalter „Intelligente Platzierung (KI analysiert PDF-Inhalt)“ aktivieren\r\n" +
                    "2. „Sortieren & umbenennen“ ausführen\r\n\r\n" +
                    "Die App liest vorhandenen Text in der PDF (Zeichnungsnummer, Schriftfeld) und platziert:\r\n" +
                    "• Stückzahl neben der Zeichnungsnummer\r\n" +
                    "• Lieferort oben Mitte\r\n" +
                    "• Baugruppennummer unten links\r\n" +
                    "• Auftragsnummer oben rechts\r\n\r\n" +
                    "Excel wird nicht geändert – nur die Position in der PDF.\r\n\r\n" +
                    "Im KI-Chat: „Intelligente Wasserzeichen-Platzierung an“"
            },
            new HelpEntry
            {
                Priority = 90,
                Keywords = new[] { "wasserzeichen", "watermark", "menge", "lieferdatum", "lieferort" },
                Answer =
                    "Wasserzeichen werden beim Klick auf „Sortieren & umbenennen“ automatisch in jede PDF geschrieben.\r\n\r\n" +
                    "Position (Standard: intelligente PDF-Analyse):\r\n" +
                    "• Stückzahl (Excel) → neben Zeichnungsnummer, wo freier Platz ist\r\n" +
                    "• Lieferort (Excel) → oben in der Mitte\r\n" +
                    "• Baugruppennummer (Excel/BG) → unten links, freier Platz\r\n" +
                    "• Auftragsnummer (Feld oben) → oben rechts\r\n" +
                    "• Lieferdatum → oben links (freier Platz)\r\n\r\n" +
                    "Schalter „Intelligente Platzierung“ im Bereich Wasserzeichen muss an sein.\r\n" +
                    "Danach erneut „Sortieren & umbenennen“!\r\n\r\n" +
                    "Voraussetzungen:\r\n" +
                    "• Schalter steht auf „An“\r\n" +
                    "• itextsharp.dll und BouncyCastle.Cryptography.dll liegen neben der EXE\r\n" +
                    "• Excel enthält die Spalten Drawing, Requested Amount, Requested Delivery Date, Requested Delivery Location\r\n\r\n" +
                    "Optional: Auftragsnummer manuell im Feld darüber eintragen."
            },
            new HelpEntry
            {
                Priority = 85,
                Keywords = new[] { "vorschau", "zusammenfügen", "zusammenfuegen", "drucken", "gesamt", "acrobat" },
                Answer =
                    "„Zusammenfügen & Vorschau“:\r\n\r\n" +
                    "1. Zuerst „Sortieren & umbenennen“ ausführen (PDFs heißen dann 01_..., 02_...)\r\n" +
                    "2. Dann „Zusammenfügen & Vorschau“ klicken\r\n" +
                    "3. Es entsteht _GESAMT_YYYYMMDD_HHMMSS.pdf im Ablage-Ordner\r\n\r\n" +
                    "Fehler mit Adobe Acrobat? Acrobat im Task-Manager beenden oder Edge als Standard-PDF-App setzen."
            },
            new HelpEntry
            {
                Priority = 80,
                Keywords = new[] { "drag", "drop", "ziehen", "ablegen", "laden", "einfügen", "einfuegen", "erkennt nicht" },
                Answer =
                    "Dateien laden per Drag & Drop:\r\n\r\n" +
                    "• PDF, Excel (.xlsx/.xls) oder ZIP ins Fenster ziehen\r\n" +
                    "• Erlaubt überall im Fenster (nicht nur auf die obere Karte)\r\n" +
                    "• Dateien landen im Ordner „Ablage“ neben der EXE\r\n" +
                    "• Excel wird als Daten.xlsx gespeichert\r\n\r\n" +
                    "Im Statusbereich sollte „Drag & Drop: X Datei(en) kopiert“ erscheinen."
            },
            new HelpEntry
            {
                Priority = 75,
                Keywords = new[] { "excel", "spalte", "drawing", "daten.xlsx" },
                Answer =
                    "Excel-Format – Spalten exakt so benennen:\r\n" +
                    "• Drawing\r\n" +
                    "• Requested Amount\r\n" +
                    "• Requested Delivery Date\r\n" +
                    "• Requested Delivery Location\r\n\r\n" +
                    "Datei per Drag & Drop laden oder als Daten.xlsx in den Ablage-Ordner legen."
            },
            new HelpEntry
            {
                Priority = 70,
                Keywords = new[] { "pdf", "nicht gefunden", "kein treffer", "match", "zuordnung", "zeichnung" },
                Answer =
                    "PDFs werden über Zeichnungsnummern mit Excel verknüpft.\r\n\r\n" +
                    "• PDF-Dateiname sollte die Zeichnungsnummer enthalten\r\n" +
                    "• „Vor Verarbeitung prüfen“ zeigt fehlende oder unsichere Zuordnungen\r\n" +
                    "• Statusbereich zeigt Diagnose (PDF-Schlüssel vs. Excel-Zellen)\r\n\r\n" +
                    "Tipp: Erst „Sortieren & umbenennen“, dann „Zusammenfügen & Vorschau“."
            },
            new HelpEntry
            {
                Priority = 60,
                Keywords = new[] { "ablage", "ordner", "speichern", "pfad" },
                Answer =
                    "Fester Arbeitsordner: „Ablage“ im gleichen Verzeichnis wie die EXE.\r\n" +
                    "Alle PDFs und die Excel-Datei (Daten.xlsx) werden dort abgelegt und verarbeitet."
            },
            new HelpEntry
            {
                Priority = 50,
                Keywords = new[] { "tesseract", "ocr", "ki", "offline", "assistent", "chat" },
                Answer =
                    "Hilfe-Chat in der App:\r\n\r\n" +
                    "• Offline-Hilfe mit festen Antworten und Regel-Engine\r\n" +
                    "• Tesseract-OCR für Schriftfelder ohne Textlayer\r\n\r\n" +
                    "Stellen Sie konkrete Fragen, z. B. „Wasserzeichen manuell?“ oder „Drag & Drop geht nicht“."
            }
        };

        public static string TryGetAnswer(string userMessage, string appContext)
        {
            var lower = (userMessage ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return "Bitte stellen Sie eine konkrete Frage, z. B. „Kann ich Wasserzeichen manuell setzen?“";

            HelpEntry best = null;
            var bestScore = 0;

            foreach (var entry in Entries)
            {
                var score = 0;
                foreach (var keyword in entry.Keywords)
                {
                    if (lower.Contains(keyword))
                        score += 10;
                }

                if (score > 0)
                    score += entry.Priority / 10;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }

            if (best != null && bestScore >= 10)
            {
                var sb = new StringBuilder(best.Answer);
                if (!string.IsNullOrWhiteSpace(appContext))
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine("Aktueller Stand:");
                    sb.AppendLine(appContext);
                }

                return sb.ToString().Trim();
            }

            return null;
        }

        public static string GetGeneralHelp()
        {
            return
                "Typische Schritte:\r\n" +
                "1. Alte Aufträge löschen (optional)\r\n" +
                "2. PDF + Excel per Drag & Drop laden\r\n" +
                "3. Wasserzeichen prüfen (An/Aus, Auftragsnummer)\r\n" +
                "4. Sortieren & umbenennen\r\n" +
                "5. Zusammenfügen & Vorschau\r\n\r\n" +
                "Fragen Sie z. B.: „Wasserzeichen manuell?“, „Excel-Spalten?“, „Vorschau geht nicht?“";
        }
    }
}
