# Anleitung zur Installation von iTextSharp

Diese App benötigt iTextSharp zur korrekten Erzeugung von Wasserzeichen in PDFs.

## Installation über Visual Studio (empfohlen)

1. Öffne Visual Studio
2. Menü: **Tools** → **NuGet Package Manager** → **Package Manager Console**
3. Kopiere und füge ein:
   ```
   Install-Package iTextSharp -Version 5.5.13.3
   ```
4. Drücke **Enter**
5. Warte auf die Installation
6. Starte die App neu

## Alternative: Manuelle Installation

Wenn die Konsole nicht funktioniert:

1. Im Solution Explorer mit Rechtsklick auf das Projekt klicken
2. **Manage NuGet Packages** auswählen
3. **Browse** Tab aufmachen
4. Nach "iTextSharp" suchen
5. Version **5.5.13.3** auswählen
6. **Install** klicken

## Falls immer noch Fehler auftritt:

Kontakt: Die PDF-Modifikation funktioniert auch mit einfacher PDF-Struktur-Manipulation, 
aber iTextSharp macht es zuverlässiger. Die aktuelle Implementierung versucht, PDFs ohne 
externe Abhängigkeiten zu manipulieren, was bei einigen PDF-Typen funktioniert.

---

**Ohne iTextSharp**: Wasserzeichen werden durch direkten PDF-Stream-Eingriff erzeugt. Dies funktioniert bei vielen PDFs, aber nicht bei allen Typen.

**Mit iTextSharp**: Wasserzeichen funktionieren zu 100% auf allen PDF-Typen.
