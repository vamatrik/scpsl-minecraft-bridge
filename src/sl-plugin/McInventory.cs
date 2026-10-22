using System;
using System.Collections.Generic;
using System.Linq;
using Interactables.Interobjects.DoorUtils;
using LabApi.Features.Wrappers;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace SlBridge
{
    /// <summary>Стак предмета Minecraft.</summary>
    public sealed class McStack
    {
        public string Id;
        public string Name;
        public int Count;
        public int Max = 64;
        /// <summary>Если не null — это предмет SCP:SL (ItemType), скрафченный в Minecraft.</summary>
        public string Sl;
        public string Model;
        /// <summary>Для патронов SL, выпавших при смерти: точное количество (иначе 80 за штуку).</summary>
        public int Ammo;

        public McStack Clone() => new McStack { Id = Id, Name = Name, Count = Count, Max = Max, Sl = Sl, Model = Model, Ammo = Ammo };

        /// <summary>Предмет SL как предмет Minecraft (для выпадения в мир).</summary>
        public static McStack ForSl(ItemType type, int ammo = 0)
        {
            string key = type.ToString();
            string name = key, icon = key.StartsWith("Ammo") ? "iron_nugget" : key.StartsWith("Keycard") ? "name_tag" : key.StartsWith("Gun") ? "crossbow" : "paper";
            if (SlItemNames.All.TryGetValue(key, out var n)) { name = L.T(n.Ru, n.En); icon = n.Icon; }
            if (ammo > 0) name += " (" + ammo + ")";
            return new McStack { Id = "minecraft:paper", Name = name, Count = 1, Max = 64, Sl = key, Model = "minecraft:" + icon, Ammo = ammo };
        }

        public static McStack FromMsg(Dictionary<string, object> o)
        {
            if (o == null) return null;
            return new McStack
            {
                Id = o.Str("id"),
                Name = o.Str("name", o.Str("id")),
                Count = Math.Max(1, (int)o.Num("count", 1)),
                Max = Math.Max(1, (int)o.Num("max", 64)),
                Sl = o.Has("sl") ? o.Str("sl") : null,
                Model = o.Has("model") ? o.Str("model") : null,
                Ammo = (int)o.Num("slAmmo", 0),
            };
        }

        public string ToJson()
            => "{\"id\":\"" + MiniJson.Escape(Id) + "\",\"count\":" + Count + ",\"name\":\"" + MiniJson.Escape(Name ?? Id) + "\",\"max\":" + Max
               + (Sl != null ? ",\"sl\":\"" + MiniJson.Escape(Sl) + "\"" : "")
               + (Model != null ? ",\"model\":\"" + MiniJson.Escape(Model) + "\"" : "")
               + (Ammo > 0 ? ",\"slAmmo\":" + Ammo : "") + "}";
    }

    /// <summary>
    /// Предметы Minecraft в инвентаре SL. Каждый стак — это кастомная ключ-карта-заглушка в обычном слоте
    /// инвентаря SL (8 слотов вместе с оружием); название карты — «Предмет ×N», цвет — по предмету.
    /// </summary>
    public sealed class McInventory
    {
        private readonly Dictionary<ushort, McStack> _stacks = new Dictionary<ushort, McStack>();
        public event Action Changed;

        public bool IsStack(Item item) => item != null && _stacks.ContainsKey(item.Serial);
        public McStack Get(Item item) => item != null && _stacks.TryGetValue(item.Serial, out var s) ? s : null;
        public IEnumerable<McStack> All => _stacks.Values;

        public void Clear() => _stacks.Clear();

        /// <summary>Стаки в порядке инвентаря SL (для переноса в инвентарь Minecraft по слотам).</summary>
        public List<KeyValuePair<ushort, McStack>> Ordered(Player p)
            => p.Items.Where(i => _stacks.ContainsKey(i.Serial)).Select(i => new KeyValuePair<ushort, McStack>(i.Serial, _stacks[i.Serial])).ToList();

        /// <summary>Снимок инвентаря для Minecraft (крафт, плавка).</summary>
        public string SnapshotJson()
            => "[" + string.Join(",", _stacks.Values.Select(s => s.ToJson())) + "]";

        private static Color ColorFor(string id)
        {
            int h = id.GetHashCode();
            float hue = ((h & 0xFFFF) / 65535f);
            var c = Color.HSVToRGB(hue, 0.55f, 0.85f);
            // не зелёный ключ! сдвигаем оттенки около зелёного
            if (c.g > c.r * 1.3f && c.g > c.b * 1.3f) c = new Color(c.g, c.r, c.b);
            return c;
        }

        private static string Short(string id) => id.StartsWith("minecraft:") ? id.Substring(10) : id;

        private Item CreateCard(Player p, McStack s)
        {
            string name = $"{s.Name} ×{s.Count}";
            var col = ColorFor(s.Id);
            var card = KeycardItem.CreateCustomKeycardManagement(p, name, Short(s.Id), new KeycardLevels(0, 0, 0), col, Color.black, Color.white);
            if (card == null) return null;
            _stacks[card.Serial] = s;
            return card;
        }

        /// <summary>Пересоздать карту стака (название с количеством меняется только так).</summary>
        private void Refresh(Player p, ushort serial, McStack s)
        {
            Item old = p.Items.FirstOrDefault(i => i.Serial == serial);
            bool wasHeld = p.CurrentItem != null && p.CurrentItem.Serial == serial;
            _stacks.Remove(serial);
            if (old != null) p.RemoveItem(old);
            if (s == null || s.Count <= 0) return;
            var card = CreateCard(p, s);
            if (card != null && wasHeld) p.CurrentItem = card;
        }

        /// <summary>Добавить предметы. Возвращает остаток, который не влез (его надо выбросить обратно в мир).</summary>
        public int Add(Player p, McStack add)
        {
            int left = add.Count;
            foreach (var kv in _stacks.ToList())
            {
                if (left <= 0) break;
                var s = kv.Value;
                if (s.Id != add.Id || s.Count >= s.Max) continue;
                int k = Math.Min(left, s.Max - s.Count);
                s.Count += k;
                left -= k;
                Refresh(p, kv.Key, s);
            }
            while (left > 0 && p.Items.Count() < 8)
            {
                int k = Math.Min(left, add.Max);
                var s = new McStack { Id = add.Id, Name = add.Name, Count = k, Max = add.Max };
                if (CreateCard(p, s) == null) break;
                left -= k;
            }
            Changed?.Invoke();
            return left;
        }

        /// <summary>Списать предметы по id. false — не хватило (тогда ничего не списано).</summary>
        public bool Consume(Player p, string id, int count)
        {
            int have = _stacks.Values.Where(s => s.Id == id).Sum(s => s.Count);
            if (have < count) return false;
            // сначала из самых маленьких стаков
            foreach (var kv in _stacks.Where(k => k.Value.Id == id).OrderBy(k => k.Value.Count).ToList())
            {
                if (count <= 0) break;
                int k = Math.Min(count, kv.Value.Count);
                kv.Value.Count -= k;
                count -= k;
                Refresh(p, kv.Key, kv.Value.Count > 0 ? kv.Value : null);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Заменить содержимое конкретного стака (после «использовать»: блок поставлен, ведро наполнено…).</summary>
        public void Replace(Player p, ushort serial, McStack s)
        {
            if (!_stacks.ContainsKey(serial)) return;
            Refresh(p, serial, s);
            Changed?.Invoke();
        }

        /// <summary>Убрать карту из инвентаря и вернуть её стак (выброс на Q, смерть).</summary>
        public McStack Take(Player p, Item item)
        {
            if (item == null || !_stacks.TryGetValue(item.Serial, out var s)) return null;
            _stacks.Remove(item.Serial);
            p.RemoveItem(item);
            Changed?.Invoke();
            return s;
        }

        public List<McStack> TakeAll(Player p)
        {
            var list = new List<McStack>();
            foreach (var it in p.Items.ToList())
            {
                var s = Take(p, it);
                if (s != null) list.Add(s);
            }
            return list;
        }

        /// <summary>Убрать записи о картах, которых больше нет у игрока.</summary>
        public void Prune(Player p)
        {
            var serials = new HashSet<ushort>(p.Items.Select(i => i.Serial));
            foreach (var k in _stacks.Keys.ToList()) if (!serials.Contains(k)) _stacks.Remove(k);
        }
    }
}
