# Sicherheitshinweis (Acronis / Antivirus)

## Warum erscheint eine Warnung?
Die App bearbeitet viele PDF-Dateien in kurzer Zeit (Umbenennen, Wasserzeichen, Zusammenführen).
Einige Schutzprogramme (z. B. Acronis Active Protection) bewerten dieses Muster als verdächtig.

## Empfohlene Freigabe
1. In Acronis bei der Meldung auf **Vertrauen** klicken.
2. Die Datei `Tesla-Auftragsbearbeitung-und-Druck.exe` zu den Ausnahmen hinzufügen.
3. Optional den Arbeitsordner mit den PDF-Dateien freigeben.

## Signierte EXE (empfohlen)
Eine digital signierte EXE reduziert SmartScreen-/Antivirus-Warnungen deutlich.

### USB-Build mit Signatur
Im Projektordner:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-usb-version.ps1 -Configuration Release -CertPath "C:\Pfad\zu\CodeSigning.pfx" -CertPassword "DEIN_PASSWORT"
```

Hinweis: `signtool.exe` muss verfügbar sein (z. B. über Windows SDK / Visual Studio Build Tools).
