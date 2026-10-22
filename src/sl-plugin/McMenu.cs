using System;
using System.Linq;
using UnityEngine;
using UserSettings.ServerSpecific;

namespace SlBridge
{
    /// <summary>
    /// Меню Minecraft в «Настройки → Server-specific»: клавиши взаимодействия (E) и инвентаря (I), быстрый выход.
    /// Крафт и плавка — только в настоящих окнах Minecraft (верстак, печь, инвентарь).
    /// </summary>
    public sealed class McMenu
    {
        public const int KeyUse = 9001, Status = 9002, KeyInv = 9003, QuitBtn = 9030;

        public readonly SSKeybindSetting Use, InvKey;
        public readonly SSTextArea StatusText;
        public readonly SSButton Quit;
        public readonly ServerSpecificSettingBase[] All;

        public McMenu()
        {
            Use = new SSKeybindSetting(KeyUse, L.T("Взаимодействие Minecraft", "Minecraft interact"), KeyCode.E, true, false,
                L.T("Поставить блок из руки, нажать дверь/рычаг, съесть еду, использовать предмет (огниво, ведро, око Эндера), открыть верстак, печь, сундук — откроется окно Minecraft.",
                    "Place the held block, press a door/lever, eat food, use an item (flint and steel, bucket, eye of ender), open a crafting table, furnace or chest — a Minecraft window opens."));
            InvKey = new SSKeybindSetting(KeyInv, L.T("Инвентарь Minecraft (крафт 2×2)", "Minecraft inventory (2×2 crafting)"), KeyCode.I, true, false,
                L.T("Открыть инвентарь Minecraft с твоими предметами: перекладывать, крафтить 2×2. Закрыть — E или Esc.",
                    "Open the Minecraft inventory with your items: move them around, craft 2×2. Close with E or Esc."));
            StatusText = new SSTextArea(Status,
                L.T("Крафт и плавка — в окнах Minecraft: E по верстаку или печке, I — инвентарь. Рецепты предметов SL — в лаунчере, вкладка «⚒ Крафты».\nБыстрый выход: Ctrl+Shift+F10 (работает, пока открыт лаунчер).",
                    "Crafting and smelting happen in Minecraft windows: E on a crafting table or furnace, I for the inventory. SL item recipes are in the launcher, “⚒ Crafting” tab.\nQuick exit: Ctrl+Shift+F10 (works while the launcher is open)."),
                SSTextArea.FoldoutMode.NotCollapsable);
            Quit = new SSButton(QuitBtn, L.T("Выйти из игры (закроет SL, Minecraft, сервер и оверлей; лаунчер останется)", "Quit the game (closes SL, Minecraft, the server and the overlay; the launcher stays)"),
                L.T("Выйти", "Quit"), 1f);
            All = new ServerSpecificSettingBase[]
            {
                new SSGroupHeader("Minecraft"),
                Use,
                InvKey,
                StatusText,
                Quit,
            };
        }

        public void Install()
        {
            var existing = ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>();
            ServerSpecificSettingsSync.DefinedSettings = existing.Where(s => s.SettingId < 9000 || s.SettingId > 9100)
                .Concat(All).ToArray();
        }

        public void SendTo(ReferenceHub hub) => ServerSpecificSettingsSync.SendToPlayer(hub);
    }
}
