# Protokoll / Konzept

## Erweiterung DOK-V01 – Lager-Kiosk für Produktionsreste

| | |
|---|---|
| **Dokument** | Konzept zur internen Vorstellung |
| **Produkt** | DOK-V01 |
| **Thema** | Erfassung und Nutzung von Kunden-Restteilen im Lager |
| **Stand** | 01.10.2026 |
| **Status** | Vorschlag – noch nicht umgesetzt |
| **Ersteller** | Alexander Hölzer |

---

## 1. Anlass

Im Betrieb liegen **Kunden-Restteile** (Überproduktion, nicht abgerufene Mengen).  
Niemand hat einen zuverlässigen Überblick, **was** im Lager liegt, **wie viel**, und **zu welchem Kunden / welcher Zeichnung**.

Beim nächsten Auftrag derselben Zeichnung wird oft **neu gefertigt**, obwohl Restteile vorhanden wären.

**Ziel dieses Dokuments:** Entscheidung, ob die Erweiterung gebaut werden soll.

---

## 2. Ist-Zustand

| Punkt | Heute |
|---|---|
| Restteile | physisch im Lager, oft ohne zentrale Liste |
| Wissen | bei einzelnen Personen, nicht in der Auftragsvorbereitung |
| DOK-V01 | sortiert Aufträge, kennt das Lager **nicht** |
| Anderer PC (Lager) | soll **nicht** die volle DOK-V01-App bedienen |
| Excel am Lager-PC | **nicht** gewünscht |

Folge: Restteile werden übersehen. Zeit, Material und Geld gehen verloren.

---

## 3. Ziel

Beim **Auftrags-Sortieren** in DOK-V01 automatisch prüfen:

> Liegt zu dieser Zeichnungsnummer schon ein Rest im Kundenlager?

Wenn ja: klarer Hinweis (Kunde, Zeichnung, Menge, optional Lagerplatz) – **bevor** neu produziert wird.

---

## 4. Vorgeschlagene Lösung: Lager-Kiosk

Kein Excel. Keine volle DOK-V01 auf den Lager-PCs.

Stattdessen eine **eigene, abgespeckte Kiosk-Oberfläche** auf den Lager-PCs:

- Vollbild, große Tasten, wenige Schritte
- Nur Rest erfassen – kein Sortieren, kein Wasserzeichen, keine Einstellungen
- Dieselbe Zeichnungslogik wie DOK-V01 (Tesla TSL-…, EcoMark RT1-…, Spiegelbild `-S`)

**Sobald die Zeichnungsnummer eingegeben ist**, sucht der Kiosk die Zeichnung **automatisch im jeweiligen Kundenordner**, **legt eine Kopie beim Rest ab** und **zeigt die Zeichnung am Bildschirm**. Der Mitarbeiter sieht sofort, ob es das richtige Teil ist.

Die Sortier-App (DOK-V01) **liest** diese Daten beim Auftrag.  
Die Kiosk-App **schreibt** sie am Lager.

---

## 5. Ablauf im Betrieb

### 5.1 Lager-PC (Mitarbeiter Produktion / Lager)

1. Kiosk starten (Desktop-Verknüpfung, startet Vollbild).
2. **Zeichnungsnummer** scannen oder tippen.
3. Der Kiosk ermittelt den Kunden (Tesla / EcoMark / …) und sucht die Zeichnung **im Kundenordner**.
4. Die gefundene Zeichnung wird **am Kiosk angezeigt** und **zum Rest im Lagerordner abgelegt** (Kopie, Original im Kundenordner bleibt).
5. Mitarbeiter prüft visuell: stimmt das Blech mit der Zeichnung überein?
6. Restmenge eingeben.
7. Optional: Lagerplatz (Regal / Fach).
8. Speichern.

Wird die Zeichnung nicht gefunden: klare Meldung am Kiosk, Rest wird **nicht** stillschweigend ohne Bild gespeichert.

Dauer: wenige Sekunden pro Rest. Keine Schulung für die volle DOK-V01 nötig. Kein manuelles Suchen der PDF.

### 5.2 Büro / Auftrags-PC (DOK-V01)

1. Auftrag wie bisher laden und sortieren.
2. Die App prüft jede Zeichnung gegen den Lagerbestand.
3. Treffer erscheinen im **Protokoll**, z. B.:

   > Lager Tesla: TSL-11935-322 – 8 Stück – Platz B-12

4. Der Bearbeiter entscheidet: Rest mitliefern / verrechnen / Lager nachziehen.

Es wird **nichts automatisch aus dem Lager gebucht**, solange das nicht ausdrücklich gewünscht ist. Zuerst nur **Hinweis**.

