using System;
using CommandSystem;
using LabApi.Features.Wrappers;
using RemoteAdmin;

namespace SlBridge
{
    /// <summary>
    /// Стартовый набор (мачете и аптечка) по запросу: в консоли (~) — «.kit start», в админ-панели (M) — «kit start».
    /// Сам набор выдаётся только при самом первом появлении в мире.
    /// </summary>
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class KitCommand : ICommand
    {
        public string Command => "kit";
        public string[] Aliases => Array.Empty<string>();
        public string Description => L.T("Выдать стартовый набор: kit start", "Give the starter kit: kit start");

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            var plugin = SlBridgePlugin.Instance;
            if (plugin == null) { response = L.T("Мост не запущен.", "The bridge is not running."); return false; }
            if (arguments.Count == 0 || !arguments.At(0).Equals("start", StringComparison.OrdinalIgnoreCase))
            { response = L.T("Использование: kit start", "Usage: kit start"); return false; }
            if (!Player.TryGet(sender, out var player) || player == null || !player.IsAlive)
            { response = L.T("Команду нужно вводить живым игроком.", "Run it as a living player."); return false; }
            plugin.GiveLoadoutTo(player);
            response = L.T("Стартовый набор выдан.", "Starter kit given.");
            return true;
        }
    }
}
