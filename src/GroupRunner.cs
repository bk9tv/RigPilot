using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RigPilot {
    public class GroupRunResult {
        public int StartFailures;
        public bool AudioFailed;
        public int IssueCount { get { return StartFailures + (AudioFailed ? 1 : 0); } }
    }
    // Keep the launch sequence independent of the window so failures and cancellation are testable.
    public static class GroupRunner {
        public static async Task<GroupRunResult> Run(Settings settings, LaunchGroup group, CancellationToken token,
            Action<string> report, Action<string> progress, Func<CancellationToken, Task> configureAudio,
            Func<ProgramEntry, string> start = null, Func<int, CancellationToken, Task> delay = null) {
            start = start ?? Launcher.Start;
            delay = delay ?? Task.Delay;
            var result = new GroupRunResult();
            bool waveLink = group.WaveLink != null && group.WaveLink.Enabled;
            Func<Task> audio = async delegate {
                token.ThrowIfCancellationRequested();
                try { await configureAudio(token); result.AudioFailed = false; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) {
                    result.AudioFailed = true;
                    report(L.T("Audio konnte nicht umgestellt werden: ") + ex.Message);
                }
            };
            if (!waveLink) await audio();
            for (int i = 0; i < group.Items.Count; i++) {
                token.ThrowIfCancellationRequested();
                var step = group.Items[i];
                var app = settings.Programs.FirstOrDefault(x => x.Id == step.ProgramId);
                try {
                    if (app == null) throw new InvalidOperationException(L.T("Ein Programm der Gruppe fehlt in der Bibliothek.") + " " + step.ProgramId);
                    if (step.DelaySeconds < 0 || step.DelaySeconds > 300)
                        throw new InvalidOperationException(L.T("Eine Startpause liegt außerhalb von 0 bis 300 Sekunden."));
                    for (int sec = step.DelaySeconds; sec > 0; sec--) {
                        progress(L.F("Warte {0} s vor {1} …", sec, app.Name));
                        await delay(1000, token);
                    }
                    token.ThrowIfCancellationRequested();
                    progress((i + 1) + " / " + group.Items.Count + " · " + app.Name);
                    report(start(app));
                } catch (OperationCanceledException) { throw; }
                catch (Exception ex) {
                    result.StartFailures++;
                    report(L.T("Nicht gestartet: ") + (app == null ? step.ProgramId : app.Name) + " · " + ex.Message);
                }
                await delay(120, token);
            }
            // Wave Link/Pimax may themselves be in the group. Retry failed Windows audio only
            // after programs have had a chance to make their endpoints available.
            if (waveLink || (result.AudioFailed && group.Items.Count > 0)) {
                progress(L.T("Audio wird eingerichtet …"));
                await audio();
            }
            return result;
        }
    }
}
