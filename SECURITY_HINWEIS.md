# Sicherheitshinweis (Antivirus / Avira)

## Warum erscheint eine Warnung?
- Unbekannter Herausgeber (selbstsigniertes Zertifikat)
- PDF-Massenbearbeitung (Umbenennen, Wasserzeichen) wirkt verdächtig
- Erstes Starten von neuem Datei-Hash nach Update

## Festinstallation (empfohlen)
1. `Programm installieren.exe` vom USB starten
2. Windows-Administratorabfrage (UAC) bestätigen
3. Bei Zertifikat-Dialog **JA** klicken
4. Nur Desktop-Verknüpfung nutzen (`C:\Program Files\DOK-V01\`)

## IT-Freigabe in Avira (einmalig)
Ausnahme für: `C:\Program Files\DOK-V01\DOK-V01.exe`

## Dauerhafte Lösung für Kunden
Händler-Code-Signing-Zertifikat (OV, ca. 220–280 €/Jahr) und signieren mit:
`.\sign-app-commercial.ps1`

## Signatur Entwicklung
```powershell
.\create-selfsigned-cert.ps1
.\sign-app.ps1 -Configuration Release
.\create-usb-version.ps1 -Configuration Release
```
