using System.Collections.Generic;
using AdminToys;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace SlBridge
{
    /// <summary>
    /// Предметы, лежащие в мире Minecraft (их рисует Minecraft). Для каждого в SL стоит невидимый
    /// интерактивный объект: навёл прицел, зажал E — как подбор предмета в SL.
    /// </summary>
    public sealed class McWorldItems
    {
        private sealed class Entry { public InteractableToy Toy; public McStack Stack; public Vector3 Pos; }

        private readonly Dictionary<int, Entry> _byEntity = new Dictionary<int, Entry>();
        private readonly Dictionary<InvisibleInteractableToy, int> _byToy = new Dictionary<InvisibleInteractableToy, int>();
        private readonly Config _cfg;

        public McWorldItems(Config cfg) { _cfg = cfg; }

        public bool TryGetEntity(InteractableToy toy, out int entityId, out McStack stack)
        {
            stack = null;
            entityId = 0;
            if (toy == null || toy.Base == null || !_byToy.TryGetValue(toy.Base, out entityId)) return false;
            stack = _byEntity.TryGetValue(entityId, out var e) ? e.Stack : null;
            return true;
        }

        public void Update(List<object> list)
        {
            var seen = new HashSet<int>();
            foreach (var o in list)
            {
                if (!(o is Dictionary<string, object> m)) continue;
                int id = (int)m.Num("e");
                seen.Add(id);
                var pos = CoordMap.McToSl(m.Num("x"), m.Num("y") + 0.15, m.Num("z"));
                var stack = McStack.FromMsg(m);
                if (_byEntity.TryGetValue(id, out var e))
                {
                    e.Stack = stack;
                    if ((e.Pos - pos).sqrMagnitude > 0.0025f && !e.Toy.IsDestroyed)
                    {
                        e.Toy.Position = pos;
                        e.Pos = pos;
                    }
                    continue;
                }
                var toy = InteractableToy.Create(pos, null, false);
                toy.Shape = InvisibleInteractableToy.ColliderShape.Sphere;
                toy.Scale = Vector3.one * 0.7f;
                toy.InteractionDuration = _cfg.PickupTime;
                toy.IsLocked = false;
                toy.Spawn();
                _byEntity[id] = new Entry { Toy = toy, Stack = stack, Pos = pos };
                _byToy[toy.Base] = id;
            }
            foreach (var id in new List<int>(_byEntity.Keys))
                if (!seen.Contains(id)) Remove(id);
        }

        private readonly List<KeyValuePair<float, InteractableToy>> _graveyard = new List<KeyValuePair<float, InteractableToy>>();

        /// <summary>
        /// Удаляем не сразу: если SL прямо сейчас «держит» этот объект по E, мгновенное уничтожение роняет
        /// его поиск (NullReference каждый кадр). Сначала блокируем и уносим, уничтожаем через 2 с.
        /// </summary>
        public void Remove(int id)
        {
            if (!_byEntity.TryGetValue(id, out var e)) return;
            if (e.Toy != null && !e.Toy.IsDestroyed)
            {
                _byToy.Remove(e.Toy.Base);
                e.Toy.IsLocked = true;
                e.Toy.Position = e.Pos + Vector3.down * 500f;
                _graveyard.Add(new KeyValuePair<float, InteractableToy>(Time.time + 2f, e.Toy));
            }
            _byEntity.Remove(id);
        }

        public void Tick()
        {
            for (int i = _graveyard.Count - 1; i >= 0; i--)
            {
                if (Time.time < _graveyard[i].Key) continue;
                var t = _graveyard[i].Value;
                if (t != null && !t.IsDestroyed) t.Destroy();
                _graveyard.RemoveAt(i);
            }
        }

        public void Clear()
        {
            foreach (var id in new List<int>(_byEntity.Keys)) Remove(id);
            _byToy.Clear();
        }
    }
}
