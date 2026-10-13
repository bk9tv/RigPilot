# RigPilot in Codex Cloud

## Repository und Arbeitsweise

Verwende `bk9tv/RigPilot` und den für die Aufgabe gewählten Branch. Lies vor Änderungen `README.md` und diese Datei, prüfe `git status` und erhalte bereits vorhandene Änderungen. Jede Cloud-Aufgabe hat eine eigene Arbeitskopie. Zusätzliche Worktrees sind dafür nicht erforderlich.

Quellcodeänderungen werden über GitHub zwischen Cloud und Windows ausgetauscht. Vor einem Windows-Build den betreffenden Commit beziehungsweise PR-Branch beziehen. Noch nicht eingecheckte lokale Änderungen sind in einer Cloud-Aufgabe nicht verfügbar. Lokale Betriebsunterlagen sind absichtlich von Git ausgeschlossen; diese Datei enthält die für Cloud-Aufgaben relevanten Projektregeln.

## Cloud und „Rig Pilot lokal“ synchron halten

Beide Arbeitskopien verwenden `bk9tv/RigPilot` als gemeinsame Quellcodebasis. Die lokale Windows-Arbeitskopie ist die führende Entwicklungsumgebung; Änderungen werden von dort auf `codex/wave-link-gruppenstart` gepusht und in der bestehenden Cloud-Aufgabe bezogen. Die Cloud soll dabei keine parallelen Änderungen beginnen. Für die Bearbeitung desselben Arbeitsstands in Cloud und Windows denselben Branch und denselben Commit verwenden. Nach abgeschlossener Prüfung werden Änderungen über einen PR in `main` übernommen; ein Push allein aktualisiert die installierte Windows-EXE nicht.

- Zu Beginn jeder Aufgabe `git status --short` prüfen, lokale Änderungen erhalten und `git fetch origin` ausführen. Den gewählten Branch anschließend nur mit `git pull --ff-only` aktualisieren. Bei abweichender Historie oder lokalen Konflikten zunächst die Änderungen sichern und den Konflikt nachvollziehbar lösen; kein Force-Push, Reset oder Löschen lokaler Arbeit zum Synchronisieren.
- Zum Ende einer Quellcodeänderung die passenden Prüfungen ausführen, die Änderungen committen und auf den gemeinsamen Arbeitsbranch pushen. Den vollständigen Commit nennen, damit die andere Arbeitskopie denselben Stand beziehen kann. Fehlt Schreibzugriff, den nicht übertragenen Stand ausdrücklich melden.
- Vor einem Windows-Build den übertragenen Commit beziehen und mit `git rev-parse HEAD` vergleichen. Windows-Ergebnisse und verbleibende Geräteprüfungen wahrheitsgemäß dokumentieren. Neue Quellcodekorrekturen auf demselben Branch zurückübertragen.
- Synchronisation betrifft Quellcode, Assets und Build-Anweisungen. Persönliche Profile, Geräte-IDs, Logs, Sicherungen und Builds bleiben lokal und außerhalb von Git. Eine vorhandene Installation nur nach den Windows-Prüfschritten aktualisieren und ihren Ordner `Daten` erhalten.

Diese Schritte werden bei Aufgabenbeginn und -ende ausgeführt. Es gibt keinen dauerhaften Synchronisationsdienst. Eine Cloud-Sitzung besitzt keinen Zugriff auf den Windows-PC oder andere lokale Codex-Projekte, sofern keine solche Verbindung ausdrücklich als Werkzeug bereitgestellt wird. Die lokale Übernahme erfolgt in einer Sitzung auf dem Windows-PC nach `WINDOWS-PRUEFUNG.md`.

## Umgebung einrichten

Für Codebearbeitung und Reviews sind keine zusätzlichen Pakete, Secrets oder Hintergrunddienste erforderlich. RigPilot verwendet keine NuGet-Pakete. Das Installationsskript kann leer bleiben. Die App während der Einrichtung nicht starten und keine persönlichen Profile oder Gerätedaten hochladen.

Als Startanweisung der Cloud-Umgebung verwenden:

