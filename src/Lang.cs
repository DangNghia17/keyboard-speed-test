// Keyboard Speed Test - localization (Vietnamese / English).
// Every user-facing string is written as L.S("tiếng Việt", "English") so both languages live side by side.
using System;
using System.IO;
using System.Text;

namespace KeyboardSpeedTest
{
    public enum Language { Vietnamese, English }

    public static class L
    {
        public static Language Current = Language.English;

        public static bool En { get { return Current == Language.English; } }

        public static string S(string vietnamese, string english)
        {
            return Current == Language.English ? english : vietnamese;
        }

        private static string SettingsFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KeyboardSpeedTest", "settings.txt");
            }
        }

        /// <summary>Last language chosen on this PC (null if never chosen).</summary>
        public static Language? LoadSaved()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return null;
                foreach (string line in File.ReadAllLines(SettingsFile, Encoding.UTF8))
                {
                    if (line.StartsWith("language=", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = line.Substring(9).Trim().ToLowerInvariant();
                        if (v == "vi") return Language.Vietnamese;
                        if (v == "en") return Language.English;
                    }
                }
            }
            catch { }
            return null;
        }

        public static void Save(Language lang)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, "language=" + (lang == Language.Vietnamese ? "vi" : "en") + "\r\n", new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
