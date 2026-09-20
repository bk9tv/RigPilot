using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RigPilot {
    public class ProgramEntry {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string FileName = "";
        public string Arguments = "";
        public string WorkingDirectory = "";
        public bool SkipIfRunning = true;
        public bool RunAsAdmin;
    }
    public class GroupItem {
        public string ProgramId = "";
        public int DelaySeconds;
    }
    public class LaunchGroup {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Neue Gruppe";
        public string Description = "Dein Setup auf Knopfdruck.";
        public string OutputId = "";
        public string InputId = "";
        public string DisplayId = "";
        public List<GroupItem> Items = new List<GroupItem>();
    }
    public class Settings {
        public int Version = 1;
        public List<ProgramEntry> Programs = new List<ProgramEntry>();
        public List<LaunchGroup> Groups = new List<LaunchGroup>();
        public List<DisplaySnapshot> Displays = new List<DisplaySnapshot>();
    }
    public static class Store {
        public static string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Daten");
        public static string Config { get { return Path.Combine(Root, "profile.json"); } }
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }; }
        public static T Clone<T>(T value) { return Serializer().Deserialize<T>(Serializer().Serialize(value)); }
        public static void Write(string path, object value) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, Serializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
        public static T Read<T>(string path) { return Serializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static Settings Load() {
            if (!File.Exists(Config)) return new Settings();
            Settings result = Read<Settings>(Config);
            if (result == null || result.Version != 1 || result.Programs == null || result.Groups == null || result.Displays == null)
                throw new InvalidDataException("Die Profildatei ist ungültig. Eine Sicherung liegt unter profile.json.bak. Die Datei wurde nicht überschrieben.");
            return result;
        }
        public static void Save(Settings data) { Write(Config, data); }
        public static void Log(string message) {
            try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root, "protokoll.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
    }
    public static class Launcher {
        public static ProgramEntry FromCommand(string name, string command) {
            string s = Environment.ExpandEnvironmentVariables(command ?? "").Trim();
            string file = s, args = "";
            if (s.StartsWith("\"")) {
                int end = s.IndexOf('"', 1);
                if (end < 1) throw new InvalidDataException("Der Programmpfad enthält ein offenes Anführungszeichen.");
                file = s.Substring(1, end - 1); args = s.Substring(end + 1).Trim();
            } else if (!File.Exists(s)) {
                int end = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (end >= 0) { file = s.Substring(0, end + 4); args = s.Substring(end + 4).Trim(); }
                else { int space = s.IndexOf(' '); if (space > 0) { file = s.Substring(0, space); args = s.Substring(space + 1); } }
            }
            return new ProgramEntry { Name = name, FileName = file, Arguments = args };
        }
        public static void Validate(ProgramEntry item) {
            string file = Environment.ExpandEnvironmentVariables(item.FileName);
            Uri uri;
            bool protocol = Uri.TryCreate(file, UriKind.Absolute, out uri) && !uri.IsFile && (uri.Scheme == "steam" || uri.Scheme == "com.epicgames.launcher");
            if (!protocol && !File.Exists(file)) throw new FileNotFoundException(item.Name + ": Programm oder Verknüpfung fehlt.", file);
            if (!String.IsNullOrWhiteSpace(item.WorkingDirectory) && !Directory.Exists(Environment.ExpandEnvironmentVariables(item.WorkingDirectory)))
                throw new DirectoryNotFoundException(item.Name + ": Arbeitsordner fehlt.");
        }
        public static string Start(ProgramEntry item) {
            Validate(item);
            string file = Environment.ExpandEnvironmentVariables(item.FileName);
            if (item.SkipIfRunning && Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase)) {
                foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(file))) {
                    using (p) {
                        try { if (String.Equals(p.MainModule.FileName, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) return item.Name + " läuft bereits – übersprungen."; } catch { }
                    }
                }
            }
            ProcessStartInfo start = new ProcessStartInfo(file, item.Arguments ?? "") { UseShellExecute = true };
            if (!String.IsNullOrWhiteSpace(item.WorkingDirectory)) start.WorkingDirectory = Environment.ExpandEnvironmentVariables(item.WorkingDirectory);
            else if (File.Exists(file)) start.WorkingDirectory = Path.GetDirectoryName(file);
            if (item.RunAsAdmin) start.Verb = "runas";
            using (Process p = Process.Start(start)) { }
            return item.Name + " gestartet.";
        }
        public static List<ProgramEntry> Discover() {
            List<ProgramEntry> result = new List<ProgramEntry>();
            string[] roots = { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) };
            foreach (string root in roots) ScanLinks(root, result);
            return result.GroupBy(x => x.Name.ToLowerInvariant()).Select(x => x.First()).OrderBy(x => x.Name).ToList();
        }
        static void ScanLinks(string root, List<ProgramEntry> result) {
            if (!Directory.Exists(root)) return;
            try {
                foreach (string file in Directory.GetFiles(root, "*.lnk")) {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (System.Text.RegularExpressions.Regex.IsMatch(name, "uninstall|deinstall|readme|handbuch|manual", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    var entry = new ProgramEntry { Name = name, FileName = file };
                    object shell = null, link = null;
                    try {
                        Type type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type);
                        link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { file });
                        string target = (string)link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null);
                        if (!String.IsNullOrEmpty(target) && File.Exists(target) && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)) {
                            entry.FileName = target;
                            entry.Arguments = (string)link.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, link, null);
                            string wd = (string)link.GetType().InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, link, null);
                            if (Directory.Exists(wd)) entry.WorkingDirectory = wd;
                        }
                    } catch { } finally {
                        if (link != null && System.Runtime.InteropServices.Marshal.IsComObject(link)) System.Runtime.InteropServices.Marshal.ReleaseComObject(link);
                        if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell)) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                    }
                    result.Add(entry);
                }
                foreach (string dir in Directory.GetDirectories(root)) ScanLinks(dir, result);
            } catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
        public static void Seed(Settings data) {
            var found = Discover();
            string[] names = { "SimHub", "Simucube Tuner", "PimaxEVO", "Steam", "Discord", "OBS Studio", "RaceLab", "CrewChiefV4" };
            foreach (string name in names) { var p = found.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); if (p != null) data.Programs.Add(p); }
            LaunchGroup sim = new LaunchGroup { Name = "Simracing", Description = "Lenkrad, Telemetrie und dein Rig." };
            foreach (var p in data.Programs.Where(x => x.Name == "SimHub" || x.Name == "Simucube Tuner")) sim.Items.Add(new GroupItem { ProgramId = p.Id, DelaySeconds = sim.Items.Count == 0 ? 0 : 3 });
            LaunchGroup game = new LaunchGroup { Name = "Gaming", Description = "Spiele, Sound und dein Lieblingsbildschirm." };
            foreach (var p in data.Programs.Where(x => x.Name == "Steam" || x.Name == "Discord")) game.Items.Add(new GroupItem { ProgramId = p.Id, DelaySeconds = game.Items.Count == 0 ? 0 : 2 });
            data.Groups.Add(sim); data.Groups.Add(game);
        }
    }
}
