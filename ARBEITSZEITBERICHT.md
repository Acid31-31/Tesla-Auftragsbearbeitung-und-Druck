# Arbeitszeitbericht & Wertermittlung – DOK-V01

**Produkt:** DOK-V01 (PDF-Auftragsbearbeitung, Sortierung, Analyse)  
**Version:** 1.1 R04 · Build 17.06.2026  
**Entwickler:** Alexander Hölzer, Kruft  
**Berichtsdatum:** 17.06.2026  

---

## 1. Gesamtübersicht

| Kennzahl | Wert |
|----------|------|
| Projektbeginn | 03.06.2026, 18:30 Uhr |
| Erste technische Fertigstellung | 11.06.2026, 19:05 Uhr |
| Erweiterung R03/R04 (KI, Rebrand, Deployment) | 12.06.–17.06.2026 |
| **Gesamte dokumentierte Arbeitszeit** | **37:50 h** |
| Quellcode (C#) | ca. 11.400 Zeilen / 71 Dateien |
| UI (XAML) | ca. 2.600 Zeilen / 21 Dateien |
| Build-/Deploy-Skripte | ca. 520 Zeilen / 12 Dateien |

---

## 2. Detaillierte Zeiterfassung

### Phase A – Kern-App (03.06.–11.06.2026): 18:45 h

| Nr. | Datum | Von | Bis | Dauer | Tätigkeit |
|----:|---|---:|---:|---:|---|
| 1 | 03.06.2026 | 18:30 | 19:45 | 1:15 h | Projektstart, Lösung/Projektstruktur, Basis-Konfiguration |
| 2 | 03.06.2026 | 20:00 | 21:20 | 1:20 h | Grundaufbau WPF-Oberfläche, Dark-Theme, Startlayout |
| 3 | 04.06.2026 | 08:30 | 10:15 | 1:45 h | Drag&Drop, Ablagekonzept, Dateiannahme |
| 4 | 04.06.2026 | 10:30 | 12:00 | 1:30 h | Excel-/PDF-Matching, erste Verarbeitung |
| 5 | 04.06.2026 | 13:00 | 14:10 | 1:10 h | Statusausgabe, Fortschritt, Abbruch |
| 6 | 05.06.2026 | 09:00 | 10:30 | 1:30 h | Wasserzeichen-Service (iTextSharp) |
| 7 | 05.06.2026 | 10:45 | 12:15 | 1:30 h | Wasserzeichen in Hauptprozess |
| 8 | 05.06.2026 | 13:15 | 14:00 | 0:45 h | Tests/Korrekturen Wasserzeichen |
| 9 | 09.06.2026 | 16:45 | 18:20 | 1:35 h | Sortieren & Umbenennen finalisiert |
| 10 | 09.06.2026 | 18:35 | 20:00 | 1:25 h | Zusammenfügen & Vorschau (_GESAMT_-PDF) |
| 11 | 09.06.2026 | 20:15 | 21:00 | 0:45 h | Fehleranalyse, Stabilisierung |
| 12 | 10.06.2026 | 16:30 | 17:40 | 1:10 h | Löschen bearbeiteter Aufträge |
| 13 | 10.06.2026 | 17:55 | 19:00 | 1:05 h | ZIP-Import per Drag&Drop |
| 14 | 10.06.2026 | 19:10 | 19:55 | 0:45 h | USB-Version, Startstruktur |
| 15 | 11.06.2026 | 17:40 | 18:20 | 0:40 h | App-Logo/Icon integriert |
| 16 | 11.06.2026 | 18:30 | 19:05 | 0:35 h | Startproblem behoben, Finalisierung |

### Phase B – Dokumentation (12.06.2026): 0:35 h

| Nr. | Datum | Von | Bis | Dauer | Tätigkeit |
|----:|---|---:|---:|---:|---|
| 17 | 12.06.2026 | 14:20 | 14:40 | 0:20 h | Bedienungsanleitung |
| 18 | 12.06.2026 | 14:45 | 15:00 | 0:15 h | Erster Arbeitszeitbericht |

### Phase C – KI-Assistent & UI R03 (14.06.–17.06.2026): 12:30 h

| Nr. | Datum | Von | Bis | Dauer | Tätigkeit |
|----:|---|---:|---:|---:|---|
| 19 | 14.06.2026 | 09:00 | 13:00 | 4:00 h | KI-Infrastruktur: Ollama-Backend, Orchestrator, Einstellungen |
| 20 | 14.06.2026 | 14:00 | 18:30 | 4:30 h | PDF-Textextraktion, Matching, Qualitätsprüfung, Chat |
| 21 | 17.06.2026 | 09:00 | 12:00 | 3:00 h | Menüleiste, Theme-UI, Wasserzeichen-Dialog, intelligente Platzierung |
| 22 | 17.06.2026 | 12:00 | 13:00 | 1:00 h | Material-Erkennung, Feldextraktion, Fallback-Logik |

### Phase D – Deployment, Marke DOK-V01, R04 (17.06.2026): 6:00 h

| Nr. | Datum | Von | Bis | Dauer | Tätigkeit |
|----:|---|---:|---:|---:|---|
| 23 | 17.06.2026 | 13:00 | 15:00 | 2:00 h | Rebrand DOK-V01, USB-Version, Backup/Git |
| 24 | 17.06.2026 | 15:00 | 16:30 | 1:30 h | Code-Signing, Zertifikat-Auto-Install, Security |
| 25 | 17.06.2026 | 16:30 | 18:00 | 1:30 h | Logo DOK-V01, Icon-Fix, Legacy-Tesla entfernt |
| 26 | 17.06.2026 | 18:00 | 19:00 | 1:00 h | Ablage-Komplettbereinigung, Gesamt-PDF-Löschung |

### Phase E – Bericht (17.06.2026): 0:30 h

| Nr. | Datum | Von | Bis | Dauer | Tätigkeit |
|----:|---|---:|---:|---:|---|
| 27 | 17.06.2026 | 19:00 | 19:30 | 0:30 h | Wertermittlung, Arbeitsbericht R04 |

---

## 3. Summen

| Phase | Zeitraum | Stunden |
|-------|----------|--------:|
| A – Kern-App | 03.06.–11.06.2026 | 18:45 h |
| B – Dokumentation | 12.06.2026 | 0:35 h |
| C – KI & UI R03 | 14.06.–17.06.2026 | 12:30 h |
| D – Deployment R04 | 17.06.2026 | 6:00 h |
| E – Bericht | 17.06.2026 | 0:30 h |
| **Gesamtsumme** | | **37:50 h** |

---

## 4. Leistungsumfang (Funktionswert)

Die App DOK-V01 ist eine **portable Windows-Fachanwendung** (.NET 4.8 / WPF) mit folgenden Modulen:

| Modul | Beschreibung |
|-------|--------------|
| Datei-Import | Drag&Drop: PDF, Excel, ZIP, Ordner → feste Ablage |
| Auftragsverarbeitung | Excel ↔ PDF-Matching, Sortieren, Umbenennen |
| Wasserzeichen | Auftragsnummer, Excel-Daten, intelligente PDF-Platzierung |
| Material-Erkennung | Werkstoff/Materialstärke aus Schriftfeld |
| Zusammenfügen | Gesamt-PDF (_GESAMT_) mit Vorschau/Druck |
| KI-Assistent | Ollama-Anbindung, Chat, Qualitätsprüfung, Befehle |
| UI/Theme | Dark/Light, Menüleiste, Einstellungsdialoge |
| Deployment | USB-Version, Code-Signing, Auto-Zertifikat, Desktop-Verknüpfung |
| Aufräumen | Komplette Ablage-Bereinigung inkl. Gesamt-PDF |

**Technische Abhängigkeiten:** iTextSharp, BouncyCastle, Docnet/PDFium, optional Ollama (lokal).

---

## 5. Punktebewertung (Funktion & Wirtschaft)

**Skala:** 0–100 Punkte pro Bereich · Bewertung ohne Stundenlohn/Kostenrechnung

| Note | Punkte | Bedeutung |
|------|--------|-----------|
| A+ | 90–100 | Exzellent |
| A | 80–89 | Sehr gut |
| B | 65–79 | Gut |
| C | 50–64 | Befriedigend |
| D | 0–49 | Ausbaubedarf |

---

### 5.1 Funktionsbewertung — **86 / 100 Punkte (Note A)**

| Kriterium | Max. | Punkte | Bewertung |
|-----------|-----:|-------:|-----------|
| Kernprozess (Excel ↔ PDF, Sortieren, Umbenennen) | 25 | **23** | Kernfunktion vollständig, zuverlässig, zentraler Arbeitsablauf abgedeckt |
| Wasserzeichen & Material-Erkennung | 15 | **13** | Mehrzeilig, intelligente Platzierung, Werkstoff aus Schriftfeld — teils PDF-abhängig |
| Zusammenfügen, Vorschau & Druck | 10 | **9** | Gesamt-PDF, Sortierreihenfolge, Druckworkflow integriert |
| Datei-Import (Drag&Drop, ZIP, Ordner) | 10 | **9** | Flexibel, ohne manuelles Entpacken |
| KI-Assistent (Ollama, Chat, Qualitätsprüfung) | 10 | **7** | Erweiterung vorhanden, optional und extern abhängig (Ollama) |
| Bedienbarkeit & UI (Theme, Menü, Status) | 10 | **8** | Dark/Light, übersichtlich, Fortschrittsanzeige |
| Deployment (USB, Signatur, Auto-Zertifikat) | 10 | **9** | Mehrere PCs ohne Installer, professionell signiert |
| Aufräumen & Wartung im Betrieb | 5 | **4** | Komplettbereinigung inkl. Gesamt-PDF |
| Dokumentation | 5 | **4** | Bedienungsanleitung vorhanden, teils verteilt |
| **Summe Funktion** | **100** | **86** | |

**Stärken:** Vollständiger Fachprozess von Import bis Druck, portable Bereitstellung, speziell auf Zeichnungs-/Auftragsworkflow zugeschnitten.

**Schwächen:** KI nur mit lokalem Ollama; Material-Erkennung nicht bei allen PDF-Formaten gleich gut; Dokumentation könnte gebündelter sein.

---

### 5.2 Wirtschaftliche Bewertung — **87 / 100 Punkte (Note A)**

| Kriterium | Max. | Punkte | Bewertung |
|-----------|-----:|-------:|-----------|
| Prozessersparnis (manuell → automatisiert) | 20 | **18** | Sortieren, Wasserzeichen, Zusammenfügen sparen deutlich Zeit pro Auftrag |
| Einzigartigkeit / Passgenauigkeit | 15 | **14** | Keine Standardsoftware deckt diesen speziellen Ablauf ab |
| Skalierbarkeit (mehrere Arbeitsplätze) | 15 | **14** | USB-Deployment, Desktop-Verknüpfung, ohne Server |
| Betriebskosten (lokal, offline) | 10 | **9** | Keine Cloud-Lizenzen, keine laufenden SaaS-Kosten |
| Amortisation / Nutzen im Alltag | 15 | **13** | Hoher Nutzen bei regelmäßigem Einsatz (wöchentliche Aufträge) |
| Wiederbeschaffbarkeit | 10 | **7** | Neubau nur als Individualentwicklung möglich, nicht „von der Stange“ |
| Technisches Risiko / Abhängigkeiten | 10 | **8** | .NET 4.8 stabil; PDF-Bibliotheken und optional Ollama zu beachten |
| Erweiterbarkeit & Zukunft | 5 | **4** | Modulare KI-Services, erweiterbarer Code (~11.400 Zeilen) |
| **Summe Wirtschaft** | **100** | **87** | |

**Stärken:** Hoher praktischer Nutzen, geringe laufende Kosten, mehrere PCs einsetzbar, Prozess direkt am Arbeitsplatz integriert.

**Schwächen:** Wiederbeschaffung nur durch Neuentwicklung; Wissen und Code liegen bei einem Entwickler.

---

### 5.3 Gesamtbewertung

| Bereich | Punkte | Note |
|---------|-------:|------|
| **Funktion** | **86 / 100** | A — Sehr gut |
| **Wirtschaft** | **87 / 100** | A — Sehr gut |
| **Gesamtnote** | **86,5 / 100** | **A — Sehr gut** |

**Wertklasse (qualitativ, ohne Stundenlohn):**

| Klasse | Bedeutung | DOK-V01 |
|--------|-----------|---------|
| Premium (90+) | Branchenführend, kaum Verbesserung nötig | — |
| **Hoch (80–89)** | **Reife Fachanwendung, klarer Nutzen, produktionsreif** | **✓ hier** |
| Mittel (65–79) | Solide Basis, gezielter Ausbau sinnvoll | — |
| Niedrig (<65) | Prototyp oder starke Lücken | — |

**Fazit:** DOK-V01 ist eine **reife, spezialisierte Fachanwendung** mit hohem praktischen und wirtschaftlichem Nutzen. Sie ersetzt mehrere manuelle Arbeitsschritte und ist für den Einsatz an mehreren Arbeitsplätzen geeignet. Der Gesamtwert liegt in der **oberen Mittelklasse** für Individualsoftware — nicht wegen Entwicklungsstunden, sondern wegen Funktionsvollständigkeit, Prozessintegration und Betriebsfähigkeit.

---

## 6. Abschluss

Die App DOK-V01 wurde vom **03.06.2026** bis **17.06.2026** entwickelt und erweitert.  
Die dokumentierte Gesamtarbeitszeit beträgt **37 Stunden 50 Minuten**.

**Punktebewertung:** Funktion **86/100**, Wirtschaft **87/100**, Gesamt **A (Sehr gut)**.

---

*Erstellt am 17.06.2026 · Alexander Hölzer · DOK-V01 v1.1 R04*
