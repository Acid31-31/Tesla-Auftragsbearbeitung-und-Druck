# DOK-V01

Desktop-WPF-App: PDF-Zeichnungen + Excel → sortieren, umbenennen, Wasserzeichen, zusammenfügen.

**Version:** siehe `AppInfo.cs`

## Ordner auf Z: (wie Rohre-Zuschnitt)

| Pfad | Inhalt |
|------|--------|
| `Z:\Programierung\DOK-V01-Optimierung\` | **Quellcode** (dieses Projekt) |
| `Z:\Programierung\DOK-V01-Absicherung\` | Programm-Kopie + Quellcode-ZIP |
| `Z:\DOK-V01\` | **USB zum Kopieren**: aktuelle Rev-Ordner + ZIP |

## Ordnerstruktur (Projekt)

| Ordner | Inhalt |
|--------|--------|
| `Assets/`, `Services/`, `Platform/`, `PDF-umbenenen/`, … | Quellcode der App |
| `Release-Version/` | GitHub-Release: Ordner + ZIP pro Revision |
| `USB-Version/` | USB-Verteilung: Ordner + ZIP pro Revision |
| `Dokumentation/` | Handbuch, Release-Notes, Vertrieb, Intern |
| `Skripte/` | Setup-, Build-, Test- und Zusatz-Skripte |
| `Marketing/` | Werbematerial |
| `Archive/` | Alte/abgetrennte Projekte |
| `Backups/` | Logs, Testergebnisse |
| `cert/`, `native/`, `Tools/` | Zertifikat, native DLLs, Hilfswerkzeuge |

## Für KI / neuer Chat

→ **`Dokumentation/Handbuch/AGENTS.md`** lesen

## USB in der Firma verteilen

| Was | Pfad |
|-----|------|
| Installationsordner | `USB-Version\DOK-V01-Rxx\` |
| ZIP | `USB-Version\DOK-V01-Rxx.zip` |

Auf jedem PC einmal **`Programm installieren.exe`** starten (nicht nur Ordner kopieren).

## Build & Release (Skripte im Projektroot)

```powershell
.\create-usb-version.ps1 -Configuration Release
.\publish-github-release.ps1
.\sign-app.ps1
```

MSBuild-Plattform: **`AnyCPU`** (ohne Leerzeichen).

Weitere Skripte: `Skripte\Setup\`, `Skripte\Build\`, `Skripte\Test\`

## Dokumentation

| Datei | Inhalt |
|-------|--------|
| `Dokumentation/Handbuch/AGENTS.md` | KI-Handoff, Architektur |
| `Dokumentation/Handbuch/BEDIENUNGSANLEITUNG.md` | Endnutzer |
| `Dokumentation/Handbuch/SECURITY_HINWEIS.md` | Antivirus / Zertifikat |
| `Dokumentation/Release/RELEASE_NOTES.md` | Changelog für GitHub |
