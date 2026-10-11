using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RigPilot {
    public static class WorkflowTests {
        static readonly Func<int, CancellationToken, Task> NoWait = delegate(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(0); };
        static Settings Programs(out LaunchGroup group) {
            var settings = new Settings(); group = new LaunchGroup();
            foreach (string name in new[] { "First", "Second" }) {
                var app = new ProgramEntry { Name = name }; settings.Programs.Add(app); group.Items.Add(new GroupItem { ProgramId = app.Id });
            }
            return settings;
        }
        public static void Add(Action<string, Action> test, Action<bool> assert) {
            test("Legacy groups keep Windows audio; Wave Link settings roundtrip", delegate {
                var old = Store.Serializer().Deserialize<LaunchGroup>("{\"OutputId\":\"windows\",\"InputId\":\"mic\",\"Items\":[]}");
                assert(old.OutputId == "windows" && old.InputId == "mic" && old.WaveLink != null && !old.WaveLink.Enabled);
                old.WaveLink = Profile(); var copy = Store.Clone(old);
                assert(copy.WaveLink.Enabled && copy.WaveLink.OutputId == "headphones" && copy.WaveLink.MicChannelId == "vr-mic");
            });
            test("A missing group program does not block later programs", delegate {
                LaunchGroup group; var settings = Programs(out group); var calls = new List<string>();
                group.Items.Insert(0, new GroupItem { ProgramId = "removed" });
                var result = GroupRunner.Run(settings, group, CancellationToken.None, delegate { }, delegate { },
                    delegate { return Task.FromResult(0); }, delegate(ProgramEntry p) { calls.Add(p.Name); return p.Name; }, NoWait).GetAwaiter().GetResult();
                assert(result.StartFailures == 1 && calls.SequenceEqual(new[] { "First", "Second" }));
            });
            test("A failed executable does not block later programs", delegate {
                LaunchGroup group; var settings = Programs(out group); var calls = new List<string>();
                var result = GroupRunner.Run(settings, group, CancellationToken.None, delegate { }, delegate { },
                    delegate { return Task.FromResult(0); }, delegate(ProgramEntry p) { if (p.Name == "First") throw new FileNotFoundException("missing"); calls.Add(p.Name); return p.Name; }, NoWait).GetAwaiter().GetResult();
                assert(result.StartFailures == 1 && calls.SequenceEqual(new[] { "Second" }));
            });
            test("Unavailable Windows audio retries after programs become available", delegate {
                LaunchGroup group; var settings = Programs(out group); var calls = new List<string>(); int attempts = 0;
                var result = GroupRunner.Run(settings, group, CancellationToken.None, delegate { }, delegate { },
                    delegate { calls.Add("audio"); if (++attempts == 1) throw new IOException("device starting"); return Task.FromResult(0); },
                    delegate(ProgramEntry p) { calls.Add(p.Name); return p.Name; }, NoWait).GetAwaiter().GetResult();
                assert(!result.AudioFailed && result.IssueCount == 0 && calls.SequenceEqual(new[] { "audio", "First", "Second", "audio" }));
            });
            test("Persistent audio failure reports partial completion and still launches programs", delegate {
                LaunchGroup group; var settings = Programs(out group); int starts = 0;
                var result = GroupRunner.Run(settings, group, CancellationToken.None, delegate { }, delegate { },
                    delegate { throw new IOException("audio unavailable"); }, delegate(ProgramEntry p) { starts++; return p.Name; }, NoWait).GetAwaiter().GetResult();
                assert(starts == 2 && result.AudioFailed && result.IssueCount == 1);
            });
            test("Wave Link audio follows its startup programs and preserves delays", delegate {
                LaunchGroup group; var settings = Programs(out group); group.WaveLink.Enabled = true; group.Items[1].DelaySeconds = 2;
                var calls = new List<string>(); int seconds = 0;
                var result = GroupRunner.Run(settings, group, CancellationToken.None, delegate { }, delegate { },
                    delegate { calls.Add("wave"); return Task.FromResult(0); }, delegate(ProgramEntry p) { calls.Add(p.Name); return p.Name; },
                    delegate(int ms, CancellationToken token) { if (ms == 1000) seconds++; return Task.FromResult(0); }).GetAwaiter().GetResult();
                assert(result.IssueCount == 0 && seconds == 2 && calls.SequenceEqual(new[] { "First", "Second", "wave" }));
            });
            test("Cancellation during a launch delay prevents pending starts", delegate {
                LaunchGroup group; var settings = Programs(out group); group.Items[0].DelaySeconds = 1; int starts = 0; bool cancelled = false;
                using (var cancel = new CancellationTokenSource()) {
                    try {
                        GroupRunner.Run(settings, group, cancel.Token, delegate { }, delegate { }, delegate { return Task.FromResult(0); },
                            delegate(ProgramEntry p) { starts++; return p.Name; }, delegate(int ms, CancellationToken token) { cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(0); }).GetAwaiter().GetResult();
                    } catch (OperationCanceledException) { cancelled = true; }
                }
                assert(cancelled && starts == 0);
            });
            test("Wave Link RPC ignores notifications and preserves errors", delegate {
                object value; assert(!WaveLinkClient.TryResponse("{\"jsonrpc\":\"2.0\",\"method\":\"channelChanged\",\"params\":{}}", 1, out value));
                assert(!WaveLinkClient.TryResponse("{\"id\":2,\"result\":{}}", 1, out value));
                assert(WaveLinkClient.TryResponse("{\"id\":1,\"result\":{\"ok\":true}}", 1, out value));
                bool failed = false; try { WaveLinkClient.TryResponse("{\"id\":1,\"error\":{\"code\":-32602,\"message\":\"Invalid params\"}}", 1, out value); }
                catch (WaveLinkRpcException ex) { failed = ex.Code == -32602; } assert(failed);
            });
            test("Wave Link selects main output and switches only the chosen mix", delegate {
                var rpc = new TestRpc(); WaveLinkControl.Apply(rpc, Profile(), CancellationToken.None).GetAwaiter().GetResult();
                assert(rpc.State.Outputs.mainOutput.outputId == "headphones");
                assert(!rpc.Muted("vr-mic", "discord") && rpc.Muted("rode", "discord"));
                assert(rpc.Muted("vr-mic", "stream") && !rpc.Muted("rode", "stream"));
                assert(rpc.ChannelPayloads.All(x => !x.ContainsKey("isMuted") && !x.ContainsKey("level")));
                var gaming = Profile(); gaming.OutputId = "speakers"; gaming.MicChannelId = "rode"; gaming.MutedChannelId = "vr-mic";
                WaveLinkControl.Apply(rpc, gaming, CancellationToken.None).GetAwaiter().GetResult();
                assert(rpc.State.Outputs.mainOutput.outputId == "speakers" && rpc.Muted("vr-mic", "discord") && !rpc.Muted("rode", "discord"));
            });
            test("Wave Link validates all devices before making any changes", delegate {
                var rpc = new TestRpc(); var profile = Profile(); profile.MicChannelId = "missing"; bool failed = false;
                try { WaveLinkControl.Apply(rpc, profile, CancellationToken.None).GetAwaiter().GetResult(); } catch (WaveLinkUnavailableException) { failed = true; }
                assert(failed && rpc.Writes == 0 && rpc.State.Outputs.mainOutput.outputId == "speakers");
            });
            test("Wave Link rejects conflicting and globally muted microphones", delegate {
                var rpc = new TestRpc(); var profile = Profile(); profile.MutedChannelId = profile.MicChannelId; bool conflict = false, muted = false;
                try { WaveLinkControl.Apply(rpc, profile, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { conflict = true; }
                profile = Profile(); rpc.State.Channels.channels[0].isMuted = true;
                try { WaveLinkControl.Apply(rpc, profile, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { muted = true; }
                assert(conflict && muted && rpc.Writes == 0);
            });
            test("Wave Link restores output and microphone states after a partial failure", delegate {
                var rpc = new TestRpc { FailAtWrite = 3 }; string before = Store.Serializer().Serialize(rpc.State); bool failed = false;
                try { WaveLinkControl.Apply(rpc, Profile(), CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { failed = true; }
                assert(failed && Store.Serializer().Serialize(rpc.State) == before);
            });
            test("Wave Link retries alternate parameter shapes only for invalid params", delegate {
                var rpc = new TestRpc { AlternateShapes = true }; WaveLinkControl.Apply(rpc, Profile(), CancellationToken.None).GetAwaiter().GetResult();
                assert(rpc.RejectedShapes == 3 && !rpc.Muted("vr-mic", "discord") && rpc.Muted("rode", "discord"));
            });
            test("Wave Link reads back state and rejects ignored writes", delegate {
                var rpc = new TestRpc { IgnoreWrites = true }; bool failed = false;
                try { WaveLinkControl.Apply(rpc, Profile(), CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { failed = true; }
                assert(failed && rpc.State.Outputs.mainOutput.outputId == "speakers");
            });
        }
        static WaveLinkProfile Profile() {
            return new WaveLinkProfile { Enabled = true, OutputDeviceId = "device", OutputId = "headphones", MixId = "discord", MicChannelId = "vr-mic", MutedChannelId = "rode" };
        }
        // In-memory JSON-RPC peer exercises the controller without changing real devices.
        class TestRpc : IWaveLinkRpc {
            public WaveLinkState State;
            public int Writes, FailAtWrite, RejectedShapes;
            public bool AlternateShapes, IgnoreWrites;
            public List<Dictionary<string, object>> ChannelPayloads = new List<Dictionary<string, object>>();
            public TestRpc() {
                State = new WaveLinkState {
                    Outputs = new WaveLinkOutputs { mainOutput = new WaveLinkMainOutput { outputDeviceId = "device", outputId = "speakers" }, outputDevices = new List<WaveLinkDevice> {
                        new WaveLinkDevice { id = "device", name = "Audio", outputs = new List<WaveLinkOutput> { new WaveLinkOutput { id = "speakers" }, new WaveLinkOutput { id = "headphones" } } } } },
                    Channels = new WaveLinkChannels { channels = new List<WaveLinkChannel>() },
                    Mixes = new WaveLinkMixes { mixes = new List<WaveLinkNamed> { new WaveLinkNamed { id = "discord" }, new WaveLinkNamed { id = "stream" } } }
                };
                foreach (string id in new[] { "vr-mic", "rode" }) State.Channels.channels.Add(new WaveLinkChannel { id = id, name = id, isMuted = false,
                    mixes = new List<WaveLinkChannelMix> { new WaveLinkChannelMix { id = "discord", isMuted = id == "vr-mic" }, new WaveLinkChannelMix { mixId = "stream", isMuted = id == "vr-mic" } } });
            }
            public bool Muted(string channel, string mix) { return State.Channels.channels.First(x => x.id == channel).mixes.First(x => (x.id ?? x.mixId) == mix).isMuted.Value; }
            public Task<object> Call(string method, object parameters, CancellationToken token) {
                token.ThrowIfCancellationRequested(); var serializer = Store.Serializer(); object result = null;
                if (method == "getOutputDevices") result = State.Outputs;
                else if (method == "getChannels") result = State.Channels;
                else if (method == "getMixes") result = State.Mixes;
                else {
                    var p = serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(parameters));
                    if (method == "setOutputDevice") {
                        if (AlternateShapes && p.ContainsKey("mainOutput")) { RejectedShapes++; throw new WaveLinkRpcException(-32602, "Invalid params"); }
                        if (++Writes == FailAtWrite) throw new IOException("Setter failed");
                        if (!IgnoreWrites) State.Outputs.mainOutput = WaveLinkControl.Decode<WaveLinkMainOutput>(p.ContainsKey("mainOutput") ? p["mainOutput"] : p);
                    } else if (method == "setChannel") {
                        ChannelPayloads.Add(p); var entry = WaveLinkControl.Decode<List<Dictionary<string, object>>>(p["mixes"])[0];
                        if (AlternateShapes && entry.ContainsKey("id")) { RejectedShapes++; throw new WaveLinkRpcException(-32602, "Invalid params"); }
                        if (++Writes == FailAtWrite) throw new IOException("Setter failed");
                        string mix = (string)entry[entry.ContainsKey("id") ? "id" : "mixId"];
                        if (!IgnoreWrites) State.Channels.channels.First(x => x.id == (string)p["id"]).mixes.First(x => (x.id ?? x.mixId) == mix).isMuted = (bool)entry["isMuted"];
                    } else throw new Exception("Unexpected RPC " + method);
                    result = new Dictionary<string, object>();
                }
                return Task.FromResult(serializer.DeserializeObject(serializer.Serialize(result)));
            }
        }
    }
}
