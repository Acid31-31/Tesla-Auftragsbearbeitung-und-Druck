# ⚠️ WICHTIG: iTextSharp Installation erforderlich

## Das Wasserzeichen-Feature benötigt iTextSharp!

Ohne iTextSharp funktionieren die Wasserzeichen **NICHT**!

---

## Installation (3 Schritte)

### Schritt 1: Visual Studio öffnen
- Öffne Visual Studio
- Das Projekt `PDF-umbenenen` sollte geöffnet sein

### Schritt 2: Package Manager Console öffnen
**Menü**: Tools → NuGet Package Manager → Package Manager Console

(Oder: Strg + ` im Visual Studio)

### Schritt 3: Command ausführen

Kopiere und füge folgendes in die Console ein:

```powershell
Install-Package iTextSharp -Version 5.5.13.3
```

Drücke **Enter** und warte auf Installation.

---

## Status überprüfen

Nach der Installation:
1. **Build** (Strg + Shift + B)
2. App starten
3. In der Statusleiste sollte erscheinen: "iTextSharp verfügbar"

---

## Falls Installation fehlschlägt

### Problem: "Access Denied"
- Starten Sie Visual Studio als Administrator

### Problem: "NuGet.exe not found"
- Nutze die GUI statt Console:
  1. Rechtsklick auf Projekt → "Manage NuGet Packages"
  2. Suche nach "iTextSharp"
  3. Version 5.5.13.3 auswählen
  4. Install klicken

### Problem: "Could not find iTextSharp"
- Stelle sicher, dass du online bist
- Gehe zu Tools → Options → NuGet Package Manager
- Überprüfe Package Sources

---

## ACHTUNG: Ohne iTextSharp

Wenn iTextSharp nicht installiert ist:
- ❌ Wasserzeichen funktionieren NICHT
- ❌ App zeigt Fehler beim Verarbeiten
- ✅ PDFs werden trotzdem umbenannt (aber ohne Wasserzeichen)

---

## NOTWENDIG zum Fortfahren!

```
┌─────────────────────────────────────┐
│  INSTALLATION VON ITEXTSHARP        │
│  IST ERFORDERLICH ZUM WEITERMACHEN  │
└─────────────────────────────────────┘
```

**Installiere jetzt iTextSharp, bevor du die App verwendest!**

---

Nach erfolgreichem Build: Siehe `QUICKSTART.md` für nächste Schritte.
