# Protokoll / Ist-Stand

## DOK-V01 – aktueller Stand der Anwendung

| | |
|---|---|
| **Dokument** | Betriebsstand zur internen Vorstellung |
| **Produkt** | DOK-V01 |
| **Version** | 1.1 **R83** |
| **Stand** | 01.10.2026 |
| **Status** | Im Einsatz / Testbetrieb (Demo 6 Monate) |
| **Ersteller** | Alexander Hölzer |

---

## 1. Zweck

**DOK-V01** bereitet technische Zeichnungen (PDF) für die Fertigung auf:

1. Auftrag laden (Excel, PDFs, ZIP, Bestell-PDF)
2. Zeichnungen **sortieren und umbenennen** (`01_…`, `02_…`)
3. Optional **Wasserzeichen** auf jede Zeichnung (Auftrag, Lieferort, Menge, Termin, BG, Material, Oberfläche)
4. **Prüfprotokoll** (Bestellung ↔ Zeichnungen)
5. **Gesamt-PDF** zum Drucken

Zielgruppe: Lohnfertigung Blech – aktuell fest verdrahtet für **Tesla** und **EcoMark**.

---

## 2. Kunden

| Kunde | Auftragseingang | Zeichnungen |
|---|---|---|
| **Tesla** | Excel (Zeichnungsnummer + Menge) | TSL-…, Spiegelbild mit Endung `-S` getrennt |
| **EcoMark** | Bestell-PDF (kein Excel nötig) | RT1 …, Lieferort fest „EcoMark“ |

Die App erkennt den Kunden an den Unterlagen. Sortierlogik und Stempel sind kundenspezifisch – Tesla-Funktionen werden bei EcoMark nicht erzwungen und umgekehrt.

---

## 3. Typischer Ablauf am Arbeitsplatz

1. Alte Bearbeitung löschen (optional)
2. Dateien per **Drag & Drop** laden (ZIP, Excel, PDFs, Bestell-PDF, auch aus Outlook)
3. Auftragsnummer prüfen
4. **Sortieren & umbenennen**
5. Optional **Zusammenfügen & Vorschau** → `_GESAMT_…pdf` drucken
6. Prüfprotokoll bei Bedarf

