using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

[assembly: AssemblyTitle("RigPilot")]
[assembly: AssemblyDescription("Autostart, Startgruppen, Audio und Monitor-Setups für Windows 11")]
[assembly: AssemblyVersion("1.1.0.0")]
namespace RigPilot {
    public static class App {
        public static string EntryPath { get { return Assembly.GetExecutingAssembly().Location; } }
        [STAThread] public static int Main(string[] args) {
            try {
                if(args.Length==2&&args[0]=="--guard") {
                    string dir=Path.GetFullPath(args[1]);string allowed=Path.GetFullPath(Path.Combine(Store.Root,"Monitor-Rueckkehr"))+Path.DirectorySeparatorChar;
                    if(!dir.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Ungültiger Monitor-Rückkehr-Pfad.");
                    return Displays.Guard(dir);
                }
                if(args.Length==3&&args[0]=="--startup") {
                    if(args[1]!="on"&&args[1]!="off")return 2;
                    Startup.Set(Store.Serializer().Deserialize<StartupEntry>(Encoding.UTF8.GetString(Convert.FromBase64String(args[2]))),args[1]=="on");return 0;
                }
                if(args.Length==2&&args[0]=="--restore-startup") {
                    Startup.Restore(Store.Serializer().Deserialize<StartupBackup>(Encoding.UTF8.GetString(Convert.FromBase64String(args[1]))));return 0;
                }
                if(args.Length==2&&args[0]=="--diagnose")return Diagnostics(args[1]);
                if(args.Length==2&&args[0]=="--self-test")return SelfTests.Run(args[1]);
                if(args.Length>0)throw new ArgumentException("Unbekannte Startoption.");
                bool isNew;
                using(var mutex=new Mutex(true,"Local\\RigPilot-"+Environment.UserName,out isNew)) {
                    if(!isNew){MessageBox.Show("RigPilot ist bereits geöffnet.","RigPilot");return 0;}
                    try {
                        var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
                        using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("RigPilot.Theme.xaml"))app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(resource));
                        app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e){UI.Error(e.Exception);e.Handled=true;};
                        bool first=!File.Exists(Store.Config);var data=Store.Load();if(first){Launcher.Seed(data);Store.Save(data);}
                        var window=new MainWindow(data);app.Run(window);return 0;
                    }finally{mutex.ReleaseMutex();}
                }
            }catch(Exception ex){Store.Log(ex.ToString());if(args.Length==0||args[0]=="--startup"||args[0]=="--restore-startup")MessageBox.Show(ex.Message,"RigPilot",MessageBoxButton.OK,MessageBoxImage.Warning);return 1;}
        }
        static int Diagnostics(string path) {
            var results=new Dictionary<string,object>();var errors=new List<string>();
            results["Time"]=DateTime.Now.ToString("s");results["OS"]=Environment.OSVersion.ToString();
            results["InteropSizes"]=new Dictionary<string,int>{{"Path",Marshal.SizeOf(typeof(DisplayPath))},{"Mode",Marshal.SizeOf(typeof(DisplayMode))},{"Target",Marshal.SizeOf(typeof(DisplayTarget))}};
            try{results["Startup"]=Startup.List();results["StartupWarnings"]=Startup.Warnings.ToArray();}catch(Exception ex){errors.Add("Startup: "+ex.ToString());}
            try{results["AudioOutput"]=Audio.List(0);results["AudioInput"]=Audio.List(1);Audio.ProbePolicy();results["AudioSwitchInterface"]="OK (nur abgefragt, nicht umgeschaltet)";}catch(Exception ex){errors.Add("Audio: "+ex.ToString());}
            try{var display=Displays.Capture("Diagnose");results["Displays"]=display;Displays.Validate(display);results["DisplayValidation"]="OK (nur geprüft, nicht umgeschaltet)";}catch(Exception ex){errors.Add("Displays: "+ex.ToString());}
            try{results["DiscoveredPrograms"]=Launcher.Discover();}catch(Exception ex){errors.Add("Programs: "+ex.ToString());}
            results["Errors"]=errors;Store.Write(Path.GetFullPath(path),results);return errors.Count==0?0:1;
        }
    }
}
