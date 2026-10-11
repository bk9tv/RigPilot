using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RigPilot {
    public partial class MainWindow {
        void EditProgram(ProgramEntry existing) {
            var item = existing == null ? new ProgramEntry() : Store.Clone(existing);
            var w = UI.Dialog(this, existing == null ? L.T("Programm hinzufügen") : L.T("Programm bearbeiten"), 650, 610);
            var root = new DockPanel { Margin = new Thickness(24) };
            var buttons = UI.Row(); buttons.Margin = new Thickness(0,20,0,0); DockPanel.SetDock(buttons,Dock.Bottom); root.Children.Add(buttons);
            var form = new StackPanel();root.Children.Add(new ScrollViewer { Content=form });
            form.Children.Add(UI.Text(L.T("Programm einrichten"),24,"#F0F4FA",true));
            form.Children.Add(UI.Label("Name"));var name=new TextBox {Text=item.Name};form.Children.Add(name);
            form.Children.Add(UI.Label(L.T("Programm, Verknüpfung oder Steam-URL")));var path=new TextBox {Text=item.FileName};
            var choose=UI.Button(L.T("Durchsuchen …"),delegate{var picker=new OpenFileDialog {Filter=L.T("Programme und Verknüpfungen|*.exe;*.lnk;*.url|Alle Dateien|*.*")};if(picker.ShowDialog(w)==true){path.Text=picker.FileName;if(String.IsNullOrWhiteSpace(name.Text))name.Text=Path.GetFileNameWithoutExtension(picker.FileName);}});choose.Margin=new Thickness(8,0,0,0);var pathrow=new DockPanel();DockPanel.SetDock(choose,Dock.Right);pathrow.Children.Add(choose);pathrow.Children.Add(path);form.Children.Add(pathrow);
            form.Children.Add(UI.Label(L.T("Startargumente (optional)")));var args=new TextBox {Text=item.Arguments};form.Children.Add(args);
            form.Children.Add(UI.Label(L.T("Arbeitsordner (optional)")));var wd=new TextBox {Text=item.WorkingDirectory};form.Children.Add(wd);
            var skip=new CheckBox {Content=L.T("Bereits laufendes Programm überspringen"),IsChecked=item.SkipIfRunning,Margin=new Thickness(0,20,0,8)};form.Children.Add(skip);
            form.Children.Add(UI.Text(L.T("Erkennung über den vollständigen EXE-Pfad. Verknüpfungen und Spiele-URLs übernimmt der jeweilige Launcher."),11,"#9AAABD"));
            var admin=new CheckBox {Content=L.T("Als Administrator starten (Windows fragt beim Start nach)"),IsChecked=item.RunAsAdmin};form.Children.Add(admin);
            buttons.Children.Add(UI.Button(L.T("Speichern"),delegate{
                if(String.IsNullOrWhiteSpace(name.Text))throw new InvalidOperationException(L.T("Bitte einen Namen eingeben."));
                item.Name=name.Text.Trim();item.FileName=path.Text.Trim().Trim('"');item.Arguments=args.Text;item.WorkingDirectory=wd.Text.Trim().Trim('"');item.SkipIfRunning=skip.IsChecked==true;item.RunAsAdmin=admin.IsChecked==true;
                Launcher.Validate(item);if(existing==null)data.Programs.Add(item);else data.Programs[data.Programs.IndexOf(existing)]=item;Save();w.DialogResult=true;
            },true));buttons.Children.Add(UI.Button(L.T("Abbrechen"),delegate{w.DialogResult=false;}));
            if(existing!=null)buttons.Children.Add(UI.Button(L.T("Entfernen"),delegate{
                if(UI.Message(L.F("„{0}“ aus der Bibliothek und allen Startgruppen entfernen? Die Anwendung bleibt installiert.",existing.Name),"RigPilot",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                data.Programs.Remove(existing);foreach(var g in data.Groups)g.Items.RemoveAll(x=>x.ProgramId==existing.Id);Save();w.DialogResult=true;
            }));
            w.Content=root;w.ShowDialog();ShowPage(page);
        }
        void DiscoverDialog() {
            var found=Launcher.Discover().Where(x=>!data.Programs.Any(y=>String.Equals(x.FileName,y.FileName,StringComparison.OrdinalIgnoreCase)&&x.Arguments==y.Arguments)).ToList();
            var w=UI.Dialog(this,L.T("Programme erkennen"),680,650);var root=new DockPanel {Margin=new Thickness(24)};
            var top=new StackPanel();top.Children.Add(UI.Text(L.T("Im Startmenü gefunden"),24,"#F0F4FA",true));top.Children.Add(UI.Text(L.T("Wähle die Programme für deine Bibliothek aus."),13,"#9AAABD"));var filter=new TextBox{Margin=new Thickness(0,16,0,15),ToolTip=L.T("Suchen")};top.Children.Add(filter);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
            var selected=new HashSet<string>();var buttons=UI.Row(UI.Button(L.T("Auswahl hinzufügen"),delegate{foreach(var p in found.Where(x=>selected.Contains(x.Id)))data.Programs.Add(p);Save();w.DialogResult=true;},true),UI.Button(L.T("Abbrechen"),delegate{w.DialogResult=false;}));buttons.Margin=new Thickness(0,18,0,0);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            var list=new StackPanel();root.Children.Add(new ScrollViewer{Content=list});
            Action render=delegate {list.Children.Clear();foreach(var p in found.Where(x=>x.Name.IndexOf(filter.Text,StringComparison.OrdinalIgnoreCase)>=0)){var cb=new CheckBox{Content=p.Name,IsChecked=selected.Contains(p.Id),ToolTip=p.FileName,Margin=new Thickness(8,10,0,10)};cb.Checked+=delegate{selected.Add(p.Id);};cb.Unchecked+=delegate{selected.Remove(p.Id);};list.Children.Add(cb);}if(list.Children.Count==0)list.Children.Add(UI.Text(L.T("Keine weiteren passenden Programme gefunden."),13,"#9AAABD"));};filter.TextChanged+=delegate{render();};render();w.Content=root;w.ShowDialog();ShowPage(page);
        }
        List<Choice> AudioChoices(int flow,string selected) {
            var values=new List<Choice> {new Choice{Id="",Name=L.T("Unverändert lassen")}};
            foreach(var dev in Audio.List(flow))values.Add(new Choice{Id=dev.Id,Name=dev.Name});
            if(!String.IsNullOrEmpty(selected)&&!values.Any(x=>x.Id==selected))values.Add(new Choice{Id=selected,Name=L.T("Gespeichertes Gerät (momentan nicht verbunden)")});return values;
        }
        void EditGroup(LaunchGroup existing) {
            var group=existing==null?new LaunchGroup():Store.Clone(existing);
            var w=UI.Dialog(this,existing==null?L.T("Startgruppe erstellen"):L.T("Startgruppe bearbeiten"),740,810);
            var root=new DockPanel {Margin=new Thickness(24)};
            var buttons=UI.Row();buttons.Margin=new Thickness(0,18,0,0);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            var form=new StackPanel();root.Children.Add(new ScrollViewer {Content=form});form.Children.Add(UI.Text(L.T("Deine Startgruppe"),24,"#F0F4FA",true));
            form.Children.Add(UI.Label("Name"));var name=new TextBox {Text=group.Name};form.Children.Add(name);
            form.Children.Add(UI.Label(L.T("Kurzbeschreibung")));var desc=new TextBox {Text=group.Description};form.Children.Add(desc);
            form.Children.Add(UI.Label(L.T("Programme · Reihenfolge und Pause vor jedem Start")));
            var items=new StackPanel();form.Children.Add(items);
            var delayBoxes=new Dictionary<GroupItem,TextBox>();
            Action captureDelays=delegate {foreach(var pair in delayBoxes){int val;if(!Int32.TryParse(pair.Value.Text,out val)||val<0||val>300)throw new InvalidOperationException(L.T("Startpausen müssen zwischen 0 und 300 Sekunden liegen."));pair.Key.DelaySeconds=val;}};
            Action render=null;
            render=delegate {
                items.Children.Clear();delayBoxes.Clear();
                for(int n=0;n<group.Items.Count;n++) {
                    GroupItem step=group.Items[n];int index=n;var app=data.Programs.FirstOrDefault(x=>x.Id==step.ProgramId);
                    var row=new DockPanel{Margin=new Thickness(0,0,0,7)};
                    var delay=new TextBox {Text=step.DelaySeconds.ToString(),Width=46,Padding=new Thickness(5),ToolTip=L.T("Pause vor diesem Start in Sekunden")};delayBoxes[step]=delay;
                    var up=UI.Button("↑",delegate{captureDelays();if(index>0){group.Items.RemoveAt(index);group.Items.Insert(index-1,step);render();}});up.Padding=new Thickness(8,4,8,4);up.IsEnabled=index>0;
                    var down=UI.Button("↓",delegate{captureDelays();if(index<group.Items.Count-1){group.Items.RemoveAt(index);group.Items.Insert(index+1,step);render();}});down.Padding=new Thickness(8,4,8,4);down.IsEnabled=index<group.Items.Count-1;
                    var remove=UI.Button("×",delegate{captureDelays();group.Items.Remove(step);render();});remove.Padding=new Thickness(9,4,9,4);
                    var sec=UI.Text(" s  ",12,"#9AAABD");sec.VerticalAlignment=VerticalAlignment.Center;var controls=UI.Row(delay,sec,up,down,remove);DockPanel.SetDock(controls,Dock.Right);row.Children.Add(controls);
                    var label=UI.Text((index+1)+".  "+(app==null?L.T("Programm fehlt"):app.Name),13,"#E7EEF8",true);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);items.Children.Add(UI.Card(row,10));
                }
                if(group.Items.Count==0)items.Children.Add(UI.Text(L.T("Noch keine Programme. Ein Profil darf auch nur Audio oder Monitore umstellen."),12,"#9AAABD"));
            };render();
            var library=UI.Choices(data.Programs.Select(x=>new Choice{Id=x.Id,Name=x.Name}),"");
            var add=UI.Button(L.T("+ Hinzufügen"),delegate{captureDelays();string id=UI.Selected(library);if(String.IsNullOrEmpty(id))return;if(group.Items.Any(x=>x.ProgramId==id))throw new InvalidOperationException(L.T("Dieses Programm ist bereits in der Gruppe."));group.Items.Add(new GroupItem{ProgramId=id,DelaySeconds=group.Items.Count==0?0:2});render();});add.Margin=new Thickness(8,0,0,0);var addrow=new DockPanel();DockPanel.SetDock(add,Dock.Right);addrow.Children.Add(add);addrow.Children.Add(library);form.Children.Add(addrow);
            form.Children.Add(UI.Label(L.T("Wiedergabegerät")));var output=UI.Choices(AudioChoices(0,group.OutputId),group.OutputId);form.Children.Add(output);
            form.Children.Add(UI.Label(L.T("Mikrofon")));var input=UI.Choices(AudioChoices(1,group.InputId),group.InputId);form.Children.Add(input);
            var wave=group.WaveLink??new WaveLinkProfile();
            var useWave=new CheckBox{Content=L.T("Audio über Wave Link 3 steuern"),IsChecked=wave.Enabled,Margin=new Thickness(0,18,0,8)};form.Children.Add(useWave);
            var wavePanel=new StackPanel();form.Children.Add(wavePanel);
            var waveStatus=UI.Text(L.T("Wave Link öffnen und die verfügbaren Geräte und Mixe laden."),12,"#9AAABD");wavePanel.Children.Add(waveStatus);
            var loadWave=UI.Button(L.T("Wave-Link-Auswahl laden"),delegate{});wavePanel.Children.Add(loadWave);
            Func<IEnumerable<Choice>,string,List<Choice>> choices=delegate(IEnumerable<Choice> values,string selected){
                var list=new List<Choice>{new Choice{Id="",Name=L.T("Unverändert lassen")}};list.AddRange(values);
                if(!String.IsNullOrEmpty(selected)&&!list.Any(x=>x.Id==selected))list.Add(new Choice{Id=selected,Name=L.T("Gespeicherte Auswahl (noch nicht verfügbar)")});return list;
            };
            string outputKey=String.IsNullOrEmpty(wave.OutputId)?"":WaveLinkControl.OutputKey(wave.OutputDeviceId,wave.OutputId);
            wavePanel.Children.Add(UI.Label(L.T("Wave Link · Hauptausgang (Main Output Device)")));var waveOutput=UI.Choices(choices(new Choice[0],outputKey),outputKey);wavePanel.Children.Add(waveOutput);
            wavePanel.Children.Add(UI.Label(L.T("Wave Link · Mikrofon-Mix (z. B. Discord Mix)")));var waveMix=UI.Choices(choices(new Choice[0],wave.MixId),wave.MixId);wavePanel.Children.Add(waveMix);
            wavePanel.Children.Add(UI.Label(L.T("Mikrofon in diesem Mix aktivieren")));var waveMic=UI.Choices(choices(new Choice[0],wave.MicChannelId),wave.MicChannelId);wavePanel.Children.Add(waveMic);
            wavePanel.Children.Add(UI.Label(L.T("Anderes Mikrofon in diesem Mix stummschalten (optional)")));var waveMute=UI.Choices(choices(new Choice[0],wave.MutedChannelId),wave.MutedChannelId);wavePanel.Children.Add(waveMute);
            Action waveMode=delegate{wavePanel.Visibility=useWave.IsChecked==true?Visibility.Visible:Visibility.Collapsed;output.IsEnabled=input.IsEnabled=useWave.IsChecked!=true;};
            useWave.Checked+=delegate{waveMode();};useWave.Unchecked+=delegate{waveMode();};waveMode();
            WaveLinkState waveState=null;var waveCancel=new CancellationTokenSource();var waveToken=waveCancel.Token;w.Closed+=delegate{waveCancel.Cancel();waveCancel.Dispose();};
            loadWave.Click+=async delegate{
                loadWave.IsEnabled=false;
                try {
                    using(var client=await WaveLinkClient.Open(waveToken)) {
                        waveState=await WaveLinkControl.Read(client,waveToken);
                        Action<ComboBox,IEnumerable<Choice>> fill=delegate(ComboBox box,IEnumerable<Choice> values){string selected=UI.Selected(box);var list=choices(values,selected);box.ItemsSource=list;box.SelectedItem=list.FirstOrDefault(x=>x.Id==selected)??list[0];};
                        fill(waveOutput,waveState.Outputs.outputDevices.SelectMany(d=>(d.outputs??new List<WaveLinkOutput>()).Select(o=>new Choice{Id=WaveLinkControl.OutputKey(d.id,o.id),Name=d.Label()+" · "+o.Label()})));
                        fill(waveMix,waveState.Mixes.mixes.Select(x=>new Choice{Id=x.id,Name=x.Label()}));
                        var channels=waveState.Channels.channels.Select(x=>new Choice{Id=x.id,Name=x.Label()}).ToList();fill(waveMic,channels);fill(waveMute,channels);
                        waveStatus.Text=L.T("Wave Link verbunden. Hauptausgang, Mix und Mikrofone auswählen.");
                    }
                }catch(OperationCanceledException){}catch(Exception ex){waveStatus.Text=ex.Message;Store.Log("Wave Link: "+ex.Message);}finally{loadWave.IsEnabled=true;}
            };
            form.Children.Add(UI.Label(L.T("Monitor-Setup")));var displayChoices=new List<Choice>{new Choice{Id="",Name=L.T("Unverändert lassen")}};displayChoices.AddRange(data.Displays.Select(x=>new Choice{Id=x.Id,Name=x.Name}));var display=UI.Choices(displayChoices,group.DisplayId);form.Children.Add(display);
            var hint=UI.Text(L.T("Wave Link: Monitor-Setup bestätigen → Programme starten → Audio setzen. Windows-Audio: Audio vor den Programmen; bei Fehlern danach erneut versuchen. Neue Monitor-Setups speicherst du unter Monitore."),12,"#9AAABD");hint.Margin=new Thickness(0,14,0,0);form.Children.Add(hint);
            buttons.Children.Add(UI.Button(L.T("Gruppe speichern"),delegate{
                captureDelays();if(String.IsNullOrWhiteSpace(name.Text))throw new InvalidOperationException(L.T("Bitte einen Gruppennamen eingeben."));
                var selectedWave=new WaveLinkProfile{Enabled=useWave.IsChecked==true,MixId=UI.Selected(waveMix),MicChannelId=UI.Selected(waveMic),MutedChannelId=UI.Selected(waveMute)};
                string key=UI.Selected(waveOutput);if(!String.IsNullOrEmpty(key)){var pair=Store.Serializer().Deserialize<string[]>(key);selectedWave.OutputDeviceId=pair[0];selectedWave.OutputId=pair[1];}
                if(selectedWave.Enabled) {
                    if((!String.IsNullOrEmpty(selectedWave.MicChannelId)||!String.IsNullOrEmpty(selectedWave.MutedChannelId))&&String.IsNullOrEmpty(selectedWave.MixId))throw new InvalidOperationException(L.T("Bitte einen verfügbaren Wave-Link-Mix für die Mikrofone auswählen."));
                    if(!String.IsNullOrEmpty(selectedWave.MicChannelId)&&selectedWave.MicChannelId==selectedWave.MutedChannelId)throw new InvalidOperationException(L.T("Ein Mikrofon kann nicht gleichzeitig aktiviert und stummgeschaltet werden."));
                    if(waveState!=null)WaveLinkControl.Validate(selectedWave,waveState);
                }
                group.Name=name.Text.Trim();group.Description=desc.Text.Trim();group.OutputId=UI.Selected(output);group.InputId=UI.Selected(input);group.DisplayId=UI.Selected(display);group.WaveLink=selectedWave;
                if(existing==null)data.Groups.Add(group);else data.Groups[data.Groups.IndexOf(existing)]=group;Save();w.DialogResult=true;
            },true));
            buttons.Children.Add(UI.Button(L.T("Abbrechen"),delegate{w.DialogResult=false;}));w.Content=root;w.ShowDialog();ShowPage(page);
        }
    }
}
