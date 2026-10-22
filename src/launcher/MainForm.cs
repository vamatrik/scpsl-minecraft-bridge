using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SlBridge.Launcher
{
    public sealed class MainForm : Form
    {
        private readonly LauncherConfig _cfg;
        private readonly Orchestrator _orc;
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
        private readonly Panel _prep, _play, _help;
        private readonly Button[] _nav;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };

        // подготовка
        private StepCard _stSl, _stDs, _stPlugin, _stMc, _stMod;
        private RadioButton _modePrism, _modeOther;
        private ComboBox _instance;
        private Label _gameDirLbl;

        // игра
        private RadioButton _wOpen, _wCreate;
        private ListBox _worlds;
        private Panel _createPanel;
        private TextBox _newName, _seed;
        private ComboBox _gm, _diff, _type;
        private CheckBox _cheats;
        private StatusRow[] _rows;
        private Button _btnPlay, _btnStop, _btnInWorld;
        private TextBox _log;
        private CancellationTokenSource _cts;
        private bool _busy;
        private string[] _worldFolders = Array.Empty<string>();

        public MainForm()
        {
            _cfg = LauncherConfig.Load();
            _orc = new Orchestrator(_cfg, Log);

            Text = "SCP:SL × Minecraft";
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Font = Ui.F(10f);
            ClientSize = new Size(940, 680);
            MinimumSize = new Size(940, 680);
            StartPosition = FormStartPosition.CenterScreen;

            // боковая панель
            var side = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Ui.Side };
            var logo = Ui.Label("SCP:SL\n× Minecraft", 16f, Ui.Text, FontStyle.Bold);
            logo.Location = new Point(18, 18);
            side.Controls.Add(logo);
            _nav = new[] { NavButton("1  Подготовка", 0), NavButton("2  Играть", 1), NavButton("?  Справка", 2) };
            for (int i = 0; i < _nav.Length; i++) { _nav[i].Location = new Point(10, 100 + i * 46); side.Controls.Add(_nav[i]); }
            var ver = Ui.Label("мост 0.3.1\nClaude x Vamatrik", 8.5f, Ui.Muted);
            ver.Location = new Point(18, 626);
            side.Controls.Add(ver);

            _prep = BuildPrep();
            _play = BuildPlay();
            _help = BuildHelp();
            _content.Controls.AddRange(new Control[] { _prep, _play, _help });
            Controls.Add(_content);
            Controls.Add(side);

            _timer.Tick += (_, _) => RefreshStatus();
            _timer.Start();
            RefreshPrep();
            bool ready = _orc.SlClientFound && _orc.SlServerFound && _orc.ModInstalled;
            Show(ready ? 1 : 0);
        }

        private Button NavButton(string text, int page)
        {
            var b = new Button
            {
                Text = text, Width = 180, Height = 40, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Ui.Text, BackColor = Ui.Side, Font = Ui.F(11f, FontStyle.Bold), Cursor = Cursors.Hand, Padding = new Padding(10, 0, 0, 0),
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => Show(page);
            return b;
        }

        private void Show(int page)
        {
            _prep.Visible = page == 0;
            _play.Visible = page == 1;
            _help.Visible = page == 2;
            for (int i = 0; i < _nav.Length; i++) _nav[i].BackColor = i == page ? Ui.Card : Ui.Side;
            if (page == 0) RefreshPrep();
            if (page == 1) LoadWorlds();
        }

        // ═════════════ ПОДГОТОВКА ═════════════

        private Panel BuildPrep()
        {
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg, Padding = new Padding(24, 20, 24, 20) };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Ui.Bg, Location = new Point(24, 20) };
            int w = 680;
            var h = Ui.Label("Подготовка (один раз)", 18f, Ui.Text, FontStyle.Bold);
            flow.Controls.Add(h);
            var hint = Ui.Wrap("Пройди шаги сверху вниз. Лаунчер сам ищет игры; если не нашёл — укажи папку. Зелёная галочка — шаг готов.", w);
            hint.Margin = new Padding(0, 4, 0, 14);
            flow.Controls.Add(hint);

            _stSl = new StepCard(1, "SCP: Secret Laboratory", "Установи игру в Steam и обнови до последней версии.", w);
            _stSl.Buttons.Controls.Add(Btn("Открыть в Steam", () => Open("steam://install/700330")));
            _stSl.Buttons.Controls.Add(Btn("Указать папку…", () => PickDir("Папка игры SCP: Secret Laboratory (где SCPSL.exe)", d => _cfg.SlClientDir = d)));
            flow.Controls.Add(_stSl);

            _stDs = new StepCard(2, "SCP:SL Dedicated Server",
                "Steam → Библиотека → «Инструменты» → SCP: Secret Laboratory Dedicated Server. Обнови до той же версии, что и игра. " +
                "Потом нажми «Первый запуск сервера», ответь на вопросы в его окне (порт 7777, согласие) и закрой окно, когда появится «Waiting for players».", w);
            _stDs.Buttons.Controls.Add(Btn("Установить в Steam", () => Open("steam://install/996560")));
            _stDs.Buttons.Controls.Add(Btn("Указать папку…", () => PickDir("Папка SCP: Secret Laboratory Dedicated Server (где LocalAdmin.exe)", d => _cfg.SlServerDir = d)));
            _stDs.Buttons.Controls.Add(Btn("Первый запуск сервера", () => Try(_orc.FirstServerRun)));
            flow.Controls.Add(_stDs);

            _stPlugin = new StepCard(3, "Плагин моста для сервера", "Ставится в LabAPI автоматически (и обновляется при каждом запуске).", w);
            _stPlugin.Buttons.Controls.Add(Btn("Установить сейчас", () => Try(_orc.InstallPlugin)));
            flow.Controls.Add(_stPlugin);

            _stMc = new StepCard(4, "Minecraft 26.3 + Fabric",
                "Нужна версия Minecraft 26.3 с загрузчиком Fabric.\n" +
                "• Prism Launcher (удобнее — лаунчер всё запустит сам): «Добавить сборку» → версия 26.3 → загрузчик Fabric → ОК.\n" +
                "• Другой лаунчер: установи Fabric Loader для 26.3 (fabricmc.net/use) и укажи папку игры — ту, где лежат mods и saves.", w);
            _modePrism = new RadioButton { Text = "Prism Launcher", AutoSize = true, ForeColor = Ui.Text, Checked = _cfg.McMode == "prism" };
            _modeOther = new RadioButton { Text = "Другой лаунчер (запускаю Minecraft сам)", AutoSize = true, ForeColor = Ui.Text, Checked = _cfg.McMode != "prism" };
            _modePrism.CheckedChanged += (_, _) => { _cfg.McMode = _modePrism.Checked ? "prism" : "other"; _cfg.Save(); RefreshPrep(); };
            var modeRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent };
            modeRow.Controls.Add(_modePrism);
            modeRow.Controls.Add(_modeOther);
            _stMc.Extra.Controls.Add(modeRow);
            var instRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 0) };
            instRow.Controls.Add(Ui.Label("Сборка Prism:", 10f, Ui.Muted));
            _instance = Ui.Combo(300);
            _instance.SelectedIndexChanged += (_, _) => { _cfg.PrismInstance = _instance.SelectedItem as string ?? ""; _cfg.Save(); RefreshPrep(false); };
            instRow.Controls.Add(_instance);
            _stMc.Extra.Controls.Add(instRow);
            _gameDirLbl = Ui.Wrap("", w - 40, 9f);
            _stMc.Extra.Controls.Add(_gameDirLbl);
            _stMc.Buttons.Controls.Add(Btn("Скачать Prism", () => Open("https://prismlauncher.org/download/")));
            _stMc.Buttons.Controls.Add(Btn("Указать Prism…", () => PickFile("prismlauncher.exe", f => _cfg.PrismExe = f)));
            _stMc.Buttons.Controls.Add(Btn("Указать папку игры…", () => PickDir("Папка игры Minecraft (где mods и saves)", d => { _cfg.GameDir = d; _cfg.McMode = "other"; })));
            _stMc.Buttons.Controls.Add(Btn("Скачать Fabric", () => Open("https://fabricmc.net/use/installer/")));
            _stMc.Buttons.Controls.Add(Btn("Обновить", () => { _cfg.AutoDetect(); RefreshPrep(); }));
            flow.Controls.Add(_stMc);

            _stMod = new StepCard(5, "Мод моста + Fabric API", "Копирует мод моста и Fabric API в папку mods выбранной сборки/игры.", w);
            _stMod.Buttons.Controls.Add(Btn("Установить мод", () => Try(_orc.InstallMod), Ui.Accent));
            _stMod.Buttons.Controls.Add(Btn("Открыть папку mods", () => { if (Directory.Exists(_orc.ModsDir)) Open(_orc.ModsDir); }));
            flow.Controls.Add(_stMod);

            var tip = new StepCard(6, "Если оверлей что-то перекрывает",
                "Ctrl+Shift+F12 — выключить/включить оверлей (картинку Minecraft поверх SL).\nCtrl+Shift+F11 — показать маску: белым — где подставляется Minecraft.", w);
            tip.SetStatus(null, "на заметку");
            flow.Controls.Add(tip);

            var go = Btn("Всё готово → Играть", () => Show(1), Ui.Accent);
            go.Margin = new Padding(0, 6, 0, 0);
            flow.Controls.Add(go);

            page.Controls.Add(flow);
            return page;
        }

        private void RefreshPrep(bool reloadInstances = true)
        {
            _stSl.SetStatus(_orc.SlClientFound, _orc.SlClientFound ? "найдена" : "не найдена");
            _stDs.SetStatus(_orc.SlServerFound ? (_orc.ServerConfigured ? true : (bool?)null) : false,
                !_orc.SlServerFound ? "не найден" : _orc.ServerConfigured ? "найден, настроен" : "найден — сделай первый запуск");
            _stPlugin.SetStatus(_orc.PluginInstalled, _orc.PluginInstalled ? "установлен" : "не установлен (поставится сам)");

            if (reloadInstances)
            {
                var list = _cfg.PrismFabricInstances();
                _instance.Items.Clear();
                foreach (var i in list) _instance.Items.Add(i);
                int idx = Array.IndexOf(list, _cfg.PrismInstance);
                if (idx < 0 && list.Length > 0) { idx = 0; _cfg.PrismInstance = list[0]; _cfg.Save(); }
                if (idx >= 0) _instance.SelectedIndex = idx;
            }
            bool prism = _cfg.McMode == "prism";
            _instance.Enabled = prism;
            bool mcOk = prism ? (_orc.PrismFound && !string.IsNullOrEmpty(_cfg.PrismInstance)) : Directory.Exists(_cfg.GameDir ?? "");
            string mcText = prism
                ? (!_orc.PrismFound ? "Prism не найден" : _instance.Items.Count == 0 ? "нет сборки 26.3 + Fabric" : "сборка «" + _cfg.PrismInstance + "»")
                : (Directory.Exists(_cfg.GameDir ?? "") ? "папка игры выбрана" : "укажи папку игры");
            _stMc.SetStatus(mcOk, mcText);
            _gameDirLbl.Text = "Папка игры: " + (string.IsNullOrEmpty(_cfg.GameDirEffective) ? "—" : _cfg.GameDirEffective);
            _stMod.SetStatus(_orc.ModInstalled, _orc.ModInstalled ? "установлены" : "не установлены");
        }

        // ═════════════ ИГРАТЬ ═════════════

        private Panel BuildPlay()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
            var h = Ui.Label("Играть", 18f, Ui.Text, FontStyle.Bold);
            h.Location = new Point(24, 20);
            page.Controls.Add(h);

            // мир
            var worldCard = new Panel { BackColor = Ui.Card, Location = new Point(24, 70), Size = new Size(330, 520) };
            var wt = Ui.Label("Мир Minecraft", 12f, Ui.Text, FontStyle.Bold);
            wt.Location = new Point(14, 12);
            _wOpen = new RadioButton { Text = "Открыть мир", AutoSize = true, ForeColor = Ui.Text, Location = new Point(16, 44), Checked = true };
            _wCreate = new RadioButton { Text = "Создать новый", AutoSize = true, ForeColor = Ui.Text, Location = new Point(150, 44) };
            _wOpen.CheckedChanged += (_, _) => { _worlds.Visible = _wOpen.Checked; _createPanel.Visible = !_wOpen.Checked; };
            _worlds = new ListBox { Location = new Point(14, 76), Size = new Size(302, 430), BackColor = Ui.Input, ForeColor = Ui.Text, BorderStyle = BorderStyle.None, Font = Ui.F(10.5f), IntegralHeight = false };

            _createPanel = new Panel { Location = new Point(14, 76), Size = new Size(302, 430), BackColor = Ui.Card, Visible = false };
            int y = 0;
            Control Row(string label, Control c) { var l = Ui.Label(label, 9.5f, Ui.Muted); l.Location = new Point(0, y); c.Location = new Point(0, y + 20); _createPanel.Controls.Add(l); _createPanel.Controls.Add(c); y += 58; return c; }
            _newName = Ui.Box(300); _newName.Text = "Мир SCP";
            Row("Название", _newName);
            _gm = Ui.Combo(300); _gm.Items.AddRange(new object[] { "Выживание", "Творческий", "Хардкор" }); _gm.SelectedIndex = 0;
            Row("Режим", _gm);
            _diff = Ui.Combo(300); _diff.Items.AddRange(new object[] { "Мирная", "Лёгкая", "Нормальная", "Сложная" }); _diff.SelectedIndex = 2;
            Row("Сложность", _diff);
            _type = Ui.Combo(300); _type.Items.AddRange(new object[] { "Обычный", "Плоский", "Большие биомы", "Усиленный" }); _type.SelectedIndex = 0;
            Row("Тип мира", _type);
            _seed = Ui.Box(300);
            Row("Сид (необязательно)", _seed);
            _cheats = new CheckBox { Text = "Читы (команды .mc …)", AutoSize = true, ForeColor = Ui.Text, Checked = true, Location = new Point(0, y + 4) };
            _createPanel.Controls.Add(_cheats);

            worldCard.Controls.AddRange(new Control[] { wt, _wOpen, _wCreate, _worlds, _createPanel });
            page.Controls.Add(worldCard);

            // состояние + кнопки
            var st = new Panel { BackColor = Ui.Card, Location = new Point(370, 70), Size = new Size(340, 176) };
            var stt = Ui.Label("Состояние", 12f, Ui.Text, FontStyle.Bold);
            stt.Location = new Point(14, 10);
            st.Controls.Add(stt);
            _rows = new[] { new StatusRow("Сервер SL"), new StatusRow("Minecraft"), new StatusRow("Мост"), new StatusRow("Оверлей"), new StatusRow("Клиент SL") };
            for (int i = 0; i < _rows.Length; i++) { _rows[i].SetBounds(14, 38 + i * 27, 316, 26); st.Controls.Add(_rows[i]); }
            page.Controls.Add(st);

            _btnPlay = Btn("▶  Играть", async () => await Play(), Ui.Accent);
            _btnPlay.AutoSize = false; _btnPlay.SetBounds(370, 256, 200, 46); _btnPlay.Font = Ui.F(12f, FontStyle.Bold);
            _btnStop = Btn("■  Стоп", async () => await Stop(), Ui.Bad);
            _btnStop.AutoSize = false; _btnStop.SetBounds(578, 256, 132, 46); _btnStop.Font = Ui.F(12f, FontStyle.Bold);
            _btnInWorld = Btn("Я зашёл в мир", () => { _orc.ManualInWorld = true; Log("Ок, продолжаю."); });
            _btnInWorld.AutoSize = false; _btnInWorld.SetBounds(370, 308, 340, 32);
            page.Controls.AddRange(new Control[] { _btnPlay, _btnStop, _btnInWorld });

            _log = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Ui.Card, ForeColor = Ui.Muted,
                BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f), Location = new Point(370, 348), Size = new Size(340, 242),
            };
            page.Controls.Add(_log);

            var links = new FlowLayoutPanel { Location = new Point(24, 600), AutoSize = true, BackColor = Ui.Bg };
            links.Controls.Add(Link("Папка лаунчера", () => Open(LauncherConfig.BaseDir)));
            links.Controls.Add(Link("Логи сервера", () => Open(Path.Combine(LauncherConfig.SlAppData, "LocalAdminLogs"))));
            links.Controls.Add(Link("Логи Minecraft", () => Open(Path.Combine(_cfg.GameDirEffective, "logs"))));
            links.Controls.Add(Link("Настройки плагина", () => Open(Path.Combine(LauncherConfig.SlAppData, "LabAPI", "configs"))));
            page.Controls.Add(links);
            return page;
        }

        private void LoadWorlds()
        {
            string keep = _worlds.SelectedIndex >= 0 && _worldFolders.Length > _worlds.SelectedIndex ? _worldFolders[_worlds.SelectedIndex] : _cfg.LastWorld;
            _worlds.Items.Clear();
            var recent = _orc.Recent();
            var all = _orc.Worlds();
            var folders = new System.Collections.Generic.List<string>();
            foreach (var r in recent) { _worlds.Items.Add("★ " + r.Name + (r.Name != r.Folder ? "  (" + r.Folder + ")" : "")); folders.Add(r.Folder); }
            foreach (var f in all.Where(f => !recent.Any(r => r.Folder == f))) { _worlds.Items.Add("   " + f); folders.Add(f); }
            _worldFolders = folders.ToArray();
            int idx = Array.IndexOf(_worldFolders, keep);
            if (idx < 0 && _worldFolders.Length > 0) idx = 0;
            if (idx >= 0) _worlds.SelectedIndex = idx;
            if (_worldFolders.Length == 0) { _wCreate.Checked = true; }
            _btnInWorld.Visible = _cfg.McMode != "prism";
        }

        private WorldChoice CurrentWorld()
        {
            if (_wCreate.Checked)
            {
                string[] modes = { "survival", "creative", "hardcore" };
                string[] diffs = { "peaceful", "easy", "normal", "hard" };
                string[] types = { "normal", "flat", "large_biomes", "amplified" };
                return new WorldChoice
                {
                    Create = true, Name = string.IsNullOrWhiteSpace(_newName.Text) ? "Мир SCP" : _newName.Text.Trim(),
                    Mode = modes[_gm.SelectedIndex], Difficulty = diffs[_diff.SelectedIndex], Type = types[_type.SelectedIndex],
                    Cheats = _cheats.Checked, Seed = _seed.Text.Trim(),
                };
            }
            if (_worlds.SelectedIndex < 0 || _worlds.SelectedIndex >= _worldFolders.Length) return null;
            string folder = _worldFolders[_worlds.SelectedIndex];
            _cfg.LastWorld = folder;
            _cfg.Save();
            return new WorldChoice { Folder = folder };
        }

        private void RefreshStatus()
        {
            try { RefreshStatusCore(); }
            catch (Exception e) { FileLog("Статус: " + e); }
        }

        private void RefreshStatusCore()
        {
            if (!_play.Visible) return;
            bool srv = Orchestrator.ServerRunning, srvReady = srv && _orc.ServerReady;
            _rows[0].Set(srvReady ? Ui.Ok : srv ? Ui.Warn : Ui.Off, srvReady ? "готов (127.0.0.1:" + _cfg.SlServerPort + ")" : srv ? "запускается…" : "выключен");
            bool mc = Orchestrator.MinecraftProcess != null, inWorld = mc && _orc.InWorld;
            _rows[1].Set(inWorld ? Ui.Ok : mc ? Ui.Warn : Ui.Off, inWorld ? "в мире" : mc ? "загружается / в меню" : "выключен");
            bool bridge = _orc.BridgeConnected;
            _rows[2].Set(bridge ? Ui.Ok : Ui.Off, bridge ? "SCP:SL ↔ Minecraft" : "нет связи");
            bool comp = Orchestrator.CompositorRunning;
            _rows[3].Set(comp ? Ui.Ok : Ui.Off, comp ? "работает (Ctrl+Shift+F12)" : "выключен");
            bool cl = Orchestrator.SlClientRunning;
            _rows[4].Set(cl ? Ui.Ok : Ui.Off, cl ? "запущен" : "выключен");
            _btnPlay.Enabled = !_busy;
        }

        private async System.Threading.Tasks.Task Play()
        {
            if (_busy) return;
            var world = CurrentWorld();
            if (world == null && Orchestrator.MinecraftProcess == null) { Log("Выбери мир или «Создать новый»."); return; }
            if (!_orc.SlServerFound || (_cfg.McMode == "prism" && !_orc.PrismFound)) { Log("Не всё готово — открой «Подготовку»."); Show(0); return; }
            _busy = true;
            _cts = new CancellationTokenSource();
            try { Clipboard.SetText("127.0.0.1:" + _cfg.SlServerPort); } catch { }
            try { await _orc.StartAll(world, _cts.Token); }
            catch (Exception e) { Log("Ошибка: " + e.Message); }
            finally { _busy = false; _cts = null; LoadWorlds(); }
        }

        private async System.Threading.Tasks.Task Stop()
        {
            _cts?.Cancel();
            try { await _orc.StopAll(); }
            catch (Exception e) { Log("Ошибка: " + e.Message); }
        }

        // ═════════════ СПРАВКА ═════════════

        private Panel BuildHelp()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(24, 20, 24, 20) };
            var box = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Ui.Bg, ForeColor = Ui.Text,
                Font = Ui.F(10.5f), DetectUrls = true,
            };
            void H(string s) { box.SelectionFont = Ui.F(14f, FontStyle.Bold); box.SelectionColor = Ui.Text; box.AppendText(s + "\n"); }
            void P(string s) { box.SelectionFont = Ui.F(10.5f); box.SelectionColor = Ui.Muted; box.AppendText(s + "\n\n"); }
            H("Что это");
            P("Две игры работают вместе: Minecraft рисует мир, блоки и мобов, а SCP: Secret Laboratory — твоего персонажа, оружие и интерфейс. " +
              "Мост связывает их: ты ходишь по блокам Minecraft с физикой SL, стреляешь — ломаешь блоки и ранишь мобов.");
            H("Управление");
            P("Ходьба, бег, прыжок, оружие, инвентарь (Tab), админ-панель (M) — как в SCP:SL.\n" +
              "Стрельба по блоку — ломает его (камень — несколько попаданий). По мобу — урон.\n" +
              "Зажать E на предмете на земле — подобрать. Q — выбросить стак.\n" +
              "E (взаимодействие Minecraft) — поставить блок из руки, открыть дверь, нажать рычаг, съесть еду, использовать огниво/ведро/око Эндера, открыть верстак/печь/сундук.\n" +
              "I — инвентарь Minecraft с крафтом 2×2. В окнах Minecraft предметы таскаются мышкой; закрыть — E или Esc.\n" +
              "Клавиши E и I меняются в SL: Esc → Настройки → Server-specific.\n" +
              "Консоль SL (~): .mc <команда Minecraft>, например .mc time set day. В админ-панели (M) — просто mc time set day.");
            H("Оверлей");
            P("Ctrl+Shift+F12 — выключить/включить оверлей (если что-то перекрывает или нужно увидеть «голый» SL).\n" +
              "Ctrl+Shift+F11 — показать маску (белое — где подставляется Minecraft).\n" +
              "SL должен быть в оконном режиме или «без рамки», не в эксклюзивном полноэкранном.");
            H("Если что-то не так");
            P("• Сервер не стартует — сделай «Первый запуск сервера» в «Подготовке» и ответь на его вопросы.\n" +
              "• Minecraft не заходит в мир сам — зайди вручную и нажми «Я зашёл в мир».\n" +
              "• Нет связи моста — проверь, что мод лежит в mods той же сборки, что запущена, и что в Minecraft открыт мир.\n" +
              "• Версии SCP:SL и Dedicated Server должны совпадать — обнови обе в Steam.\n" +
              "• Тормозит — уменьши дальность прорисовки в Minecraft (8–10 чанков).\n" +
              "• Логи: ссылки внизу вкладки «Играть». Их можно отправить автору моста.");
            page.Controls.Add(box);
            return page;
        }

        // ═════════════ мелочи ═════════════

        private Button Btn(string text, Action onClick, Color? color = null)
        {
            var b = Ui.Button(text, color);
            b.Click += (_, _) => onClick();
            return b;
        }

        private static LinkLabel Link(string text, Action onClick)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, LinkColor = Ui.Muted, ActiveLinkColor = Ui.Text, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, 18, 0) };
            l.Click += (_, _) => onClick();
            return l;
        }

        private void Try(Action a)
        {
            try { a(); } catch (Exception e) { Log("Ошибка: " + e.Message); MessageBox.Show(this, e.Message, "SCP:SL × Minecraft", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            RefreshPrep(false);
        }

        private void PickDir(string title, Action<string> set)
        {
            using var d = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            set(d.SelectedPath);
            _cfg.Save();
            RefreshPrep();
        }

        private void PickFile(string name, Action<string> set)
        {
            using var d = new OpenFileDialog { Filter = name + "|" + name, Title = "Укажи " + name };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            set(d.FileName);
            _cfg.Save();
            RefreshPrep();
        }

        private static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        private void Log(string s)
        {
            FileLog(s);
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(() => Log2(s))); } catch { } return; }
            Log2(s);
        }

        private void Log2(string s)
        {
            if (_log == null || _log.IsDisposed) return;
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
        }

        /// <summary>launcher.log рядом с exe — туда же падают все неожиданные ошибки (без всплывающих окон).</summary>
        public static void FileLog(string s)
        {
            try { File.AppendAllText(Path.Combine(LauncherConfig.BaseDir, "launcher.log"), $"[{DateTime.Now:dd.MM HH:mm:ss}] {s}\r\n"); } catch { }
        }

        [STAThread]
        private static void Main()
        {
            // Никаких всплывающих окон с ошибками: всё неожиданное — в launcher.log.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => FileLog("Ошибка: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => FileLog("Ошибка: " + e.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => { FileLog("Ошибка задачи: " + e.Exception); e.SetObserved(); };
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new MainForm());
        }
    }
}
