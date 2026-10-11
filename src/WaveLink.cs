using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RigPilot {
    public class WaveLinkProfile {
        public bool Enabled;
        public string OutputDeviceId = "";
        public string OutputId = "";
        public string MixId = "";
        public string MicChannelId = "";
        public string MutedChannelId = "";
    }
    // Lowercase fields match Wave Link 3's JSON-RPC wire format.
    public class WaveLinkImage { public string name; }
    public class WaveLinkNamed { public string id; public string name; public WaveLinkImage image; public string Label() { return name ?? (image == null ? null : image.name) ?? id; } }
    public class WaveLinkChannelMix { public string id; public string mixId; public bool? isMuted; }
    public class WaveLinkChannel : WaveLinkNamed { public bool? isMuted; public List<WaveLinkChannelMix> mixes; }
    public class WaveLinkOutput : WaveLinkNamed { }
    public class WaveLinkDevice : WaveLinkNamed { public List<WaveLinkOutput> outputs; }
    public class WaveLinkMainOutput { public string outputDeviceId; public string outputId; }
    public class WaveLinkOutputs { public WaveLinkMainOutput mainOutput; public List<WaveLinkDevice> outputDevices; }
    public class WaveLinkChannels { public List<WaveLinkChannel> channels; }
    public class WaveLinkMixes { public List<WaveLinkNamed> mixes; }
    public class WaveLinkState {
        public WaveLinkOutputs Outputs;
        public WaveLinkChannels Channels;
        public WaveLinkMixes Mixes;
    }
    public class WaveLinkRpcException : IOException {
        public int Code;
        public WaveLinkRpcException(int code, string message) : base(message) { Code = code; }
    }
    public class WaveLinkUnavailableException : InvalidOperationException {
        public WaveLinkUnavailableException(string message) : base(message) { }
    }
    public interface IWaveLinkRpc {
        Task<object> Call(string method, object parameters, CancellationToken token);
    }
    public sealed class WaveLinkClient : IWaveLinkRpc, IDisposable {
        ClientWebSocket socket;
        int requestId;
        public object ApplicationInfo { get; private set; }
        public static IEnumerable<int> Ports() {
            var ports = new List<int>();
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            try {
                if (Directory.Exists(packages)) foreach (string dir in Directory.GetDirectories(packages, "Elgato.WaveLink*")) {
                    string path = Path.Combine(dir, "LocalState", "ws-info.json");
                    try {
                        if (!File.Exists(path)) continue;
                        var info = Store.Read<Dictionary<string, object>>(path); object value; int port;
                        if (info != null && info.TryGetValue("port", out value) &&
                            Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out port) && port > 0 && port <= 65535) ports.Add(port);
                    } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            ports.AddRange(Enumerable.Range(1884, 10));
            return ports.Distinct();
        }
        public static async Task<WaveLinkClient> Open(CancellationToken token) {
            var elapsed=Stopwatch.StartNew();
            foreach (int port in Ports()) {
                token.ThrowIfCancellationRequested();
                if(elapsed.ElapsedMilliseconds>=5000)break;
                var client = new WaveLinkClient { socket = new ClientWebSocket() };
                client.socket.Options.Proxy = null;
                client.socket.Options.SetRequestHeader("Origin", "streamdeck://");
                try {
                    using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        attempt.CancelAfter(900);
                        await client.socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port), attempt.Token);
                        var app = await client.Call("getApplicationInfo", null, attempt.Token);
                        var info = app as Dictionary<string, object>; object appId;
                        if (info == null || !info.TryGetValue("appID", out appId) || !Object.Equals(appId, "EWL"))
                            throw new InvalidDataException(L.T("Die lokale Schnittstelle gehört nicht zu Wave Link."));
                        client.ApplicationInfo = app;
                        return client;
                    }
                } catch (Exception ex) {
                    client.Dispose();
                    token.ThrowIfCancellationRequested();
                    if (!(ex is WebSocketException || ex is OperationCanceledException || ex is IOException)) throw;
                }
            }
            throw new IOException(L.T("Wave Link 3 ist nicht erreichbar. Wave Link starten und die Verbindung erneut laden."));
        }
        public async Task<object> Call(string method, object parameters, CancellationToken token) {
            int id = ++requestId;
            var request = new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "method", method } };
            if (parameters != null) request.Add("params", parameters);
            byte[] bytes = Encoding.UTF8.GetBytes(Store.Serializer().Serialize(request));
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                timeout.CancelAfter(3500);
                try {
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
                    while (true) {
                        using (var buffer = new MemoryStream()) {
                            byte[] chunk = new byte[4096]; WebSocketReceiveResult part;
                            do {
                                part = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), timeout.Token);
                                if (part.MessageType != WebSocketMessageType.Text) throw new IOException(L.T("Wave Link hat die Verbindung beendet oder ungültige Daten gesendet."));
                                buffer.Write(chunk, 0, part.Count);
                                if (buffer.Length > 1024 * 1024) throw new InvalidDataException(L.T("Die Wave-Link-Antwort ist zu groß."));
                            } while (!part.EndOfMessage);
                            object result;
                            if (TryResponse(Encoding.UTF8.GetString(buffer.ToArray()), id, out result)) return result;
                        }
                    }
                } catch (OperationCanceledException) {
                    token.ThrowIfCancellationRequested();
                    throw new IOException(L.F("Wave Link antwortet nicht auf {0}.", method));
                }
            }
        }
        internal static bool TryResponse(string json, int id, out object result) {
            result = null;
            var response = Store.Serializer().Deserialize<Dictionary<string, object>>(json); object value;
            if (response == null) throw new InvalidDataException(L.T("Ungültige Wave-Link-Antwort."));
            if (!response.TryGetValue("id", out value) || Convert.ToString(value, CultureInfo.InvariantCulture) != id.ToString(CultureInfo.InvariantCulture)) return false;
            if (response.TryGetValue("error", out value)) {
                var error = value as Dictionary<string, object>; object code, message;
                if (error == null || !error.TryGetValue("code", out code) || !error.TryGetValue("message", out message))
                    throw new InvalidDataException(L.T("Ungültige Wave-Link-Antwort."));
                throw new WaveLinkRpcException(Convert.ToInt32(code, CultureInfo.InvariantCulture), Convert.ToString(message, CultureInfo.InvariantCulture));
            }
            if (!response.TryGetValue("result", out result)) throw new InvalidDataException(L.T("Ungültige Wave-Link-Antwort."));
            return true;
        }
        public void Dispose() { if (socket != null) socket.Dispose(); }
    }
    public static class WaveLinkControl {
        internal static T Decode<T>(object value) { var serializer=Store.Serializer();return serializer.Deserialize<T>(serializer.Serialize(value)); }
        public static async Task<WaveLinkState> Read(IWaveLinkRpc client, CancellationToken token) {
            var state = new WaveLinkState {
                Outputs = Decode<WaveLinkOutputs>(await client.Call("getOutputDevices", null, token)),
                Channels = Decode<WaveLinkChannels>(await client.Call("getChannels", null, token)),
                Mixes = Decode<WaveLinkMixes>(await client.Call("getMixes", null, token))
            };
            if (state.Outputs == null || state.Outputs.outputDevices == null || state.Channels == null || state.Channels.channels == null || state.Mixes == null || state.Mixes.mixes == null)
                throw new InvalidDataException(L.T("Die Audio-Steuerung benötigt die Schnittstelle von Wave Link 3."));
            return state;
        }
        public static string OutputKey(string device, string output) { return Store.Serializer().Serialize(new[] { device, output }); }
        public static void Validate(WaveLinkProfile profile, WaveLinkState state) {
            if (profile == null || !profile.Enabled) return;
            if (String.IsNullOrEmpty(profile.OutputDeviceId) != String.IsNullOrEmpty(profile.OutputId)) throw new InvalidDataException(L.T("Bitte einen vollständigen Wave-Link-Ausgang auswählen."));
            if (!String.IsNullOrEmpty(profile.OutputId) && !state.Outputs.outputDevices.Any(d => d.id == profile.OutputDeviceId && d.outputs != null && d.outputs.Any(o => o.id == profile.OutputId)))
                throw new WaveLinkUnavailableException(L.T("Der gespeicherte Wave-Link-Ausgang ist nicht verfügbar."));
            bool microphone = !String.IsNullOrEmpty(profile.MicChannelId) || !String.IsNullOrEmpty(profile.MutedChannelId);
            if (!microphone) return;
            if (String.IsNullOrEmpty(profile.MixId) || !state.Mixes.mixes.Any(x => x.id == profile.MixId))
                throw new WaveLinkUnavailableException(L.T("Bitte einen verfügbaren Wave-Link-Mix für die Mikrofone auswählen."));
            if (!String.IsNullOrEmpty(profile.MicChannelId) && profile.MicChannelId == profile.MutedChannelId)
                throw new InvalidOperationException(L.T("Ein Mikrofon kann nicht gleichzeitig aktiviert und stummgeschaltet werden."));
            foreach (string id in new[] { profile.MicChannelId, profile.MutedChannelId }.Where(x => !String.IsNullOrEmpty(x))) {
                var channel = state.Channels.channels.FirstOrDefault(x => x.id == id);
                if (channel == null || channel.mixes == null || !channel.mixes.Any(x => (x.id ?? x.mixId) == profile.MixId && x.isMuted.HasValue))
                    throw new WaveLinkUnavailableException(L.T("Der gespeicherte Mikrofonkanal ist in diesem Mix nicht verfügbar."));
                if (id == profile.MicChannelId && channel.isMuted == true)
                    throw new InvalidOperationException(L.T("Das gewünschte Mikrofon ist in Wave Link global stumm. Die globale Stummschaltung dort zuerst aufheben."));
            }
        }
        static bool MuteState(WaveLinkState state, string channel, string mix) {
            return state.Channels.channels.First(x => x.id == channel).mixes.First(x => (x.id ?? x.mixId) == mix).isMuted.Value;
        }
        internal static object MainOutputParameters(string device, string output, bool direct) {
            var main = new { outputDeviceId = device, outputId = output };
            return direct ? (object)main : new { mainOutput = main };
        }
        internal static object MuteParameters(string channel, string mix, bool muted, bool useMixId) {
            var entry = new Dictionary<string, object> { { useMixId ? "mixId" : "id", mix }, { "isMuted", muted } };
            return new { id = channel, mixes = new[] { entry } };
        }
        static async Task SetOutput(IWaveLinkRpc client, string device, string output, CancellationToken token) {
            bool retry=false;
            try { await client.Call("setOutputDevice", MainOutputParameters(device, output, false), token); }
            catch (WaveLinkRpcException ex) {
                if (ex.Code != -32602) throw;
                retry=true;
            }
            if(retry)await client.Call("setOutputDevice", MainOutputParameters(device, output, true), token);
        }
        static async Task SetMute(IWaveLinkRpc client, string channel, string mix, bool muted, CancellationToken token) {
            bool retry=false;
            try { await client.Call("setChannel", MuteParameters(channel, mix, muted, false), token); }
            catch (WaveLinkRpcException ex) {
                if (ex.Code != -32602) throw;
                retry=true;
            }
            if(retry)await client.Call("setChannel", MuteParameters(channel, mix, muted, true), token);
        }
        public static async Task Apply(IWaveLinkRpc client, WaveLinkProfile profile, CancellationToken token) {
            var before = await Read(client, token);
            Validate(profile, before);
            if (profile == null || !profile.Enabled) return;
            if (!String.IsNullOrEmpty(profile.OutputId) && before.Outputs.mainOutput == null)
                throw new InvalidDataException(L.T("Wave Link meldet den bisherigen Hauptausgang nicht."));
            var restore = new List<Func<Task>>();
            Exception failure=null;
            try {
                if (!String.IsNullOrEmpty(profile.OutputId)) {
                    var previous = before.Outputs.mainOutput;
                    restore.Add(delegate { return SetOutput(client, previous.outputDeviceId, previous.outputId, CancellationToken.None); });
                    await SetOutput(client, profile.OutputDeviceId, profile.OutputId, token);
                }
                // Mute the other microphone first, then enable only the selected mic in this mix.
                foreach (string id in new[] { profile.MutedChannelId, profile.MicChannelId }.Where(x => !String.IsNullOrEmpty(x))) {
                    string channelId = id; bool previous = MuteState(before, id, profile.MixId);
                    restore.Add(delegate { return SetMute(client, channelId, profile.MixId, previous, CancellationToken.None); });
                    await SetMute(client, id, profile.MixId, id == profile.MutedChannelId, token);
                }
                var after = await Read(client, token);
                Validate(profile,after);
                if (!String.IsNullOrEmpty(profile.OutputId) && (after.Outputs.mainOutput == null || after.Outputs.mainOutput.outputDeviceId != profile.OutputDeviceId || after.Outputs.mainOutput.outputId != profile.OutputId))
                    throw new IOException(L.T("Wave Link hat den Hauptausgang nicht übernommen."));
                if ((!String.IsNullOrEmpty(profile.MicChannelId) && MuteState(after, profile.MicChannelId, profile.MixId)) ||
                    (!String.IsNullOrEmpty(profile.MutedChannelId) && !MuteState(after, profile.MutedChannelId, profile.MixId)))
                    throw new IOException(L.T("Wave Link hat die Mikrofon-Stummschaltung nicht übernommen."));
            } catch(Exception ex) { failure=ex; }
            if(failure!=null) {
                bool restored=true;
                for (int i = restore.Count - 1; i >= 0; i--) {
                    try { await restore[i](); } catch (Exception ex) { restored=false;Store.Log(L.T("Wave-Link-Rückstellung fehlgeschlagen: ") + ex.Message); }
                }
                if(!restored)throw new IOException(L.T("Das Wave-Link-Profil wurde nicht übernommen und konnte nicht vollständig zurückgestellt werden. Bitte Wave Link prüfen. ")+failure.Message,failure);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        public static async Task ApplyLocal(WaveLinkProfile profile, CancellationToken token) {
            WaveLinkClient connection=null;
            var elapsed=Stopwatch.StartNew();
            for(int i=0;connection==null;i++) {
                try { connection=await WaveLinkClient.Open(token); }
                catch(IOException) { if(i==19||elapsed.ElapsedMilliseconds>=15000)throw; }
                if(connection==null)await Task.Delay(500,token);
            }
            using (var client = connection) {
                // Give applications started in the group time to expose their devices.
                for (int i = 0; ; i++) {
                    token.ThrowIfCancellationRequested();
                    try { Validate(profile, await Read(client, token)); break; }
                    catch (WaveLinkUnavailableException) { if (i == 19) throw; }
                    await Task.Delay(500, token);
                }
                await Apply(client, profile, token);
            }
        }
    }
}
