# Sicherheitshinweis (Antivirus / Avira)

## Code-Sicherheit in der App

| Maßnahme | Implementierung |
|----------|-----------------|
| Digitale Signatur (Release) | `AppSecurityService.EnforceStartupSecurity` |
| Update-Integrität | SHA256 im GitHub-Release + Signatur aller App-EXEs (`GitHubUpdateService`, `AppSecurityService`) |
| Update-Anwendung | Staging nur aus `%LOCALAPPDATA%\\DOK-V01\\Updates\\staging-*`, Paketprüfung vor Installation |
| Tesseract-Download | HTTPS-Whitelist + SHA256-Pinning (`TesseractInstallService`) |
| Sicheres ZIP-Entpacken | Zip-Slip-Schutz in `AppSecurityService.ExtractZipSafely` |
| Pfad-Sicherheit | `SanitizeFileName`, `ResolveSafeTargetPath`, `IsPathInsideDirectory` |
| Download-Whitelist | Nur HTTPS zu github.com / api.github.com / objects.githubusercontent.com |
| Geschützte Installation | Schreibschutz-Logik für `Program Files` (`AppInfo.IsProtectedInstallDirectory`) |

KI-Regeln und Handoff: **`AGENTS.md`**, `.cursor/rules/security-release-git.mdc`

---

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
