using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace RigPilot {
    public class StartupEntry {
        public string Name, Command, Hive, Kind, Source;
        public bool Enabled, Known;
        public bool Machine { get { return Hive == "HKLM"; } }
        public string Identity { get { return Hive + "|" + Kind + "|" + Name; } }
        public string Caption { get { return (Machine ? "Alle Benutzer" : "Dein Benutzer") + "  ·  " + Source; } }
    }
    public class StartupBackup {
        public string Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public StartupEntry Entry;
        public byte[] Previous;
        public byte[] Written;
    }
    public static class Startup {
        const string Approval = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static List<string> Warnings = new List<string>();
        public static bool? Decode(byte[] value) {
            if (value == null) return true;
            if (value.Length < 4) return null;
            uint state = BitConverter.ToUInt32(value, 0);
            if (state == 2 || state == 6) return true;
            if (state == 3 || state == 7) return false;
            return null;
        }
        public static byte[] Encode(bool enabled) {
            var data = new byte[12]; data[0] = (byte)(enabled ? 2 : 3);
            if (!enabled) Buffer.BlockCopy(BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()), 0, data, 4, 8);
            return data;
        }
        public static byte[] ReadApproval(StartupEntry entry) {
            using (var hive = RegistryKey.OpenBaseKey(entry.Machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.OpenSubKey(Approval + entry.Kind)) { return key == null ? null : key.GetValue(entry.Name) as byte[]; }
        }
        static void AddState(StartupEntry entry, List<StartupEntry> list) {
            bool? state = Decode(ReadApproval(entry)); entry.Known = state.HasValue; entry.Enabled = state.GetValueOrDefault(); list.Add(entry);
        }
        public static List<StartupEntry> List() {
            Warnings.Clear(); var entries = new List<StartupEntry>();
            foreach (bool machine in new[] { false, true }) {
                foreach (bool wow in new[] { false, true }) {
                    if (!machine && wow) continue;
                    try {
                        using (var hive = RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, wow ? RegistryView.Registry32 : RegistryView.Registry64))
                        using (var key = hive.OpenSubKey(Run)) {
                            if (key != null) foreach (string name in key.GetValueNames()) {
                                object command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                                if (command is string) AddState(new StartupEntry { Name = name, Command = (string)command, Hive = machine ? "HKLM" : "HKCU", Kind = wow ? "Run32" : "Run", Source = wow ? "Registry · 32 Bit" : "Registry" }, entries);
                            }
                        }
                    } catch (Exception ex) { Warnings.Add("Autostart-Registry: " + ex.Message); }
                }
                try {
                    string dir = Environment.GetFolderPath(machine ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
                    if (Directory.Exists(dir)) foreach (string path in Directory.GetFiles(dir)) {
                        if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(path) & FileAttributes.Hidden) != 0) continue;
                        AddState(new StartupEntry { Name = Path.GetFileName(path), Command = "\"" + path + "\"", Hive = machine ? "HKLM" : "HKCU", Kind = "StartupFolder", Source = "Autostart-Ordner" }, entries);
                    }
                } catch (Exception ex) { Warnings.Add("Autostart-Ordner: " + ex.Message); }
            }
            return entries.OrderBy(x => x.Name).ToList();
        }
        static StartupEntry Current(StartupEntry requested) {
            var current = List().FirstOrDefault(x => x.Identity == requested.Identity);
            if (current == null || current.Command != requested.Command) throw new InvalidOperationException("Der Autostart-Eintrag wurde inzwischen geändert. Bitte die Ansicht aktualisieren.");
            return current;
        }
        public static void Set(StartupEntry requested, bool enabled) {
            StartupEntry current = Current(requested);
            if (!current.Known) throw new InvalidOperationException("Dieser Windows-Status wird nicht sicher unterstützt. Bitte die Windows-Autostart-Einstellungen verwenden.");
            var backup = new StartupBackup { Entry = current, Previous = ReadApproval(current), Written = Encode(enabled) };
            // Backup must succeed before any registry mutation.
            Store.Write(Path.Combine(Store.Root, "Autostart-Sicherungen", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json"), backup);
            WriteApproval(current, backup.Written);
            if (Decode(ReadApproval(current)) != enabled) throw new IOException("Windows hat den Autostart-Status nicht übernommen.");
            Store.Log("Autostart " + current.Name + (enabled ? " aktiviert." : " deaktiviert."));
        }
        static void WriteApproval(StartupEntry current, byte[] value) {
            using (var hive = RegistryKey.OpenBaseKey(current.Machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = hive.CreateSubKey(Approval + current.Kind)) {
                if (value == null) key.DeleteValue(current.Name, false);
                else key.SetValue(current.Name, value, RegistryValueKind.Binary);
            }
        }
        public static void Restore(StartupBackup backup) {
            StartupEntry current = Current(backup.Entry); byte[] now = ReadApproval(current);
            if (!(now == null && backup.Written == null) && (now == null || backup.Written == null || !now.SequenceEqual(backup.Written))) throw new InvalidOperationException("Der Status wurde nach dieser Sicherung geändert. Eine neuere Änderung wird nicht überschrieben.");
            var reverse = new StartupBackup { Entry = current, Previous = now, Written = backup.Previous };
            Store.Write(Path.Combine(Store.Root, "Autostart-Sicherungen", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-undo.json"), reverse);
            WriteApproval(current, backup.Previous);
            byte[] restored = ReadApproval(current);
            if (!(restored == null && backup.Previous == null) && (restored == null || backup.Previous == null || !restored.SequenceEqual(backup.Previous))) throw new IOException("Die Wiederherstellung konnte nicht bestätigt werden.");
        }
    }
}
