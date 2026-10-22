using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UserSettings.ServerSpecific;

namespace SlBridge
{
    /// <summary>
    /// Меню Minecraft в «Настройки → Server-specific»: клавиша взаимодействия (E), крафт и печка.
    /// Списки рецептов приходят из Minecraft (настоящие рецепты) и обновляются под твой инвентарь.
    /// </summary>
    public sealed class McMenu
    {
        public const int KeyUse = 9001, Status = 9002, KeyInv = 9003;
        public const int CraftDrop = 9010, CraftAmt = 9011, CraftBtn = 9012, RefreshBtn = 9013;
        public const int SmeltDrop = 9020, SmeltAmt = 9021, SmeltBtn = 9022;
        private readonly string[] Amounts = { "×1", "×4", "×16", L.T("Максимум", "Max") };

        public readonly SSKeybindSetting Use, InvKey;
        public readonly SSTextArea StatusText;
        public readonly SSDropdownSetting CraftList, CraftCount, SmeltList, SmeltCount;
        public readonly SSButton CraftButton, Refresh, SmeltButton;
        public readonly ServerSpecificSettingBase[] All;

        /// <summary>rid рецептов в том же порядке, что пункты выпадающих списков.</summary>
        public List<string> CraftIds = new List<string>();
        public List<int> CraftMax = new List<int>();
        public List<string> SmeltIds = new List<string>();
        public List<int> SmeltMax = new List<int>();

        public McMenu()
        {
            Use = new SSKeybindSetting(KeyUse, L.T("Взаимодействие Minecraft", "Minecraft interact"), KeyCode.E, true, false,
                L.T("Поставить блок из руки, нажать дверь/рычаг, съесть еду, использовать предмет (огниво, ведро, око Эндера), открыть верстак, печь, сундук — откроется окно Minecraft.", "Place the held block, press a door/lever, eat food, use an item (flint and steel, bucket, eye of ender), open a crafting table, furnace or chest — a Minecraft window opens."));
            InvKey = new SSKeybindSetting(KeyInv, L.T("Инвентарь Minecraft (крафт 2×2)", "Minecraft inventory (2×2 crafting)"), KeyCode.I, true, false,
                L.T("Открыть инвентарь Minecraft с твоими предметами: перекладывать, крафтить 2×2. Закрыть — E или Esc.", "Open the Minecraft inventory with your items: move them around, craft 2×2. Close with E or Esc."));
            StatusText = new SSTextArea(Status, L.T("Подойди к верстаку или печке и нажми E — здесь появятся рецепты.", "Walk up to a crafting table or furnace and press E — recipes will appear here."), SSTextArea.FoldoutMode.NotCollapsable);
            CraftList = new SSDropdownSetting(CraftDrop, L.T("Рецепт", "Recipe"), new[] { L.T("(нажми «Обновить»)", "(press “Refresh”)") }, 0, SSDropdownSetting.DropdownEntryType.Scrollable);
            CraftCount = new SSDropdownSetting(CraftAmt, L.T("Сколько раз", "How many times"), Amounts, 0);
            CraftButton = new SSButton(CraftBtn, L.T("Крафт", "Craft"), L.T("Скрафтить", "Craft it"));
            Refresh = new SSButton(RefreshBtn, L.T("Списки рецептов", "Recipe lists"), L.T("Обновить", "Refresh"));
            SmeltList = new SSDropdownSetting(SmeltDrop, L.T("Что плавить", "What to smelt"), new[] { L.T("(нужна печь рядом)", "(you need a furnace nearby)") }, 0, SSDropdownSetting.DropdownEntryType.Scrollable);
            SmeltCount = new SSDropdownSetting(SmeltAmt, L.T("Сколько", "How many"), Amounts, 0);
            SmeltButton = new SSButton(SmeltBtn, L.T("Печка", "Furnace"), L.T("Переплавить", "Smelt"));
            All = new ServerSpecificSettingBase[]
            {
                new SSGroupHeader("Minecraft"),
                Use,
                InvKey,
                StatusText,
                new SSGroupHeader(L.T("Быстрый крафт (без окна)", "Quick crafting (no window)")),
                CraftList, CraftCount, CraftButton, Refresh,
                new SSGroupHeader(L.T("Быстрая плавка (без окна)", "Quick smelting (no window)")),
                SmeltList, SmeltCount, SmeltButton,
            };
        }

        public void Install()
        {
            var existing = ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>();
            ServerSpecificSettingsSync.DefinedSettings = existing.Where(s => s.SettingId < 9000 || s.SettingId > 9100)
                .Concat(All).ToArray();
        }

        public void SendTo(ReferenceHub hub) => ServerSpecificSettingsSync.SendToPlayer(hub);

        public static int AmountFor(int index, int max)
        {
            switch (index)
            {
                case 0: return 1;
                case 1: return Math.Min(4, max);
                case 2: return Math.Min(16, max);
                default: return max;
            }
        }

        public void SetCraft(ReferenceHub hub, List<string> labels, List<string> ids, List<int> max)
        {
            CraftIds = ids;
            CraftMax = max;
            CraftList.SendDropdownUpdate(labels.Count > 0 ? labels.ToArray() : new[] { L.T("(нечего крафтить из того, что есть)", "(nothing to craft from what you have)") }, true, h => h == hub);
        }

        public void SetSmelt(ReferenceHub hub, List<string> labels, List<string> ids, List<int> max, bool furnace)
        {
            SmeltIds = ids;
            SmeltMax = max;
            string empty = furnace ? L.T("(нечего плавить или нет топлива)", "(nothing to smelt or no fuel)") : L.T("(нужна печь рядом)", "(you need a furnace nearby)");
            SmeltList.SendDropdownUpdate(labels.Count > 0 ? labels.ToArray() : new[] { empty }, true, h => h == hub);
        }

        public void SetStatus(ReferenceHub hub, string text) => StatusText.SendTextUpdate(text, true, h => h == hub);

        public static int Selected(ReferenceHub hub, int id)
            => ServerSpecificSettingsSync.TryGetSettingOfUser<SSDropdownSetting>(hub, id, out var d) ? d.SyncSelectionIndexValidated : 0;
    }
}
