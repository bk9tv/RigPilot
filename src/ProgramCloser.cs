using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RigPilot {
    public interface IRunningProgram : IDisposable {
        string FileName { get; }
        bool HasExited { get; }
        bool RequestClose();
    }
    sealed class RunningProgram : IRunningProgram {
        readonly Process process;
        public RunningProgram(Process process) { this.process = process; }
        public string FileName { get { return process.MainModule.FileName; } }
        public bool HasExited { get { return process.HasExited; } }
        public bool RequestClose() { return process.CloseMainWindow(); }
        public void Dispose() { process.Dispose(); }
    }
    public static class ProgramCloser {
        internal static string ExecutablePath(ProgramEntry program) {
            string file = Environment.ExpandEnvironmentVariables(program.FileName ?? "").Trim().Trim('"');
            try {
                if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
                    object shell = null, link = null;
                    try {
                        Type type = Type.GetTypeFromProgID("WScript.Shell");
                        shell = Activator.CreateInstance(type);
                        link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { file });
                        file = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                    } finally {
                        if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
                        if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
                    }
                }
                if (String.IsNullOrWhiteSpace(file) || !Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return "";
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(file));
            } catch { return ""; }
        }
        internal static List<ProgramEntry> OtherGroupPrograms(Settings settings, LaunchGroup target) {
            var targetIds = new HashSet<string>(target.Items.Select(i => i.ProgramId));
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool needsSteam = false;
            foreach (var entry in settings.Programs.Where(p => targetIds.Contains(p.Id))) {
                string path = ExecutablePath(entry);
                if (!String.IsNullOrEmpty(path)) targetPaths.Add(path);
                if ((entry.FileName ?? "").StartsWith("steam:", StringComparison.OrdinalIgnoreCase)) needsSteam = true;
            }
            var result = new List<ProgramEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in settings.Groups.Where(g => g.Id != target.Id)) {
                foreach (var step in group.Items.AsEnumerable().Reverse()) {
                    var entry = settings.Programs.FirstOrDefault(p => p.Id == step.ProgramId);
                    if (entry == null || targetIds.Contains(entry.Id)) continue;
                    string path = ExecutablePath(entry);
                    if (targetPaths.Contains(path) || (needsSteam && Path.GetFileName(path).Equals("steam.exe", StringComparison.OrdinalIgnoreCase))) continue;
                    if (seen.Add(String.IsNullOrEmpty(path) ? entry.Id : path)) result.Add(entry);
                }
            }
            return result;
        }
        static IEnumerable<IRunningProgram> Find(string path) {
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)).Select(p => (IRunningProgram)new RunningProgram(p)).ToArray();
        }
        static void ShutdownSteam(string path) {
            var info = FileVersionInfo.GetVersionInfo(path);
            if ((info.ProductName ?? "").IndexOf("Steam", StringComparison.OrdinalIgnoreCase) < 0 ||
                (info.CompanyName ?? "").IndexOf("Valve", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(L.T("Die Datei konnte nicht als Steam-Client erkannt werden."));
            using (var process = Process.Start(new ProcessStartInfo(path, "-shutdown") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(path)
            })) { }
        }
        public static async Task<string> Close(ProgramEntry entry, CancellationToken token,
            Func<string, IEnumerable<IRunningProgram>> find = null, Func<int, CancellationToken, Task> delay = null,
            Action<string> shutdownSteam = null) {
            token.ThrowIfCancellationRequested();
            string path = ExecutablePath(entry);
            if (String.IsNullOrEmpty(path)) throw new InvalidOperationException(L.T("Für diesen Starttyp ist kein zuverlässiges automatisches Schließen möglich."));
            if (String.Equals(path, Path.GetFullPath(App.EntryPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.T("RigPilot selbst wird nicht automatisch geschlossen."));
            find = find ?? Find; delay = delay ?? Task.Delay; shutdownSteam = shutdownSteam ?? ShutdownSteam;
            var processes = find(path).ToList();
            try {
                var matching = new List<IRunningProgram>();
                bool unreadable = false;
                foreach (var process in processes) {
                    token.ThrowIfCancellationRequested();
                    try { if (!process.HasExited && String.Equals(path, Path.GetFullPath(process.FileName), StringComparison.OrdinalIgnoreCase)) matching.Add(process); }
                    catch { unreadable = true; }
                }
                if (matching.Count == 0) {
                    if (unreadable) throw new InvalidOperationException(L.T("Der laufende Prozess konnte nicht sicher dem Programmpfad zugeordnet werden."));
                    return entry.Name + L.T(" ist nicht aktiv – nichts zu schließen.");
                }
                if (Path.GetFileName(path).Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) shutdownSteam(path);
                else foreach (var process in matching) {
                    token.ThrowIfCancellationRequested();
                    if (!process.HasExited) process.RequestClose();
                }
                for (int i = 0; i < 30 && matching.Any(p => !p.HasExited); i++) await delay(200, token);
                if (matching.Any(p => !p.HasExited)) throw new InvalidOperationException(L.T("Das Programm bleibt geöffnet (z. B. im Infobereich oder mit einem offenen Dialog). Es wird nicht zwangsweise beendet."));
                return entry.Name + L.T(" geschlossen.");
            } finally { foreach (var process in processes) process.Dispose(); }
        }
    }
}