---

## 6. Datenhaltung (für IT / Geschäftsführung)

| Punkt | Vorschlag |
|---|---|
| Kundenordner | vorhandene Zeichnungsablage je Kunde (Quelle für die Anzeige) |
| Lagerablage | **ein gemeinsamer Netzordner**, z. B. `\\Server\Kundenlager\` |
| Struktur | Unterordner je Kunde (`Tesla`, `EcoMark`, …) |
| Pro Rest | Datensatz (Nummer, Menge, Platz, Datum) **plus Kopie der Zeichnung** |
| Excel | nicht erforderlich |
| Internet | nicht erforderlich (nur lokales Netz) |

Alle Kiosk-PCs und der Sortier-PC zeigen auf **denselben** Lagerordner.  
Die Zeichnung kommt **nicht** vom Mitarbeiter per Datei, sondern **automatisch aus dem Kundenordner**.

Ohne Netzfreigabe kennt jeder PC nur sich selbst – das wäre unbrauchbar.

---

## 7. Was bewusst nicht vorgesehen ist

- Keine volle DOK-V01 auf Lager-PCs
- Kein Excel am Lager-PC
- Kein ERP / keine Barcode-Pflicht in der ersten Stufe
- Keine automatische Bestandsbuchung gegen den Kundenauftrag (kann später kommen)
- Keine Cloud, keine externe KI

---

## 8. Nutzen

| Nutzen | Wirkung |
|---|---|
| Transparenz | Jeder Auftrag sieht, ob Restteile existieren |
| Weniger Doppelproduktion | Material und Maschinenzeit sparen |
| Weniger Abhängigkeit von Einzelwissen | Bestand steht in der App, nicht nur „im Kopf“ |
| Geringe Hürde am Lager | Kiosk statt komplexer Software |
| Sichtprüfung | Zeichnung erscheint sofort – falsche Nummer fällt auf |
| Kundengetrennt | Tesla-Reste und EcoMark-Reste bleiben getrennt |

---

## 9. Voraussetzungen im Betrieb

1. **Kundenordner** je Kunde mit den Original-Zeichnungen (lesen).
2. **Netzwerk-Lagerordner**, den Lager-PCs beschreiben und der Auftrags-PC lesen darf.
3. Kiosk-PCs: Windows, .NET 4.8 (wie DOK-V01).
4. Verantwortlicher, der Kunden- und Lager-Pfad einmal festlegt.
5. Kurze Betriebsregel: Jeder Rest, der ins Lager geht, wird **sofort** im Kiosk erfasst.

Ohne Punkt 5 bleibt das Lager in der App leer – die Technik allein reicht nicht.  
Ohne erreichbaren Kundenordner kann der Kiosk die Zeichnung nicht anzeigen.

---

## 10. Umsetzung (wenn genehmigt)

| Schritt | Inhalt |
|---|---|
| 1 | Netzordner und Kundenunterordner anlegen |
| 2 | Lager-Kiosk: Nummer eingeben → Zeichnung aus Kundenordner laden, anzeigen, zum Rest ablegen; Menge/Platz speichern |
| 3 | DOK-V01: beim Sortieren Lager prüfen und ins Protokoll schreiben |
| 4 | Test mit Tesla und EcoMark |
| 5 | Verteilung per Update / USB wie bei DOK-V01 |

**Erst nach Freigabe durch den Arbeitgeber wird entwickelt.**  
Dieses Dokument ist keine Beauftragung.

---

## 11. Offene Punkte zur Entscheidung

Bitte ankreuzen bzw. festlegen:

- [ ] Erweiterung wird gewünscht
- [ ] Erweiterung wird zurückgestellt
- [ ] Netz-Lagerordner vorhanden / wird bereitgestellt: _______________________
- [ ] Kundenordner (Zeichnungen) je Kunde: _______________________
- [ ] Lagerplatz-Feld Pflicht oder optional: _______________________
- [ ] Stückzahl Pflicht: ja / nein
- [ ] Hinweis im Protokoll reicht (keine automatische Buchung)
- [ ] Später: Restmenge nach Verwendung im Auftrag automatisch verringern

---

## 12. Kenntnisnahme

| Rolle | Name | Datum | Unterschrift |
|---|---|---|---|
| Vorstellung | Alexander Hölzer | 01.10.2026 | |
| Arbeitgeber / Entscheidung | | | |

---

*DOK-V01 · Konzept Lager-Kiosk · 01.10.2026 · nicht Bestandteil der aktuellen Version 1.1 R83*
