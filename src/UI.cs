using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RigPilot {
    public class Choice { public string Id, Name; public override string ToString() { return Name; } }
    public static class UI {
        public static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        public static TextBlock Text(string text, double size = 13, string color = "#F0F4FA", bool bold = false) {
            return new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        }
        public static TextBlock Label(string text) { var b = Text(text, 12, "#A4B4C7"); b.Margin = new Thickness(0, 17, 0, 7); return b; }
        public static Button Button(string name, Action click, bool primary = false) {
            var b = new Button { Content = name, Margin = new Thickness(0, 0, 8, 0) };
            if (primary) b.Style = (Style)Application.Current.FindResource("Primary");
            b.Click += delegate { try { click(); } catch (Exception ex) { Error(ex); } }; return b;
        }
        public static Border Card(UIElement child, int padding = 18) { return new Border { Child = child, Background = Brush("#171F2B"), BorderBrush = Brush("#2B3646"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 12) }; }
        public static void Error(Exception ex) { Store.Log(L.T("Fehler: ") + ex.ToString()); Message(ex.Message, L.T("RigPilot · Hinweis")); }
        public static MessageBoxResult Message(string text, string title, MessageBoxButton buttons = MessageBoxButton.OK) {
            var app = Application.Current;
            if (app == null) return MessageBox.Show(text, title, buttons);
            var owner = app.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? app.MainWindow;
            var w = Dialog(owner, title, 540, 330);
            var root = new DockPanel { Margin = new Thickness(24) };
            bool question = buttons == MessageBoxButton.YesNo;
            MessageBoxResult result = question ? MessageBoxResult.No : MessageBoxResult.OK;
            var accept = Button(question ? L.T("Ja") : "OK", delegate { result = question ? MessageBoxResult.Yes : MessageBoxResult.OK; w.DialogResult = true; }, true);
            accept.IsDefault = !question;
            var actions = Row(accept); actions.Margin = new Thickness(0, 18, 0, 0);
            if (question) { var no = Button(L.T("Nein"), delegate { w.DialogResult = false; }); no.IsDefault = true; no.IsCancel = true; actions.Children.Add(no); }
            else accept.IsCancel = true;
            DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
            root.Children.Add(new ScrollViewer { Content = Text(text, 14) });
            w.Content = root; w.ShowDialog(); return result;
        }
        public static StackPanel Row(params UIElement[] elements) { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var e in elements) row.Children.Add(e); return row; }
        public static Window Dialog(Window owner, string title, double width, double height) {
            var w = new Window { Owner = owner, Icon = Branding.WindowIcon, Title = title, Width = width, Height = Math.Min(height, SystemParameters.WorkArea.Height - 60), MinWidth = Math.Min(width, 500), MinHeight = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize };
            w.SourceInitialized += delegate { DarkTitle(w); }; return w;
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        public static void DarkTitle(Window w) { try { int yes = 1; DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 20, ref yes, 4); } catch { } }
        public static string AskName(Window owner, string title, string initial) {
            var w = Dialog(owner, title, 480, 220); w.ResizeMode = ResizeMode.NoResize;
            var p = new StackPanel { Margin = new Thickness(24) }; p.Children.Add(Text(title, 20, "#F0F4FA", true));
            var box = new TextBox { Text = initial, Margin = new Thickness(0, 16, 0, 20) }; p.Children.Add(box);
            var save = Button(L.T("Speichern"), delegate { if (!String.IsNullOrWhiteSpace(box.Text)) w.DialogResult = true; }, true); save.IsDefault = true;
            p.Children.Add(Row(save, Button(L.T("Abbrechen"), delegate { w.DialogResult = false; }))); w.Content = p;
            w.Loaded += delegate { box.Focus(); box.SelectAll(); }; return w.ShowDialog() == true ? box.Text.Trim() : null;
        }
        public static ComboBox Choices(IEnumerable<Choice> values, string selected) {
            var list = values.ToList(); var box = new ComboBox { ItemsSource = list };
            box.SelectedItem = list.FirstOrDefault(x => x.Id == selected) ?? list.FirstOrDefault(); return box;
        }
        public static string Selected(ComboBox box) { return box.SelectedItem == null ? "" : ((Choice)box.SelectedItem).Id; }
        public static void OpenGuide(Window owner) {
            string text;
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("RigPilot." + L.GuideFile))
            using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
            var w = Dialog(owner, L.T("Kurzanleitung  ↗").Replace("  ↗", ""), 810, 740);
            w.Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20), FontSize = 13 };
            w.ShowDialog();
        }
        public static void Open(string target) { using (var p = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })) { } }
    }
    public partial class MainWindow : Window {
        Settings data;
        StackPanel body, nav;
        TextBlock status;
        string page = "overview";
        bool busy;
        CancellationTokenSource cancellation;
        List<StartupEntry> startup = new List<StartupEntry>();
        List<AudioDevice> outputs = new List<AudioDevice>(), inputs = new List<AudioDevice>();
        DisplaySnapshot currentDisplay;
        string hardwareError = "";
        public MainWindow(Settings settings) {
            Style=(Style)Application.Current.FindResource(typeof(Window));
            Icon=Branding.WindowIcon;
            data = settings; Title = "RigPilot"; Width = 1110; Height = 850; MinWidth = 900; MinHeight = 620;
            Height=Math.Min(Height,SystemParameters.WorkArea.Height-50);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SourceInitialized += delegate { UI.DarkTitle(this); };
            Closing += delegate(System.Object sender, System.ComponentModel.CancelEventArgs e) { if (busy) { e.Cancel = true; SetStatus(L.T("Ein Vorgang läuft. Bitte erst abbrechen oder abschließen.")); } };
            BuildInterface();
        }
        void BuildInterface() {
            var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) }); root.ColumnDefinitions.Add(new ColumnDefinition());
            var side = new DockPanel { Background = UI.Brush("#111923"), LastChildFill = true };
            var brand = new StackPanel { Margin = new Thickness(24, 26, 20, 32) };
            var wordmark=UI.Text("RigPilot",23,"#81E0B6",true);wordmark.VerticalAlignment=VerticalAlignment.Center;wordmark.Margin=new Thickness(6,0,0,0);
            brand.Children.Add(UI.Row(Branding.Mark(38),wordmark));
            var subtitle = UI.Text(L.T("DEIN SETUP. DEIN MOMENT."), 9, "#8FA2B8", true); subtitle.Margin = new Thickness(0, 8, 0, 0); brand.Children.Add(subtitle); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
            var foot = new StackPanel { Margin = new Thickness(17, 12, 14, 22) };
            foot.Children.Add(UI.Label(L.T("Sprache / Language")));
            var language = UI.Choices(new[] { new Choice { Id = "de", Name = "Deutsch" }, new Choice { Id = "en", Name = "English" } }, L.Language);
            AutomationProperties.SetName(language, L.T("Sprache / Language"));
            language.Margin = new Thickness(0, 0, 0, 14); foot.Children.Add(language);
            language.SelectionChanged += delegate {
                string next = UI.Selected(language);
                if (next == L.Language) return;
                if (busy) { language.SelectedItem = ((IEnumerable<Choice>)language.ItemsSource).First(x => x.Id == L.Language); return; }
                try { L.Save(next); BuildInterface(); SetStatus(L.T("Sprache geändert.")); }
                catch (Exception ex) { language.SelectedItem = ((IEnumerable<Choice>)language.ItemsSource).First(x => x.Id == L.Language); UI.Error(ex); }
            };
            var help = UI.Button(L.T("Kurzanleitung  ↗"), delegate { UI.OpenGuide(this); }); help.FontSize = 11; help.Margin = new Thickness(0,0,0,8); foot.Children.Add(help);
            var folder = UI.Button(L.T("Daten & Sicherungen"), delegate { Directory.CreateDirectory(Store.Root); UI.Open(Store.Root); }); folder.FontSize = 11; folder.Margin = new Thickness(0,0,0,15); foot.Children.Add(folder);
            foot.Children.Add(UI.Text(L.T("●  Lokal auf deinem PC"), 11, "#81E0B6")); foot.Children.Add(UI.Text("Version 1.2 · Windows 11", 10, "#778A9F")); DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
            nav = new StackPanel { Margin = new Thickness(12, 0, 12, 0) }; side.Children.Add(new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); root.Children.Add(side);
            var right = new DockPanel { Margin = new Thickness(30, 23, 26, 16) }; Grid.SetColumn(right, 1); root.Children.Add(right);
            var bottom = new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = UI.Brush("#2B3646"), Padding = new Thickness(0, 12, 0, 0), Margin = new Thickness(0, 10, 0, 0) };
            status = UI.Text(L.T("Bereit. Wähle dein Setup."), 12, "#A4B4C7"); bottom.Child = status; DockPanel.SetDock(bottom, Dock.Bottom); right.Children.Add(bottom);
            body = new StackPanel { Margin = new Thickness(0,0,10,0) }; right.Children.Add(new ScrollViewer { Content = body }); Content = root;
            RefreshHardware(); ShowPage(page);
        }
        void SetStatus(string message) { status.Text = message; Store.Log(message); }
        void RefreshHardware() {
            var errors = new List<string>();
            try { startup = Startup.List(); errors.AddRange(Startup.Warnings); } catch (Exception ex) { errors.Add(L.T("Autostart: ") + ex.Message); }
            try { outputs = Audio.List(0); inputs = Audio.List(1); } catch (Exception ex) { errors.Add("Audio: " + ex.Message); }
            try { currentDisplay = Displays.Capture(L.T("Aktuell")); } catch (Exception ex) { currentDisplay = null; errors.Add(L.T("Monitore: ") + ex.Message); }
            hardwareError = String.Join("\n", errors);
        }
        public void ShowPage(string name) {
            page = name; nav.Children.Clear();
            string[] pages = { "overview", "startup", "programs", "groups", "audio", "displays" };
            string[] titles = { L.T("Übersicht"), L.T("Autostart"), L.T("Programme"), L.T("Startgruppen"), "Audio", L.T("Monitore") };
            string[] icons = { "◈", "◷", "▦", "▷", "♫", "▱" };
            for (int i = 0; i < pages.Length; i++) {
                string target = pages[i]; var label = UI.Row(UI.Text(icons[i], 19, target == page ? "#81E0B6" : "#8B9EB5"), UI.Text("   " + titles[i], 14, target == page ? "#C3F3DD" : "#A4B4C7", target == page));
                var button = UI.Button("", delegate { if (!busy) ShowPage(target); }); button.Content = label;
                button.Background = UI.Brush(target == page ? "#213B34" : "#111923"); button.BorderBrush = UI.Brush(target == page ? "#375C4E" : "#111923");
                button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(13, 12, 10, 12); button.Margin = new Thickness(0,0,0,5); AutomationProperties.SetName(button, titles[i]); nav.Children.Add(button);
            }
            body.Children.Clear();
            if (page == "overview") Overview(); else if (page == "startup") StartupPage(); else if (page == "programs") ProgramsPage(); else if (page == "groups") GroupsPage(); else if (page == "audio") AudioPage(); else DisplaysPage();
        }
        void Header(string title, string subtitle, params UIElement[] actions) {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9) }; var buttons = UI.Row(actions); buttons.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(buttons, Dock.Right); row.Children.Add(buttons); row.Children.Add(UI.Text(title, 29, "#F0F4FA", true)); body.Children.Add(row);
            var hint = UI.Text(subtitle, 13, "#9AAABD"); hint.Margin = new Thickness(0, 0, 0, 23); body.Children.Add(hint);
        }
        void Section(string text) { var t = UI.Text(text, 16, "#DCE6F2", true); t.Margin = new Thickness(0, 13, 0, 13); body.Children.Add(t); }
        void Overview() {
            Header(L.T("Alles bereit für deinen Moment."), L.T("Autostart, Programme, Sound und Bildschirme an einem Ort."), UI.Button("↻", delegate { RefreshHardware(); ShowPage(page); }));
            var banner = new Grid(); banner.ColumnDefinitions.Add(new ColumnDefinition()); banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
            var intro = new StackPanel(); intro.Children.Add(UI.Text(L.T("WENIGER KLICKEN. MEHR LOSLEGEN."), 10, "#81E0B6", true));
            var headline = UI.Text(L.T("Dein Setup. Ein Klick."), 31, "#F0F4FA", true); headline.Margin = new Thickness(0,12,0,8); intro.Children.Add(headline);
            intro.Children.Add(UI.Text(L.T("Wähle eine Startgruppe. RigPilot bringt deine Programme,\ndein Audio und deine Monitore zusammen."), 13, "#B0C3C6")); banner.Children.Add(intro);
            var emblem = Branding.Mark(132); emblem.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(emblem,1); banner.Children.Add(emblem);
            var hero = UI.Card(banner,24); hero.Background = new LinearGradientBrush(Color.FromRgb(25,52,47),Color.FromRgb(24,34,47),20); hero.BorderBrush = UI.Brush("#345A4D"); body.Children.Add(hero);
            var stats = new Grid { Margin = new Thickness(0,0,0,5) };
            string[] values = { startup.Count(x=>x.Known && x.Enabled).ToString(), data.Programs.Count.ToString(), data.Groups.Count.ToString() };
            string[] labels = { L.T("Autostarts aktiv"), L.T("Programme griffbereit"), L.T("Startgruppen") };
            for(int i=0;i<3;i++) { stats.ColumnDefinitions.Add(new ColumnDefinition()); var p=new StackPanel(); p.Children.Add(UI.Text(values[i],27,"#F0F4FA",true)); p.Children.Add(UI.Text(labels[i],12,"#9AAABD")); var c=UI.Card(p,16); c.Margin=new Thickness(0,0,i<2?12:0,0); Grid.SetColumn(c,i); stats.Children.Add(c); } body.Children.Add(stats);
            Section(L.T("Deine Startgruppen"));
            if(data.Groups.Count==0) body.Children.Add(UI.Card(UI.Text(L.T("Erstelle deine erste Gruppe unter Startgruppen."),14,"#9AAABD")));
            foreach(var group in data.Groups) GroupCard(group);
            if (!String.IsNullOrEmpty(hardwareError)) body.Children.Add(UI.Card(UI.Text(hardwareError,12,"#F4C68B")));
        }
        void GroupCard(LaunchGroup group) {
            var content = new DockPanel();
            var run = UI.Button(L.T("▶  Starten"), delegate { RunGroup(group); }, true); run.VerticalAlignment = VerticalAlignment.Center; run.Margin = new Thickness(15,0,0,0); run.IsEnabled = !busy; DockPanel.SetDock(run,Dock.Right); content.Children.Add(run);
            var p = new StackPanel(); p.Children.Add(UI.Text(group.Name,20,"#F0F4FA",true));
            var desc = UI.Text(group.Description,12,"#9AAABD"); desc.Margin = new Thickness(0,4,0,10); p.Children.Add(desc);
            var names = group.Items.Select(x=>data.Programs.FirstOrDefault(y=>y.Id==x.ProgramId)).Where(x=>x!=null).Select(x=>x.Name).ToList();
            p.Children.Add(UI.Text(names.Count==0?L.T("Noch keine Programme ausgewählt"):String.Join("  ·  ",names),12,"#C5D1E2"));
            var badges = UI.Text((String.IsNullOrEmpty(group.OutputId)&&String.IsNullOrEmpty(group.InputId)?L.T("Audio unverändert"):L.T("Audio-Profil")) + "   /   " + (String.IsNullOrEmpty(group.DisplayId)?L.T("Monitore unverändert"):L.T("Monitor-Setup")),11,"#81E0B6"); badges.Margin=new Thickness(0,8,0,12); p.Children.Add(badges);
            var edit=UI.Button(L.T("Bearbeiten"),delegate { EditGroup(group); }); edit.Padding=new Thickness(10,5,10,5); edit.FontSize=11;
            var check=UI.Button(L.T("Prüfen"),delegate { CheckGroup(group); UI.Message(L.T("Programmpfade und ausgewähltes Setup sind gültig. Es wurde nichts gestartet oder umgestellt.")+(String.IsNullOrEmpty(group.DisplayId)?"":L.T("\nAudio wird nach dem Monitorwechsel erneut geprüft.")),L.T("RigPilot · Gruppe geprüft")); }); check.Padding=new Thickness(10,5,10,5); check.FontSize=11;
            if(page=="groups") { var remove=UI.Button(L.T("Entfernen"),delegate { if(UI.Message(L.F("Startgruppe „{0}“ entfernen?",group.Name), "RigPilot",MessageBoxButton.YesNo)==MessageBoxResult.Yes) { data.Groups.Remove(group); Save(); ShowPage(page); } }); remove.FontSize=11; remove.Padding=new Thickness(10,5,10,5); p.Children.Add(UI.Row(edit,check,remove)); } else p.Children.Add(UI.Row(edit,check));
            content.Children.Add(p); body.Children.Add(UI.Card(content));
        }
        void Save() { Store.Save(data); }
        void GroupsPage() { Header(L.T("Startgruppen"), L.T("Programme in Reihenfolge starten – mit passendem Audio und Monitor-Setup."),UI.Button(L.T("+ Neue Gruppe"),delegate{EditGroup(null);},true)); foreach(var g in data.Groups) GroupCard(g); }
        void ProgramsPage() {
            Header(L.T("Programme"), L.T("Deine Bibliothek. Einzeln starten oder zu einer Gruppe hinzufügen."), UI.Button(L.T("Erkennen"),delegate{DiscoverDialog();}), UI.Button(L.T("+ Hinzufügen"),delegate{EditProgram(null);},true));
            var search=new TextBox { Margin=new Thickness(0,0,0,15), ToolTip=L.T("Programme durchsuchen") }; AutomationProperties.SetName(search,L.T("Programme durchsuchen")); body.Children.Add(search);
            var list=new StackPanel(); body.Children.Add(list);
            Action render=delegate { list.Children.Clear(); foreach(var app in data.Programs.Where(x=>x.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(x=>x.Name)) {
                var row=new DockPanel(); var actions=UI.Row(UI.Button("▶",delegate{SetStatus(Launcher.Start(app));},true),UI.Button(L.T("Bearbeiten"),delegate{EditProgram(app);})); DockPanel.SetDock(actions,Dock.Right); row.Children.Add(actions);
                var p=new StackPanel { Margin=new Thickness(0,0,15,0) }; p.Children.Add(UI.Text(app.Name,16,"#F0F4FA",true)); var path=UI.Text(app.FileName,11,"#8FA2B8"); path.TextWrapping=TextWrapping.NoWrap; path.TextTrimming=TextTrimming.CharacterEllipsis; path.ToolTip=app.FileName; path.Margin=new Thickness(0,5,0,0); p.Children.Add(path); row.Children.Add(p); list.Children.Add(UI.Card(row,16));
            } if(list.Children.Count==0) list.Children.Add(UI.Card(UI.Text(L.T("Keine Programme in dieser Auswahl. Nutze „Erkennen“ oder „Hinzufügen“."),13,"#9AAABD"))); };
            search.TextChanged+=delegate{render();}; render();
        }
        void StartupPage() {
            Header(L.T("Autostart"), L.T("Was beim Windows-Login startet, entscheidest du hier."),UI.Button("Windows ↗",delegate{UI.Open("ms-settings:startupapps");}),UI.Button("↻",delegate{RefreshHardware();ShowPage(page);}));
            var note = UI.Text(L.T("Klassische Registry- und Ordner-Autostarts. Aufgaben, Dienste und Store-Apps verwaltet Windows separat. Änderungen werden gesichert."),12,"#A4B4C7"); note.Margin=new Thickness(0,0,0,16); body.Children.Add(note);
            foreach(var entry in startup) {
                var row=new DockPanel(); var toggle=new CheckBox { IsChecked=entry.Enabled, IsEnabled=entry.Known&&!busy, Style=(Style)FindResource("Switch"), ToolTip=entry.Known ? L.F("Autostart für {0} umschalten",entry.Name) : L.T("Unbekannter Windows-Status – bitte Windows-Einstellungen verwenden"), Margin=new Thickness(14,0,2,0) }; AutomationProperties.SetName(toggle,L.F("Autostart {0}",entry.Name)); DockPanel.SetDock(toggle,Dock.Right); row.Children.Add(toggle);
                toggle.Click+=async delegate { if(busy)return; bool desired=toggle.IsChecked==true; toggle.IsEnabled=false; busy=true; ((UIElement)Content).IsEnabled=false; try { await ToggleStartup(entry,desired); SetStatus(entry.Name+(desired?L.T(": Autostart aktiviert."):L.T(": Autostart deaktiviert."))); } catch(Exception ex) { UI.Error(ex); } finally { busy=false; ((UIElement)Content).IsEnabled=true; RefreshHardware(); ShowPage(page); } };
                var actions=UI.Row(UI.Button("▶",delegate{SetStatus(Launcher.Start(Launcher.FromCommand(entry.Name,entry.Command)));}),UI.Button(L.T("+ Bibliothek"),delegate{var p=Launcher.FromCommand(entry.Name,entry.Command); if(!data.Programs.Any(x=>String.Equals(x.FileName,p.FileName,StringComparison.OrdinalIgnoreCase)&&x.Arguments==p.Arguments)){data.Programs.Add(p);Save();SetStatus(p.Name+L.T(" zur Bibliothek hinzugefügt."));}})); actions.VerticalAlignment=VerticalAlignment.Center; DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
                var p2=new StackPanel { Margin=new Thickness(0,0,12,0) };p2.Children.Add(UI.Text(entry.Name,15,"#F0F4FA",true)); var detail=UI.Text(entry.Caption+(entry.Machine?"  ·  Admin":"")+"  ·  "+(entry.Known?(entry.Enabled?L.T("An"):L.T("Aus")):L.T("Unbekannt")),11,"#9AAABD");detail.Margin=new Thickness(0,5,0,0);p2.Children.Add(detail);p2.ToolTip=entry.Command;row.Children.Add(p2);body.Children.Add(UI.Card(row,15));
            }
            if(startup.Count==0) body.Children.Add(UI.Card(UI.Text(L.T("Keine klassischen Autostart-Einträge gefunden."),14,"#9AAABD")));
            body.Children.Add(UI.Row(UI.Button(L.T("Sicherung wiederherstellen …"),delegate{RestoreStartup();})));
            if(Startup.Warnings.Count>0) body.Children.Add(UI.Text(String.Join("\n",Startup.Warnings),12,"#F4C68B"));
        }
        async Task ToggleStartup(StartupEntry entry, bool desired) {
            if(!entry.Machine) { Startup.Set(entry,desired); return; }
            string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Store.Serializer().Serialize(entry)));
            await RunElevated("--startup "+(desired?"on":"off")+" "+encoded);
        }
        async Task RunElevated(string arguments) {
            var info=new ProcessStartInfo(App.EntryPath,arguments) {UseShellExecute=true,Verb="runas"};
            using(var process=Process.Start(info)) { if(process==null) throw new InvalidOperationException(L.T("Windows konnte den Admin-Vorgang nicht starten.")); await Task.Run(delegate {process.WaitForExit();}); if(process.ExitCode!=0) throw new InvalidOperationException(L.T("Der Admin-Vorgang wurde nicht abgeschlossen. Details stehen im Protokoll.")); }
        }
        async void RestoreStartup() {
            if(busy)return;var picker=new OpenFileDialog{Title=L.T("Autostart-Sicherung auswählen"),Filter=L.T("RigPilot-Sicherung|*.json"),InitialDirectory=Path.Combine(Store.Root,"Autostart-Sicherungen")}; if(picker.ShowDialog(this)!=true)return;
            try { var backup=Store.Read<StartupBackup>(picker.FileName); if(backup==null||backup.Entry==null)throw new InvalidDataException(L.T("Keine gültige Autostart-Sicherung."));
                if(UI.Message(L.F("Den vorherigen Autostart-Status von „{0}“ wiederherstellen?",backup.Entry.Name),"RigPilot",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                busy=true; ((UIElement)Content).IsEnabled=false; if(backup.Entry.Machine) { string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Store.Serializer().Serialize(backup)));await RunElevated("--restore-startup "+encoded); } else Startup.Restore(backup);
                SetStatus(L.T("Autostart-Status wiederhergestellt."));
            }catch(Exception ex){UI.Error(ex);}finally{busy=false;((UIElement)Content).IsEnabled=true;RefreshHardware();ShowPage(page);}
        }
        void AudioPage() {
            Header("Audio", L.T("Wiedergabe und Mikrofon direkt wechseln oder in Startgruppen speichern."),UI.Button("Windows ↗",delegate{UI.Open("ms-settings:sound");}),UI.Button("↻",delegate{RefreshHardware();ShowPage(page);}));
            Section(L.T("♫  Wiedergabe")); AudioRows(outputs,0); Section(L.T("◉  Mikrofon")); AudioRows(inputs,1);
            var hint=UI.Text(L.T("Die Auswahl gilt für Standard, Multimedia und Kommunikation. Apps mit fest eingestelltem Audiogerät behalten ihre eigene Auswahl."),12,"#9AAABD");hint.Margin=new Thickness(0,12,0,0);body.Children.Add(hint);
        }
        void AudioRows(List<AudioDevice> devices,int flow) {
            if(devices.Count==0)body.Children.Add(UI.Card(UI.Text(L.T("Kein aktives Gerät gefunden. Verbinden und aktualisieren."),13,"#9AAABD")));
            foreach(var device in devices) {
                var row=new DockPanel();var button=UI.Button(device.IsDefault?L.T("✓  Standard"):L.T("Auswählen"),delegate { Audio.SetDefault(device.Id,flow);SetStatus(device.Name+L.T(" als Standard gesetzt."));RefreshHardware();ShowPage(page);},!device.IsDefault);DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);
                var p=new StackPanel {Margin=new Thickness(0,0,12,0)};p.Children.Add(UI.Text(device.Name,14,"#F0F4FA",true));p.Children.Add(UI.Text(device.IsDefault?L.T("Aktuelles Standardgerät"):L.T("Verbunden"),11,device.IsDefault?"#81E0B6":"#9AAABD"));row.Children.Add(p);body.Children.Add(UI.Card(row,16));
            }
        }
        void DisplaysPage() {
            Header(L.T("Monitore"), L.T("Speichere dein aktuelles Bildschirmlayout als wiederverwendbares Setup."),UI.Button("Windows ↗",delegate{UI.Open("ms-settings:display");}),UI.Button("↻",delegate{RefreshHardware();ShowPage(page);}));
            Section(L.T("Gerade aktiv"));
            if(currentDisplay!=null)foreach(var monitor in currentDisplay.Monitors) { var p=new StackPanel();p.Children.Add(UI.Text("▱  "+monitor.Name,17,"#F0F4FA",true));p.Children.Add(UI.Text(monitor.Summary,12,"#9AAABD"));body.Children.Add(UI.Card(p,16)); }
            else body.Children.Add(UI.Card(UI.Text(L.T("Monitorinformationen sind derzeit nicht verfügbar."),13,"#F4C68B")));
            body.Children.Add(UI.Row(UI.Button(L.T("+ Aktuelles Setup speichern"),delegate{SaveDisplay();},true)));
            var info=UI.Text(L.T("Richte z. B. Schreibtisch oder Simrig in Windows ein und speichere den Zustand hier. Enthält aktive Anzeigen, Anordnung, Hauptanzeige, Auflösung und Hz. HDR, Skalierung und VR-Brillen werden separat verwaltet."),12,"#9AAABD");info.Margin=new Thickness(0,13,0,5);body.Children.Add(info);
            Section(L.T("Gespeicherte Setups"));
            foreach(var saved in data.Displays) {
                var row=new DockPanel();var actions=UI.Row(UI.Button(L.T("Anwenden"),async delegate{await ApplyDisplay(saved);RefreshHardware();ShowPage(page);},true),UI.Button(L.T("Entfernen"),delegate{if(data.Groups.Any(x=>x.DisplayId==saved.Id))throw new InvalidOperationException(L.T("Dieses Setup wird noch von einer Startgruppe verwendet. Entferne dort zuerst die Zuordnung."));if(UI.Message(L.F("Monitor-Setup „{0}“ entfernen?",saved.Name),"RigPilot",MessageBoxButton.YesNo)==MessageBoxResult.Yes){data.Displays.Remove(saved);Save();ShowPage(page);}}));DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
                var p=new StackPanel {Margin=new Thickness(0,0,12,0)};p.Children.Add(UI.Text(saved.Name,17,"#F0F4FA",true));p.Children.Add(UI.Text(L.F("{0} Anzeige(n)  ·  {1}",saved.Monitors.Count,saved.Captured),11,"#9AAABD"));row.Children.Add(p);body.Children.Add(UI.Card(row,16));
            }
            if(data.Displays.Count==0)body.Children.Add(UI.Text(L.T("Noch kein Setup gespeichert."),13,"#9AAABD"));
        }
        void SaveDisplay() { string name=UI.AskName(this,L.T("Monitor-Setup speichern"),L.T("Schreibtisch"));if(name==null)return;data.Displays.Add(Displays.Capture(name));Save();SetStatus(L.F("Monitor-Setup „{0}“ gespeichert.",name));RefreshHardware();ShowPage(page); }
    }
}
