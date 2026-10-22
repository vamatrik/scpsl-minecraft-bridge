using System;
using CommandSystem;
using LabApi.Features.Wrappers;
using RemoteAdmin;

namespace SlBridge
{
    /// <summary>
    /// Команда Minecraft из SCP:SL: в консоли (~) — «.mc time set day», в админ-панели (M) — «mc time set day».
    /// Ответ Minecraft приходит в консоль.
    /// </summary>
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class McCommand : ICommand
    {
        public string Command => "mc";
        public string[] Aliases => new[] { "minecraft" };
        public string Description => L.T("Выполнить команду Minecraft (например: mc time set day, mc give @s diamond 5).", "Run a Minecraft command (e.g. mc time set day, mc give @s diamond 5).");

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            var plugin = SlBridgePlugin.Instance;
            if (plugin == null) { response = L.T("Мост не запущен.", "The bridge is not running."); return false; }
            if (arguments.Count == 0) { response = L.T("Использование: mc <команда Minecraft>", "Usage: mc <Minecraft command>"); return false; }
            string text = string.Join(" ", arguments);
            Player.TryGet(sender, out var player);
            if (!plugin.SendMinecraftCommand(text, player)) { response = L.T("Minecraft не подключён.", "Minecraft is not connected."); return false; }
            response = "→ Minecraft: /" + text.TrimStart('/');
            return true;
        }
    }
}
