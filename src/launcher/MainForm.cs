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
        private readonly Panel _prep, _play, _crafts, _help;
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
            L.En = _cfg.Language == "en";
            _cfg.WriteLanguageFile();
            _orc = new Orchestrator(_cfg, Log);

            Text = "SCP:SL × Minecraft";
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Font = Ui.F(10f);
            FormBorderStyle = FormBorderStyle.None; // своя тёмная полоса сверху вместо белой виндовой
            Padding = new Padding(1); // тонкая рамка по краю
            ClientSize = new Size(942, 682 + TitleBar.H);
            MinimumSize = new Size(942, 682 + TitleBar.H);
            StartPosition = FormStartPosition.CenterScreen;
            try
            {
                using var ico = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico");
                if (ico != null) Icon = new Icon(ico);
            }
            catch { }

            // боковая панель
            var side = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Ui.Side };
            var logo = Ui.Label("SCP:SL\n× Minecraft", 16f, Ui.Text, FontStyle.Bold);
            logo.Location = new Point(18, 18);
            side.Controls.Add(logo);
            _nav = new[] { NavButton(L.T("1  Подготовка", "1  Setup"), 0), NavButton(L.T("2  Играть", "2  Play"), 1), NavButton(L.T("⚒  Крафты", "⚒  Crafting"), 2), NavButton(L.T("?  Справка", "?  Help"), 3) };
            for (int i = 0; i < _nav.Length; i++) { _nav[i].Location = new Point(10, 100 + i * 46); side.Controls.Add(_nav[i]); }
            var ver = Ui.Label(L.T("мост 1.0.0\nClaude x Vamatrik", "bridge 1.0.0\nClaude x Vamatrik"), 8.5f, Ui.Muted);
            ver.Location = new Point(18, 626);
            side.Controls.Add(ver);

            // язык всего мода: лаунчер, плагин SL, мод Minecraft
            var langLbl = Ui.Label("Язык / Language", 8.5f, Ui.Muted);
            langLbl.Location = new Point(18, 560);
            side.Controls.Add(langLbl);
            var lang = Ui.Combo(160);
            lang.Items.AddRange(new object[] { "Русский", "English" });
            lang.SelectedIndex = L.En ? 1 : 0;
            lang.Location = new Point(18, 582);
            lang.SelectedIndexChanged += (_, _) => SetLanguage(lang.SelectedIndex == 1 ? "en" : "ru");
            side.Controls.Add(lang);

            _prep = BuildPrep();
            _play = BuildPlay();
            _crafts = CraftsPage.Build();
            _help = BuildHelp();
            _content.Controls.AddRange(new Control[] { _prep, _play, _crafts, _help });
            Controls.Add(_content);
            Controls.Add(side);
            Controls.Add(new TitleBar(this, Text));

            _timer.Tick += (_, _) => { RefreshStatus(); CheckQuitRequest(); };
            try { if (File.Exists(QuitFile)) File.Delete(QuitFile); } catch { } // старый запрос с прошлого раза
            _timer.Start();
            RefreshPrep();
            bool ready = _orc.SlClientFound && _orc.SlServerFound && _orc.ModInstalled;
            Show(ready ? 1 : 0);
        }

        // окно без рамки: сворачивание по клику на панели задач, тень, перетаскивание за края — как у обычного
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000; // WS_MINIMIZEBOX | WS_SYSMENU
                cp.ClassStyle |= 0x00020000;         // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyQuit) { _ = QuickExit(); return; }
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                const int g = 6;
                bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
                if (t && l) m.Result = (IntPtr)13; else if (t && r) m.Result = (IntPtr)14;
                else if (b && l) m.Result = (IntPtr)16; else if (b && r) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10; else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12; else if (b) m.Result = (IntPtr)15;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(48, 53, 63));
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
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
            _crafts.Visible = page == 2;
            _help.Visible = page == 3;
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
            var h = Ui.Label(L.T("Подготовка (один раз)", "Setup (once)"), 18f, Ui.Text, FontStyle.Bold);
            flow.Controls.Add(h);
            var hint = Ui.Wrap(L.T("Пройди шаги сверху вниз. Лаунчер сам ищет игры; если не нашёл — укажи папку. Зелёная галочка — шаг готов.", "Go through the steps top to bottom. The launcher finds the games by itself; if it doesn't, point it to the folder. A green check means the step is done."), w);
            hint.Margin = new Padding(0, 4, 0, 14);
            flow.Controls.Add(hint);

            _stSl = new StepCard(1, "SCP: Secret Laboratory", L.T("Установи игру в Steam и обнови до последней версии.", "Install the game in Steam and update it to the latest version."), w);
            _stSl.Buttons.Controls.Add(Btn(L.T("Открыть в Steam", "Open in Steam"), () => Open("steam://install/700330")));
            _stSl.Buttons.Controls.Add(Btn(L.T("Указать папку…", "Choose folder…"), () => PickDir(L.T("Папка игры SCP: Secret Laboratory (где SCPSL.exe)", "SCP: Secret Laboratory game folder (with SCPSL.exe)"), d => _cfg.SlClientDir = d)));
            flow.Controls.Add(_stSl);

            _stDs = new StepCard(2, "SCP:SL Dedicated Server",
                L.T("Steam → Библиотека → «Инструменты» → SCP: Secret Laboratory Dedicated Server. Обнови до той же версии, что и игра. ", "Steam → Library → “Tools” → SCP: Secret Laboratory Dedicated Server. Update it to the same version as the game. ") +
                L.T("Потом нажми «Первый запуск сервера», ответь на вопросы в его окне (порт 7777, согласие) и закрой окно, когда появится «Waiting for players».", "Then press “First server run”, answer the questions in its window (port 7777, consent) and close the window once “Waiting for players” appears."), w);
            _stDs.Buttons.Controls.Add(Btn(L.T("Установить в Steam", "Install in Steam"), () => Open("steam://install/996560")));
            _stDs.Buttons.Controls.Add(Btn(L.T("Указать папку…", "Choose folder…"), () => PickDir(L.T("Папка SCP: Secret Laboratory Dedicated Server (где LocalAdmin.exe)", "SCP: Secret Laboratory Dedicated Server folder (with LocalAdmin.exe)"), d => _cfg.SlServerDir = d)));
            _stDs.Buttons.Controls.Add(Btn(L.T("Первый запуск сервера", "First server run"), () => Try(_orc.FirstServerRun)));
            flow.Controls.Add(_stDs);

            _stPlugin = new StepCard(3, L.T("Плагин моста для сервера", "Bridge plugin for the server"), L.T("Ставится в LabAPI автоматически (и обновляется при каждом запуске).", "Installed into LabAPI automatically (and updated on every launch)."), w);
            _stPlugin.Buttons.Controls.Add(Btn(L.T("Установить сейчас", "Install now"), () => Try(_orc.InstallPlugin)));
            flow.Controls.Add(_stPlugin);

            _stMc = new StepCard(4, "Minecraft 26.3 + Fabric",
                L.T("Нужна версия Minecraft 26.3 с загрузчиком Fabric.\n", "You need Minecraft 26.3 with the Fabric loader.\n") +
                L.T("• Prism Launcher (удобнее — лаунчер всё запустит сам): «Добавить сборку» → версия 26.3 → загрузчик Fabric → ОК.\n", "• Prism Launcher (easier — this launcher starts everything itself): “Add Instance” → version 26.3 → Fabric loader → OK.\n") +
                L.T("• Другой лаунчер: установи Fabric Loader для 26.3 (fabricmc.net/use) и укажи папку игры — ту, где лежат mods и saves.", "• Another launcher: install Fabric Loader for 26.3 (fabricmc.net/use) and choose the game folder — the one with mods and saves."), w);
            _modePrism = new RadioButton { Text = "Prism Launcher", AutoSize = true, ForeColor = Ui.Text, Checked = _cfg.McMode == "prism" };
            _modeOther = new RadioButton { Text = L.T("Другой лаунчер (запускаю Minecraft сам)", "Another launcher (I start Minecraft myself)"), AutoSize = true, ForeColor = Ui.Text, Checked = _cfg.McMode != "prism" };
            _modePrism.CheckedChanged += (_, _) => { _cfg.McMode = _modePrism.Checked ? "prism" : "other"; _cfg.Save(); RefreshPrep(); };
            var modeRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent };
            modeRow.Controls.Add(_modePrism);
            modeRow.Controls.Add(_modeOther);
            _stMc.Extra.Controls.Add(modeRow);
            var instRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 0) };
            instRow.Controls.Add(Ui.Label(L.T("Сборка Prism:", "Prism instance:"), 10f, Ui.Muted));
            _instance = Ui.Combo(300);
            _instance.SelectedIndexChanged += (_, _) => { _cfg.PrismInstance = _instance.SelectedItem as string ?? ""; _cfg.Save(); RefreshPrep(false); };
            instRow.Controls.Add(_instance);
            _stMc.Extra.Controls.Add(instRow);
            _gameDirLbl = Ui.Wrap("", w - 40, 9f);
            _stMc.Extra.Controls.Add(_gameDirLbl);
            _stMc.Buttons.Controls.Add(Btn(L.T("Скачать Prism", "Download Prism"), () => Open("https://prismlauncher.org/download/")));
            _stMc.Buttons.Controls.Add(Btn(L.T("Указать Prism…", "Locate Prism…"), () => PickFile("prismlauncher.exe", f => _cfg.PrismExe = f)));
            _stMc.Buttons.Controls.Add(Btn(L.T("Указать папку игры…", "Choose game folder…"), () => PickDir(L.T("Папка игры Minecraft (где mods и saves)", "Minecraft game folder (with mods and saves)"), d => { _cfg.GameDir = d; _cfg.McMode = "other"; })));
            _stMc.Buttons.Controls.Add(Btn(L.T("Скачать Fabric", "Download Fabric"), () => Open("https://fabricmc.net/use/installer/")));
            _stMc.Buttons.Controls.Add(Btn(L.T("Обновить", "Refresh"), () => { _cfg.AutoDetect(); RefreshPrep(); }));
            flow.Controls.Add(_stMc);

            _stMod = new StepCard(5, L.T("Мод моста + Fabric API", "Bridge mod + Fabric API"), L.T("Копирует мод моста и Fabric API в папку mods выбранной сборки/игры.", "Copies the bridge mod and Fabric API into the mods folder of the chosen instance/game."), w);
            _stMod.Buttons.Controls.Add(Btn(L.T("Установить мод", "Install mod"), () => Try(_orc.InstallMod), Ui.Accent));
            _stMod.Buttons.Controls.Add(Btn(L.T("Открыть папку mods", "Open mods folder"), () => { if (Directory.Exists(_orc.ModsDir)) Open(_orc.ModsDir); }));
            // моды оптимизации с Modrinth (ставятся/убираются сразу и проверяются перед каждым запуском)
            var opt = new CheckBox
            {
                Text = L.T("Моды оптимизации: Lithium, FerriteCore, ImmediatelyFast, EntityCulling", "Optimization mods: Lithium, FerriteCore, ImmediatelyFast, EntityCulling"),
                AutoSize = true, ForeColor = Ui.Text, Checked = _cfg.OptMods, Margin = new Padding(0, 2, 0, 2),
            };
            var sod = new CheckBox
            {
                Text = L.T("Sodium — быстрее рисует мир (меняет отрисовку сильнее всех — включай вторым)", "Sodium — faster world rendering (changes rendering the most — enable it second)"),
                AutoSize = true, ForeColor = Ui.Text, Checked = _cfg.SodiumMod, Margin = new Padding(0, 2, 0, 2),
            };
            async void SyncOpt()
            {
                _cfg.OptMods = opt.Checked; _cfg.SodiumMod = sod.Checked; _cfg.Save();
                if (Orchestrator.MinecraftProcess != null) { Log(L.T("Minecraft запущен — моды обновятся при следующем запуске.", "Minecraft is running — mods will update on the next launch.")); return; }
                opt.Enabled = sod.Enabled = false;
                try { await OptimizationMods.Sync(_cfg, _orc.ModsDir, Log); }
                catch (Exception e) { Log(L.T("Ошибка: ", "Error: ") + e.Message); }
                finally { opt.Enabled = sod.Enabled = true; }
            }
            opt.CheckedChanged += (_, _) => SyncOpt();
            sod.CheckedChanged += (_, _) => SyncOpt();
            _stMod.Extra.Controls.Add(opt);
            _stMod.Extra.Controls.Add(sod);
            flow.Controls.Add(_stMod);

            var tip = new StepCard(6, L.T("Если оверлей что-то перекрывает", "If the overlay covers something"),
                L.T("Ctrl+Shift+F12 — выключить/включить оверлей (картинку Minecraft поверх SL).\nCtrl+Shift+F11 — показать маску: белым — где подставляется Minecraft.\nCtrl+Shift+F10 — быстрый выход (закрыть всё, кроме лаунчера).", "Ctrl+Shift+F12 — turn the overlay (Minecraft picture over SL) off/on.\nCtrl+Shift+F11 — show the mask: white is where Minecraft is drawn.\nCtrl+Shift+F10 — quick exit (close everything except the launcher)."), w);
            tip.SetStatus(null, L.T("на заметку", "good to know"));
            flow.Controls.Add(tip);

            var go = Btn(L.T("Всё готово → Играть", "All set → Play"), () => Show(1), Ui.Accent);
            go.Margin = new Padding(0, 6, 0, 0);
            flow.Controls.Add(go);

            page.Controls.Add(flow);
            return page;
        }

        private void RefreshPrep(bool reloadInstances = true)
        {
            _stSl.SetStatus(_orc.SlClientFound, _orc.SlClientFound ? L.T("найдена", "found") : L.T("не найдена", "not found"));
            _stDs.SetStatus(_orc.SlServerFound ? (_orc.ServerConfigured ? true : (bool?)null) : false,
                !_orc.SlServerFound ? L.T("не найден", "not found") : _orc.ServerConfigured ? L.T("найден, настроен", "found, configured") : L.T("найден — сделай первый запуск", "found — do the first run"));
            _stPlugin.SetStatus(_orc.PluginInstalled, _orc.PluginInstalled ? L.T("установлен", "installed") : L.T("не установлен (поставится сам)", "not installed (will install itself)"));

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
                ? (!_orc.PrismFound ? L.T("Prism не найден", "Prism not found") : _instance.Items.Count == 0 ? L.T("нет сборки 26.3 + Fabric", "no 26.3 + Fabric instance") : L.T("сборка «", "instance “") + _cfg.PrismInstance + L.T("»", "”"))
                : (Directory.Exists(_cfg.GameDir ?? "") ? L.T("папка игры выбрана", "game folder chosen") : L.T("укажи папку игры", "choose the game folder"));
            _stMc.SetStatus(mcOk, mcText);
            _gameDirLbl.Text = L.T("Папка игры: ", "Game folder: ") + (string.IsNullOrEmpty(_cfg.GameDirEffective) ? "—" : _cfg.GameDirEffective);
            _stMod.SetStatus(_orc.ModInstalled, _orc.ModInstalled ? L.T("установлены", "installed") : L.T("не установлены", "not installed"));
        }

        // ═════════════ ИГРАТЬ ═════════════

        private Panel BuildPlay()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
            var h = Ui.Label(L.T("Играть", "Play"), 18f, Ui.Text, FontStyle.Bold);
            h.Location = new Point(24, 20);
            page.Controls.Add(h);

            // мир
            var worldCard = new Panel { BackColor = Ui.Card, Location = new Point(24, 70), Size = new Size(330, 520) };
            var wt = Ui.Label(L.T("Мир Minecraft", "Minecraft world"), 12f, Ui.Text, FontStyle.Bold);
            wt.Location = new Point(14, 12);
            _wOpen = new RadioButton { Text = L.T("Открыть мир", "Open world"), AutoSize = true, ForeColor = Ui.Text, Location = new Point(16, 44), Checked = true };
            _wCreate = new RadioButton { Text = L.T("Создать новый", "Create new"), AutoSize = true, ForeColor = Ui.Text, Location = new Point(150, 44) };
            _wOpen.CheckedChanged += (_, _) => { _worlds.Visible = _wOpen.Checked; _createPanel.Visible = !_wOpen.Checked; };
            _worlds = new ListBox { Location = new Point(14, 76), Size = new Size(302, 430), BackColor = Ui.Input, ForeColor = Ui.Text, BorderStyle = BorderStyle.None, Font = Ui.F(10.5f), IntegralHeight = false };

            _createPanel = new Panel { Location = new Point(14, 76), Size = new Size(302, 430), BackColor = Ui.Card, Visible = false };
            int y = 0;
            Control Row(string label, Control c) { var l = Ui.Label(label, 9.5f, Ui.Muted); l.Location = new Point(0, y); c.Location = new Point(0, y + 20); _createPanel.Controls.Add(l); _createPanel.Controls.Add(c); y += 58; return c; }
            _newName = Ui.Box(300); _newName.Text = L.T("Мир SCP", "SCP World");
            Row(L.T("Название", "Name"), _newName);
            _gm = Ui.Combo(300); _gm.Items.AddRange(new object[] { L.T("Выживание", "Survival"), L.T("Творческий", "Creative"), L.T("Хардкор", "Hardcore") }); _gm.SelectedIndex = 0;
            Row(L.T("Режим", "Mode"), _gm);
            _diff = Ui.Combo(300); _diff.Items.AddRange(new object[] { L.T("Мирная", "Peaceful"), L.T("Лёгкая", "Easy"), L.T("Нормальная", "Normal"), L.T("Сложная", "Hard") }); _diff.SelectedIndex = 2;
            Row(L.T("Сложность", "Difficulty"), _diff);
            _type = Ui.Combo(300); _type.Items.AddRange(new object[] { L.T("Обычный", "Default"), L.T("Плоский", "Flat"), L.T("Большие биомы", "Large biomes"), L.T("Усиленный", "Amplified") }); _type.SelectedIndex = 0;
            Row(L.T("Тип мира", "World type"), _type);
            _seed = Ui.Box(300);
            Row(L.T("Сид (необязательно)", "Seed (optional)"), _seed);
            _cheats = new CheckBox { Text = L.T("Читы (команды .mc …)", "Cheats (.mc … commands)"), AutoSize = true, ForeColor = Ui.Text, Checked = true, Location = new Point(0, y + 4) };
            _createPanel.Controls.Add(_cheats);

            worldCard.Controls.AddRange(new Control[] { wt, _wOpen, _wCreate, _worlds, _createPanel });
            page.Controls.Add(worldCard);

            // состояние + кнопки
            var st = new Panel { BackColor = Ui.Card, Location = new Point(370, 70), Size = new Size(340, 176) };
            var stt = Ui.Label(L.T("Состояние", "Status"), 12f, Ui.Text, FontStyle.Bold);
            stt.Location = new Point(14, 10);
            st.Controls.Add(stt);
            _rows = new[] { new StatusRow(L.T("Сервер SL", "SL server")), new StatusRow("Minecraft"), new StatusRow(L.T("Мост", "Bridge")), new StatusRow(L.T("Оверлей", "Overlay")), new StatusRow(L.T("Клиент SL", "SL client")) };
            for (int i = 0; i < _rows.Length; i++) { _rows[i].SetBounds(14, 38 + i * 27, 316, 26); st.Controls.Add(_rows[i]); }
            page.Controls.Add(st);

            _btnPlay = Btn(L.T("▶  Играть", "▶  Play"), async () => await Play(), Ui.Accent);
            _btnPlay.AutoSize = false; _btnPlay.SetBounds(370, 256, 200, 46); _btnPlay.Font = Ui.F(12f, FontStyle.Bold);
            _btnStop = Btn(L.T("■  Стоп", "■  Stop"), async () => await Stop(), Ui.Bad);
            _btnStop.AutoSize = false; _btnStop.SetBounds(578, 256, 132, 46); _btnStop.Font = Ui.F(12f, FontStyle.Bold);
            _btnInWorld = Btn(L.T("Я зашёл в мир", "I'm in the world"), () => { _orc.ManualInWorld = true; Log(L.T("Ок, продолжаю.", "OK, continuing.")); });
            _btnInWorld.AutoSize = false; _btnInWorld.SetBounds(370, 308, 340, 32);
            page.Controls.AddRange(new Control[] { _btnPlay, _btnStop, _btnInWorld });

            _log = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Ui.Card, ForeColor = Ui.Muted,
                BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f), Location = new Point(370, 348), Size = new Size(340, 242),
            };
            page.Controls.Add(_log);

            var links = new FlowLayoutPanel { Location = new Point(24, 600), AutoSize = true, BackColor = Ui.Bg };
            links.Controls.Add(Link(L.T("Папка лаунчера", "Launcher folder"), () => Open(LauncherConfig.BaseDir)));
            links.Controls.Add(Link(L.T("Логи сервера", "Server logs"), () => Open(Path.Combine(LauncherConfig.SlAppData, "LocalAdminLogs"))));
            links.Controls.Add(Link(L.T("Логи Minecraft", "Minecraft logs"), () => Open(Path.Combine(_cfg.GameDirEffective, "logs"))));
            links.Controls.Add(Link(L.T("Настройки плагина", "Plugin settings"), () => Open(Path.Combine(LauncherConfig.SlAppData, "LabAPI", "configs"))));
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
                    Create = true, Name = string.IsNullOrWhiteSpace(_newName.Text) ? L.T("Мир SCP", "SCP World") : _newName.Text.Trim(),
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
            _rows[0].Set(srvReady ? Ui.Ok : srv ? Ui.Warn : Ui.Off, srvReady ? L.T("готов (127.0.0.1:", "ready (127.0.0.1:") + _cfg.SlServerPort + ")" : srv ? L.T("запускается…", "starting…") : L.T("выключен", "off"));
            bool mc = Orchestrator.MinecraftProcess != null, inWorld = mc && _orc.InWorld;
            _rows[1].Set(inWorld ? Ui.Ok : mc ? Ui.Warn : Ui.Off, inWorld ? L.T("в мире", "in the world") : mc ? L.T("загружается / в меню", "loading / in menu") : L.T("выключен", "off"));
            bool bridge = _orc.BridgeConnected;
            _rows[2].Set(bridge ? Ui.Ok : Ui.Off, bridge ? "SCP:SL ↔ Minecraft" : L.T("нет связи", "not connected"));
            bool comp = Orchestrator.CompositorRunning;
            _rows[3].Set(comp ? Ui.Ok : Ui.Off, comp ? L.T("работает (Ctrl+Shift+F12)", "running (Ctrl+Shift+F12)") : L.T("выключен", "off"));
            bool cl = Orchestrator.SlClientRunning;
            _rows[4].Set(cl ? Ui.Ok : Ui.Off, cl ? L.T("запущен", "running") : L.T("выключен", "off"));
            _btnPlay.Enabled = !_busy;
        }

        private async System.Threading.Tasks.Task Play()
        {
            if (_busy) return;
            var world = CurrentWorld();
            if (world == null && Orchestrator.MinecraftProcess == null) { Log(L.T("Выбери мир или «Создать новый».", "Pick a world or “Create new”.")); return; }
            if (!_orc.SlServerFound || (_cfg.McMode == "prism" && !_orc.PrismFound)) { Log(L.T("Не всё готово — открой «Подготовку».", "Not everything is ready — open “Setup”.")); Show(0); return; }
            _busy = true;
            _cts = new CancellationTokenSource();
            try { Clipboard.SetText("127.0.0.1:" + _cfg.SlServerPort); } catch { }
            try { await _orc.StartAll(world, _cts.Token); }
            catch (Exception e) { Log(L.T("Ошибка: ", "Error: ") + e.Message); }
            finally { _busy = false; _cts = null; LoadWorlds(); }
        }

        // ───── быстрый выход: Ctrl+Shift+F10 (глобально) или кнопка «Выйти» в Server-specific SL ─────

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int HotkeyQuit = 0x5110;
        private bool _quitting;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F10
            if (!RegisterHotKey(Handle, HotkeyQuit, 0x0002 | 0x0004 | 0x4000, 0x79)) FileLog("Ctrl+Shift+F10: hotkey is busy");
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotKey(Handle, HotkeyQuit);
            base.OnHandleDestroyed(e);
        }


        private static string QuitFile => Path.Combine(Path.GetDirectoryName(LauncherConfig.LanguageFile), "quit.req");

        /// <summary>Плагин SL кладёт quit.req, когда в игре нажали «Выйти».</summary>
        private void CheckQuitRequest()
        {
            try
            {
                if (!File.Exists(QuitFile)) return;
                File.Delete(QuitFile);
                _ = QuickExit();
            }
            catch { }
        }

        private async System.Threading.Tasks.Task QuickExit()
        {
            if (_quitting) return;
            _quitting = true;
            try
            {
                Log(L.T("Быстрый выход — закрываю SL, Minecraft, сервер и оверлей.", "Quick exit — closing SL, Minecraft, the server and the overlay."));
                await Stop();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            }
            finally { _quitting = false; }
        }

        private async System.Threading.Tasks.Task Stop()
        {
            _cts?.Cancel();
            try { await _orc.StopAll(); }
            catch (Exception e) { Log(L.T("Ошибка: ", "Error: ") + e.Message); }
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
            H(L.T("Что это", "What is this"));
            P(L.T("Две игры работают вместе: Minecraft рисует мир, блоки и мобов, а SCP: Secret Laboratory — твоего персонажа, оружие и интерфейс. ", "Two games run together: Minecraft draws the world, blocks and mobs, and SCP: Secret Laboratory draws your character, weapons and interface. ") +
              L.T("Мост связывает их: ты ходишь по блокам Minecraft с физикой SL, стреляешь — ломаешь блоки и ранишь мобов.", "The bridge connects them: you walk on Minecraft blocks with SL physics, and your shots break blocks and hurt mobs."));
            H(L.T("Управление", "Controls"));
            P(L.T("Ходьба, бег, прыжок, оружие, инвентарь (Tab), админ-панель (M) — как в SCP:SL.\n", "Walking, sprinting, jumping, weapons, inventory (Tab), admin panel (M) — just like in SCP:SL.\n") +
              L.T("Стрельба по блоку — ломает его (камень — несколько попаданий). По мобу — урон.\n", "Shooting a block breaks it (stone takes several hits). Shooting a mob damages it.\n") +
              L.T("Зажать E на предмете на земле — подобрать. Q — выбросить стак.\n", "Hold E on an item on the ground to pick it up. Q drops a stack.\n") +
              L.T("E (взаимодействие Minecraft) — поставить блок из руки, открыть дверь, нажать рычаг, съесть еду, использовать огниво/ведро/око Эндера, открыть верстак/печь/сундук.\n", "E (Minecraft interact) places the held block, opens doors, presses levers, eats food, uses flint and steel/buckets/eyes of ender, opens crafting tables/furnaces/chests.\n") +
              L.T("Оружие, патроны, броню, аптечки и SCP-предметы можно скрафтить — рисунки рецептов во вкладке «⚒ Крафты».\n", "Weapons, ammo, armor, medkits and SCP items can be crafted — recipe drawings are on the “⚒ Crafting” tab.\n") +
              L.T("I — инвентарь Minecraft с крафтом 2×2. В окнах Minecraft предметы таскаются мышкой; закрыть — E или Esc.\n", "I opens the Minecraft inventory with 2×2 crafting. In Minecraft windows you drag items with the mouse; close with E or Esc.\n") +
              L.T("В воде и лаве всплываешь сам; посмотреть вниз (или присесть) — нырнуть. У берега плыви на него — выберешься.\n", "In water and lava you float up by yourself; look down (or crouch) to dive. Swim into the shore to climb out.\n") +
              L.T("Лестница и лианы: смотришь вверх — лезешь, вниз — спускаешься, прямо или присел — висишь.\n", "Ladders and vines: look up to climb, look down to go down, look straight or crouch to hang.\n") +
              L.T("Кровать (E): ночью — сон до утра, в любое время — точка возрождения. Отойдёшь — проснёшься.\n", "Bed (E): at night you sleep until morning, any time it sets your respawn point. Walk away to wake up.\n") +
              L.T("Гранаты и SCP-018 взрывают и мир Minecraft: ломают блоки и ранят мобов.\n", "Grenades and SCP-018 blow up the Minecraft world too: they break blocks and hurt mobs.\n") +
              L.T("F4 — экран отладки Minecraft, F4+B — хитбоксы, F4+G — границы чанков (F3 в SL занят своей отладкой).\n", "F4 — Minecraft debug screen, F4+B — hitboxes, F4+G — chunk borders (F3 is SL's own debug).\n") +
              L.T("Мачете и аптечка выдаются один раз — при первом входе в мир; ещё раз — командой .kit start в консоли (~).\n", "The machete and medkit are given once — on your first time in a world; get them again with .kit start in the console (~).\n") +
              L.T("Сохранять вещи после смерти: .mc gamerule keep_inventory true. Без этого после смерти появляешься с пустыми руками.\n", "Keep items after death: .mc gamerule keep_inventory true. Otherwise you respawn empty-handed.\n") +
              L.T("Клавиши E, I и F4 меняются в SL: Esc → Настройки → Server-specific.\n", "E, I and F4 can be rebound in SL: Esc → Settings → Server-specific.\n") +
              L.T("Консоль SL (~): .mc <команда Minecraft>, например .mc time set day. В админ-панели (M) — просто mc time set day.", "SL console (~): .mc <Minecraft command>, e.g. .mc time set day. In the admin panel (M) — just mc time set day."));
            H(L.T("Оверлей", "Overlay"));
            P(L.T("Ctrl+Shift+F12 — выключить/включить оверлей (если что-то перекрывает или нужно увидеть «голый» SL).\n", "Ctrl+Shift+F12 — turn the overlay off/on (if something is covered or you want to see “bare” SL).\n") +
              L.T("Ctrl+Shift+F11 — показать маску (белое — где подставляется Minecraft).\n" +
              "Ctrl+Shift+F10 — быстрый выход: закрыть SL, Minecraft (с сохранением), сервер и оверлей. То же — кнопка «Выйти» в SL: Esc → Настройки → Server-specific.\n", "Ctrl+Shift+F11 — show the mask (white is where Minecraft is drawn).\n" +
              "Ctrl+Shift+F10 — quick exit: close SL, Minecraft (saving the world), the server and the overlay. Same as the “Quit” button in SL: Esc → Settings → Server-specific.\n") +
              L.T("SL должен быть в оконном режиме или «без рамки», не в эксклюзивном полноэкранном.", "SL must run windowed or borderless, not in exclusive fullscreen."));
            H(L.T("Если что-то не так", "Troubleshooting"));
            P(L.T("• Сервер не стартует — сделай «Первый запуск сервера» в «Подготовке» и ответь на его вопросы.\n", "• The server doesn't start — do “First server run” in “Setup” and answer its questions.\n") +
              L.T("• Minecraft не заходит в мир сам — зайди вручную и нажми «Я зашёл в мир».\n", "• Minecraft doesn't join the world by itself — join manually and press “I'm in the world”.\n") +
              L.T("• Нет связи моста — проверь, что мод лежит в mods той же сборки, что запущена, и что в Minecraft открыт мир.\n", "• No bridge connection — check that the mod is in the mods folder of the instance you started and that a world is open in Minecraft.\n") +
              L.T("• Версии SCP:SL и Dedicated Server должны совпадать — обнови обе в Steam.\n", "• SCP:SL and the Dedicated Server must be the same version — update both in Steam.\n") +
              L.T("• Тормозит — уменьши дальность прорисовки в Minecraft (8–10 чанков).\n", "• Lagging — lower the render distance in Minecraft (8–10 chunks).\n") +
              L.T("• Логи: ссылки внизу вкладки «Играть». Их можно отправить автору моста.", "• Logs: links at the bottom of the “Play” tab. You can send them to the bridge author."));
            page.Controls.Add(box);
            return page;
        }

        private void SetLanguage(string code)
        {
            if (_cfg.Language == code) return;
            _cfg.Language = code;
            _cfg.Save();
            _cfg.WriteLanguageFile();
            bool mcRunning = Orchestrator.MinecraftProcess != null;
            if (!mcRunning) _cfg.WriteMinecraftLanguage();
            L.En = code == "en";
            MessageBox.Show(this,
                L.T("Язык сменится после перезапуска лаунчера. Плагин SL подхватит его при следующем запуске сервера, мод Minecraft — сразу.",
                    "The language changes after the launcher restarts. The SL plugin picks it up on the next server start, the Minecraft mod right away.")
                + (mcRunning ? "\n\n" + L.T("Minecraft сейчас запущен — язык самой игры поменяй в её настройках.", "Minecraft is running now — change the game's own language in its settings.") : ""),
                "SCP:SL × Minecraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Restart();
            Environment.Exit(0);
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
            try { a(); } catch (Exception e) { Log(L.T("Ошибка: ", "Error: ") + e.Message); MessageBox.Show(this, e.Message, "SCP:SL × Minecraft", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            using var d = new OpenFileDialog { Filter = name + "|" + name, Title = L.T("Укажи ", "Locate ") + name };
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
            Application.ThreadException += (_, e) => FileLog(L.T("Ошибка: ", "Error: ") + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => FileLog(L.T("Ошибка: ", "Error: ") + e.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => { FileLog("Ошибка задачи: " + e.Exception); e.SetObserved(); };
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new MainForm());
        }
    }
}
