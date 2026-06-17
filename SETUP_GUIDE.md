# 🚀 SETUP - Wasserzeichen Feature

## Was Sie tun müssen (WICHTIG!)

### ✅ Schritt 1: iTextSharp installieren

Das ist **ZWINGEND erforderlich** für Wasserzeichen!

1. Öffne Visual Studio
2. **Tools → NuGet Package Manager → Package Manager Console**
3. Kopiere-Einfüge:
   ```powershell
   Install-Package iTextSharp -Version 5.5.13.3
   ```
4. **Enter** drücken
5. Warten auf Installation (1-2 Minuten)

### ✅ Schritt 2: Projekt neu bauen

Nach Installation:
1. **Build → Rebuild Solution** (Strg + Shift + B)
2. Überprüfe, dass keine Fehler auftauchten

### ✅ Schritt 3: App starten und testen

1. **Debug → Start Without Debugging** (Strg + F5)
2. Bei Start sollte angezeigt werden: **"Wasserzeichen-Status: ✅ AKTIV"**
3. Teste mit Test-Dateien

---

## Excel-Datei vorbereiten

Ihre Excel-Datei braucht folgende **EXAKTE Spalten-Namen**:

```
| Drawing | Requested Amount | Requested Delivery Date | Requested Delivery Location |
|---------|-----------------|------------------------|---------------------------|
| Z001    | 100             | 2024-01-15            | Neuwied                    |
| Z002    | 50              | 2024-01-20            | Prüm                       |
```

**Spalten-Namen MÜSSEN exakt so sein** (Groß-/Kleinschreibung, Leerzeichen!)

---

## PDFs vorbereiten

1. PDFs sollten sich in **EINEM Ordner** befinden
2. Dateinamen sollten Zeichnungsnummern enthalten (z.B. `Z001.pdf`)
3. PDFs können `.pdf` in jede Größe sein

---

## Verwendung

### Start:
1. App starten
2. Excel-Datei auswählen
3. PDF-Ordner auswählen
4. Klick: **"Verarbeitung starten"**

### Was passiert:
```
1. PDFs werden gelesen und gematcht
2. PDFs werden umbenannt (01_..., 02_..., etc.)
3. Wasserzeichen werden hinzugefügt:
   - Menge
   - Lieferdatum
   - Lieferort
```

### Output:
```
E:\Dein\PDF\Ordner\
├─ 01_Zeichnung001.pdf     ← mit Wasserzeichen
├─ 02_Zeichnung002.pdf     ← mit Wasserzeichen
├─ 03_Zeichnung003.pdf     ← mit Wasserzeichen
└─ ...
```

---

## Überprüfen, ob Wasserzeichen funktionieren

Nach Verarbeitung:
1. Öffne eine erzeugte PDF mit Adobe Reader
2. Schaue **rechts oben auf jeder Seite**
3. Du solltest sehen:
   ```
   Menge: 100
   Lieferdatum: 2024-01-15
   Lieferort: Neuwied
   ```

---

## Falls Wasserzeichen NICHT sichtbar

### Problem 1: iTextSharp nicht installiert
- Status zeigt: "❌ iTextSharp nicht installiert"
- **Lösung**: Folge Schritt 1 oben

### Problem 2: Excel-Spalten falsch
- Überprüfe Spalten-Namen (müssen EXAKT sein)
- Check für Leerzeichen am Ende

### Problem 3: PDFs nicht gematcht
- Diagnose-Output zeigt, welche PDFs nicht gefunden wurden
- Überprüfe Zeichnungsnummern in Excel vs. PDF-Namen

### Problem 4: Fehler beim Wasserzeichen
- Überprüfe ob PDF beschädigt ist
- Versuche PDF zu öffnen und neu zu speichern

---

##  Fehler-Behebung

### Build fehlgeschlagen
- **Visual Studio neu starten**
- **Projekt erneut bauen** (Strg + Shift + B)

### iTextSharp Installation schlägt fehl
1. **Visual Studio als Administrator starten**
2. **Tools → Options → NuGet Package Manager**
3. Überprüfe "Package Sources"
4. Versuche erneut

### App zeigt Fehler
- Lese die Statusausgabe genau
- Copy-Paste die Fehler-Nachricht
- Überprüfe Excel-Spalten

---

## ✅ Checkliste

Bevor du loslegst:

- [ ] iTextSharp installiert (Package Manager Console)
- [ ] Projekt neu gebaut (kein Fehler)
- [ ] App zeigt "✅ AKTIV" im Status
- [ ] Excel-Datei hat korrekte Spalten
- [ ] PDF-Ordner existiert und hat PDFs
- [ ] Zeichnungsnummern in Excel matchen PDFs

---

## Bereit?

Wenn alles ok ist:
1. **Starten Sie die App**
2. **Wählen Sie Excel und PDF-Ordner**
3. **Klick: "Verarbeitung starten"**
4. **✅ Fertig!**

---

**Status**: 🟢 **READY TO USE**

**Bei Fragen oder Problemen**: Siehe Fehler-Behebung oben oder kontaktiere den Support.
