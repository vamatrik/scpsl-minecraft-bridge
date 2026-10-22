using System;
using System.IO;

namespace SlBridge
{
    /// <summary>
    /// Язык мода (русский/английский). Выбирается в лаунчере: он пишет «ru» или «en» в
    /// %APPDATA%\SlBridge\language.txt — этот файл читают и плагин, и мод Minecraft.
    /// </summary>
    public static class L
    {
        public static bool En { get; private set; }

        public static string T(string ru, string en) => En ? en : ru;

        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlBridge", "language.txt");

        /// <summary>configLang: "auto" — из файла лаунчера, иначе "ru"/"en".</summary>
        public static void Load(string configLang)
        {
            string lang = (configLang ?? "auto").Trim().ToLowerInvariant();
            if (lang == "auto" || lang.Length == 0)
            {
                lang = "ru";
                try { if (File.Exists(FilePath)) lang = File.ReadAllText(FilePath).Trim().ToLowerInvariant(); } catch { }
            }
            En = lang.StartsWith("en");
        }
    }
}
