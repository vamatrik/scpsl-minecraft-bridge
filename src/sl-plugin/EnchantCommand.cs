using System;
using System.Collections.Generic;
using System.Linq;
using CommandSystem;
using LabApi.Features.Wrappers;
using RemoteAdmin;

namespace SlBridge
{
    /// <summary>
    /// Зачаровать предмет SL в руке, как ванильный /enchant (тот не видит предметы SL — в руке Minecraft пусто).
    /// В консоли (~): «.enchant sharpness 5», «.enchant power», «.enchant list», «.enchant clear». В админ-панели — без точки.
    /// Правила те же, что у стола зачарований и наковальни: только подходящие предмету чары, уровень до максимума,
    /// несовместимые (Острота/Небесная кара/Бич, виды Защиты, Удача/Шёлковое касание, Бесконечность/Починка) не смешиваются.
    /// </summary>
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class EnchantCommand : ICommand
    {
        public string Command => "enchant";
        public string[] Aliases => new[] { "ench" };
        public string Description => L.T("Зачаровать предмет SL в руке: enchant <чары> [уровень] | list | clear",
                                         "Enchant the SL item in hand: enchant <enchantment> [level] | list | clear");

        private static readonly Dictionary<string, int> Max = new Dictionary<string, int>
        {
            { "sharpness", 5 }, { "smite", 5 }, { "bane_of_arthropods", 5 }, { "fire_aspect", 2 }, { "knockback", 2 },
            { "sweeping_edge", 3 }, { "looting", 3 }, { "efficiency", 5 }, { "fortune", 3 }, { "silk_touch", 1 },
            { "power", 5 }, { "flame", 1 }, { "piercing", 4 }, { "infinity", 1 }, { "unbreaking", 3 }, { "mending", 1 },
            { "protection", 4 }, { "fire_protection", 4 }, { "blast_protection", 4 }, { "projectile_protection", 4 },
            { "thorns", 3 }, { "feather_falling", 4 }, { "respiration", 3 }, { "depth_strider", 3 },
        };

        private static readonly string[][] Exclusive =
        {
            new[] { "sharpness", "smite", "bane_of_arthropods" },
            new[] { "protection", "fire_protection", "blast_protection", "projectile_protection" },
            new[] { "fortune", "silk_touch" },
            new[] { "infinity", "mending" },
        };

        private static readonly string[] Melee = { "sharpness", "smite", "bane_of_arthropods", "fire_aspect", "knockback", "sweeping_edge", "looting" };
        private static readonly string[] Ranged = { "power", "flame", "piercing", "infinity" };

        /// <summary>Какие чары можно наложить на этот предмет SL (как в моде, SlEnchants.allowed).</summary>
        public static string[] AllowedFor(ItemType t)
        {
            string k = t.ToString();
            if (k.StartsWith("Gun") || t == ItemType.ParticleDisruptor) return Ranged;
            if (t == ItemType.MicroHID) return Ranged.Concat(new[] { "unbreaking", "mending" }).ToArray();
            if (t == ItemType.SCP1509) return Melee.Concat(new[] { "efficiency", "fortune", "silk_touch" }).ToArray();
            if (t == ItemType.Jailbird) return Melee.Concat(new[] { "unbreaking", "mending" }).ToArray();
            if (k.StartsWith("Armor")) return new[] { "protection", "fire_protection", "blast_protection", "projectile_protection", "thorns", "feather_falling", "respiration", "depth_strider" };
            return Array.Empty<string>();
        }

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!Player.TryGet(sender, out var player) || player == null || !player.IsAlive)
            { response = L.T("Команду нужно вводить живым игроком.", "Run it as a living player."); return false; }
            var item = player.CurrentItem;
            var plugin = SlBridgePlugin.Instance;
            if (item == null || (plugin != null && plugin.IsMcStack(item)))
            { response = L.T("Возьми в руку предмет SL (оружие, Micro HID, мачете, Jailbird, бронежилет).", "Hold an SL item (gun, Micro HID, machete, Jailbird, armor)."); return false; }
            var allowed = AllowedFor(item.Type);
            if (allowed.Length == 0) { response = L.T($"{item.Type} не зачаровывается.", $"{item.Type} can't be enchanted."); return false; }

            var cur = Parse(SlItemState.EnchOf(item));
            string a0 = arguments.Count > 0 ? arguments.At(0).ToLowerInvariant() : "list";
            if (a0.StartsWith("minecraft:")) a0 = a0.Substring(10);

            if (a0 == "list" || a0 == "?")
            {
                response = L.T("Можно: ", "Allowed: ") + string.Join(", ", allowed.Select(e => $"{e} (1–{Max[e]})"))
                           + "\n" + L.T("Сейчас: ", "Current: ") + (cur.Count == 0 ? "—" : SlItemState.DescribeEnch(item));
                return true;
            }
            if (a0 == "clear" || a0 == "remove")
            {
                if (arguments.Count > 1)
                {
                    string r = arguments.At(1).ToLowerInvariant().Replace("minecraft:", "");
                    cur.Remove(r);
                }
                else cur.Clear();
                Save(item, cur);
                response = L.T("Готово. Чары: ", "Done. Enchantments: ") + (cur.Count == 0 ? "—" : SlItemState.DescribeEnch(item));
                return true;
            }
            if (!allowed.Contains(a0))
            {
                response = Max.ContainsKey(a0)
                    ? L.T($"На {item.Type} нельзя «{a0}». Можно: ", $"\"{a0}\" doesn't fit {item.Type}. Allowed: ") + string.Join(", ", allowed)
                    : L.T($"Нет таких чар: «{a0}». Можно: ", $"Unknown enchantment \"{a0}\". Allowed: ") + string.Join(", ", allowed);
                return false;
            }
            int lvl = Max[a0];
            if (arguments.Count > 1 && !int.TryParse(arguments.At(1), out lvl))
            { response = L.T("Уровень — число.", "Level must be a number."); return false; }
            if (lvl < 1 || lvl > Max[a0])
            { response = L.T($"Уровень {a0}: от 1 до {Max[a0]}.", $"{a0} level: 1 to {Max[a0]}."); return false; }
            var group = Exclusive.FirstOrDefault(g => g.Contains(a0));
            var clash = group?.Where(e => e != a0 && cur.ContainsKey(e)).ToList();
            if (clash != null && clash.Count > 0)
            {
                response = L.T($"Несовместимо с: {string.Join(", ", clash)}. Сними их: enchant clear {clash[0]}",
                               $"Incompatible with: {string.Join(", ", clash)}. Remove it: enchant clear {clash[0]}");
                return false;
            }
            cur[a0] = lvl;
            Save(item, cur);
            response = L.T("Зачаровано: ", "Enchanted: ") + SlItemState.DescribeEnch(item);
            return true;
        }

        private static Dictionary<string, int> Parse(string s)
        {
            var d = new Dictionary<string, int>();
            foreach (var p in (s ?? "").Split(','))
            {
                int i = p.IndexOf(':');
                if (i > 0 && int.TryParse(p.Substring(i + 1), out var l)) d[p.Substring(0, i)] = l;
            }
            return d;
        }

        private static void Save(Item item, Dictionary<string, int> d)
        {
            if (d.Count == 0) SlItemState.Ench.Remove(item.Serial);
            else SlItemState.Ench[item.Serial] = string.Join(",", d.Select(kv => kv.Key + ":" + kv.Value));
        }
    }
}
