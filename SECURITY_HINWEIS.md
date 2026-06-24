# Sicherheitshinweis (Acronis / Antivirus)

## Warum erscheint eine Warnung?
Die App bearbeitet viele PDF-Dateien in kurzer Zeit (Umbenennen, Wasserzeichen, Zusammenführen).
Einige Schutzprogramme (z. B. Acronis Active Protection) bewerten dieses Muster als verdächtig.

## Empfohlene Freigabe
1. In Acronis bei der Meldung auf **Vertrauen** klicken.
2. Die Datei `DOK-V01.exe` zu den Ausnahmen hinzufügen.
3. Optional den Arbeitsordner mit den PDF-Dateien freigeben.

## Signierte EXE (empfohlen)
Eine digital signierte EXE reduziert SmartScreen-/Antivirus-Warnungen deutlich.

### Neues Zertifikat erzeugen (nach Leak oder AV-Block)

```powershell
powershell -ExecutionPolicy Bypass -File .\create-selfsigned-cert.ps1
powershell -ExecutionPolicy Bypass -File .\sign-app.ps1 -Configuration Debug
powershell -ExecutionPolicy Bypass -File .\create-usb-version.ps1 -Configuration Release
```

Zertifikat lokal vertrauenswürdig machen (einmal pro PC):

```powershell
powershell -ExecutionPolicy Bypass -File .\install-trusted-cert.ps1
```

Oder in der USB-Version: `ZERTIFIKAT_INSTALLIEREN.bat`

Passwort liegt nur lokal in `cert\signing-password.txt` (nicht in Git).

Hinweis: `signtool.exe` muss verfügbar sein (z. B. über Windows SDK / Visual Studio Build Tools).
