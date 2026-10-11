# RigPilot

Eine kleine Windows-App für klassische Autostarts, einzelne Programmstarts und bearbeitbare Startgruppen. Gruppen können ein Wiedergabegerät, Mikrofon und Monitor-Setup auswählen und Programme mit Startpausen ausführen.

## Download und Installation

Fertige Downloads liegen unter [Releases](https://github.com/bk9tv/RigPilot/releases):

- **Windows-Installer:** `RigPilot-<Version>-Setup-x64.exe` mit Startmenü-Eintrag und Deinstallation. Installation für den aktuellen Benutzer unter `%LOCALAPPDATA%\Programs\RigPilot`.
- **Portable Ausgabe:** ZIP entpacken und `RigPilot.exe` starten. Die EXE ist außerdem einzeln verfügbar.
- **Prüfsummen:** `SHA256SUMS.txt` enthält die SHA256-Werte der Downloads.

Voraussetzungen: Windows 11 x64 und .NET Framework 4.8 oder neuer. EXE und Installer sind derzeit nicht digital signiert.

RigPilot speichert Einstellungen im beschreibbaren Ordner `Daten` neben der EXE. Vor Updates den Anwendungsordner sichern und RigPilot schließen. Ein laufender Monitorwechsel einschließlich Rückstellung muss abgeschlossen sein. Updates erhalten `Daten`; auch bei Deinstallation bleiben persönliche Einstellungen erhalten. Details: [UPDATE.txt](UPDATE.txt), Bedienung: [ANLEITUNG.txt](ANLEITUNG.txt).

## Funktionen und Grenzen

- Oberfläche und integrierte Kurzanleitung auf Deutsch oder Englisch; sofort umschaltbar unter **Sprache / Language** in der Seitenleiste. Die Auswahl bleibt beim Neustart erhalten, eigene Namen und Profile bleiben unverändert. English guide: [GUIDE.en.txt](GUIDE.en.txt), installation and updates: [UPDATE.en.txt](UPDATE.en.txt).
- Klassische Windows-Autostarts prüfen und umschalten; einzelne Programme starten.
- Startgruppen mit Reihenfolge, Pausen, Audio- und Monitorzuordnung; einzelne Programm- oder Audiofehler blockieren die übrigen Programmstarts nicht.
- Wave Link 3: Hauptausgang auswählen und Mikrofone gezielt in einem Mix aktivieren beziehungsweise stummschalten.
- Monitorwechsel mit 25 Sekunden Bestätigungszeit und unabhängigem Rückstellprozess.
- Lokale Profile mit atomarer Speicherung und Sicherung.

Die App wirkt auf dem PC, auf dem sie läuft. Sie bietet keine Fernsteuerung. HDR, DPI-Skalierung, ICC-Profile, NVIDIA Surround und VR-Konfiguration sind nicht enthalten. Windows-Audio-Umschaltung nutzt die nicht öffentlich dokumentierte PolicyConfig-Schnittstelle; Wave-Link-Profile verwenden die lokale JSON-RPC-Schnittstelle von Wave Link 3. Hardwarefunktionen müssen auf dem jeweiligen Ziel-PC geprüft werden; Selbsttests ersetzen diese Prüfung nicht.

## Wave Link für Simrig und Gaming

In jeder Gruppe **Bearbeiten → Audio über Wave Link 3 steuern → Wave-Link-Auswahl laden** verwenden. Wave Link muss für die Auswahl geöffnet sein. Geräte werden vom jeweiligen PC geladen; vorhandene Windows-Audioauswahlen bleiben gespeichert und werden im Wave-Link-Modus nicht angewendet.

- **Simrig:** Als Main Output Device die Pimax-Crystal-Kopfhörer wählen, als Mix den eigenen Discord Mix und das gewünschte Simrig-Mikrofon unter „Mikrofon in diesem Mix aktivieren“. Optional das RØDE-Mikrofon in diesem Mix stummschalten.
- **Gaming:** Boxen als Hauptausgang, denselben Discord Mix und RØDE als aktives Mikrofon wählen. Optional das Simrig-Mikrofon in diesem Mix stummschalten.

Wave Link und benötigte Pimax-Software bei Bedarf zuerst in die Programmliste der Gruppe aufnehmen. Bei Wave-Link-Profilen werden nach der Monitorbestätigung zuerst die Programme gestartet und danach die Audioauswahl gesetzt. Andere Mixe und die globale Mikrofon-Stummschaltung bleiben unverändert. Discord muss seinen Eingang in den Discord Mix von Wave Link routen; ein fest ausgewähltes physisches Mikrofon in Discord wird durch das Profil nicht geändert. Wave Link 1/2 verwendet eine andere Schnittstelle und wird von diesen Profilen nicht unterstützt.

Ein fehlender Programmpfad wird mit Name und Pfad im Gruppenfenster und unter `Daten/protokoll.txt` angezeigt; nachfolgende Programme werden trotzdem gestartet. Audiofehler werden ebenfalls gemeldet. Windows-Audio wird bei einem Fehler nach den Programmstarts erneut versucht, damit neu gestartete Geräte verfügbar werden können. Ein unbestätigter Monitorwechsel stoppt weiterhin die Gruppe und stellt die vorherigen Monitore zurück.

## Entwicklung

Native WPF-App mit C# 5 und .NET Framework 4.8+. `build.ps1` verwendet den Windows-Framework-Compiler; der App-Build benötigt keine externen Pakete oder ein zusätzliches .NET SDK.

```powershell
.\build.ps1
$testFolder = Join-Path $PWD 'Pruefung\aktuell'
$process = Start-Process -FilePath '.\RigPilot.exe' -ArgumentList ('--self-test "' + $testFolder + '"') -Wait -PassThru -WindowStyle Hidden
$process.ExitCode
Get-Content (Join-Path $testFolder 'self-tests.txt')
```

Bei laufender EXE mit `-OutputPath` in eine separate Datei bauen. Logo-Dateien liegen unter `assets`; `prepare-icons.ps1` erzeugt UI-PNG und ICO aus der Masterdatei.

```powershell
.\package.ps1                         # EXE, portables ZIP und Prüfsummen
.\package.ps1 -Installer              # zusätzlich Windows-Setup
```

Der Installer benötigt Inno Setup. Einrichtung, Prüfung und Veröffentlichung sind in [RELEASING.md](RELEASING.md) beschrieben. Falls Windows PowerShell Skripte blockiert, kann für den einzelnen Build-Prozess `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\package.ps1 -Installer` verwendet werden; dies ändert keine systemweite Richtlinie.

Quellcode, Assets und Build-Rezepte gehören ins Repository. Persönliche Profile, Geräte-IDs, Logs, Sicherungen, lokale Übergabeunterlagen und fertige Builds sind ausgeschlossen. Downloads werden als Release-Dateien veröffentlicht.

## Arbeit mit Codex Cloud

Das GitHub-Repository ist die gemeinsame Grundlage für Cloud-Aufgaben und die Windows-Arbeitskopie. Codex Cloud kann den Quellcode bearbeiten und prüfen. Der vorhandene WPF-Build, die Selbsttests und der Installer benötigen Windows; die Cloud ersetzt außerdem keine Prüfung mit den echten Monitoren, Audiogeräten und der Sim-Hardware. Einrichtung, Projektregeln und Windows-Prüfbefehle stehen in [CLOUD.md](CLOUD.md).
