using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SlBridge.Launcher
{
    /// <summary>
    /// Иконки предметов Minecraft для вкладки «Крафты». Берутся из клиента Minecraft, который уже установлен у игрока
    /// (Prism Launcher или обычный лаунчер) — сами текстуры с лаунчером не распространяются.
    /// Не нашли Minecraft — вкладка рисует цветные квадраты с подписями, как раньше.
    /// </summary>
    internal static class McIcons
    {
        public static LauncherConfig Config;
        private const string Version = "26.3";
        private static ZipArchive _zip;
        private static bool _tried;
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        public static bool Available { get { Open(); return _zip != null; } }

        private static IEnumerable<string> Candidates()
        {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var roots = new List<string>();
            if (Config != null && !string.IsNullOrEmpty(Config.PrismInstancesDir)) roots.Add(Path.GetFullPath(Path.Combine(Config.PrismInstancesDir, "..")));
            roots.Add(Path.Combine(app, "PrismLauncher"));
            foreach (var r in roots)
            {
                string d = Path.Combine(r, "libraries", "com", "mojang", "minecraft", Version);
                if (Directory.Exists(d)) foreach (var f in Directory.GetFiles(d, "*client*.jar")) yield return f;
            }
            yield return Path.Combine(app, ".minecraft", "versions", Version, Version + ".jar");
            // Любой другой лаунчер (MultiMC, TLauncher, ATLauncher…): поднимаемся от папки игры вверх и ищем,
            // где лежит клиент Minecraft — в libraries/com/mojang/minecraft/<версия> или versions/<версия>.
            string dir = Config?.GameDirEffective;
            for (int up = 0; up < 5 && !string.IsNullOrEmpty(dir); up++, dir = Path.GetDirectoryName(dir))
            {
                string lib = Path.Combine(dir, "libraries", "com", "mojang", "minecraft", Version);
                if (Directory.Exists(lib)) foreach (var f in Directory.GetFiles(lib, "*client*.jar")) yield return f;
                string ver = Path.Combine(dir, "versions", Version);
                if (Directory.Exists(ver)) foreach (var f in Directory.GetFiles(ver, "*.jar")) yield return f;
                string vers = Path.Combine(dir, "versions");
                if (Directory.Exists(vers)) // у TLauncher/ATLauncher папка версии бывает «26.3-fabric…» или «Fabric 26.3»
                    foreach (var d in Directory.GetDirectories(vers).Where(d => Path.GetFileName(d).Contains(Version) && !Path.GetFileName(d).Equals(Version)))
                        foreach (var f in Directory.GetFiles(d, "*.jar")) yield return f;
            }
        }

        private static void Open()
        {
            if (_tried) return;
            _tried = true;
            foreach (var f in Candidates())
            {
                try
                {
                    if (!File.Exists(f)) continue;
                    var z = ZipFile.OpenRead(f);
                    if (z.GetEntry("assets/minecraft/textures/item/iron_ingot.png") == null) { z.Dispose(); continue; }
                    _zip = z;
                    return;
                }
                catch { }
            }
        }

        /// <summary>Иконка 16×16 предмета («minecraft:iron_ingot») или null.</summary>
        public static Bitmap Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Cache.TryGetValue(id, out var b)) return b;
            Open();
            Bitmap res = null;
            if (_zip != null)
            {
                string n = id.Contains(':') ? id.Substring(id.IndexOf(':') + 1) : id;
                if (n == "enchanted_golden_apple") n = "golden_apple"; // своей текстуры нет — это золотое яблоко с блеском
                foreach (var p in new[] { $"item/{n}", $"item/{n}_standby", $"item/{n}_00", $"block/{n}", $"block/{n}_side", $"block/{n}_front", $"block/{n}_top" })
                {
                    var e = _zip.GetEntry($"assets/minecraft/textures/{p}.png");
                    if (e == null) continue;
                    try
                    {
                        using var s = e.Open();
                        using var ms = new MemoryStream();
                        s.CopyTo(ms);
                        ms.Position = 0;
                        using var full = new Bitmap(ms);
                        // анимированные текстуры — вертикальная лента кадров: берём первый кадр
                        int w = full.Width;
                        res = full.Clone(new Rectangle(0, 0, w, Math.Min(w, full.Height)), PixelFormat.Format32bppArgb);
                        break;
                    }
                    catch { }
                }
            }
            Cache[id] = res;
            return res;
        }

        /// <summary>Нарисовать иконку чётко (без размытия пикселей).</summary>
        public static bool Draw(Graphics g, string id, Rectangle r)
        {
            var bmp = Get(id);
            if (bmp == null) return false;
            var im = g.InterpolationMode; var po = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, r);
            g.InterpolationMode = im; g.PixelOffsetMode = po;
            return true;
        }
    }
}
