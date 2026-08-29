# Nutzenbasierte Preisbewertung DOK-V01 (ohne Entwickler-Stundenlohn)

**Stand:** Version 1.1 R44 · nur **Basisversion** (Standard-Workflow Zeichnung + Excel)  
**Zielgruppe:** **Lohnfertiger / Lohnanfertiger**  
**Methode:** Betriebsnutzen aus **realer Bearbeitungszeit** · kein Entwickler-Stundenlohn

---

## Messwerte aus dem Betrieb

Pro Bauteil müssen Sortierung, Umbenennung und Wasserzeichen-Einträge (Menge, Termin, Auftragsnr. usw.) **von Hand** erledigt werden.

| Situation | Manuell | Mit DOK-V01 | **Ersparnis** |
|-----------|--------:|------------:|--------------:|
| **1 Bauteil** | **2 Min** | ~1 Sek | **~2 Min** |
| **40 Bauteile** | **30 Min** | ~1 Min | **~29 Min** |

*Hochrechnung für beliebige Stückzahl:* `Manuell (Min) = 30 ÷ 40 × Anzahl Teile`

---

## Grundformel

```
Manuell pro Auftrag (Min)  = 30 ÷ 40 × Anzahl Bauteile
Mit App pro Auftrag (Min)  ≈ Anzahl Bauteile × 1 Sek ÷ 60
Ersparnis pro Auftrag      = Manuell − Mit App

Stunden/Jahr               = Aufträge/Jahr × Ersparnis Ø pro Auftrag ÷ 60
Jahresnutzen (€)           = Stunden × 45 €/h (interner Vollkostensatz Fertigung)
```

**Beispiele pro Auftrag:**

| Bauteile | Manuell | Mit App | **Ersparnis** |
|----------|--------:|--------:|--------------:|
| 4 | ~3 Min | ~4 Sek | **~3 Min** |
| 15 | ~11 Min | ~15 Sek | **~11 Min** |
| 19 *(Ø)* | ~14 Min | ~19 Sek | **~14 Min** |
| 30 | ~23 Min | ~30 Sek | **~22 Min** |
| 40 | **30 Min** | ~1 Min | **~29 Min** |
| 50 | ~38 Min | ~50 Sek | **~37 Min** |

---

## 1. Ihr Betrieb (Richtwerte)

| Größe | Annahme |
|-------|---------|
| Aktive Phase | ca. **5–8 Aufträge/Woche** |
| Flauten | teils **mehrere Monate ohne Aufträge** |
| **Aktive Monate/Jahr** | **10** (2 Monate ohne Aufträge) |
| **Ø Aufträge/Woche (aktiv)** | **6,5** |
| **Aufträge/Jahr** | 10 × 4,3 × 6,5 ≈ **280** |
| **Ø Bauteile/Auftrag** (Mix 4–50) | **~19** (30 % klein / 50 % mittel / 20 % groß) |

### Normales Jahr (gut ausgelastet)

| Schritt | Rechnung |
|---------|----------|
| Manuell/Auftrag | 30 ÷ 40 × 19 ≈ **14 Min** |
| Mit App/Auftrag | ≈ **19 Sek** |
| Ersparnis/Auftrag | ≈ **14 Min** |
| Ersparnis/Jahr | 280 × 14 Min ≈ **3.920 Min** |
| Stunden/Jahr | ≈ **64 h** |
| **Zeitnutzen (45 €/h)** | **≈ 2.900 €/Jahr** |

### Flautenjahr (z. B. nur 40 Aufträge)

| Schritt | Rechnung |
|---------|----------|
| Ersparnis | 40 × 14 Min ≈ **560 Min** |
| Stunden | ≈ **9 h** |
| **Zeitnutzen** | **≈ 420 €/Jahr** |

### Drei-Jahres-Durchschnitt (2 normale + 1 Flaute)

| | Stunden/Jahr Ø | Nutzen/Jahr Ø |
|--|---------------:|--------------:|
| **Durchschnitt** | **≈ 46 h** | **≈ 2.070 €** |

