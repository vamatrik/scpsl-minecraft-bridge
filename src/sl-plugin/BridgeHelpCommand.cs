using System;
using CommandSystem;
using RemoteAdmin;

namespace SlBridge
{
    /// <summary>
    /// Справка по командам моста: «.bridge» в консоли (~). Встроенный «.help» SL тоже показывает их описания,
    /// но вперемешку с командами SL — здесь только мост и с примерами.
    /// </summary>
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class BridgeHelpCommand : ICommand
    {
        public string Command => "bridge";
        public string[] Aliases => new[] { "most", "slbridge" };
        public string Description => L.T("Справка по командам моста SCP:SL ↔ Minecraft.", "Help for the SCP:SL ↔ Minecraft bridge commands.");

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            response = L.T(
                "Команды моста (в консоли ~ с точкой, в админ-панели M — без):\n" +
                "  .mc <команда>          — команда Minecraft: .mc time set day, .mc give @s diamond 5, .mc gamerule keep_inventory true\n" +
                "  .enchant <чары> [ур.]  — зачаровать предмет SL в руке: .enchant sharpness 5, .enchant power\n" +
                "  .enchant list          — какие чары подходят предмету в руке и какие уже есть\n" +
                "  .enchant clear [чары]  — снять все чары или одни: .enchant clear sharpness\n" +
                "  .kit start             — выдать стартовый набор (мультитул и аптечка)\n" +
                "  .bridge                — эта справка\n" +
                "Клавиши (меняются в настройках SL → Server-specific): E — использовать, K — инвентарь Minecraft (крафт 2×2), U — книга крафтов SL,\n" +
                "  O — настройки Minecraft, F4 — отладка (как F3), F4+B — хитбоксы, F4+G — границы чанков.",
                "Bridge commands (in the ~ console with a dot, in the M admin panel without):\n" +
                "  .mc <command>           — a Minecraft command: .mc time set day, .mc give @s diamond 5, .mc gamerule keep_inventory true\n" +
                "  .enchant <ench> [lvl]   — enchant the SL item in hand: .enchant sharpness 5, .enchant power\n" +
                "  .enchant list           — which enchantments fit the item in hand and which it has\n" +
                "  .enchant clear [ench]   — remove all enchantments or one: .enchant clear sharpness\n" +
                "  .kit start              — give the starter kit (multitool and medkit)\n" +
                "  .bridge                 — this help\n" +
                "Keys (change them in SL settings → Server-specific): E — use, K — Minecraft inventory (2×2 crafting), U — SL crafting book,\n" +
                "  O — Minecraft options, F4 — debug (like F3), F4+B — hitboxes, F4+G — chunk borders.");
            return true;
        }
    }
}
