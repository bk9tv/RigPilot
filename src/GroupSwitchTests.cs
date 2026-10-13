using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RigPilot {
    public static class GroupSwitchTests {
        static readonly Func<int, CancellationToken, Task> NoWait = delegate(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(0); };
        sealed class TestProcess : IRunningProgram {
            public string Path;
            public bool Exited, Refuse, Disposed;
            public int CloseRequests;
            public string FileName { get { return Path; } }
            public bool HasExited { get { return Exited; } }
            public bool RequestClose() { CloseRequests++; if (!Refuse) Exited = true; return !Refuse; }
            public void Dispose() { Disposed = true; }
        }
        static Settings Groups(out LaunchGroup sim, out LaunchGroup gaming) {
            var settings = new Settings(); sim = new LaunchGroup { Name = "Sim" }; gaming = new LaunchGroup { Name = "Gaming" };
            settings.Groups.Add(sim); settings.Groups.Add(gaming);
            AddProgram(settings, sim, "SimApp", @"C:\Test\Sim.exe");
            AddProgram(settings, gaming, "GameApp", @"C:\Test\Gaming.exe");
            return settings;
        }
        static ProgramEntry AddProgram(Settings settings, LaunchGroup group, string name, string path) {
            var entry = new ProgramEntry { Name = name, FileName = path }; settings.Programs.Add(entry);
            group.Items.Add(new GroupItem { ProgramId = entry.Id }); return entry;
        }
        public static void Add(Action<string, Action> test, Action<bool> assert) {
            test("Switching Sim and Gaming closes only programs exclusive to other groups", delegate {
                LaunchGroup sim, gaming; var settings = Groups(out sim, out gaming);
                var shared = AddProgram(settings, sim, "Shared", @"C:\Test\Shared.exe"); gaming.Items.Add(new GroupItem { ProgramId = shared.Id });
                settings.Programs.Add(new ProgramEntry { Name = "Unrelated", FileName = @"C:\Test\Unrelated.exe" });
                assert(ProgramCloser.OtherGroupPrograms(settings, gaming).Select(p => p.Name).SequenceEqual(new[] { "SimApp" }));
                assert(ProgramCloser.OtherGroupPrograms(settings, sim).Select(p => p.Name).SequenceEqual(new[] { "GameApp" }));
                assert(ProgramCloser.OtherGroupPrograms(settings, Store.Clone(sim)).All(p => p.Name != "SimApp"));
            });
            test("Shared executable paths survive switches despite different library IDs and arguments", delegate {
                LaunchGroup sim, gaming; var settings = Groups(out sim, out gaming);
                AddProgram(settings, sim, "Shared first", @"C:\Test\Shared.exe");
                var alias = AddProgram(settings, gaming, "Shared second", @"c:\test\SHARED.EXE"); alias.Arguments = "--tray";
                assert(ProgramCloser.OtherGroupPrograms(settings, gaming).Select(p => p.Name).SequenceEqual(new[] { "SimApp" }));
            });
            test("Other programs close in reverse order and duplicate paths close only once", delegate {
                LaunchGroup sim, gaming; var settings = Groups(out sim, out gaming);
                AddProgram(settings, sim, "Second", @"C:\Test\Second.exe");
                AddProgram(settings, sim, "Duplicate", @"c:\test\SIM.EXE");
                sim.Items.Add(new GroupItem { ProgramId = "removed" });
                var plan = ProgramCloser.OtherGroupPrograms(settings, gaming);
                assert(plan.Count == 2 && plan[0].Name == "Duplicate" && plan[1].Name == "Second");
            });
            test("A Steam game URL keeps the Steam client available", delegate {
                LaunchGroup sim, gaming; var settings = Groups(out sim, out gaming);
                AddProgram(settings, sim, "Steam", @"C:\Test\Steam.exe");
                AddProgram(settings, gaming, "Game URL", "steam://rungameid/123");
                assert(ProgramCloser.OtherGroupPrograms(settings, gaming).All(p => p.Name != "Steam"));
            });
            test("Graceful closing matches the full path and leaves same-name foreign processes untouched", delegate {
                var entry = new ProgramEntry { Name = "Sim", FileName = @"C:\Test\Sim.exe" };
                var owned = new TestProcess { Path = @"c:\test\SIM.EXE" };
                var foreign = new TestProcess { Path = @"C:\Other\Sim.exe" };
                string result = ProgramCloser.Close(entry, CancellationToken.None, delegate { return new[] { foreign, owned }; }, NoWait).GetAwaiter().GetResult();
                assert(result.Contains("geschlossen") && owned.CloseRequests == 1 && foreign.CloseRequests == 0);
                assert(owned.Disposed && foreign.Disposed);
            });
            test("A tray app that refuses to exit is reported after a bounded graceful wait", delegate {
                var entry = new ProgramEntry { Name = "Tray", FileName = @"C:\Test\Tray.exe" };
                var process = new TestProcess { Path = entry.FileName, Refuse = true }; int waits = 0; bool failed = false;
                try { ProgramCloser.Close(entry, CancellationToken.None, delegate { return new[] { process }; },
                    delegate(int ms, CancellationToken token) { waits++; return Task.FromResult(0); }).GetAwaiter().GetResult(); }
                catch (InvalidOperationException ex) { failed = ex.Message.Contains("nicht zwangsweise"); }
                assert(failed && waits == 30 && process.CloseRequests == 1 && !process.Exited && process.Disposed);
            });
            test("Steam shutdown uses its exit command instead of closing only its window", delegate {
                var entry = new ProgramEntry { Name = "Steam", FileName = @"C:\Test\Steam.exe" };
                var process = new TestProcess { Path = entry.FileName }; int exits = 0;
                ProgramCloser.Close(entry, CancellationToken.None, delegate { return new[] { process }; }, NoWait,
                    delegate(string path) { assert(path == entry.FileName); exits++; process.Exited = true; }).GetAwaiter().GetResult();
                assert(exits == 1 && process.CloseRequests == 0 && process.Disposed);
            });
            test("Unsupported launch types and RigPilot itself never enumerate or close processes", delegate {
                int lookups = 0;
                foreach (string path in new[] { "steam://rungameid/123", "https://example.invalid", App.EntryPath }) {
                    bool failed = false;
                    try { ProgramCloser.Close(new ProgramEntry { FileName = path }, CancellationToken.None,
                        delegate { lookups++; return new IRunningProgram[0]; }, NoWait).GetAwaiter().GetResult(); }
                    catch (InvalidOperationException) { failed = true; }
                    assert(failed);
                }
                assert(lookups == 0);
            });
            test("Close failures do not block the remaining closures, target launches or Wave Link", delegate {
                LaunchGroup sim, gaming; var settings = Groups(out sim, out gaming); gaming.WaveLink.Enabled = true;
                AddProgram(settings, sim, "Refuses", @"C:\Test\Refuses.exe");
                var calls = new List<string>();
                var result = GroupRunner.Run(settings, gaming, CancellationToken.None, delegate { }, delegate { },
                    delegate { calls.Add("audio"); return Task.FromResult(0); }, delegate(ProgramEntry p) { calls.Add("start " + p.Name); return "started"; }, NoWait,
                    delegate(ProgramEntry p, CancellationToken token) { calls.Add("close " + p.Name); if (p.Name == "Refuses") throw new InvalidOperationException("still running"); return Task.FromResult("closed"); }).GetAwaiter().GetResult();
                assert(result.CloseFailures == 1 && result.StartFailures == 0 && result.IssueCount == 1);
                assert(calls.SequenceEqual(new[] { "close Refuses", "close SimApp", "start GameApp", "audio" }));
            });
            test("Cancellation between closures prevents pending closures, starts and audio changes", delegate {
                LaunchGroup sim, gaming; var settings = Groups(out sim, out gaming);
                AddProgram(settings, sim, "Second", @"C:\Test\Second.exe");
                int closes = 0, starts = 0, audio = 0; bool cancelled = false;
                using (var cancel = new CancellationTokenSource()) {
                    try { GroupRunner.Run(settings, gaming, cancel.Token, delegate { }, delegate { },
                        delegate { audio++; return Task.FromResult(0); }, delegate { starts++; return "started"; }, NoWait,
                        delegate { closes++; cancel.Cancel(); return Task.FromResult("closed"); }).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { cancelled = true; }
                }
                assert(cancelled && closes == 1 && starts == 0 && audio == 0);
            });
            test("Already-cancelled closing never queries a running process", delegate {
                bool looked = false, cancelled = false;
                using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel();
                    try { ProgramCloser.Close(new ProgramEntry { FileName = @"C:\Test\App.exe" }, cancel.Token,
                        delegate { looked = true; return new IRunningProgram[0]; }, NoWait).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { cancelled = true; }
                }
                assert(cancelled && !looked);
            });
        }
    }
}
