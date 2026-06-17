# Wasserzeichen-Feature - Konfiguration und Troubleshooting

## Wie die Wasserzeichen-Funktion funktioniert

Die App fügt automatisch folgende Informationen zu jeder PDF hinzu:
- **Menge** (aus "Requested Amount" Spalte)
- **Lieferdatum** (aus "Requested Delivery Date" Spalte)  
- **Lieferort** (aus "Requested Delivery Location" Spalte)

Die Wasserzeichen werden auf der **rechten Seite** jeder PDF-Seite positioniert.

## Excel-Struktur (erforderliche Spalten)

Ihre Excel-Datei benötigt folgende Spalten:

| Spalte | Bezeichnung | Beispiel |
|--------|-------------|----------|
| - | Drawing | Z001-001 |
| - | Requested Amount | 100 |
| - | Requested Delivery Date | 2024-01-15 |
| - | Requested Delivery Location | Neuwied |

## Installation von iTextSharp (optional, aber empfohlen)

**Für beste Ergebnisse**: Installiere iTextSharp über Visual Studio:

1. Tools → NuGet Package Manager → Package Manager Console
2. Eingabe:
   ```powershell
   Install-Package iTextSharp -Version 5.5.13.3
   ```
3. Enter drücken
4. App neu starten

**Ohne iTextSharp**: Die App funktioniert trotzdem mit Fallback-Methode, aber iTextSharp bietet bessere Kompatibilität.

## Troubleshooting

### Problem: Wasserzeichen erscheinen nicht

**Ursachen:**
1. iTextSharp nicht installiert (versuche Installation siehe oben)
2. PDF-Format wird von Fallback-Methode nicht unterstützt
3. Excel-Spalten haben nicht die exakten Namen

**Lösung:**
1. Überprüfe Excel-Spalten-Namen (exakt: "Requested Amount", etc.)
2. Teste mit einer Beispiel-PDF
3. Installiere iTextSharp
4. Schaue die Debug-Ausgabe an

### Problem: App stürzt ab beim Wasserzeichen hinzufügen

**Lösung:**
- Dies kann bei beschädigten PDFs passieren
- Versuche die PDF zu öffnen und neu zu speichern
- Kontaktiere Support mit der PDF-Datei

## Debug-Modus

Wenn Wasserzeichen nicht funktionieren:
1. Öffne Visual Studio
2. Unter den ProcessDocuments-Aufrufen findest du Status-Meldungen
3. "Wasserzeichen hinzugefügt zu: ..." = erfolgreich
4. "Warnung: Wasserzeichen-Fehler..." = Fehler

## Weitere Informationen

Die Lösung nutzt zwei Methoden:

**Methode 1: iTextSharp (falls installiert)**
- Professionelle PDF-Manipulation
- 100% zuverlässig
- Funktioniert mit allen PDF-Typen

**Methode 2: PDF-Stream-Manipulation (Fallback)**
- Funktioniert ohne externe Abhängigkeiten
- Funktioniert mit den meisten PDFs
- Kann bei komprimierten Streams fehlschlagen

---

Bei Fragen oder Problemen: Bitte Debug-Ausgaben und die verwendete PDF teilen!
