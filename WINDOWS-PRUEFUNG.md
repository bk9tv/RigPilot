# RigPilot 1.3 unter Windows prüfen und übernehmen

Die Cloud-Änderungen erweitern Startgruppen um Wave Link 3 und verhindern, dass ein einzelner Programm- oder Audiofehler alle weiteren Programmstarts blockiert. Ein unbestätigter Monitorwechsel stoppt weiterhin die Gruppe und aktiviert die Rückstellung.

Der Quellcode wurde mit C# 5 gegen .NET-Framework-4.8-Referenzen kompiliert. 14 neue Logiktests wurden mit künstlichen Daten ausgeführt; zusätzlich wurden 19 JSON-RPC-Anfragen über die echte WebSocket-Implementierung gegen einen lokalen Testserver geprüft. Das ersetzt weder den Windows-Build noch die Prüfung mit der installierten Wave-Link-Version und den echten Geräten. Auf dem Windows-PC wurde aus dieser Cloud-Aufgabe nichts installiert.

## Übernahme im Projekt „Rig Pilot lokal“

Diese Schritte in einer lokalen Codex-Sitzung auf dem Windows-PC ausführen. Die Cloud kann weder die lokale Projektkopie öffnen noch den PC fernsteuern.

Zuerst `README.md`, `CLOUD.md` und vorhandene lokale `AGENTS.md` lesen. Den Projektordner, `git status --short`, den Origin und die installierte RigPilot-EXE prüfen. Vorhandene lokale Quellcodeänderungen erhalten. Die gemeinsame Quellcodebasis ist `https://github.com/bk9tv/RigPilot.git`, der Update-Branch heißt `codex/wave-link-gruppenstart`.

In der vorhandenen Git-Arbeitskopie zunächst `git fetch origin` ausführen. Bei sauberem Arbeitsstand den Update-Branch auswählen: falls er lokal noch fehlt, `git switch --track origin/codex/wave-link-gruppenstart`; andernfalls `git switch codex/wave-link-gruppenstart` und `git pull --ff-only`. Nicht auf einen anderen Stand zurücksetzen oder lokale Änderungen löschen. Falls noch keine Git-Arbeitskopie vorhanden ist, den gemeinsamen Branch in einen neuen, leeren Ordner klonen und bestehende lokale Dateien erhalten.

Den vollständigen Commit mit `git rev-parse HEAD` prüfen. Danach den folgenden Windows-Build und alle Selbsttests ausführen. Bei Fehlern die Ursache untersuchen, Quellcodekorrekturen testen und auf denselben Branch pushen. Erfolgreiche Cloud-Prüfungen ersetzen die Windows-Prüfung nicht.

Erst nach erfolgreicher Prüfung die vorhandene Installation gemäß dem Abschnitt „Bestehende Installation übernehmen“ sichern und aktualisieren. Bei mehreren installierten Kopien den tatsächlichen Zielpfad klären. Weder Profile noch Wave-Link-Geräte anhand angenommener Namen überschreiben; die lokale Wave-Link-Version und vorhandenen Geräte prüfen und die Gruppen passend konfigurieren. Anschließend Programmstarts, beide Audio-Profile und die WPF-Oberfläche am Ziel-PC prüfen. Hardwareänderungen nur im Rahmen dieser angeforderten Prüfung durchführen, die Monitor-Rückstellung erhalten und Testergebnisse ausdrücklich nennen.

Quellcodeänderungen zwischen diesem lokalen Projekt und der Cloud vor und nach jeder Aufgabe gemäß `CLOUD.md` synchronisieren. Persönliche Daten und installierte EXE-Dateien werden dabei nicht in Git aufgenommen.

## Build und vollständige Selbsttests

Den Quellcode in einen separaten Ordner entpacken. Unter Windows 11 x64 mit .NET Framework 4.8+ in diesem Ordner PowerShell öffnen und ausführen:

```powershell
$ErrorActionPreference = 'Stop'
$checkFolder = Join-Path $PWD ('Pruefung\wave-link-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkFolder | Out-Null
$checkExe = Join-Path $checkFolder 'RigPilot.exe'
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\build.ps1 -OutputPath $checkExe
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
$testFolder = Join-Path $checkFolder 'self-test'
$process = Start-Process -FilePath $checkExe -ArgumentList ('--self-test "' + $testFolder + '"') -Wait -PassThru -WindowStyle Hidden
$report = @(Get-Content -LiteralPath (Join-Path $testFolder 'self-tests.txt'))
$report
if ($process.ExitCode -ne 0 -or $report.Count -eq 0 -or @($report | Where-Object { $_ -notmatch '^PASS ' }).Count -ne 0) {
    throw 'Selbsttests fehlgeschlagen oder Bericht unvollständig.'
}
$checkExe
```

Die Selbsttests starten weder Wave Link noch echte Gerätewechsel. Die künstlichen Gruppen- und Wave-Link-Prüfungen sind in die vorhandene Testsuite integriert.

## Bestehende Installation übernehmen

RigPilot schließen und eine laufende Monitor-Rückstellung vollständig abwarten. Den bisherigen Anwendungsordner einschließlich `Daten` sichern. Nach erfolgreicher Windows-Prüfung ausschließlich die neue `RigPilot.exe` aus dem Prüfungsordner in den bisherigen Anwendungsordner übernehmen. Den vorhandenen Ordner `Daten` behalten; er enthält die Programme, Gruppen, Gerätezuordnungen und Sicherungen. Die Anwendung aus dem bisherigen Anwendungsordner starten.

Für einen neuen Installer stattdessen den dokumentierten Paketbau und die Installer-Prüfungen in `RELEASING.md` verwenden. Das Quellcodearchiv ist kein fertiger Installer.

## Geräte und Gruppen konfigurieren

1. Wave Link 3 öffnen. Das gewünschte Simrig-Mikrofon und RØDE müssen dort bereits als auswählbare Kanäle eingerichtet sein. Falls es nur einen Mikrofonkanal gibt, zuerst die gewünschten Eingänge in Wave Link konfigurieren. Die globale Stummschaltung eines zu aktivierenden Mikrofons dort aufheben.
2. Die Simrig-Gruppe bearbeiten: **Audio über Wave Link 3 steuern**, anschließend **Wave-Link-Auswahl laden**. Pimax-Crystal-Kopfhörer als **Main Output Device**, den eigenen **Discord Mix** und das gewünschte Simrig-Mikrofon zum Aktivieren auswählen. Bei Bedarf RØDE in diesem Mix stummschalten.
3. Die Gaming-Gruppe entsprechend mit **Boxen**, **Discord Mix** und **RØDE** konfigurieren; bei Bedarf das Simrig-Mikrofon in diesem Mix stummschalten.
4. Discord muss den entsprechenden Wave-Link-Mix als Eingang verwenden. Ein direkt in Discord ausgewähltes physisches Mikrofon wird nicht umgestellt.
5. Wave Link und benötigte Pimax-Software bei Bedarf zuerst in die Programme der Gruppe aufnehmen und passende Startpausen einstellen. **Prüfen** liest Geräte und Programmpfade ohne Umschalten.
6. Beide Gruppen unter Windows starten. Hauptausgang, Mikrofonzustand ausschließlich im ausgewählten Mix, erhaltene andere Mixe und die tatsächlichen Programmstarts kontrollieren. Monitorwechsel und Rückstellung separat am Ziel-PC prüfen.

Wave Link 1/2 wird von der neuen Integration nicht unterstützt. Die installierte Version konnte aus der Cloud nicht abgefragt werden.

## Wenn weiterhin kein Programm startet

Das Gruppenfenster zeigt die Anzahl der gespeicherten Programme sowie Meldungen für jeden Start. `Daten/protokoll.txt` enthält dieselben Meldungen. Bei fehlenden Programmen den dort genannten Pfad unter **Programme → Bearbeiten** korrigieren oder die passende Verknüpfung auswählen. Die genaue Ursache in der bisherigen Installation kann erst anhand ihrer Meldungen festgestellt werden.

Ein leerer Gruppeneintrag startet keine Programme. Bereits laufende Programme werden bei aktivierter Option übersprungen. Ein Monitorwechsel ohne Bestätigung stoppt die Gruppe weiterhin bewusst; nachfolgende Audio- oder Programmschritte werden dann nicht ausgeführt.