### Vergleich: häufig große Aufträge (40 Teile)

| | Wert |
|--|-----:|
| Ersparnis je 40-Teile-Auftrag | **29 Min** |
| Bei 280 Aufträgen/Jahr (alle 40 Teile) | **≈ 135 h/Jahr** |
| Nutzen (45 €/h) | **≈ 6.100 €/Jahr** |

*In der Praxis liegt das Volumen zwischen Durchschnittsmix (~2.900 €) und reinen Großaufträgen (~6.100 €).*

---

## 2. Lizenzpreis aus Nutzen (ohne Stundenlohn Entwicklung)

Faustregel: **Einmallizenz ≈ 9–12 Monate Jahresnutzen** (für Lohnfertiger realistisch verkaufbar)

| Bezugsgröße | Jahresnutzen | Amortisation | **Einmallizenz** |
|-------------|-------------|--------------|-----------------:|
| Durchschnittsmix | ~2.900 € | 9 Monate | ~2.175 € |
| Durchschnittsmix | ~2.900 € | 12 Monate | **~2.900 €** |
| **3-Jahres-Ø** | **~2.070 €** | **12 Monate** | **~2.070 €** |
| Große Aufträge (40 Teile) | ~6.100 € | 6 Monate | ~3.050 € |

### Praxis-Empfehlung (Angebot)

| Variante | Preis | Begründung |
|----------|------:|------------|
| **Regulär (Listenpreis)** | **2.990 €** | ~12 Monate Nutzen im Durchschnittsmix — fair und vertretbar |
| **Pilotkunde** (Mitentwicklung mit Unterlagen) | **2.240 €** | 25 % Rabatt · inkl. Einschulung + 12 Monate Updates |
| Zusatz: weiteres Kundenprofil | **990 – 1.490 €** | Separates Angebot (z. B. Makat) |

**Zusatzleistung „Weitere Kunden“** (Makat u. a.): **nicht** in der Basislizenz enthalten.

### Amortisation (Übersicht)

| Preis | Szenario | Ergebnis |
|------:|----------|----------|
| **2.240 €** | Durchschnittsmix (~2.900 €/Jahr) | **ca. 9 Monate** |
| **2.240 €** | Auftrag 40 Teile (29 Min Ersparnis) | **~103** solche Aufträge |
| **2.990 €** | Durchschnittsmix | **ca. 12 Monate** |

---

## 3. Eigene Kalkulation (ausfüllen)

```
Aktive Monate/Jahr:        _____
Aufträge/Woche (aktiv):    _____
→ Aufträge/Jahr = Monate × 4,3 × Aufträge/Woche

Ø Bauteile/Auftrag:        _____
→ Manuell/Auftrag (Min) = 30 ÷ 40 × Bauteile
→ Ersparnis/Auftrag      ≈ Manuell − (Bauteile × 1 Sek ÷ 60)

Min/Jahr = Aufträge × Ersparnis/Auftrag
Stunden/Jahr = Min/Jahr ÷ 60
Nutzen €/Jahr = Stunden × 45 €

Lizenz = Jahresnutzen × (12 bis 24 Monate ÷ 12)
```

---

## 4. Kurzantwort

Mit **2 Min/Bauteil manuell** (bzw. **30 Min für 40 Teile**), **~1 Sek/Bauteil mit App** und **~280 Aufträgen/Jahr** (Ø 19 Teile):

> **Nutzen im Durchschnittsmix: ~2.900 €/Jahr (~64 h)**  
> **Nutzen im 3-Jahres-Durchschnitt: ~2.070 €/Jahr**  
> **Listenpreis Basis: 2.990 €**  
> **Pilotkunden-Kondition: 2.240 €** (25 % · Mitentwicklung mit Unterlagen)

**2.240 €** amortisieren sich im **Durchschnittsmix in ca. 9 Monaten** — allein ein **40-Teile-Auftrag** spart **~29 Minuten** (~22 € Kapazität bei 45 €/h).

---

*Alexander Hölzer · DOK-V01 · Nutzenbasierte Bewertung*
