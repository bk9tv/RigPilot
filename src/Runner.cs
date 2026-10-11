using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RigPilot {
    public partial class MainWindow {
        void CheckGroup(LaunchGroup group) {
            foreach(var item in group.Items) {
                var program=data.Programs.FirstOrDefault(x=>x.Id==item.ProgramId);
                if(program==null)throw new InvalidOperationException(L.T("Ein Programm der Gruppe fehlt in der Bibliothek."));
                if(item.DelaySeconds<0||item.DelaySeconds>300)throw new InvalidOperationException(L.T("Eine Startpause liegt außerhalb von 0 bis 300 Sekunden."));
                Launcher.Validate(program);
            }
            if(!String.IsNullOrEmpty(group.DisplayId)) {
                var setup=data.Displays.FirstOrDefault(x=>x.Id==group.DisplayId);if(setup==null)throw new InvalidOperationException(L.T("Das Monitor-Setup der Gruppe fehlt."));Displays.Validate(setup);
            } else if(group.WaveLink==null||!group.WaveLink.Enabled)ValidateAudio(group);
        }
        async void CheckGroupAndReport(LaunchGroup group) {
            try {
                CheckGroup(group);
                if(group.WaveLink!=null&&group.WaveLink.Enabled) {
                    using(var client=await WaveLinkClient.Open(CancellationToken.None))
                        WaveLinkControl.Validate(group.WaveLink,await WaveLinkControl.Read(client,CancellationToken.None));
                }
                UI.Message(L.T("Programmpfade und ausgewähltes Setup sind gültig. Es wurde nichts gestartet oder umgestellt.")+(String.IsNullOrEmpty(group.DisplayId)?"":L.T("\nAudio wird nach dem Monitorwechsel erneut geprüft.")),L.T("RigPilot · Gruppe geprüft"));
            }catch(Exception ex){UI.Error(ex);}
        }
        void ValidateAudio(LaunchGroup group) {
            if(!String.IsNullOrEmpty(group.OutputId)&&!Audio.List(0).Any(x=>x.Id==group.OutputId))throw new InvalidOperationException(L.T("Das Wiedergabegerät der Gruppe ist nicht verfügbar."));
            if(!String.IsNullOrEmpty(group.InputId)&&!Audio.List(1).Any(x=>x.Id==group.InputId))throw new InvalidOperationException(L.T("Das Mikrofon der Gruppe ist nicht verfügbar."));
        }
        async Task WaitForAudio(LaunchGroup group,CancellationToken token) {
            for(int i=0;i<16;i++){token.ThrowIfCancellationRequested();try{ValidateAudio(group);return;}catch(InvalidOperationException){if(i==15)throw;}await Task.Delay(500,token);}
        }
        void RunGroup(LaunchGroup group) {
            if(busy)return;
            busy=true;cancellation=new CancellationTokenSource();var token=cancellation.Token;
            var w=UI.Dialog(this,L.T("Startgruppe · ")+group.Name,620,470);bool finished=false;
            var root=new DockPanel {Margin=new Thickness(26)};var top=new StackPanel();top.Children.Add(UI.Text(group.Name,26,"#F0F4FA",true));var progress=UI.Text(L.T("Setup wird vorbereitet …"),13,"#81E0B6");progress.Margin=new Thickness(0,8,0,18);top.Children.Add(progress);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
            var bottom=new StackPanel {Margin=new Thickness(0,16,0,0)};var close=UI.Button(L.T("Abbrechen"),delegate{if(finished)w.Close();else {cancellation.Cancel();progress.Text=L.T("Ausstehende Starts werden abgebrochen …");}});bottom.Children.Add(close);var note=UI.Text(L.T("Abbrechen stoppt ausstehende Starts. Bereits gestartete Programme bleiben geöffnet."),11,"#9AAABD");note.Margin=new Thickness(0,10,0,0);bottom.Children.Add(note);DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
            var log=new TextBox {IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=UI.Brush("#111923"),FontSize=12};root.Children.Add(log);w.Content=root;
            Action<string> report=delegate(string line){log.AppendText(line+Environment.NewLine+Environment.NewLine);log.ScrollToEnd();Store.Log(group.Name+": "+line);};
            w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!finished){e.Cancel=true;cancellation.Cancel();}};
            w.Loaded+=async delegate {
                try {
                    if(!String.IsNullOrEmpty(group.DisplayId)) {
                        report(L.T("Monitor-Setup wird angewendet …"));
                        if(!await ApplyDisplay(data.Displays.First(x=>x.Id==group.DisplayId),w))throw new OperationCanceledException(L.T("Monitorwechsel nicht bestätigt. Gruppenstart gestoppt."));
                        report(L.T("Monitor-Setup bestätigt."));
                    }
                    report(L.F("{0} Programm(e) in der Startgruppe.",group.Items.Count));
                    var result=await GroupRunner.Run(data,group,token,report,delegate(string text){progress.Text=text;},async delegate(CancellationToken audioToken){
                        if(group.WaveLink!=null&&group.WaveLink.Enabled) {
                            await WaveLinkControl.ApplyLocal(group.WaveLink,audioToken);
                            report(L.T("Wave-Link-Audio-Profil übernommen."));
                        } else {
                            await WaitForAudio(group,audioToken);
                            if(!String.IsNullOrEmpty(group.OutputId)){Audio.SetDefault(group.OutputId,0);report(L.T("Wiedergabegerät umgestellt."));}
                            if(!String.IsNullOrEmpty(group.InputId)){Audio.SetDefault(group.InputId,1);report(L.T("Mikrofon umgestellt."));}
                        }
                    });
                    progress.Text=result.IssueCount==0?L.T("✓  Dein Setup ist bereit."):L.F("Abgeschlossen mit {0} Fehler(n). Details stehen im Protokoll.",result.IssueCount);SetStatus(group.Name+": "+progress.Text);
                } catch(OperationCanceledException){progress.Text=L.T("Gruppenstart abgebrochen.");report(L.T("Weitere Schritte wurden gestoppt."));SetStatus(group.Name+L.T(": abgebrochen."));}
                catch(Exception ex){progress.Text=L.T("Gruppenstart gestoppt.");report(ex.Message);SetStatus(group.Name+": "+ex.Message);}
                finally{finished=true;close.Content=L.T("Schließen");RefreshHardware();}
            };
            try{w.ShowDialog();}finally{busy=false;cancellation.Dispose();cancellation=null;ShowPage(page);}
        }
        async Task<bool> ApplyDisplay(DisplaySnapshot target,Window owner=null) {
            if(owner==null&&busy)return false;
            bool wasBusy=busy;busy=true;string dir=null;bool watchdogStarted=false;bool accepted=false;DisplaySnapshot previous=null;
            if(owner==null)((UIElement)Content).IsEnabled=false;
            try {
                Displays.Validate(target);previous=Displays.Capture(L.T("Vor Monitorwechsel"));
                dir=Path.Combine(Store.Root,"Monitor-Rueckkehr",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);Store.Write(Path.Combine(dir,"previous.json"),previous);
                using(var guard=Process.Start(new ProcessStartInfo(App.EntryPath,"--guard \""+dir+"\""){UseShellExecute=false,CreateNoWindow=true})) {
                    if(guard==null)throw new InvalidOperationException(L.T("Die automatische Monitor-Rückkehr konnte nicht gestartet werden."));
                    watchdogStarted=true;
                    for(int i=0;i<40&&!File.Exists(Path.Combine(dir,"ready"));i++){if(guard.HasExited)throw new InvalidOperationException(L.T("Die Monitor-Rückkehr ist nicht verfügbar."));await Task.Delay(100);}
                    if(!File.Exists(Path.Combine(dir,"ready")))throw new InvalidOperationException(L.T("Die Monitor-Rückkehr antwortet nicht."));
                    DateTime guardDeadline=new DateTime(Int64.Parse(File.ReadAllText(Path.Combine(dir,"ready"))),DateTimeKind.Utc);
                    Displays.Apply(target);
                    if(File.Exists(Path.Combine(dir,"decision"))||DateTime.UtcNow>=guardDeadline.AddSeconds(-2))throw new InvalidOperationException(L.T("Der Monitorwechsel hat zu lange gedauert. Das vorherige Setup wird wiederhergestellt."));
                    var confirm=UI.Dialog(owner??this,L.T("Monitor-Setup bestätigen"),540,300);confirm.WindowStartupLocation=WindowStartupLocation.CenterScreen;confirm.Topmost=true;
                    var p=new StackPanel{Margin=new Thickness(25)};p.Children.Add(UI.Text(L.T("Ist alles gut sichtbar?"),24,"#F0F4FA",true));
                    var countdown=UI.Text("",14,"#B3C5D8");countdown.Margin=new Thickness(0,18,0,20);p.Children.Add(countdown);
                    DateTime until=DateTime.MaxValue;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(200)};
                    Action update=delegate{int sec=Math.Max(0,(int)Math.Ceiling((until-DateTime.UtcNow).TotalSeconds));countdown.Text=L.F("Ohne Bestätigung geht es in {0} Sekunden zurück.",sec);if(sec<=0){timer.Stop();confirm.DialogResult=false;}};
                    timer.Tick+=delegate{update();};
                    var keep=UI.Button(L.T("Ja, beibehalten"),delegate{
                        if(DateTime.UtcNow>=until){confirm.DialogResult=false;return;}
                        if(!Displays.ClaimDecision(dir,"keep")){confirm.DialogResult=false;return;}
                        try{Displays.PersistCurrent();accepted=true;confirm.DialogResult=true;}catch{confirm.DialogResult=false;throw;}
                    },true);
                    p.Children.Add(UI.Row(keep,UI.Button(L.T("Zurück"),delegate{confirm.DialogResult=false;})));confirm.Content=p;
                    confirm.Loaded+=delegate{
                        // Start only when the confirmation dialog is visible; switching time is extra.
                        until=DateTime.UtcNow.AddSeconds(Displays.ConfirmationSeconds);
                        Store.Write(Path.Combine(dir,"confirmation-deadline.json"),until.Ticks);
                        update();timer.Start();
                    };
                    confirm.Closed+=delegate{timer.Stop();};confirm.ShowDialog();
                    if(accepted)SetStatus(L.F("Monitor-Setup „{0}“ aktiviert.",target.Name));
                }
            }catch(Exception ex){UI.Error(ex);}
            try {
                if(watchdogStarted&&!accepted&&dir!=null) {
                    try {
                        File.WriteAllText(Path.Combine(dir,"rollback"),"rollback");string result=Path.Combine(dir,"result");
                        for(int i=0;i<100&&!File.Exists(result);i++)await Task.Delay(100);
                        if(!File.Exists(result))throw new IOException(L.T("Die automatische Rückkehr wurde noch nicht bestätigt. Der unabhängige Rückstellprozess bleibt aktiv."));
                        string state=File.ReadAllText(result);if(state.StartsWith("error:"))throw new IOException(state);
                        // Also restore from the UI after a slow apply or failed persistence.
                        Displays.Apply(previous);Displays.PersistCurrent();
                        SetStatus(L.T("Vorheriges Monitor-Setup wiederhergestellt."));
                    }catch(Exception ex){UI.Error(ex);}
                }
            }finally{busy=wasBusy;if(owner==null)((UIElement)Content).IsEnabled=true;}
            return accepted;
        }
    }
}
