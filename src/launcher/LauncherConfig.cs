using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SlBridge.Launcher
{
    /// <summary>launcher.json рядом с exe. Пути находятся сами, при необходимости указываются в «Подготовке».</summary>
    public sealed class LauncherConfig
    {
        public string SlClientDir { get; set; } = "";
        public string SlServerDir { get; set; } = "";
        public int SlServerPort { get; set; } = 7777;
        /// <summary>"prism" — запуск через Prism Launcher; "other" — любой другой лаунчер (Minecraft запускаешь сам).</summary>
        public string McMode { get; set; } = "prism";
        public string PrismExe { get; set; } = "";
        public string PrismInstancesDir { get; set; } = "";
        public string PrismInstance { get; set; } = "";
        /// <summary>Для «другого лаунчера»: папка игры (где mods, saves, config).</summary>
        public string GameDir { get; set; } = "";
        public string LastWorld { get; set; } = "";
        /// <summary>Язык лаунчера и всего мода: "ru" или "en" (пусто — по языку Windows).</summary>
        public string Language { get; set; } = "";
        /// <summary>Моды оптимизации (Lithium, FerriteCore, ImmediatelyFast, EntityCulling) и отдельно Sodium.</summary>
        public bool OptMods { get; set; } = false;
        public bool SodiumMod { get; set; } = false;
        /// <summary>Что поставил лаунчер: проект Modrinth → имя файла в mods (удаляем только своё).</summary>
        public System.Collections.Generic.Dictionary<string, string> OptModFiles { get; set; } = new();
        public string SlClientArgs { get; set; } = "-screen-fullscreen 0 -window-mode borderless";

        public static string BaseDir => AppContext.BaseDirectory;
        private static string File_ => Path.Combine(BaseDir, "launcher.json");

        public string DistDir => Path.Combine(BaseDir, "dist");
        public string CompositorExe => Path.Combine(DistDir, "compositor", "SlBridgeCompositor.exe");
        public string PluginDll => Path.Combine(DistDir, "SlBridge.dll");
        public string ModJar => Directory.Exists(DistDir) ? Directory.GetFiles(DistDir, "slbridge-fabric*.jar").FirstOrDefault() : null;
        public string FabricApiJar => Directory.Exists(DistDir) ? Directory.GetFiles(DistDir, "fabric-api*.jar").FirstOrDefault() : null;
        public static string SlAppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCP Secret Laboratory");
        public static string PluginTarget => Path.Combine(SlAppData, "LabAPI", "plugins", "global", "SlBridge.dll");

        /// <summary>Папка игры Minecraft (mods, saves, config, logs) — для Prism это minecraft внутри сборки.</summary>
        public string GameDirEffective
        {
            get
            {
                if (McMode == "prism")
                {
                    if (string.IsNullOrEmpty(PrismInstance)) return "";
                    string d = Path.Combine(PrismInstancesDir, PrismInstance, "minecraft");
                    if (!Directory.Exists(d)) d = Path.Combine(PrismInstancesDir, PrismInstance, ".minecraft");
                    return d;
                }
                return GameDir ?? "";
            }
        }

        public static LauncherConfig Load()
        {
            LauncherConfig c = null;
            try { if (File.Exists(File_)) c = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(File_)); } catch { }
            c ??= new LauncherConfig();
            if (c.Language != "ru" && c.Language != "en")
                c.Language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
            c.AutoDetect();
            c.Save();
            return c;
        }

        /// <summary>Общий файл языка: его читают плагин SL и мод Minecraft.</summary>
        public static string LanguageFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlBridge", "language.txt");

        public void WriteLanguageFile()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LanguageFile)); File.WriteAllText(LanguageFile, Language); } catch { }
        }

        /// <summary>
        /// Кнопки моста в SL (Esc → Настройки → Server-specific). SL сам их не назначает — только подсвечивает «предложенную»
        /// клавишу, и поле остаётся пустым. Прописываем их в настройки клиента SL (registry.txt), если игрок их ещё не задал.
        /// Только когда SL не запущен (иначе он перезапишет файл при выходе).
        /// </summary>
        public void EnsureSlKeybinds()
        {
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName("SCPSL").Length > 0) return;
                string f = Path.Combine(SlAppData, "registry.txt");
                if (!File.Exists(f)) return;
                // id настройки → Unity KeyCode: E, K (инвентарь), U (книга крафтов), O (настройки), F4, B, G
                var want = new (int id, int key)[] { (9001, 101), (9003, 107), (9008, 117), (9007, 111), (9004, 285), (9005, 98), (9006, 103) };
                string text = File.ReadAllText(f);
                string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                var add = new System.Text.StringBuilder();
                foreach (var (id, key) in want)
                {
                    string prefix = $"07SrvSp_127.0.0.1_1_{id}::";
                    if (text.Contains(prefix)) continue; // игрок уже назначил (или очистил) сам — не трогаем
                    add.Append(prefix).Append("-%(|::").Append(key).Append(nl);
                }
                if (add.Length == 0) return;
                File.Copy(f, f + ".slbridge.bak", true);
                if (text.Length > 0 && !text.EndsWith("\n")) text += nl;
                File.WriteAllText(f, text + add, new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>Язык самого Minecraft (options.txt), чтобы названия предметов совпадали с языком мода.</summary>
        public bool WriteMinecraftLanguage()
        {
            try
            {
                string dir = GameDirEffective;
                if (string.IsNullOrEmpty(dir)) return false;
                string f = Path.Combine(dir, "options.txt");
                string code = Language == "en" ? "en_us" : "ru_ru";
                var lines = File.Exists(f) ? File.ReadAllLines(f).ToList() : new System.Collections.Generic.List<string>();
                int i = lines.FindIndex(l => l.StartsWith("lang:"));
                if (i >= 0) lines[i] = "lang:" + code; else lines.Add("lang:" + code);
                File.WriteAllLines(f, lines);
                return true;
            }
            catch { return false; }
        }

        public void Save()
        {
            try { File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { }
        }

        public void AutoDetect()
        {
            if (!File.Exists(Path.Combine(SlClientDir ?? "", "SCPSL.exe")))
                SlClientDir = FindSteamApp("SCP Secret Laboratory") ?? "";
            if (!File.Exists(Path.Combine(SlServerDir ?? "", "LocalAdmin.exe")))
                SlServerDir = FindSteamApp("SCP Secret Laboratory Dedicated Server") ?? "";
            if (!File.Exists(PrismExe ?? ""))
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[] candidates =
                {
                    Path.Combine(local, "Programs", "PrismLauncher", "prismlauncher.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PrismLauncher", "prismlauncher.exe"),
                };
                PrismExe = candidates.FirstOrDefault(File.Exists) ?? "";
            }
            if (!Directory.Exists(PrismInstancesDir ?? ""))
                PrismInstancesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismLauncher", "instances");
            if (string.IsNullOrEmpty(GameDir))
            {
                string mc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
                if (Directory.Exists(mc)) GameDir = mc;
            }
        }

        /// <summary>Сборки Prism с Minecraft 26.3 и Fabric (по mmc-pack.json).</summary>
        public string[] PrismFabricInstances()
        {
            try
            {
                return new DirectoryInfo(PrismInstancesDir).GetDirectories().Where(d =>
                {
                    string pack = Path.Combine(d.FullName, "mmc-pack.json");
                    if (!File.Exists(pack)) return false;
                    string t = File.ReadAllText(pack);
                    return t.Contains("net.fabricmc.fabric-loader") && Regex.IsMatch(t, "\"uid\"\\s*:\\s*\"net.minecraft\"[^}]*\"version\"\\s*:\\s*\"26\\.3");
                }).Select(d => d.Name).OrderBy(n => n).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Ищет игру/программу Steam по всем библиотекам (libraryfolders.vdf).</summary>
        public static string FindSteamApp(string folderName)
        {
            try
            {
                string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";
                steam = steam.Replace('/', '\\');
                var libs = new System.Collections.Generic.List<string> { steam };
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string p = Path.Combine(lib, "steamapps", "common", folderName);
                    if (Directory.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }
    }
}
