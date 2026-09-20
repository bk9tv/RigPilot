using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RigPilot {
    public static class SelfTests {
        public static int Run(string outputDirectory) {
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
            test("A slow monitor switch does not shorten the 25-second confirmation",delegate{
                string dir=Path.Combine(root,"countdown-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),now=start,restored=DateTime.MinValue;
                int code=Displays.RunGuard(dir,delegate{restored=now;},delegate{return now;},delegate{
                    now=now.AddSeconds(1);
                    if(now==start.AddSeconds(30))Store.Write(Path.Combine(dir,"confirmation-deadline.json"),now.AddSeconds(Displays.ConfirmationSeconds).Ticks);
                    if(now>start.AddSeconds(70))throw new Exception("Watchdog did not stop");
                });
                assert(code==0);assert(restored==start.AddSeconds(55));assert(File.ReadAllText(Path.Combine(dir,"result"))=="restored");
            });
            test("A crashed UI before the confirmation dialog still rolls back",delegate{
                string dir=Path.Combine(root,"no-dialog-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),now=start;bool restored=false;
                int code=Displays.RunGuard(dir,delegate{restored=true;},delegate{return now;},delegate{now=now.AddSeconds(1);if(now>start.AddSeconds(60))throw new Exception("Watchdog did not stop");});
                assert(code==0);assert(restored);assert(now>=start.AddSeconds(25)&&now<=start.AddSeconds(60));
            });
            test("Confirmation just before timeout prevents rollback",delegate{
                string dir=Path.Combine(root,"keep-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),now=start;bool restored=false;
                Store.Write(Path.Combine(dir,"confirmation-deadline.json"),now.AddSeconds(Displays.ConfirmationSeconds).Ticks);
                int code=Displays.RunGuard(dir,delegate{restored=true;},delegate{return now;},delegate{now=now.AddSeconds(1);if(now==start.AddSeconds(24))Displays.ClaimDecision(dir,"keep");if(now>start.AddSeconds(60))throw new Exception("Watchdog did not stop");});
                assert(code==0);assert(!restored);assert(File.ReadAllText(Path.Combine(dir,"result"))=="confirmed");
            });
            File.WriteAllLines(Path.Combine(root,"self-tests.txt"),results);return failed==0?0:1;
        }
    }
}
