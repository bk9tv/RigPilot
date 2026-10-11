using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace RigPilot {
    // Only application text passes through T/F. User names, paths and device IDs stay untouched.
    public static class L {
        public static string Language { get; private set; }
        internal static readonly Dictionary<string, string> English = ReadEnglish();
        static L() { Use("de"); }
        static Dictionary<string, string> ReadEnglish() {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RigPilot.English.json"))
            using (var reader = new StreamReader(stream))
                return Store.Serializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        public static string Normalize(string language) { return language == "en" ? "en" : "de"; }
        public static void Use(string language) { Language = Normalize(language); }
        public static string T(string german) {
            string translated;
            return Language == "en" && English.TryGetValue(german, out translated) ? translated : german;
        }
        public static string F(string german, params object[] values) {
            return String.Format(CultureInfo.GetCultureInfo(Language), T(german), values);
        }
        public static string GuideFile { get { return Language == "en" ? "GUIDE.en.txt" : "ANLEITUNG.txt"; } }
        // Separate from profile.json so changing language never rewrites profiles or their backups.
        public static void Load() {
            Use("de");
            string path = Path.Combine(Store.Root, "language.json");
            try { if (File.Exists(path)) Use(Store.Read<LanguagePreference>(path).Language); }
            catch { /* A damaged preference must never prevent monitor recovery or app startup. */ }
        }
        public static void Save(string language) {
            string next = Normalize(language);
            Store.Write(Path.Combine(Store.Root, "language.json"), new LanguagePreference { Language = next });
            Use(next);
        }
    }
    public class LanguagePreference { public string Language = "de"; }
}
