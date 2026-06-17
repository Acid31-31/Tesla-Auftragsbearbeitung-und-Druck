# 🚀 START HIER

## Sie sind ungeduldig? Ok, hier ist das Minimum:

### ⏱️ 3 Minuten Schnell-Setup

```powershell
# 1. NuGet Console öffnen
#    Tools → NuGet Package Manager → Package Manager Console

# 2. Copy-Paste diesen Befehl:
Install-Package iTextSharp -Version 5.5.13.3

# 3. Enter drücken, warten, fertig!

# 4. Projekt bauen (Strg + Shift + B)

# 5. App starten (F5)
```

### ✅ Das war's für die Installation!

---

## Jetzt können Sie testen:

### Schritt 1: Excel-Datei vorbereiten

Erstelle eine Excel-Datei mit diesen **EXAKTEN Spalten**:

| Drawing | Requested Amount | Requested Delivery Date | Requested Delivery Location |
|---------|-----------------|------------------------|---------------------------|
| Z001    | 100             | 2024-01-15            | Neuwied                    |
| Z002    | 50              | 2024-01-20            | Prüm                       |

**WICHTIG**: Spalten-Namen müssen EXAKT so sein!

### Schritt 2: PDFs zusammeln

- Sammle PDF-Dateien in EINEM Ordner
- PDF-Namen sollten Zeichnungsnummern enthalten (z.B. `Z001.pdf`)

### Schritt 3: App nutzen

1. App starten
2. "Excel-Datei auswählen" → deine Excel wählen
3. "PDF-Ordner auswählen" → dein PDF-Ordner wählen
4. "Verarbeitung starten" klicken
5. Warten...
6. **Fertig!**

---

## Was passiert jetzt?

```
Deine PDFs werden:
✅ Umbenannt (01_Z001.pdf, 02_Z002.pdf, etc.)
✅ Mit Wasserzeichen versehen:
   - Menge: 100
   - Lieferdatum: 2024-01-15
   - Lieferort: Neuwied
```

---

## Überprüfung

Öffne eine erzeugte PDF und überprüfe:
- **Rechts oben auf jeder Seite** sollten die Wasserzeichen-Daten stehen

Wenn ja → **Gratuliere! Es funktioniert!** 🎉

---

## Falls es NICHT funktioniert

### Status zeigt: "❌ iTextSharp nicht installiert"
→ Du hast Schritt 1 übersprungen! Installiere iTextSharp jetzt.

### Wasserzeichen nicht sichtbar
→ Überprüfe Excel-Spalten-Namen (müssen EXAKT sein!)

### PDFs nicht gefunden
→ Überprüfe Zeichnungsnummern in Excel vs. PDF-Namen

### Andere Fehler
→ Siehe **README.md** oder **SETUP_GUIDE.md**

---

## Ausführliche Dokumentation

Falls du mehr Details brauchst:

- **README.md** - Kompletter Überblick
- **SETUP_GUIDE.md** - Schritt-für-Schritt Anleitung
- **INSTALLATION_REQUIRED.md** - iTextSharp Installation im Detail

---

## Das war's!

Du bist jetzt bereit. Viel Erfolg! 🚀

**Nächster Schritt**: Probiere es jetzt aus!
