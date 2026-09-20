using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace RigPilot {
    public class AudioDevice {
        public string Id;
        public string Name;
        public int Flow;
        public bool IsDefault;
        public override string ToString() { return Name + (IsDefault ? "  ·  Standard" : ""); }
    }
    [StructLayout(LayoutKind.Sequential)] public struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] public struct PropVariant {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMCollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice {
        [PreserveSig] int Activate(ref Guid id, uint context, IntPtr param, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint mode, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyClient { }
    // ABI slots preceding SetDefaultEndpoint are deliberately unused. PolicyConfig is undocumented.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPolicyConfig {
        void Slot03(); void Slot04(); void Slot05(); void Slot06(); void Slot07();
        void Slot08(); void Slot09(); void Slot10(); void Slot11(); void Slot12();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
    public static class Audio {
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
        static void Release(object o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
        public static void ProbePolicy() { IPolicyConfig policy = (IPolicyConfig)new PolicyClient(); Release(policy); }
        public static string Default(int flow, int role) {
            IMMEnumerator enumerator = (IMMEnumerator)new MMEnumerator(); IMMDevice device = null;
            try { if (enumerator.GetDefaultAudioEndpoint(flow, role, out device) < 0) return ""; string id; Marshal.ThrowExceptionForHR(device.GetId(out id)); return id; }
            finally { Release(device); Release(enumerator); }
        }
        public static List<AudioDevice> List(int flow) {
            IMMEnumerator enumerator = (IMMEnumerator)new MMEnumerator(); IMMCollection collection = null;
            var result = new List<AudioDevice>(); string defaultId = Default(flow, 0);
            try {
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, 1, out collection)); uint count; Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                for (uint i = 0; i < count; i++) {
                    IMMDevice device = null; IPropertyStore props = null;
                    try {
                        Marshal.ThrowExceptionForHR(collection.Item(i, out device)); string id; Marshal.ThrowExceptionForHR(device.GetId(out id));
                        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out props));
                        PropertyKey key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                        PropVariant val; Marshal.ThrowExceptionForHR(props.GetValue(ref key, out val)); string name;
                        try { name = val.Type == 31 ? Marshal.PtrToStringUni(val.Pointer) : id; } finally { PropVariantClear(ref val); }
                        result.Add(new AudioDevice { Id = id, Name = name, Flow = flow, IsDefault = id == defaultId });
                    } finally { Release(props); Release(device); }
                }
            } finally { Release(collection); Release(enumerator); }
            return result.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToList();
        }
        public static void SetDefault(string id, int flow) {
            if (!List(flow).Any(x => x.Id == id)) throw new InvalidOperationException("Das ausgewählte Audiogerät ist nicht verbunden oder deaktiviert.");
            var previous = new string[] { Default(flow, 0), Default(flow, 1), Default(flow, 2) };
            IPolicyConfig policy = (IPolicyConfig)new PolicyClient();
            try {
                for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
                for (int role = 0; role < 3; role++) if (Default(flow, role) != id) throw new InvalidOperationException("Windows hat die Audioauswahl nicht übernommen.");
            } catch {
                for (int role = 0; role < 3; role++) if (!String.IsNullOrEmpty(previous[role])) policy.SetDefaultEndpoint(previous[role], role);
                throw;
            } finally { Release(policy); }
        }
    }
}