> Arbeite im ausgecheckten Repository bk9tv/RigPilot. Lies README.md und CLOUD.md vor Änderungen und beachte die dort beschriebenen Projektregeln und Prüfgrenzen. Prüfe git status und erhalte vorhandene Änderungen. RigPilot ist eine Windows-WPF-App mit C# 5 und .NET Framework 4.8+. In einer Linux-Umgebung sind Codebearbeitung, statische Prüfungen und Reviews möglich; der dokumentierte Build, die App-Selbsttests und der Installer benötigen Windows. Erfinde keine erfolgreichen Windows- oder Hardwaretests. Installiere keine Ersatzlaufzeit oder Windows-API-Mocks nur für die Einrichtung. Persönliche Daten, Profile, Geräte-IDs, Logs, Sicherungen und Builds bleiben außerhalb von Git. Nenne ausgeführte Prüfungen und noch offene Windows- beziehungsweise Hardwareprüfungen ausdrücklich.

Vor dem Veröffentlichen der Umgebung Repository-Zugriff und vorhandene Build-Eingaben prüfen. Bei späteren Änderungen mindestens `git diff --check` ausführen. Statische Prüfungen belegen weder einen erfolgreichen WPF-Build noch bestandene Selbsttests.

## Verbindliche Projektregeln

- Native Windows-WPF-App, .NET Framework 4.8+, C# 5. `build.ps1` verwendet den Windows-Framework-Compiler. Keine Framework- oder Sprachmigration als Nebenwirkung der Cloud-Einrichtung.
- Die 60-sekündige Monitor-Bestätigung beginnt mit dem sichtbaren Dialog. Der unabhängige Rückstellprozess und die 45-Sekunden-Frist vor Erscheinen des Dialogs müssen erhalten bleiben.
- Gruppenwechsel schließen nur Programme anderer Gruppen mit eindeutig zugeordnetem EXE-Pfad; gemeinsame Programme bleiben geöffnet. Reguläres Schließen verwenden, niemals Prozesse zwangsweise beenden. Ein Schließfehler wird gemeldet und blockiert keine nachfolgenden Schritte. Ohne Monitorbestätigung dürfen keine Programme geschlossen oder gestartet werden.
- Vorhandene Profile und Sicherungen erhalten. Datenformatänderungen benötigen eine kompatible Migration. Die Sprachwahl liegt getrennt von `profile.json`; eigene Namen, Pfade und Geräte-IDs werden nicht übersetzt.
- Relevante Logikänderungen mit den vorhandenen Selbsttests unter Windows prüfen. UI-Änderungen unter Windows visuell prüfen. Selbsttests verändern keine realen Geräte und ersetzen keine Hardwareabnahme.
- `Daten/`, `Pruefung/`, `PC-Sicherung/`, Umzugspakete, persönliche Profile, Geräte-IDs, Logs und EXE-Builds bleiben außerhalb von Git. Ausschließlich künstliche Daten für Prüfungen verwenden.
- Verteilpakete mit `package.ps1`, vollständige Pakete mit `package.ps1 -Installer` erstellen. `Distribution/` bleibt außerhalb von Git. Pakete enthalten niemals `Daten/`, persönliche Profile oder die PC-Sicherung.
- Installer-Prüfung und Veröffentlichung nach `RELEASING.md`. EXE, ZIP und Setup werden als GitHub-Release-Dateien verteilt. Ein Quellcode-Push ist noch kein geprüftes Release.
- Auf dem Ziel-PC nur bei geschlossener App aktualisieren, laufende Monitor-Rückstellung abwarten und dessen `Daten/` erhalten.
- Git-Name `RigPilot`, Commit-E-Mail die verifizierte GitHub-NoReply-Adresse von `bk9tv`. Keine private E-Mail in Commits verwenden.

## Windows-Prüfung

Im Repository unter Windows 11 x64 mit .NET Framework 4.8+ ausführen. Der separate Ausgabeordner erhält eine eventuell vorhandene Anwendungs-EXE:

```powershell
$ErrorActionPreference = 'Stop'
$checkFolder = Join-Path $PWD ('Pruefung\cloud-' + [Guid]::NewGuid().ToString('N'))
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
```

Audio-Umschaltung, Monitorwechsel einschließlich Bestätigung/Rückstellung, Autostart/UAC und Sim-Hardware müssen zusätzlich auf dem Ziel-PC geprüft werden. Eine Windows-Entwicklungs-VM ersetzt diese Prüfung nicht.
