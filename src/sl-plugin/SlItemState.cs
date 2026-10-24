using System;
using System.Collections.Generic;
using System.Globalization;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace SlBridge
{
    /// <summary>
    /// Состояние предмета SL, которое переживает «превращение» в предмет Minecraft (выброс, смерть, окна Minecraft):
    /// обвесы и патроны в магазине у оружия (и у молекулярного разрушителя), заряд и поломка Micro H.I.D.,
    /// число зарядов Jailbird. Хранится строкой «ключ=значение;…» в метке предмета Minecraft (slState).
    /// </summary>
    public static class SlItemState
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── Мультитул: мачете SL (SCP-1509) с уровнем материала — от него урон и скорость добычи ──
        public static readonly Dictionary<ushort, string> Tier = new Dictionary<ushort, string>();

        /// <summary>Урон (множитель к урону мачете SL) и «добыча» (сколько попаданий по блоку за удар).</summary>
        public static (float Dmg, float Mine, string Ru, string En) TierStats(string tier)
        {
            switch (tier)
            {
                case "wood": return (0.6f, 1.5f, "деревянный мультитул", "wooden multitool");
                case "stone": return (0.75f, 2.5f, "каменный мультитул", "stone multitool");
                case "copper": return (0.8f, 3f, "медный мультитул", "copper multitool");
                case "iron": return (0.9f, 4f, "железный мультитул", "iron multitool");
                case "gold": return (0.7f, 6f, "золотой мультитул", "golden multitool");
                case "diamond": return (1.1f, 5f, "алмазный мультитул", "diamond multitool");
                case "netherite": return (1.3f, 6.5f, "незеритовый мультитул", "netherite multitool");
                default: return (1f, 3f, "мачете SCP-1509", "SCP-1509 machete");
            }
        }

        /// <summary>Цвет названия мультитула в подсказке (как материал).</summary>
        public static string TierColor(string tier)
        {
            switch (tier)
            {
                case "wood": return "#C49A6C";
                case "stone": return "#A8A8A8";
                case "copper": return "#E8875E";
                case "iron": return "#E6E6E6";
                case "gold": return "#FFD84A";
                case "diamond": return "#5CE6F0";
                case "netherite": return "#B49AB0";
                default: return "#FF6A4D";
            }
        }

        public static string TierOf(Item it) => it != null && Tier.TryGetValue(it.Serial, out var t) ? t : null;

        // ── Зачарования (стол зачарований / книги на наковальне в Minecraft): «sharpness:3,looting:1» ──
        public static readonly Dictionary<ushort, string> Ench = new Dictionary<ushort, string>();

        public static string EnchOf(Item it) => it != null && Ench.TryGetValue(it.Serial, out var e) ? e : "";

        public static int EnchLevel(Item it, string name)
        {
            foreach (var p in EnchOf(it).Split(','))
            {
                int i = p.IndexOf(':');
                if (i > 0 && p.Substring(0, i) == name && int.TryParse(p.Substring(i + 1), NumberStyles.Integer, Inv, out var lvl)) return lvl;
            }
            return 0;
        }

        private static readonly Dictionary<string, (string Ru, string En)> EnchNames = new Dictionary<string, (string, string)>
        {
            { "sharpness", ("Острота", "Sharpness") }, { "smite", ("Небесная кара", "Smite") }, { "bane_of_arthropods", ("Бич членистоногих", "Bane of Arthropods") },
            { "fire_aspect", ("Заговор огня", "Fire Aspect") }, { "knockback", ("Отдача", "Knockback") }, { "sweeping_edge", ("Разящий клинок", "Sweeping Edge") },
            { "looting", ("Добыча", "Looting") }, { "efficiency", ("Эффективность", "Efficiency") }, { "fortune", ("Удача", "Fortune") },
            { "silk_touch", ("Шёлковое касание", "Silk Touch") }, { "power", ("Сила", "Power") }, { "flame", ("Горящая стрела", "Flame") },
            { "piercing", ("Пронзание", "Piercing") }, { "infinity", ("Бесконечность", "Infinity") }, { "unbreaking", ("Прочность", "Unbreaking") },
            { "mending", ("Починка", "Mending") }, { "protection", ("Защита", "Protection") }, { "fire_protection", ("Огнеупорность", "Fire Protection") },
            { "blast_protection", ("Взрывоустойчивость", "Blast Protection") }, { "projectile_protection", ("Защита от снарядов", "Projectile Protection") },
            { "thorns", ("Шипы", "Thorns") }, { "feather_falling", ("Невесомость", "Feather Falling") }, { "respiration", ("Подводное дыхание", "Respiration") },
            { "depth_strider", ("Подводная ходьба", "Depth Strider") },
        };

        private static string Roman(int n) => n >= 1 && n <= 10 ? new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" }[n - 1] : n.ToString(Inv);

        /// <summary>Чары предмета для подсказки SL: «Острота III, Добыча II» (пусто — без чар).</summary>
        public static string DescribeEnch(Item it)
        {
            var parts = new List<string>();
            foreach (var p in EnchOf(it).Split(','))
            {
                int i = p.IndexOf(':');
                if (i <= 0) continue;
                string id = p.Substring(0, i);
                int.TryParse(p.Substring(i + 1), NumberStyles.Integer, Inv, out var lvl);
                string n = EnchNames.TryGetValue(id, out var nm) ? L.T(nm.Ru, nm.En) : id;
                parts.Add(id == "infinity" || id == "mending" || id == "silk_touch" || id == "flame" ? n : n + " " + Roman(lvl));
            }
            return string.Join(", ", parts);
        }

        public static string Capture(Item it)
        {
            var d = new List<string>();
            try
            {
                if (it != null && it.Type == ItemType.SCP1509 && TierOf(it) is string tier) d.Add("tier=" + tier);
                if (it != null && EnchOf(it) is string en && en.Length > 0) d.Add("ench=" + en);
                switch (it)
                {
                    case FirearmItem fa:
                        d.Add("att=" + fa.AttachmentsCode.ToString(Inv));
                        d.Add("mag=" + (fa.StoredAmmo + fa.ChamberedAmmo).ToString(Inv));
                        if (TryGet127(fa, out var m127)) d.Add("x127=" + m127.ServerExp.ToString("0.#", Inv));
                        break;
                    case MicroHIDItem hid:
                        d.Add("en=" + hid.Energy.ToString("0.###", Inv));
                        if (hid.IsBroken) d.Add("br=1");
                        break;
                    case JailbirdItem jb:
                        d.Add("jc=" + jb.Base.TotalChargesPerformed.ToString(Inv));
                        break;
                }
            }
            catch (Exception e) { Logger.Warn("[SlBridge] состояние предмета: " + e.Message); }
            return d.Count > 0 ? string.Join(";", d) : null;
        }

        /// <summary>SCP-127: модуль опыта (уровень оружия).</summary>
        public static bool TryGet127(FirearmItem fa, out InventorySystem.Items.Firearms.Modules.Scp127.Scp127TierManagerModule m)
        {
            m = null;
            try { return fa != null && InventorySystem.Items.Firearms.Modules.ModulesUtils.TryGetModule(fa.Base, out m); } catch { return false; }
        }

        private static Dictionary<string, string> Parse(string state)
        {
            var r = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(state)) return r;
            foreach (var part in state.Split(';'))
            {
                int i = part.IndexOf('=');
                if (i > 0) r[part.Substring(0, i)] = part.Substring(i + 1);
            }
            return r;
        }

        public static void Apply(Item it, string state)
        {
            if (it == null || string.IsNullOrEmpty(state)) return;
            var d = Parse(state);
            try
            {
                if (it.Type == ItemType.SCP1509 && d.TryGetValue("tier", out var tr)) Tier[it.Serial] = tr;
                if (d.TryGetValue("ench", out var ench) && ench.Length > 0) Ench[it.Serial] = ench; else Ench.Remove(it.Serial);
                switch (it)
                {
                    case FirearmItem fa:
                        if (d.TryGetValue("att", out var a) && uint.TryParse(a, NumberStyles.Integer, Inv, out var code)) fa.AttachmentsCode = code;
                        if (d.TryGetValue("mag", out var m) && int.TryParse(m, NumberStyles.Integer, Inv, out var mag))
                            fa.StoredAmmo = Math.Max(0, Math.Min(mag, fa.MaxAmmo));
                        if (d.TryGetValue("x127", out var x) && float.TryParse(x, NumberStyles.Float, Inv, out var xp) && TryGet127(fa, out var m127))
                            m127.ServerSetStats(xp);
                        break;
                    case MicroHIDItem hid:
                        if (d.TryGetValue("en", out var e) && float.TryParse(e, NumberStyles.Float, Inv, out var en)) hid.Energy = Math.Max(0f, Math.Min(1f, en));
                        if (d.ContainsKey("br")) hid.IsBroken = true;
                        break;
                    case JailbirdItem jb:
                        if (d.TryGetValue("jc", out var c) && int.TryParse(c, NumberStyles.Integer, Inv, out var jc))
                            typeof(InventorySystem.Items.Jailbird.JailbirdItem).GetProperty("TotalChargesPerformed")?.SetValue(jb.Base, jc);
                        break;
                }
            }
            catch (Exception ex) { Logger.Warn("[SlBridge] восстановление предмета: " + ex.Message); }
        }

        /// <summary>Подпись к названию в Minecraft: «(75%)», «(30 патр.)», «(зарядов: 2)».</summary>
        public static string Describe(string state)
        {
            var d = Parse(state);
            if (d.TryGetValue("tier", out var tier)) { var st = TierStats(tier); return " — " + L.T(st.Ru, st.En); }
            if (d.ContainsKey("br")) return L.T(" (сломан)", " (broken)");
            if (d.TryGetValue("en", out var e) && float.TryParse(e, NumberStyles.Float, Inv, out var en)) return $" ({Math.Round(en * 100)}%)";
            if (d.TryGetValue("jc", out var c) && c != "0") return L.T($" (ударов: {c})", $" (charges used: {c})");
            if (d.TryGetValue("mag", out var m)) return L.T($" ({m} в магазине)", $" ({m} loaded)");
            return "";
        }
    }
}
