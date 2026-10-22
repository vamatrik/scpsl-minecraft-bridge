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
        private static readonly string[] Amounts = { "×1", "×4", "×16", "Максимум" };

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
            Use = new SSKeybindSetting(KeyUse, "Взаимодействие Minecraft", KeyCode.E, true, false,
                "Поставить блок из руки, нажать дверь/рычаг, съесть еду, использовать предмет (огниво, ведро, око Эндера), открыть верстак, печь, сундук — откроется окно Minecraft.");
            InvKey = new SSKeybindSetting(KeyInv, "Инвентарь Minecraft (крафт 2×2)", KeyCode.I, true, false,
                "Открыть инвентарь Minecraft с твоими предметами: перекладывать, крафтить 2×2. Закрыть — E или Esc.");
            StatusText = new SSTextArea(Status, "Подойди к верстаку или печке и нажми E — здесь появятся рецепты.", SSTextArea.FoldoutMode.NotCollapsable);
            CraftList = new SSDropdownSetting(CraftDrop, "Рецепт", new[] { "(нажми «Обновить»)" }, 0, SSDropdownSetting.DropdownEntryType.Scrollable);
            CraftCount = new SSDropdownSetting(CraftAmt, "Сколько раз", Amounts, 0);
            CraftButton = new SSButton(CraftBtn, "Крафт", "Скрафтить");
            Refresh = new SSButton(RefreshBtn, "Списки рецептов", "Обновить");
            SmeltList = new SSDropdownSetting(SmeltDrop, "Что плавить", new[] { "(нужна печь рядом)" }, 0, SSDropdownSetting.DropdownEntryType.Scrollable);
            SmeltCount = new SSDropdownSetting(SmeltAmt, "Сколько", Amounts, 0);
            SmeltButton = new SSButton(SmeltBtn, "Печка", "Переплавить");
            All = new ServerSpecificSettingBase[]
            {
                new SSGroupHeader("Minecraft"),
                Use,
                InvKey,
                StatusText,
                new SSGroupHeader("Быстрый крафт (без окна)"),
                CraftList, CraftCount, CraftButton, Refresh,
                new SSGroupHeader("Быстрая плавка (без окна)"),
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
            CraftList.SendDropdownUpdate(labels.Count > 0 ? labels.ToArray() : new[] { "(нечего крафтить из того, что есть)" }, true, h => h == hub);
        }

        public void SetSmelt(ReferenceHub hub, List<string> labels, List<string> ids, List<int> max, bool furnace)
        {
            SmeltIds = ids;
            SmeltMax = max;
            string empty = furnace ? "(нечего плавить или нет топлива)" : "(нужна печь рядом)";
            SmeltList.SendDropdownUpdate(labels.Count > 0 ? labels.ToArray() : new[] { empty }, true, h => h == hub);
        }

        public void SetStatus(ReferenceHub hub, string text) => StatusText.SendTextUpdate(text, true, h => h == hub);

        public static int Selected(ReferenceHub hub, int id)
            => ServerSpecificSettingsSync.TryGetSettingOfUser<SSDropdownSetting>(hub, id, out var d) ? d.SyncSelectionIndexValidated : 0;
    }
}
