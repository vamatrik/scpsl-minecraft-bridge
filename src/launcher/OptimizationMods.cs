using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SlBridge.Launcher
{
    /// <summary>
    /// Моды оптимизации с Modrinth для той же версии Minecraft (26.3, Fabric).
    /// Набор подобран так, чтобы не трогать то, на чём держится мост (камера, ввод, интерфейс, коллизии):
    /// Lithium — логика мира (быстрее встроенный сервер), FerriteCore — память,
    /// ImmediatelyFast — отрисовка интерфейса и текста, EntityCulling — не рисует невидимых мобов.
    /// Sodium (рендер чанков) — отдельно: он меняет отрисовку сильнее остальных. Берётся релиз (бета/альфа — только если релиза нет).
    /// </summary>
    internal static class OptimizationMods
    {
        public const string GameVersion = "26.3";
        public static readonly string[] Safe = { "lithium", "ferrite-core", "immediatelyfast", "entityculling" };
        public static readonly string[] SodiumSet = { "sodium" };

        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Vamatrik-SlBridgeLauncher/0.8 (github.com/vamatrik/scpsl-minecraft-bridge)");
            return h;
        }

        /// <summary>Свежая версия проекта под 26.3 + Fabric: сначала релиз, потом бета, потом альфа.</summary>
        private static async Task<(string url, string file, string version)> Latest(string project)
        {
            string q = $"https://api.modrinth.com/v2/project/{project}/version?loaders=%5B%22fabric%22%5D&game_versions=%5B%22{GameVersion}%22%5D";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(q));
            var versions = doc.RootElement.EnumerateArray().ToList();
            if (versions.Count == 0) throw new Exception(L.T($"{project}: нет версии для Minecraft {GameVersion}", $"{project}: no version for Minecraft {GameVersion}"));
            JsonElement pick = default; bool found = false;
            foreach (var type in new[] { "release", "beta", "alpha" })
            {
                foreach (var v in versions)
                    if (v.GetProperty("version_type").GetString() == type) { pick = v; found = true; break; }
                if (found) break;
            }
            var files = pick.GetProperty("files").EnumerateArray().ToList();
            var f = files.FirstOrDefault(x => x.GetProperty("primary").GetBoolean());
            if (f.ValueKind == JsonValueKind.Undefined) f = files[0];
            return (f.GetProperty("url").GetString(), f.GetProperty("filename").GetString(), pick.GetProperty("version_number").GetString());
        }

        /// <summary>Привести папку mods к выбранному набору: поставить нужное, убрать то, что ставили мы и что больше не выбрано.</summary>
        public static async Task Sync(LauncherConfig cfg, string modsDir, Action<string> log)
        {
            if (string.IsNullOrEmpty(modsDir)) return;
            Directory.CreateDirectory(modsDir);
            var want = new List<string>();
            if (cfg.OptMods) want.AddRange(Safe);
            if (cfg.SodiumMod) want.AddRange(SodiumSet);

            var installed = cfg.OptModFiles ?? new Dictionary<string, string>();
            // убрать лишнее (только файлы, которые ставил лаунчер)
            foreach (var kv in installed.ToList())
            {
                if (want.Contains(kv.Key)) continue;
                try { var p = Path.Combine(modsDir, kv.Value); if (File.Exists(p)) File.Delete(p); } catch { }
                installed.Remove(kv.Key);
                log(L.T("Убран мод ", "Removed mod ") + kv.Value);
            }
            // поставить / обновить нужное
            foreach (var project in want)
            {
                try
                {
                    var (url, file, ver) = await Latest(project);
                    string target = Path.Combine(modsDir, file);
                    if (installed.TryGetValue(project, out var cur) && cur == file && File.Exists(target)) continue;
                    if (cur != null && cur != file) { try { File.Delete(Path.Combine(modsDir, cur)); } catch { } }
                    var bytes = await Http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(target, bytes);
                    installed[project] = file;
                    log(L.T($"Установлен мод {file}", $"Installed mod {file}"));
                }
                catch (Exception e) { log(L.T($"Не удалось установить {project}: ", $"Could not install {project}: ") + e.Message); }
            }
            cfg.OptModFiles = installed;
            cfg.Save();
        }

        /// <summary>Все выбранные моды на месте?</summary>
        public static bool InSync(LauncherConfig cfg, string modsDir)
        {
            if (string.IsNullOrEmpty(modsDir)) return true;
            var want = new List<string>();
            if (cfg.OptMods) want.AddRange(Safe);
            if (cfg.SodiumMod) want.AddRange(SodiumSet);
            var inst = cfg.OptModFiles ?? new Dictionary<string, string>();
            return want.All(p => inst.TryGetValue(p, out var f) && File.Exists(Path.Combine(modsDir, f)))
                   && inst.Keys.All(want.Contains);
        }
    }
}
