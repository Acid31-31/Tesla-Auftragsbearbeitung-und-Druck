# DOK-V01

Desktop-WPF-App: PDF-Zeichnungen + Excel → sortieren, umbenennen, Wasserzeichen, zusammenfügen.

**Version:** siehe `AppInfo.cs` (aktuell **1.1 R41**)

## Für KI / neuer Chat

→ **`AGENTS.md`** lesen (komplette Projektbeschreibung, Pfade, Module, Fallstricke, Checkliste)

Kurz-Prompt zum Einfügen:

```
Lies zuerst AGENTS.md in diesem Repo. Das ist DOK-V01 (WPF, PDF-Sortierung, Wasserzeichen).
Halte dich an .cursor/rules/. Kein Git-Commit ohne meine Anfrage.
```

## USB in der Firma verteilen

| Was | Pfad |
|-----|------|
| Installationsordner | `USB-Version\DOK-V01\` |
| ZIP | `USB-Version\DOK-V01.zip` |

Auf jedem PC einmal **`Programm installieren.exe`** starten (nicht nur Ordner kopieren).

- Festinstallation: `C:\Program Files\DOK-V01\`
- Arbeitsdaten: `%LOCALAPPDATA%\DOK-V01\Ablage\`
- Updates: automatisch von GitHub ([Releases](https://github.com/Acid31-31/Tesla-Auftragsbearbeitung-und-Druck/releases))

## Build & USB erstellen

```powershell
.\create-usb-version.ps1 -Configuration Release
```

## GitHub-Release (Auto-Update)

```powershell
.\publish-github-release.ps1
```

MSBuild-Plattform: **`AnyCPU`** (ohne Leerzeichen).

## Signatur

- Entwicklung: `.\sign-app.ps1`
- Händler-Zertifikat: `.\sign-app-commercial.ps1`

## Dokumentation

| Datei | Inhalt |
|-------|--------|
| **`AGENTS.md`** | **KI-Handoff, Architektur, alle kritischen Pfade** |
| `BEDIENUNGSANLEITUNG.md` | Endnutzer-Bedienung |
| `SECURITY_HINWEIS.md` | Antivirus / Zertifikat / Code-Sicherheit |
| `.cursor/rules/` | Cursor-Regeln (Build, Wasserzeichen, Git, pdfium) |

## Wichtige Dateien

| Datei | Zweck |
|-------|--------|
| `PDF-umbenenen.csproj` | Hauptprojekt |
| `AppInfo.cs` | Version, GitHub, Pfade |
| `Services/SmartWatermarkLayoutService.cs` | Wasserzeichen-Layout |
| `Services/PdfRasterLayoutService.cs` | pdfium-Platzierung |
| `Tools/PdfDiag/` | Diagnose-CLI |
| `CodeSigning.cer` | Zertifikat für Installation |
