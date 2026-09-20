# Releases erstellen

## Voraussetzungen

Windows 11 x64, PowerShell, Git und der vorhandene .NET-Framework-Compiler. Für den Setup-Assistenten wird [Inno Setup](https://jrsoftware.org/isdl.php) benötigt; geprüft mit Version 7.1.0 x64. Der App-Build bleibt unabhängig davon.

Den offiziellen Compiler installieren oder nach Prüfung des Downloads [portabel bereitstellen](https://jrsoftware.org/ishelp/topic_technotes.htm). `package.ps1` erkennt `ISCC.exe` im PATH oder unter `Pruefung\tools\InnoSetup-7.1.0`. Alternativ den Pfad übergeben:

```powershell
.\package.ps1 -Installer -IsccPath 'C:\Tools\Inno Setup\ISCC.exe'
```

Die beim Aufbau verwendete offizielle Datei `innosetup-7.1.0-x64.exe` hatte SHA256 `0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f`. Compiler und Downloads gehören nicht ins Repository.

## Paketbau

1. Version in `src/App.cs` und `app.manifest` anpassen, wenn sich die Anwendung ändert. Release-Tag und Versionsangabe müssen zur Assembly passen.
2. `package.ps1 -Installer` ausführen. Das Skript baut separat und führt die Selbsttests aus; fehlgeschlagene Tests verhindern ein fertiges Paket.
3. Nur einen vollständig erfolgreichen Build veröffentlichen. Unter `Distribution\RigPilot-<Version>-<BuildId>` liegen `RigPilot.exe`, portables ZIP, Setup-EXE, ZIP-Prüfsumme und `SHA256SUMS.txt`.
4. Den Installer in einem separaten Testordner prüfen: Erstinstallation, erneute Installation als Update und Deinstallation. Ein künstliches Profil und eine Sicherung unter `Daten` müssen bytegenau erhalten bleiben. Die App nicht ungefragt starten; reale Audio-/Monitoränderungen auf dem Ziel-PC prüfen.
5. Quellstand committen und mit passendem Versions-Tag versehen. Den Tag zusammen mit dem Commit hochladen.
6. Unter [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases) eine Version für diesen Tag anlegen und die fünf Dateien aus genau diesem Build-Ordner anhängen. Vorhandene Versionen nicht überschreiben.

GitHub CLI kann die Dateien als Release hochladen. Bei mehrzeiligen Release-Notizen eine Datei über `--notes-file` übergeben. Keine Zugangsdaten in Skripten hinterlegen. EXE und ZIP werden nicht in die Git-Historie aufgenommen.

## Installer-Verhalten

`installer/RigPilot.iss` definiert einen Installer für den aktuellen Windows-Benutzer ohne Admin-Anforderung. Das stabile AppId ordnet Folgeversionen derselben Installation zu. Der Zielordner ist beschreibbar, damit das bestehende portable Datenformat erhalten bleibt. Das Setup legt Startmenü- und optional Desktop-Verknüpfungen an; es startet weder RigPilot noch einen Autostart.

Nur EXE, Bedienungsanleitung, Update-Anleitung und Versionsmanifest werden installiert. Der Ordner `Daten` ist kein Paketbestandteil und wird bei Deinstallation nicht gelöscht. Der vorhandene Anwendungs-Mutex verhindert Updates während einer laufenden Oberfläche. Laufende Monitor-Rückstellprozesse müssen vor dem Update abgeschlossen sein; Setup beendet solche Prozesse nicht automatisch.

EXE und Setup sind aktuell unsigniert. Eine digitale Signatur ist ein separater zukünftiger Schritt. Solange die vollständige Hardwareabnahme aussteht, dies in den Release-Notizen ausweisen.
