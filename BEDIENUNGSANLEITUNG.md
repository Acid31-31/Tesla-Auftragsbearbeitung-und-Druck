# Bedienungsanleitung – Tesla Auftragsbearbeitung & Druck

## 1. Zweck der App
Die App verarbeitet Zeichnungs-PDFs auf Basis einer Excel-Datei, sortiert und benennt sie um, fügt optional Wasserzeichen hinzu und kann alle bearbeiteten PDFs zu einer Gesamtdatei zusammenführen.

---

## 2. Voraussetzungen
- Windows-PC mit .NET Framework 4.8
- Excel-Datei (`.xlsx` oder `.xls`) mit Auftragsdaten
- Zeichnungsdateien als PDF (`.pdf`) oder als ZIP-Datei mit PDFs
- Optional für Wasserzeichen: `itextsharp.dll` und `BouncyCastle.Cryptography.dll` im gleichen Ordner wie die EXE

---

## 3. Programmstart
1. App starten (`Tesla-Auftragsbearbeitung-und-Druck.exe`)
2. Die Anwendung öffnet im Vollbild im Dunkelmodus.
3. Der feste Ablage-Ordner wird automatisch verwendet.

---

## 4. Dateien in die App laden
Sie haben drei Möglichkeiten:

### A) Einzeldateien per Drag & Drop
- Excel-Datei und PDFs direkt ins Fenster ziehen.

### B) Ganzen Ordner per Drag & Drop
- Einen Ordner mit PDFs/Excel ins Fenster ziehen.

### C) ZIP-Datei per Drag & Drop (ohne manuelles Entpacken)
- ZIP-Datei direkt ins Fenster ziehen.
- Die App extrahiert automatisch:
  - PDFs
  - Excel-Datei (`.xlsx`/`.xls`)

Hinweise:
- Die erkannte Excel-Datei wird als `Daten.xlsx` im Ablage-Ordner gesetzt.
- Unter **„Unterordner einbeziehen“** kann gesteuert werden, ob Unterordner berücksichtigt werden.

---

## 5. Wasserzeichen einstellen
Im Bereich **„Wasserzeichen“**:
- **☑ An** = Wasserzeichen werden auf bearbeitete PDFs geschrieben
- **☐ Aus** = keine Wasserzeichen

Zusätzlich kann eine **Auftragsnummer** eingetragen werden.

---

## 6. Verarbeitung starten (Sortieren & umbenennen)
1. Prüfen, ob Excel-Datei und PDFs geladen sind.
2. Auf **„Sortieren & umbenennen“** klicken.
3. Die App:
   - liest Excel-Daten,
   - matched PDFs mit Zeichnungsnummern,
   - benennt Dateien in Reihenfolge um (z. B. `01_...pdf`, `02_...pdf`),
   - ergänzt optional Wasserzeichen.
4. Fortschritt und Ergebnisse stehen im Feld **Statusmeldungen**.

Bei Bedarf kann mit **„Abbrechen“** gestoppt werden.

---

## 7. Zusammenfügen & Vorschau
Mit **„Zusammenfügen & Vorschau“**:
- werden alle bearbeiteten PDFs in Sortierreihenfolge zusammengeführt,
- eine Datei `_GESAMT_YYYYMMDD_HHMMSS.pdf` erstellt,
- die Vorschau zum Drucken geöffnet.

---

## 8. Bearbeitete Aufträge löschen
Mit **„Bearbeitete Aufträge löschen“**:
- werden bearbeitete Dateien im Ablage-Ordner entfernt:
  - nummerierte PDFs (`01_...pdf`, `2_...pdf`, …)
  - `_GESAMT_...pdf`
- es erscheint vorher eine Sicherheitsabfrage.

Damit kann der nächste Auftrag sauber neu geladen und bearbeitet werden.

---

## 9. Typischer Arbeitsablauf
1. Alte bearbeitete Aufträge löschen
2. Neue ZIP/Excel/PDFs per Drag & Drop einfügen
3. Wasserzeichen prüfen (An/Aus)
4. **Sortieren & umbenennen**
5. Optional **Zusammenfügen & Vorschau**
6. Drucken

---

## 10. Fehlerbehebung

### App startet nicht
- Prüfen, ob alle Programmdateien im gleichen Ordner liegen.
- EXE erneut aus `bin\Debug` oder USB-Version übernehmen.

### Keine PDFs gefunden
- Sicherstellen, dass PDFs wirklich im Ablage-Ordner liegen.
- Option „Unterordner einbeziehen“ bei Bedarf aktivieren.

### Wasserzeichen fehlen
- Prüfen, ob Wasserzeichen auf **An** steht.
- Prüfen, ob `itextsharp.dll` und `BouncyCastle.Cryptography.dll` neben der EXE liegen.

### Excel wird nicht erkannt
- Nur `.xlsx` oder `.xls` verwenden.
- Datei erneut per Drag & Drop einfügen.

---

## 11. Hinweise zur USB-Version
Wenn Änderungen an der App gemacht werden, den Ordner
`USB-Version\Tesla-Startpaket\Programmdateien`
immer mit den aktuellen Build-Dateien aktualisieren.
