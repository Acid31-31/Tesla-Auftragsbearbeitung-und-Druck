# DOK-V01

Ein Projektordner für Entwicklung, USB-Verteilung und GitHub-Updates.

**Version:** siehe `AppInfo.cs` (aktuell 1.1 R09)

## USB in der Firma verteilen

| Was | Pfad |
|-----|------|
| Installationsordner | `USB-Version\DOK-V01\` |
| ZIP | `USB-Version\DOK-V01.zip` |

Auf jedem PC einmal **`Programm installieren.exe`** starten (nicht nur Ordner kopieren).

- Festinstallation: `C:\Program Files\DOK-V01\`
- Arbeitsdaten: `%LOCALAPPDATA%\DOK-V01\Ablage\`
- Updates: automatisch von GitHub

## Build & USB erstellen

```powershell
.\create-usb-version.ps1 -Configuration Release
```

## GitHub-Release (Auto-Update)

```powershell
.\publish-github-release.ps1
```

## Signatur

- Entwicklung: `.\sign-app.ps1`
- Händler-Zertifikat: `.\sign-app-commercial.ps1`

## Wichtige Dateien

| Datei | Zweck |
|-------|--------|
| `PDF-umbenenen.csproj` | Projekt |
| `CodeSigning.cer` | Zertifikat für Installation |
| `BEDIENUNGSANLEITUNG.md` | Bedienung |
| `SECURITY_HINWEIS.md` | Antivirus / Signatur |
| `ANGEBOT_DOK-V01_Basis.*` | Angebot |
| `PREISBewertung_Nutzenbasiert.*` | Preisbewertung |
