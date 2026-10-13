using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RigPilot {
    public static class SelfTests {
        public static int Run(string outputDirectory) {
            string originalLanguage=L.Language;L.Use("de");
            string root=Path.GetFullPath(outputDirectory);Directory.CreateDirectory(root);var results=new List<string>();int failed=0;
            Action<string,Action> test=delegate(string name,Action act){try{act();results.Add("PASS "+name);}catch(Exception ex){failed++;results.Add("FAIL "+name+": "+ex.Message);}};
            Action<bool> assert=delegate(bool condition){if(!condition)throw new Exception("Assertion failed");};
            test("CCD ABI sizes: source20 target48 path72 mode64",delegate{assert(Marshal.SizeOf(typeof(DisplaySource))==20);assert(Marshal.SizeOf(typeof(DisplayTarget))==48);assert(Marshal.SizeOf(typeof(DisplayPath))==72);assert(Marshal.SizeOf(typeof(DisplayMode))==64);});
            test("Quoted EXE and arguments stay separate",delegate{var p=Launcher.FromCommand("Test","\"C:\\Program Files\\SimHub\\SimHub.exe\" --tray \"a b\"");assert(p.FileName==@"C:\Program Files\SimHub\SimHub.exe");assert(p.Arguments=="--tray \"a b\"");});
            test("Unquoted EXE paths containing spaces",delegate{var p=Launcher.FromCommand("Test",@"C:\Program Files\SimHub\SimHub.exe -silent");assert(p.FileName==@"C:\Program Files\SimHub\SimHub.exe");assert(p.Arguments=="-silent");});
            test("Unbalanced quotes fail safely",delegate{bool threw=false;try{Launcher.FromCommand("Test","\"broken");}catch(InvalidDataException){threw=true;}assert(threw);});
            test("Unknown startup states are read only",delegate{assert(Startup.Decode(null)==true);assert(Startup.Decode(new byte[]{2,0,0,0})==true);assert(Startup.Decode(new byte[]{3,0,0,0})==false);assert(Startup.Decode(new byte[]{6,0,0,0})==true);assert(Startup.Decode(new byte[]{7,0,0,0})==false);assert(Startup.Decode(new byte[]{8,0,0,0})==null);assert(Startup.Decode(new byte[]{2})==null);});
            test("Startup toggle payload includes disable timestamp",delegate{byte[] off=Startup.Encode(false);assert(off.Length==12);assert(Startup.Decode(off)==false);assert(DateTime.FromFileTimeUtc(BitConverter.ToInt64(off,4))<=DateTime.UtcNow);byte[] on=Startup.Encode(true);assert(Startup.Decode(on)==true);assert(BitConverter.ToInt64(on,4)==0);});
            test("Profile roundtrip preserves ordered groups and device ids",delegate{var data=new Settings();var app=new ProgramEntry{Name="SimHub",FileName=@"C:\SimHub\SimHub.exe",Arguments="--silent"};data.Programs.Add(app);data.Groups.Add(new LaunchGroup{Name="Rig",OutputId="audio-id",DisplayId="display-id",Items=new List<GroupItem>{new GroupItem{ProgramId=app.Id,DelaySeconds=8}}});var copy=Store.Clone(data);assert(copy.Groups[0].Items[0].ProgramId==copy.Programs[0].Id);assert(copy.Groups[0].Items[0].DelaySeconds==8);assert(copy.Groups[0].OutputId=="audio-id");assert(copy.Groups[0].DisplayId=="display-id");});
            test("Monitor mode binary data survives serialization",delegate{var mode=new DisplayMode {Type=2,Id=41,Adapter=new Luid{High=-1,Low=334},Data=Enumerable.Range(0,48).Select(x=>(byte)(x*3)).ToArray()};var copy=Store.Clone(mode);assert(copy.Data.SequenceEqual(mode.Data));assert(copy.Adapter.High==-1);assert(copy.Id==41);});
            test("Atomic save retains prior working backup",delegate{string p=Path.Combine(root,"atomic-test.json");Store.Write(p,new LaunchGroup{Name="Before"});Store.Write(p,new LaunchGroup{Name="After"});assert(Store.Read<LaunchGroup>(p).Name=="After");assert(Store.Read<LaunchGroup>(p+".bak").Name=="Before");});
            test("Missing executable fails before launch",delegate{bool threw=false;try{Launcher.Validate(new ProgramEntry{Name="Missing",FileName=Path.Combine(root,"missing.exe")});}catch(FileNotFoundException){threw=true;}assert(threw);});
            test("Unsupported protocol fails before shell execution",delegate{bool threw=false;try{Launcher.Validate(new ProgramEntry{Name="Unsupported",FileName="javascript:alert(1)"});}catch(FileNotFoundException){threw=true;}assert(threw);});
            test("Steam URL accepted as launcher target",delegate{Launcher.Validate(new ProgramEntry{Name="Steam game",FileName="steam://rungameid/250820"});});
            test("Running executable is skipped without starting another instance",delegate{assert(Launcher.Start(new ProgramEntry{Name="Test runner",FileName=App.EntryPath,SkipIfRunning=true}).Contains("übersprungen"));});
            test("Confirmation and monitor timeout have exactly one winner",delegate{string dir=Path.Combine(root,"race-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);int winners=0;System.Threading.Tasks.Parallel.For(0,32,delegate(int i){if(Displays.ClaimDecision(dir,i%2==0?"keep":"restore"))System.Threading.Interlocked.Increment(ref winners);});assert(winners==1);assert(new[]{"keep","restore"}.Contains(File.ReadAllText(Path.Combine(dir,"decision"))));});
            test("A slow monitor switch does not shorten the 60-second confirmation",delegate{
                assert(Displays.ConfirmationSeconds==60);
                string dir=Path.Combine(root,"countdown-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),now=start,restored=DateTime.MinValue;
                int code=Displays.RunGuard(dir,delegate{restored=now;},delegate{return now;},delegate{
                    now=now.AddSeconds(1);
                    if(now==start.AddSeconds(30))Store.Write(Path.Combine(dir,"confirmation-deadline.json"),now.AddSeconds(Displays.ConfirmationSeconds).Ticks);
                    if(now>start.AddSeconds(105))throw new Exception("Watchdog did not stop");
                });
                assert(code==0);assert(restored==start.AddSeconds(90));assert(File.ReadAllText(Path.Combine(dir,"result"))=="restored");
            });
            test("A crashed UI before the confirmation dialog still rolls back",delegate{
                string dir=Path.Combine(root,"no-dialog-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),now=start;bool restored=false;
                int code=Displays.RunGuard(dir,delegate{restored=true;},delegate{return now;},delegate{now=now.AddSeconds(1);if(now>start.AddSeconds(60))throw new Exception("Watchdog did not stop");});
                assert(code==0);assert(restored);assert(now==start.AddSeconds(45));
            });
            test("Confirmation just before timeout prevents rollback",delegate{
                string dir=Path.Combine(root,"keep-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),now=start;bool restored=false;
                Store.Write(Path.Combine(dir,"confirmation-deadline.json"),now.AddSeconds(Displays.ConfirmationSeconds).Ticks);
                int code=Displays.RunGuard(dir,delegate{restored=true;},delegate{return now;},delegate{now=now.AddSeconds(59);Displays.ClaimDecision(dir,"keep");if(now>start.AddSeconds(60))throw new Exception("Watchdog did not stop");});
                assert(code==0);assert(!restored);assert(File.ReadAllText(Path.Combine(dir,"result"))=="confirmed");
            });
            test("Both languages include valid format placeholders and embedded guides",delegate{
                assert(L.English.Count>200);
                foreach(var pair in L.English) {
                    assert(!String.IsNullOrWhiteSpace(pair.Value));
                    var pattern=@"\{[0-9]+\}";
                    var de=System.Text.RegularExpressions.Regex.Matches(pair.Key,pattern).Cast<System.Text.RegularExpressions.Match>().Select(x=>x.Value).OrderBy(x=>x).ToArray();
                    var en=System.Text.RegularExpressions.Regex.Matches(pair.Value,pattern).Cast<System.Text.RegularExpressions.Match>().Select(x=>x.Value).OrderBy(x=>x).ToArray();assert(de.SequenceEqual(en));
                    String.Format(pair.Value,new object[]{25,"Test"});
                }
                foreach(string resource in new[]{"RigPilot.ANLEITUNG.txt","RigPilot.GUIDE.en.txt"})
                    using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)) {assert(stream!=null&&stream.Length>1000);}
            });
            test("Language switch persists without touching legacy profiles and backups",delegate{
                string previousRoot=Store.Root,previousLanguage=L.Language;
                try {
                    Store.Root=Path.Combine(root,"language-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Store.Root);
                    File.WriteAllText(Store.Config,"{\"Version\":1,\"Programs\":[{\"Id\":\"program-id\",\"Name\":\"Programme\",\"FileName\":\"C:\\\\Test.exe\"}],\"Groups\":[{\"Name\":\"Übersicht\",\"Description\":\"Mein Rig\",\"OutputId\":\"audio-id\",\"InputId\":\"mic-id\",\"DisplayId\":\"display-id\",\"Items\":[{\"ProgramId\":\"program-id\",\"DelaySeconds\":25}]}],\"Displays\":[]}");
                    File.WriteAllText(Store.Config+".bak","existing-backup");var before=File.ReadAllBytes(Store.Config);var backup=File.ReadAllBytes(Store.Config+".bak");
                    L.Save("en");L.Use("de");L.Load();assert(L.Language=="en");assert(L.T("Programme")=="Programs");
                    var profile=Store.Load();assert(profile.Version==1);assert(profile.Programs[0].Name=="Programme");assert(profile.Groups[0].Name=="Übersicht");assert(profile.Groups[0].Description=="Mein Rig");assert(profile.Groups[0].OutputId=="audio-id");assert(profile.Groups[0].InputId=="mic-id");assert(profile.Groups[0].DisplayId=="display-id");assert(profile.Groups[0].Items[0].DelaySeconds==25);
                    L.Save("de");L.Use("en");L.Load();assert(L.Language=="de");assert(before.SequenceEqual(File.ReadAllBytes(Store.Config)));assert(backup.SequenceEqual(File.ReadAllBytes(Store.Config+".bak")));
                } finally {Store.Root=previousRoot;L.Use(previousLanguage);}
            });
            test("Missing unknown and damaged language preferences fall back safely",delegate{
                string previousRoot=Store.Root,previousLanguage=L.Language;
                try {
                    Store.Root=Path.Combine(root,"language-fallback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Store.Root);
                    L.Use("en");L.Load();assert(L.Language=="de");
                    string path=Path.Combine(Store.Root,"language.json");
                    foreach(string text in new[]{"{\"Language\":\"fr\"}","null","broken JSON"}) {File.WriteAllText(path,text);L.Use("en");L.Load();assert(L.Language=="de");assert(File.ReadAllText(path)==text);}
                } finally {Store.Root=previousRoot;L.Use(previousLanguage);}
            });
            test("Failed language save preserves the active language",delegate{
                string previousRoot=Store.Root,previousLanguage=L.Language;
                try {
                    Store.Root=Path.Combine(root,"language-blocked-"+Guid.NewGuid().ToString("N"));File.WriteAllText(Store.Root,"file blocks directory");L.Use("de");bool threw=false;
                    try {L.Save("en");}catch(IOException){threw=true;}assert(threw);assert(L.Language=="de");
                } finally {Store.Root=previousRoot;L.Use(previousLanguage);}
            });
            test("Localized validation and countdown preserve values and user names",delegate{
                string previousLanguage=L.Language;
                try {
                    L.Use("en");assert(L.F("Ohne Bestätigung geht es in {0} Sekunden zurück.",Displays.ConfirmationSeconds)=="Reverting in 60 seconds unless you confirm.");
                    assert(L.F("Startgruppe „{0}“ entfernen?","Übersicht")=="Remove launch group “Übersicht”?");
                    bool threw=false;try{Launcher.Validate(new ProgramEntry{Name="Mein Programm",FileName=Path.Combine(root,"missing-localized.exe")});}catch(FileNotFoundException ex){threw=true;assert(ex.Message.Contains("Mein Programm: program or shortcut not found."));}assert(threw);
                    assert(new LaunchGroup().Name=="New group");assert(new MonitorBinding{Primary=true}.Summary.Contains("Primary display"));assert(L.GuideFile=="GUIDE.en.txt");
                    L.Use("de");assert(new LaunchGroup().Name=="Neue Gruppe");assert(L.F("Ohne Bestätigung geht es in {0} Sekunden zurück.",Displays.ConfirmationSeconds).Contains("60 Sekunden"));assert(L.GuideFile=="ANLEITUNG.txt");
                } finally {L.Use(previousLanguage);}
            });
            WorkflowTests.Add(test,assert);
            GroupSwitchTests.Add(test,assert);
            L.Use(originalLanguage);File.WriteAllLines(Path.Combine(root,"self-tests.txt"),results);return failed==0?0:1;
        }
    }
}
