using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace RigPilot {
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; public string Key { get { return High + ":" + Low; } } }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct DisplaySource { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayTarget {
        public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling; public Rational Refresh; public uint Scanline; public int Available; public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public DisplaySource Source; public DisplayTarget Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayMode {
        public uint Type, Id; public Luid Adapter;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] Data;
    }
    [StructLayout(LayoutKind.Sequential)] struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TargetName {
        public DeviceHeader Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    public class MonitorBinding {
        public string DevicePath, Name;
        public uint Width, Height;
        public int X, Y;
        public double Hz;
        public bool Primary;
        public string Summary { get { return Width + " × " + Height + "  ·  " + Hz.ToString("0.##") + " Hz" + (Primary ? L.T("  ·  Hauptanzeige") : ""); } }
    }
    public class DisplaySnapshot {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = L.T("Monitor-Setup");
        public string Captured = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        public DisplayPath[] Paths;
        public DisplayMode[] Modes;
        public List<MonitorBinding> Monitors = new List<MonitorBinding>();
    }
    public static class Displays {
        public const int ConfirmationSeconds = 25;
        const int PreparationTimeoutSeconds = 45;
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayPath[] paths, ref uint modeCount, [Out] DisplayMode[] modes, IntPtr topology);
        [DllImport("user32.dll")] static extern int SetDisplayConfig(uint pathCount, [In] DisplayPath[] paths, uint modeCount, [In] DisplayMode[] modes, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DisplayConfigGetDeviceInfo(ref TargetName name);
        static void Check(int result, string action) { if (result != 0) throw new Win32Exception(result, action + ": " + new Win32Exception(result).Message); }
        static TargetName NameOf(DisplayTarget target) {
            TargetName name = new TargetName { Header = new DeviceHeader { Type = 2, Size = (uint)Marshal.SizeOf(typeof(TargetName)), Adapter = target.Adapter, Id = target.Id } };
            Check(DisplayConfigGetDeviceInfo(ref name), L.T("Monitorname lesen")); return name;
        }
        static DisplaySnapshot Query(uint flags) {
            for (int attempt = 0; attempt < 5; attempt++) {
                uint p, m; Check(GetDisplayConfigBufferSizes(flags, out p, out m), L.T("Monitoranzahl lesen"));
                if (p > 4096 || m > 16384) throw new InvalidDataException(L.T("Unerwartete Anzahl von Monitorkonfigurationen."));
                var paths = new DisplayPath[p]; var modes = new DisplayMode[m];
                int result = QueryDisplayConfig(flags, ref p, paths, ref m, modes, IntPtr.Zero);
                if (result == 122) continue;
                Check(result, L.T("Monitor-Setup lesen")); Array.Resize(ref paths, (int)p); Array.Resize(ref modes, (int)m);
                return new DisplaySnapshot { Paths = paths, Modes = modes };
            }
            throw new InvalidOperationException(L.T("Die Monitorverbindungen ändern sich gerade. Bitte kurz warten und erneut versuchen."));
        }
        public static DisplaySnapshot Capture(string name) {
            DisplaySnapshot data = Query(2); data.Name = name;
            foreach (var path in data.Paths) {
                TargetName target = NameOf(path.Target);
                var b = new MonitorBinding { DevicePath = target.DevicePath, Name = String.IsNullOrWhiteSpace(target.FriendlyName) ? "Monitor" : target.FriendlyName };
                if (path.Source.ModeIndex < data.Modes.Length) {
                    byte[] bytes = data.Modes[path.Source.ModeIndex].Data;
                    b.Width = BitConverter.ToUInt32(bytes, 0); b.Height = BitConverter.ToUInt32(bytes, 4);
                    b.X = BitConverter.ToInt32(bytes, 12); b.Y = BitConverter.ToInt32(bytes, 16); b.Primary = b.X == 0 && b.Y == 0;
                }
                b.Hz = path.Target.Refresh.Denominator == 0 ? 0 : (double)path.Target.Refresh.Numerator / path.Target.Refresh.Denominator;
                data.Monitors.Add(b);
            }
            if (data.Paths.Length == 0) throw new InvalidOperationException(L.T("Kein aktiver Desktop-Monitor gefunden."));
            return data;
        }
        public static DisplaySnapshot Prepare(DisplaySnapshot saved) {
            if (saved.Paths == null || saved.Modes == null || saved.Paths.Length == 0 || saved.Monitors.Count != saved.Paths.Length) throw new InvalidDataException(L.T("Unvollständiges Monitor-Setup."));
            DisplaySnapshot copy = Store.Clone(saved), available = Query(1);
            var names = new Dictionary<string, string>();
            foreach (var path in available.Paths) {
                string key = path.Target.Adapter.Key + ":" + path.Target.Id;
                if (!names.ContainsKey(key)) { try { names[key] = NameOf(path.Target).DevicePath; } catch { names[key] = ""; } }
            }
            var sources = new Dictionary<string, DisplaySource>();
            var used = new HashSet<string>();
            for (int i = 0; i < copy.Paths.Length; i++) {
                var old = copy.Paths[i]; string identity = copy.Monitors[i].DevicePath;
                if (String.IsNullOrEmpty(identity)) throw new InvalidOperationException(L.T("Die Hardware-ID eines gespeicherten Monitors fehlt. Setup bitte erneut speichern."));
                var matches = available.Paths.Where(x => x.Target.Available != 0 && String.Equals(names[x.Target.Adapter.Key + ":" + x.Target.Id], identity, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0) throw new InvalidOperationException(L.T("Monitor nicht verbunden: ") + copy.Monitors[i].Name);
                string oldSourceKey = old.Source.Adapter.Key + ":" + old.Source.Id;
                DisplaySource source;
                DisplayPath selected;
                if (sources.TryGetValue(oldSourceKey, out source)) {
                    var cloned = matches.Where(x => x.Source.Adapter.Key == source.Adapter.Key && x.Source.Id == source.Id).ToList();
                    if (cloned.Count == 0) throw new InvalidOperationException(L.T("Die gespeicherte Duplizierung ist mit diesen Anschlüssen nicht verfügbar."));
                    selected = cloned[0];
                } else {
                    var candidates = matches.Where(x => !used.Contains(x.Source.Adapter.Key + ":" + x.Source.Id)).OrderByDescending(x => x.Source.Id == old.Source.Id).ToList();
                    if (candidates.Count == 0) throw new InvalidOperationException(L.T("Die Monitore können nicht eindeutig zugeordnet werden. Setup bitte erneut speichern."));
                    selected = candidates[0]; sources[oldSourceKey] = selected.Source; used.Add(selected.Source.Adapter.Key + ":" + selected.Source.Id);
                }
                old.Source.Adapter = selected.Source.Adapter; old.Source.Id = selected.Source.Id;
                old.Target.Adapter = selected.Target.Adapter; old.Target.Id = selected.Target.Id;
                old.Target.Available = 1; old.Flags = 1;
                if (old.Source.ModeIndex >= copy.Modes.Length || old.Target.ModeIndex >= copy.Modes.Length) throw new InvalidDataException(L.T("Monitor-Modus fehlt im gespeicherten Setup."));
                copy.Modes[old.Source.ModeIndex].Adapter = old.Source.Adapter; copy.Modes[old.Source.ModeIndex].Id = old.Source.Id;
                copy.Modes[old.Target.ModeIndex].Adapter = old.Target.Adapter; copy.Modes[old.Target.ModeIndex].Id = old.Target.Id;
                copy.Paths[i] = old;
            }
            return copy;
        }
        public static void Validate(DisplaySnapshot saved) {
            var ready = Prepare(saved); Check(SetDisplayConfig((uint)ready.Paths.Length, ready.Paths, (uint)ready.Modes.Length, ready.Modes, 0x20 | 0x40), L.T("Monitor-Setup prüfen"));
        }
        public static void Apply(DisplaySnapshot saved) {
            var ready = Prepare(saved);
            Check(SetDisplayConfig((uint)ready.Paths.Length, ready.Paths, (uint)ready.Modes.Length, ready.Modes, 0x20 | 0x40), L.T("Monitor-Setup prüfen"));
            // Apply transiently. Persist only after the user confirms visibility.
            Check(SetDisplayConfig((uint)ready.Paths.Length, ready.Paths, (uint)ready.Modes.Length, ready.Modes, 0x20 | 0x80), L.T("Monitor-Setup anwenden"));
        }
        public static void PersistCurrent() {
            var now = Query(2); Check(SetDisplayConfig((uint)now.Paths.Length, now.Paths, (uint)now.Modes.Length, now.Modes, 0x20 | 0x80 | 0x200), L.T("Monitor-Setup speichern"));
        }
        public static bool ClaimDecision(string dir, string decision) {
            try {
                using (var file = new FileStream(Path.Combine(dir, "decision"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(decision); file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                return true;
            } catch (IOException) { if (File.Exists(Path.Combine(dir, "decision"))) return false; throw; }
        }
        public static int Guard(string dir) {
            var previous = Store.Read<DisplaySnapshot>(Path.Combine(dir, "previous.json"));
            return RunGuard(dir, delegate { Apply(previous); }, delegate { return DateTime.UtcNow; }, delegate { Thread.Sleep(150); });
        }
        internal static int RunGuard(string dir, Action restore, Func<DateTime> now, Action pause) {
            // Allow a slow driver switch, then count the same 25 seconds as the visible dialog.
            // The independent process still restores the previous layout if the UI freezes.
            try {
                DateTime until = now().AddSeconds(PreparationTimeoutSeconds);
                bool confirmationStarted = false;
                File.WriteAllText(Path.Combine(dir, "ready"), until.Ticks.ToString());
                while (true) {
                    string deadlineFile = Path.Combine(dir, "confirmation-deadline.json");
                    if (!confirmationStarted && File.Exists(deadlineFile)) {
                        until = new DateTime(Store.Read<long>(deadlineFile), DateTimeKind.Utc);
                        confirmationStarted = true;
                    }
                    if (File.Exists(Path.Combine(dir, "decision"))) { File.WriteAllText(Path.Combine(dir, "result"), "confirmed"); return 0; }
                    if (File.Exists(Path.Combine(dir, "rollback"))) break;
                    if (now() >= until) break;
                    pause();
                }
                if (!ClaimDecision(dir, "restore")) { File.WriteAllText(Path.Combine(dir, "result"), "confirmed"); return 0; }
                restore(); File.WriteAllText(Path.Combine(dir, "result"), "restored"); return 0;
            } catch (Exception ex) { Store.Log(L.T("Monitor-Rückkehr fehlgeschlagen: ") + ex.Message); File.WriteAllText(Path.Combine(dir, "result"), "error: " + ex.Message); return 1; }
        }
    }
}