Die Ablage liegt lokal am PC (`%LOCALAPPDATA%\DOK-V01\Ablage\`). Desktop und Festinstallation bleiben getrennt von der Entwicklungsvorschau.

---

## 4. Was die App heute kann

### 4.1 Sortierung

- Zuordnung Zeichnungs-PDF ↔ Auftragszeile
- Reihenfolge wie in Excel bzw. Bestell-PDF
- **Spiegelbild:** `…-S` ist eine eigene Position, Mengen werden nicht mit der Basiszeichnung vermischt
- Defekte Zeichnung per Drag & Drop ersetzen, nur bei gleicher Zeichnungsnummer (mit Bestätigung)

### 4.2 Wasserzeichen (einschaltbar)

| Stempel | Inhalt |
|---|---|
| Auftrag / Lieferort / Menge / Termin / BG | aus Excel, Bestellung oder Eingabe |
| Material | z. B. `VA-4,0` aus Schriftfeld (lokal, ohne Cloud-KI) |
| RAL | Pulver, wenn im Schriftfeld erkannt (Einstellung) |
| ELOX | wenn erkannt (Einstellung) |
| Gebürstet | neben der Materialangabe (u. a. EcoMark) |
| Vernickelt / Oberflächenfeld | **nur Tesla:** Text aus „OBERFLÄCHENBEHANDLUNG“ |

Leeres Oberflächenfeld, Rauheitstabelle (`Rohe Oberfläche`, Rz, …) und Nachbarzellen im Schriftfeld werden **nicht** gestempelt.

Manuelle Korrektur der **Materialstärke** in der Vorschau ändert nur die Stärke. RAL, Gebürstet, Termin und übrige Stempel bleiben stehen.

### 4.3 Vorschau & Gesamt-PDF

- Zusammengeführte Datei mit durchgehenden Seitenzahlen
- Zeichnung in der Vorschau prüfen
- Liefertermin und Material nachträglich anpassen, ohne den ganzen Auftrag neu zu sortieren (wo vorgesehen)

### 4.4 Prüfprotokoll

Abgleich Bestellung / Excel mit den vorliegenden Zeichnungen: fehlende Positionen, Mengen, Termine. Bezeichnungen wie bei Tesla, auch im EcoMark-Modus.

---

## 5. Technik und Betrieb (kurz)

| Punkt | Stand |
|---|---|
| Plattform | Windows, .NET 4.8, lokale Desktop-App |
| Erkennung Schriftfeld | PDF-Text + optionales Tesseract-OCR, **ohne Internet / ohne Cloud-KI** |
| Updates | automatisch von GitHub, signierte EXE, SHA256-Prüfung |
| Installation | USB-Setup (`Programm installieren.exe`) oder Update in der App |
| Demo | **6 Monate** Testlaufzeit (mit R82/R83 neu gestartet, auch nach Ablauf) |
| Sicherheit | Code-Signatur, geschütztes Entpacken von ZIP-Dateien |

Aktuelles Release: [GitHub v1.1-R83](https://github.com/Acid31-31/Tesla-Auftragsbearbeitung-und-Druck/releases/tag/v1.1-R83)

USB / Programmstand:

- `Z:\Programierung\DOK-V01-Optimierung\USB-Version\DOK-V01-R83`
- `Z:\DOK-V01\DOK-V01-R83`

---

## 6. Letzte wesentliche Änderungen (R81–R83)

| Rev. | Inhalt |
|---|---|
| **R81** | Zeichnung ersetzen; Materialstärke in der Vorschau ändern, ohne andere Stempel zu verlieren |
| **R82** | Demo 6 Monate; EcoMark behält Stempel nach manueller Änderung; Protokoll-Texte vereinheitlicht |
| **R83** | Tesla-Oberflächenbehandlung (z. B. Vernickelt); leeres Feld nicht stempeln; Spiegelbild `-S` getrennt |

---

## 7. Was bewusst noch nicht enthalten ist

- **Lager-Kiosk** für Produktionsreste (separates Konzept, Stand 01.10.2026 – noch nicht gebaut)
- Kein ERP-Anschluss
- Keine Cloud-KI
- Keine Erfassung von Restteilen auf fremden PCs in der aktuellen Version

---

## 8. Nutzen im Betrieb (Ist)

| Vorher (manuell) | Mit DOK-V01 |
|---|---|
| Zeichnungen einzeln suchen, umbenennen, Stempel von Hand | Batch: laden → sortieren → fertige PDFs |
| Material / RAL / Oberfläche oft übersehen oder vertippt | Aus Schriftfeld, tesla-/kundenspezifisch |
| EcoMark und Tesla unterschiedliche Listen | Ein Programm, zwei Kundenprofile |
| Nach Korrektur „alles weg“ | Stempel bleiben (R81/R82) |

---

## 9. Offene Punkte / nächste Schritte (ohne Lager-Kiosk)

- [ ] R83 auf allen Arbeits-PCs per Update einspielen
- [ ] Festinstallation prüfen (nicht die Entwicklungsvorschau)
- [ ] Entscheidung Lager-Kiosk: siehe separates Protokoll `PROTOKOLL_Lager-Kiosk.pdf`
- [ ] Weitere Kunden nur nach Auftrag

---

## 10. Kenntnisnahme

| Rolle | Name | Datum | Unterschrift |
|---|---|---|---|
| Vorstellung | Alexander Hölzer | 01.10.2026 | |
| Arbeitgeber / Kenntnisnahme | | | |

---

*DOK-V01 · Ist-Stand 1.1 R83 · 01.10.2026*
