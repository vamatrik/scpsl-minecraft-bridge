using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace SlBridge.Launcher
{
    /// <summary>
    /// Вкладка «Настройки»: главное из трёх конфигов в одном окне — плагин SL (config.yml), оверлей (compositor.json)
    /// и мод Minecraft (slbridge.properties). Меняются только перечисленные строки, остальное в файлах не трогается.
    /// </summary>
    internal static class ConfigPage
    {
        private enum Src { Plugin, Overlay, Mod }
        private enum Kind { Int, Float, Bool, Choice }

        private sealed class Field
        {
            public Src Src; public string Key, Ru, En, HintRu, HintEn; public Kind Kind;
            public decimal Min, Max, Step; public int Dec;
            public (string Value, string Ru, string En)[] Options;
            public Control Ctl;
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static LauncherConfig _cfg;
        private static readonly List<Field> Fields = new List<Field>();
        private static Label _status;

        private static string PluginFile => Path.Combine(LauncherConfig.SlAppData, "LabAPI", "configs", _cfg.SlServerPort.ToString(), "SlBridge", "config.yml");
        private static string OverlayFile => Path.Combine(_cfg.DistDir, "compositor", "compositor.json");
        private static string ModFile => string.IsNullOrEmpty(_cfg.GameDirEffective) ? "" : Path.Combine(_cfg.GameDirEffective, "config", "slbridge.properties");

        private static Field Num(Src s, string key, string ru, string en, decimal min, decimal max, decimal step, int dec, string hru = "", string hen = "")
            => new Field { Src = s, Key = key, Ru = ru, En = en, Kind = dec == 0 ? Kind.Int : Kind.Float, Min = min, Max = max, Step = step, Dec = dec, HintRu = hru, HintEn = hen };
        private static Field Flag(Src s, string key, string ru, string en, string hru = "", string hen = "")
            => new Field { Src = s, Key = key, Ru = ru, En = en, Kind = Kind.Bool, HintRu = hru, HintEn = hen };
        private static Field Pick(Src s, string key, string ru, string en, (string, string, string)[] opts, string hru = "", string hen = "")
            => new Field { Src = s, Key = key, Ru = ru, En = en, Kind = Kind.Choice, Options = opts, HintRu = hru, HintEn = hen };

        private static readonly (string, string, string)[] Roles =
        {
            ("ClassD", "Класс D (по умолчанию)", "Class-D (default)"),
            ("Tutorial", "Обучение", "Tutorial"),
            ("Scientist", "Научный сотрудник", "Scientist"),
            ("FacilityGuard", "Охрана комплекса", "Facility Guard"),
            ("NtfPrivate", "МОГ: рядовой", "MTF Private"),
            ("NtfSergeant", "МОГ: сержант", "MTF Sergeant"),
            ("NtfSpecialist", "МОГ: специалист", "MTF Specialist"),
            ("NtfCaptain", "МОГ: капитан", "MTF Captain"),
            ("ChaosConscript", "Повстанцы Хаоса: новобранец ⚠", "Chaos Insurgency: Conscript ⚠"),
            ("ChaosRifleman", "Повстанцы Хаоса: стрелок ⚠", "Chaos Insurgency: Rifleman ⚠"),
            ("ChaosMarauder", "Повстанцы Хаоса: мародёр ⚠", "Chaos Insurgency: Marauder ⚠"),
            ("ChaosRepressor", "Повстанцы Хаоса: подавитель ⚠", "Chaos Insurgency: Repressor ⚠"),
        };

        private static readonly (string, string, string)[] Tiers =
        {
            ("wood", "деревянный", "wooden"), ("stone", "каменный", "stone"), ("copper", "медный", "copper"),
            ("iron", "железный", "iron"), ("gold", "золотой", "golden"), ("diamond", "алмазный", "diamond"),
            ("netherite", "незеритовый", "netherite"), ("", "обычное мачете SCP-1509", "classic SCP-1509 machete"),
        };

        public static Panel Build(LauncherConfig cfg)
        {
            _cfg = cfg;
            Fields.Clear();
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg, Padding = new Padding(24, 20, 24, 20),
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
            };
            const int w = 680;
            flow.Controls.Add(Ui.Label(L.T("Настройки", "Settings"), 16f, Ui.Text, FontStyle.Bold));
            flow.Controls.Add(Ui.Wrap(L.T("Главное из конфигов плагина, оверлея и мода — в одном месте. Изменения применятся при следующем «Играть» (оверлей и мод — после их перезапуска).",
                                          "The main options of the plugin, overlay and mod in one place. Changes apply on the next “Play” (overlay and mod — after they restart)."), w));

            Section(flow, w, L.T("Персонаж", "Character"), new[]
            {
                Pick(Src.Plugin, "role", "Роль в мире Minecraft", "Role in the Minecraft world", Roles,
                    "⚠ Повстанцы Хаоса в зелёной форме — её цвет близок к зелёному экрану, местами может просвечивать.",
                    "⚠ Chaos Insurgency wear green — close to the green screen, some parts may show through."),
                Num(Src.Plugin, "jump_boost_percent", "Высота прыжка, %", "Jump boost, %", 0, 60, 1, 0, "18 — чуть выше блока.", "18 — a bit over one block."),
                Pick(Src.Plugin, "starter_multitool_tier", "Стартовый мультитул", "Starter multitool", Tiers),
                Num(Src.Plugin, "respawn_delay", "Возрождение через, с", "Respawn delay, s", 0, 60, 1, 1),
                Num(Src.Plugin, "scp127_exp_multiplier", "SCP-127: опыт за мобов, ×", "SCP-127: XP from mobs, ×", 0, 10, 0.1m, 1),
            });
            Section(flow, w, L.T("Зелёный экран и свет", "Green screen and light"), new[]
            {
                Num(Src.Plugin, "backdrop_glow", "Яркость зелёного фона", "Green background brightness", 1, 4, 0.1m, 1, "Ярче — надёжнее при затемнении, но сильнее отсвет на металле.", "Brighter is more robust to darkening but tints metal more."),
                Num(Src.Plugin, "light_intensity", "Свет на оружии", "Light on the weapon", 0, 5, 0.1m, 1),
                Num(Src.Plugin, "backdrop_half_size", "Расстояние до зелёных стен, м", "Distance to the green walls, m", 2, 20, 0.5m, 1, "Дальше — предметы SL видно дальше, но фонарик сильнее засвечивает стены.", "Farther: SL items are visible farther, but the flashlight lights the walls more."),
                Flag(Src.Plugin, "no_fog", "Без тумана SL", "No SL fog"),
                Flag(Src.Overlay, "AdaptiveKey", "Подстройка под эффекты SL (SCP-268, затемнение)", "Adapt to SL effects (SCP-268, darkening)"),
                Num(Src.Overlay, "KeyThreshold", "Порог вырезания фона", "Key threshold", 0.05m, 0.6m, 0.01m, 2, "Больше — меньше просвечивает оружие, но края фона грубее.", "Higher: less weapon see-through, rougher background edges."),
                Num(Src.Overlay, "KeySoftness", "Мягкость краёв", "Edge softness", 0.05m, 0.6m, 0.01m, 2),
            });
            Section(flow, w, L.T("Мир и предметы", "World and items"), new[]
            {
                Num(Src.Plugin, "lantern_light", "Свет лампы", "Lantern light", 0, 15, 1, 0),
                Num(Src.Plugin, "flashlight_light", "Свет фонарика", "Flashlight light", 0, 15, 1, 0),
                Num(Src.Plugin, "grenade_explosion_power", "Взрыв гранаты в Minecraft", "Grenade explosion in Minecraft", 0, 8, 0.5m, 1, "4 — как динамит, 0 — не ломать мир.", "4 — like TNT, 0 — don't break the world."),
                Num(Src.Plugin, "scp018_explosion_power", "Взрыв SCP-018", "SCP-018 explosion", 0, 8, 0.5m, 1),
                Num(Src.Plugin, "jailbird_explosion_power", "Взрыв сломанного Jailbird", "Broken Jailbird explosion", 0, 8, 0.5m, 1),
                Flag(Src.Plugin, "cleanup_ragdolls", "Убирать трупы SL", "Remove SL ragdolls"),
                Flag(Src.Plugin, "cleanup_decals", "Убирать кровь и следы от пуль", "Remove blood and bullet holes"),
            });
            Section(flow, w, L.T("Производительность", "Performance"), new[]
            {
                Num(Src.Overlay, "McRenderScale", "Размер картинки Minecraft", "Minecraft render scale", 0.4m, 1, 0.05m, 2, "0.75 — почти вдвое меньше пикселей, заметно больше FPS.", "0.75 — about half the pixels, noticeably more FPS."),
                Num(Src.Plugin, "collision_budget_per_frame", "Коробок коллизий за кадр", "Collision boxes per frame", 20, 400, 10, 0, "Меньше — плавнее, но мир вокруг строится медленнее.", "Lower is smoother but the world builds slower."),
                Flag(Src.Overlay, "AvoidFullscreenFlip", "Совместимость с OBS (окно Minecraft на 1 пиксель меньше)", "OBS compatibility (Minecraft window 1 px smaller)"),
            });
            Section(flow, w, "Minecraft", new[]
            {
                Num(Src.Mod, "fov", "Поле зрения (как в SL)", "Field of view (match SL)", 30, 110, 1, 0, "Должно совпадать с SL, иначе мир «плывёт» относительно прицела.", "Must match SL, otherwise the world drifts from the crosshair."),
                Num(Src.Mod, "damageScale", "Делитель урона SL → Minecraft", "SL → Minecraft damage divisor", 1, 20, 0.5m, 1, "5: 100 HP SL = 20 HP Minecraft.", "5: 100 SL HP = 20 Minecraft HP."),
                Num(Src.Mod, "shotsPerHardness", "Попаданий на единицу твёрдости блока", "Hits per block hardness", 0.5m, 10, 0.5m, 1),
                Flag(Src.Mod, "drawCursor", "Свой курсор в окнах Minecraft", "Own cursor in Minecraft windows"),
                Flag(Src.Mod, "bossBar", "Полоска здоровья босса (дракон, иссушитель)", "Boss health bar (dragon, wither)"),
            });

            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Ui.Bg, Margin = new Padding(0, 10, 0, 0) };
            var save = Ui.Button(L.T("Сохранить", "Save"), Ui.Accent);
            save.Click += (_, _) => Save();
            var reload = Ui.Button(L.T("Отменить изменения", "Revert"));
            reload.Click += (_, _) => Load();
            var open = Ui.Button(L.T("Открыть папку конфига SL", "Open SL config folder"));
            open.Click += (_, _) => { try { var d = Path.GetDirectoryName(PluginFile); if (Directory.Exists(d)) System.Diagnostics.Process.Start("explorer.exe", d); } catch { } };
            row.Controls.AddRange(new Control[] { save, reload, open });
            flow.Controls.Add(row);
            _status = Ui.Wrap("", w, 9.5f, Ui.Muted);
            flow.Controls.Add(_status);

            page.Controls.Add(flow);
            page.VisibleChanged += (_, _) => { if (page.Visible) Load(); };
            return page;
        }

        private static void Section(FlowLayoutPanel flow, int w, string title, Field[] fields)
        {
            var card = new FlowLayoutPanel { Width = w, AutoSize = true, BackColor = Ui.Card, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 12, 0, 0) };
            card.Controls.Add(Ui.Label(title, 12f, Ui.Text, FontStyle.Bold));
            foreach (var f in fields)
            {
                Fields.Add(f);
                var line = new Panel { Width = w - 30, Height = 32, BackColor = Ui.Card, Margin = new Padding(0, 4, 0, 0) };
                var lab = Ui.Label(L.T(f.Ru, f.En), 10f, Ui.Text);
                lab.Location = new Point(0, 6);
                Control ctl;
                switch (f.Kind)
                {
                    case Kind.Bool:
                        ctl = new CheckBox { AutoSize = true, BackColor = Ui.Card, ForeColor = Ui.Text, Text = "" };
                        break;
                    case Kind.Choice:
                        var cb = Ui.Combo(270);
                        foreach (var o in f.Options) cb.Items.Add(L.T(o.Ru, o.En));
                        ctl = cb;
                        break;
                    default:
                        ctl = new NumericUpDown
                        {
                            Width = 110, Minimum = f.Min, Maximum = f.Max, Increment = f.Step, DecimalPlaces = f.Dec,
                            BackColor = Ui.Input, ForeColor = Ui.Text, BorderStyle = BorderStyle.FixedSingle, Font = Ui.F(10f),
                        };
                        break;
                }
                ctl.Location = new Point(line.Width - (f.Kind == Kind.Choice ? 270 : f.Kind == Kind.Bool ? 24 : 110), 3);
                f.Ctl = ctl;
                line.Controls.Add(lab);
                line.Controls.Add(ctl);
                card.Controls.Add(line);
                string hint = L.T(f.HintRu ?? "", f.HintEn ?? "");
                if (hint.Length > 0) card.Controls.Add(Ui.Wrap(hint, w - 40, 8.5f, Ui.Muted));
            }
            flow.Controls.Add(card);
        }

        // ───── чтение / запись ─────

        private static Dictionary<string, string> ReadYaml(string file)
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(file)) return d;
            foreach (var l in File.ReadAllLines(file))
            {
                if (l.Length == 0 || char.IsWhiteSpace(l[0]) || l.StartsWith("#") || l.StartsWith("-")) continue;
                int i = l.IndexOf(':');
                if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim().Trim('\'', '"');
            }
            return d;
        }

        private static Dictionary<string, string> ReadProps(string file)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return d;
            foreach (var l in File.ReadAllLines(file))
            {
                if (l.StartsWith("#")) continue;
                int i = l.IndexOf('=');
                if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
            return d;
        }

        private static Dictionary<string, string> ReadJson(string file)
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(file)) return d;
                var o = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                if (o != null) foreach (var kv in o) if (kv.Value is JsonValue v) d[kv.Key] = v.ToJsonString().Trim('"');
            }
            catch { }
            return d;
        }

        private static void Load()
        {
            var y = ReadYaml(PluginFile); var j = ReadJson(OverlayFile); var p = ReadProps(ModFile);
            foreach (var f in Fields)
            {
                var src = f.Src == Src.Plugin ? y : f.Src == Src.Overlay ? j : p;
                bool has = src.TryGetValue(f.Key, out var v);
                f.Ctl.Enabled = has;
                if (!has) continue;
                try
                {
                    switch (f.Ctl)
                    {
                        case CheckBox c: c.Checked = v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case ComboBox cb:
                            int idx = Array.FindIndex(f.Options, o => o.Value.Equals(v, StringComparison.OrdinalIgnoreCase));
                            cb.SelectedIndex = idx >= 0 ? idx : 0; break;
                        case NumericUpDown n:
                            decimal dv = decimal.Parse(v, NumberStyles.Float, Inv);
                            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, Math.Round(dv, f.Dec))); break;
                    }
                }
                catch { }
            }
            var missing = new List<string>();
            if (y.Count == 0) missing.Add(L.T("плагина SL (появится после первого запуска сервера)", "the SL plugin (appears after the first server start)"));
            if (j.Count == 0) missing.Add(L.T("оверлея (появится после первого запуска)", "the overlay (appears after the first start)"));
            if (p.Count == 0) missing.Add(L.T("мода Minecraft (появится после первого запуска)", "the Minecraft mod (appears after the first start)"));
            _status.Text = missing.Count == 0 ? "" : L.T("Нет конфига ", "No config for ") + string.Join(", ", missing) + L.T(" — эти настройки пока недоступны.", " — those settings are unavailable for now.");
        }

        private static string Value(Field f)
        {
            switch (f.Ctl)
            {
                case CheckBox c: return c.Checked ? "true" : "false";
                case ComboBox cb: return cb.SelectedIndex >= 0 ? f.Options[cb.SelectedIndex].Value : null;
                case NumericUpDown n: return f.Kind == Kind.Int ? ((int)n.Value).ToString(Inv) : n.Value.ToString("0.###", Inv);
            }
            return null;
        }

        private static void Save()
        {
            int n = 0;
            try
            {
                // плагин: построчно заменяем «ключ: значение» (комментарии и остальное не трогаем)
                if (File.Exists(PluginFile))
                {
                    var lines = File.ReadAllLines(PluginFile);
                    foreach (var f in Fields.Where(x => x.Src == Src.Plugin && x.Ctl.Enabled))
                    {
                        string v = Value(f); if (v == null) continue;
                        for (int i = 0; i < lines.Length; i++)
                            if (lines[i].StartsWith(f.Key + ":")) { lines[i] = f.Key + ": " + (v.Length == 0 ? "''" : v); n++; break; }
                    }
                    File.WriteAllLines(PluginFile, lines);
                }
                if (File.Exists(OverlayFile))
                {
                    var o = JsonNode.Parse(File.ReadAllText(OverlayFile)) as JsonObject ?? new JsonObject();
                    foreach (var f in Fields.Where(x => x.Src == Src.Overlay && x.Ctl.Enabled))
                    {
                        string v = Value(f); if (v == null) continue;
                        o[f.Key] = f.Kind == Kind.Bool ? JsonValue.Create(v == "true") : JsonValue.Create(double.Parse(v, Inv));
                        n++;
                    }
                    File.WriteAllText(OverlayFile, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
                if (!string.IsNullOrEmpty(ModFile) && File.Exists(ModFile))
                {
                    var lines = File.ReadAllLines(ModFile).ToList();
                    foreach (var f in Fields.Where(x => x.Src == Src.Mod && x.Ctl.Enabled))
                    {
                        string v = Value(f); if (v == null) continue;
                        int i = lines.FindIndex(l => l.StartsWith(f.Key + "="));
                        if (i >= 0) lines[i] = f.Key + "=" + v; else lines.Add(f.Key + "=" + v);
                        n++;
                    }
                    File.WriteAllLines(ModFile, lines);
                }
                _status.Text = L.T($"Сохранено ({n}). Применится при следующем запуске.", $"Saved ({n}). Applies on the next launch.");
            }
            catch (Exception e) { _status.Text = L.T("Не удалось сохранить: ", "Could not save: ") + e.Message; }
        }
    }
}
