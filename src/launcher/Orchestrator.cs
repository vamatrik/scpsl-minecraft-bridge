using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SlBridge.Launcher
{
    /// <summary>Что сделать с миром при запуске.</summary>
    public sealed class WorldChoice
    {
        public bool Create;
        public string Folder;          // открыть
        public string Name;            // создать
        public string Mode = "survival", Difficulty = "normal", Type = "normal", Seed = "";
        public bool Cheats;
    }

    public sealed class RecentWorld { public string Folder; public string Name; public long Time; }

    /// <summary>Запуск/остановка всех частей в правильном порядке, проверки «Подготовки» и состояния.</summary>
    public sealed class Orchestrator
    {
        private readonly LauncherConfig _c;
        private readonly Action<string> _log;
        private DateTime _startedAt = DateTime.MinValue;
        public volatile bool ManualInWorld;

        public Orchestrator(LauncherConfig c, Action<string> log) { _c = c; _log = log; }

        // ───────────── подготовка ─────────────

        public bool SlClientFound => File.Exists(Path.Combine(_c.SlClientDir ?? "", "SCPSL.exe"));
        public bool SlServerFound => File.Exists(Path.Combine(_c.SlServerDir ?? "", "LocalAdmin.exe"));
        public bool ServerConfigured => Directory.Exists(Path.Combine(LauncherConfig.SlAppData, "config", _c.SlServerPort.ToString()));
        public bool PluginInstalled => File.Exists(_c.PluginDll) && File.Exists(LauncherConfig.PluginTarget) && SameFile(_c.PluginDll, LauncherConfig.PluginTarget);
        public bool PrismFound => File.Exists(_c.PrismExe ?? "");
        public string ModsDir => string.IsNullOrEmpty(_c.GameDirEffective) ? "" : Path.Combine(_c.GameDirEffective, "mods");

        public bool ModInstalled
        {
            get
            {
                if (!Directory.Exists(ModsDir) || _c.ModJar == null) return false;
                string target = Path.Combine(ModsDir, Path.GetFileName(_c.ModJar));
                bool mod = File.Exists(target) && SameFile(_c.ModJar, target);
                bool api = Directory.GetFiles(ModsDir, "fabric-api*.jar").Any();
                return mod && api;
            }
        }

        public void InstallPlugin()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LauncherConfig.PluginTarget));
            File.Copy(_c.PluginDll, LauncherConfig.PluginTarget, true);
            _log(L.T("Плагин моста установлен в LabAPI.", "Bridge plugin installed into LabAPI."));
        }

        public void InstallMod()
        {
            if (string.IsNullOrEmpty(ModsDir)) throw new Exception(L.T("Сначала выбери сборку Prism или папку игры.", "Pick a Prism instance or a game folder first."));
            Directory.CreateDirectory(ModsDir);
            // старые версии мода — убрать, чтобы не было двух
            foreach (var old in Directory.GetFiles(ModsDir, "slbridge-fabric*.jar"))
                if (!string.Equals(Path.GetFileName(old), Path.GetFileName(_c.ModJar), StringComparison.OrdinalIgnoreCase)) File.Delete(old);
            File.Copy(_c.ModJar, Path.Combine(ModsDir, Path.GetFileName(_c.ModJar)), true);
            if (!Directory.GetFiles(ModsDir, "fabric-api*.jar").Any() && _c.FabricApiJar != null)
                File.Copy(_c.FabricApiJar, Path.Combine(ModsDir, Path.GetFileName(_c.FabricApiJar)), true);
            _log(L.T("Мод моста и Fabric API установлены в ", "Bridge mod and Fabric API installed into ") + ModsDir);
        }

        public void FirstServerRun()
        {
            InstallPluginIfNeeded();
            Process.Start(new ProcessStartInfo(Path.Combine(_c.SlServerDir, "LocalAdmin.exe"), _c.SlServerPort.ToString())
            { WorkingDirectory = _c.SlServerDir, UseShellExecute = true });
            _log(L.T("Открыт сервер SL. Ответь на его вопросы (порт, согласие), дождись «Waiting for players» и закрой окно.", "The SL server is open. Answer its questions (port, consent), wait for “Waiting for players” and close the window."));
        }

        private void InstallPluginIfNeeded() { try { if (!PluginInstalled) InstallPlugin(); } catch (Exception e) { _log(L.T("Плагин: ", "Plugin: ") + e.Message); } }

        private static bool SameFile(string a, string b)
        {
            try { return new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)); }
            catch { return false; }
        }

        // ───────────── миры ─────────────

        public string[] Worlds()
        {
            try
            {
                return new DirectoryInfo(Path.Combine(_c.GameDirEffective, "saves")).GetDirectories()
                    .Where(d => File.Exists(Path.Combine(d.FullName, "level.dat")))
                    .OrderByDescending(d => d.LastWriteTimeUtc).Select(d => d.Name).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        public List<RecentWorld> Recent()
        {
            try
            {
                string f = Path.Combine(_c.GameDirEffective, "config", "slbridge-recent.json");
                if (!File.Exists(f)) return new List<RecentWorld>();
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                return doc.RootElement.EnumerateArray().Select(e => new RecentWorld
                {
                    Folder = e.GetProperty("folder").GetString(),
                    Name = e.TryGetProperty("name", out var n) ? n.GetString() : e.GetProperty("folder").GetString(),
                    Time = e.TryGetProperty("time", out var t) ? t.GetInt64() : 0,
                }).Where(r => Directory.Exists(Path.Combine(_c.GameDirEffective, "saves", r.Folder))).ToList();
            }
            catch { return new List<RecentWorld>(); }
        }

        private void WriteLaunchRequest(WorldChoice w)
        {
            string cfg = Path.Combine(_c.GameDirEffective, "config");
            Directory.CreateDirectory(cfg);
            object req = w.Create
                ? new { action = "create", name = w.Name, mode = w.Mode, difficulty = w.Difficulty, type = w.Type, cheats = w.Cheats, seed = w.Seed ?? "" }
                : new { action = "open", world = w.Folder };
            File.WriteAllText(Path.Combine(cfg, "slbridge-launch.json"), JsonSerializer.Serialize(req), new UTF8Encoding(false));
        }

        // ───────────── состояние ─────────────

        public static bool ServerRunning => Process.GetProcessesByName("LocalAdmin").Length > 0;
        public static bool CompositorRunning => Process.GetProcessesByName("SlBridgeCompositor").Length > 0;
        public static bool SlClientRunning => Process.GetProcessesByName("SCPSL").Any(HasWindow);

        public static Process MinecraftProcess =>
            Process.GetProcessesByName("javaw").Concat(Process.GetProcessesByName("java"))
                   .FirstOrDefault(p => { try { return p.MainWindowTitle.StartsWith("Minecraft"); } catch { return false; } });

        private static bool HasWindow(Process p) { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } }

        private string ServerLogTail()
        {
            try
            {
                string dir = Path.Combine(LauncherConfig.SlAppData, "LocalAdminLogs", _c.SlServerPort.ToString());
                var f = new DirectoryInfo(dir).GetFiles().OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                return f == null ? "" : Tail(f.FullName, 300_000);
            }
            catch { return ""; }
        }

        public bool ServerReady
        {
            get
            {
                if (!ServerRunning) return false;
                string t = ServerLogTail();
                return t.Contains("Waiting for players") || t.Contains("[SlBridge] Включён") || t.Contains("[SlBridge] Enabled");
            }
        }

        /// <summary>Мост: плагин на сервере SL подключён к моду Minecraft (по логу сервера — работает с любым лаунчером).</summary>
        public bool BridgeConnected
        {
            get
            {
                if (!ServerRunning || MinecraftProcess == null) return false;
                string t = ServerLogTail();
                int on = Math.Max(t.LastIndexOf("Подключено к Minecraft", StringComparison.Ordinal), t.LastIndexOf("Connected to Minecraft", StringComparison.Ordinal));
                int off = Math.Max(t.LastIndexOf("Minecraft недоступен", StringComparison.Ordinal), t.LastIndexOf("Minecraft is not reachable", StringComparison.Ordinal));
                return on >= 0 && on > off;
            }
        }

        public bool InWorld
        {
            get
            {
                if (MinecraftProcess == null) return false;
                if (ManualInWorld) return true;
                string t = Tail(Path.Combine(_c.GameDirEffective, "logs", "latest.log"), 400_000);
                return t.Contains("logged in with entity id");
            }
        }

        public static string Tail(string path, int bytes)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(Math.Max(0, fs.Length - bytes), SeekOrigin.Begin);
                using var r = new StreamReader(fs, Encoding.UTF8);
                return r.ReadToEnd();
            }
            catch { return ""; }
        }

        // ───────────── запуск ─────────────

        public async Task StartAll(WorldChoice world, CancellationToken ct)
        {
            _startedAt = DateTime.Now;
            ManualInWorld = false;
            if (!SlServerFound) { _log(L.T("Не найден SCP:SL Dedicated Server — см. «Подготовку».", "SCP:SL Dedicated Server not found — see “Setup”.")); return; }
            InstallPluginIfNeeded();
            if (ModsDir != "" && !ModInstalled && _c.ModJar != null) { try { InstallMod(); } catch (Exception e) { _log(L.T("Мод: ", "Mod: ") + e.Message); } }

            if (!ServerRunning)
            {
                _log(L.T("Запускаю сервер SCP:SL…", "Starting the SCP:SL server…"));
                Process.Start(new ProcessStartInfo(Path.Combine(_c.SlServerDir, "LocalAdmin.exe"), _c.SlServerPort.ToString())
                { WorkingDirectory = _c.SlServerDir, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized });
            }
            if (!await WaitFor(() => ServerReady, 180, L.T("сервер SL", "SL server"), ct)) return;
            _log(L.T("Сервер SL готов.", "SL server is ready."));

            if (MinecraftProcess == null)
            {
                if (world != null && !string.IsNullOrEmpty(_c.GameDirEffective))
                {
                    WriteLaunchRequest(world);
                    _log(world.Create ? L.T($"Minecraft создаст мир «{world.Name}» и зайдёт в него сам.", $"Minecraft will create the world “{world.Name}” and join it by itself.") : L.T($"Minecraft сам откроет мир «{world.Folder}».", $"Minecraft will open the world “{world.Folder}” by itself."));
                }
                if (_c.McMode == "prism")
                {
                    if (!PrismFound) { _log(L.T("Не найден Prism Launcher — см. «Подготовку».", "Prism Launcher not found — see “Setup”.")); return; }
                    _log(L.T($"Запускаю Minecraft (Prism, сборка «{_c.PrismInstance}»)…", $"Starting Minecraft (Prism, instance “{_c.PrismInstance}”)…"));
                    Process.Start(new ProcessStartInfo(_c.PrismExe, $"-l \"{_c.PrismInstance}\"") { UseShellExecute = true });
                }
                else
                {
                    _log(L.T("Запусти Minecraft 26.3 с Fabric в своём лаунчере. Мир откроется сам; если нет — зайди в мир и нажми «Я зашёл в мир».", "Start Minecraft 26.3 with Fabric in your launcher. The world opens by itself; if not, join a world and press “I'm in the world”."));
                }
            }
            else _log(L.T("Minecraft уже запущен — зайди в мир, если ещё не в нём.", "Minecraft is already running — join a world if you aren't in one yet."));

            if (!await WaitFor(() => InWorld, 900, L.T("вход в мир Minecraft", "joining the Minecraft world"), ct)) return;
            _log(L.T("Minecraft в мире.", "Minecraft is in the world."));
            if (!await WaitFor(() => BridgeConnected, 60, L.T("подключение моста", "bridge connection"), ct)) return;
            _log(L.T("Мост SCP:SL ↔ Minecraft подключён.", "SCP:SL ↔ Minecraft bridge connected."));

            if (!SlClientRunning)
            {
                _log(L.T("Запускаю SCP:SL через Steam…", "Starting SCP:SL via Steam…"));
                Process.Start(new ProcessStartInfo($"steam://run/700330//{_c.SlClientArgs}/") { UseShellExecute = true });
            }
            _log(L.T($"Готово! В SL: Play → Direct connect → 127.0.0.1:{_c.SlServerPort} (адрес в буфере обмена — Ctrl+V).", $"Done! In SL: Play → Direct connect → 127.0.0.1:{_c.SlServerPort} (the address is in your clipboard — Ctrl+V)."));

            // Оверлей — только когда игрок зашёл на сервер (в меню SL он не нужен).
            if (!CompositorRunning)
            {
                _log(L.T("Оверлей включится, когда зайдёшь на сервер.", "The overlay will start once you join the server."));
                while (!PlayerJoined)
                {
                    if (ct.IsCancellationRequested || !ServerRunning) return;
                    await Task.Delay(1000);
                }
                if (File.Exists(_c.CompositorExe))
                {
                    _log(L.T("Ты на сервере — запускаю оверлей…", "You're on the server — starting the overlay…"));
                    Process.Start(new ProcessStartInfo(_c.CompositorExe) { WorkingDirectory = Path.GetDirectoryName(_c.CompositorExe), UseShellExecute = true });
                }
                else _log(L.T("Не найден оверлей: ", "Overlay not found: ") + _c.CompositorExe);
            }
        }

        /// <summary>Игрок зашёл на сервер (строка плагина в логе сервера).</summary>
        public bool PlayerJoined => ServerRunning && ServerLogTail().Contains("[SlBridge] Player joined the server");

        private async Task<bool> WaitFor(Func<bool> cond, int seconds, string what, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                if (ct.IsCancellationRequested) { _log(L.T("Запуск отменён.", "Launch cancelled.")); return false; }
                bool ok;
                try { ok = await Task.Run(cond); } catch { ok = false; }
                if (ok) return true;
                await Task.Delay(1000);
            }
            _log(L.T($"Не дождался: {what} ({seconds} с). Посмотри окно этой части или «Справку».", $"Timed out: {what} ({seconds} s). Check that part's window or the “Help” tab."));
            return false;
        }

        // ───────────── остановка ─────────────

        public async Task StopAll()
        {
            _log(L.T("Останавливаю всё…", "Stopping everything…"));
            foreach (var p in Process.GetProcessesByName("SCPSL").Where(HasWindow)) { try { p.CloseMainWindow(); } catch { } }
            foreach (var p in Process.GetProcessesByName("SlBridgeCompositor")) { try { p.Kill(); } catch { } }
            var mc = MinecraftProcess;
            if (mc != null)
            {
                _log(L.T("Minecraft сохраняет мир и закрывается…", "Minecraft is saving the world and closing…"));
                try { mc.CloseMainWindow(); } catch { }
                for (int i = 0; i < 30 && !mc.HasExited; i++) await Task.Delay(1000);
                if (!mc.HasExited) { try { mc.Kill(true); } catch { } }
            }
            foreach (var p in Process.GetProcessesByName("LocalAdmin")) { try { p.Kill(true); } catch { } }
            await Task.Delay(1500);
            foreach (var p in Process.GetProcessesByName("SCPSL").Where(HasWindow)) { try { if (!p.HasExited) p.Kill(); } catch { } }
            _log(L.T("Всё остановлено.", "Everything stopped."));
        }
    }
}
