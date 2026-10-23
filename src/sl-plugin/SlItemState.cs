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

        public static string TierOf(Item it) => it != null && Tier.TryGetValue(it.Serial, out var t) ? t : null;

        public static string Capture(Item it)
        {
            var d = new List<string>();
            try
            {
                if (it != null && it.Type == ItemType.SCP1509 && TierOf(it) is string tier) d.Add("tier=" + tier);
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
