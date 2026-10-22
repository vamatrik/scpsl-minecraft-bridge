using System.Collections.Generic;
using LabApi.Features.Wrappers;
using UnityEngine;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace SlBridge
{
    /// <summary>
    /// Хитбоксы мобов Minecraft в SL: невидимые твёрдые коробки размером с хитбокс моба.
    /// Сквозь моба не пройти, а Jailbird в рывке упирается в него и сам бьёт — как в стену или игрока в SL.
    /// </summary>
    public sealed class McMobs
    {
        private readonly Dictionary<int, PrimitiveObjectToy> _boxes = new Dictionary<int, PrimitiveObjectToy>();
        private readonly Config _cfg;

        public McMobs(Config cfg) { _cfg = cfg; }

        public void Update(List<object> list)
        {
            var seen = new HashSet<int>();
            foreach (var o in list)
            {
                if (!(o is Dictionary<string, object> m)) continue;
                int id = (int)m.Num("e");
                seen.Add(id);
                float w = (float)m.Num("w", 0.6), h = (float)m.Num("h", 1.8), d = (float)m.Num("d", w);
                var center = CoordMap.McToSl(m.Num("x"), m.Num("y") + h * 0.5, m.Num("z"));
                var size = new Vector3(w, h, d);
                if (_boxes.TryGetValue(id, out var t) && !t.IsDestroyed)
                {
                    t.Position = center;
                    if ((t.Scale - size).sqrMagnitude > 0.0001f) t.Scale = size;
                    continue;
                }
                t = PrimitiveObjectToy.Create(center, Quaternion.identity, size, null, false);
                t.Type = PrimitiveType.Cube;
                t.Flags = _cfg.DebugShowCollision ? (PrimitiveFlags.Collidable | PrimitiveFlags.Visible) : PrimitiveFlags.Collidable;
                t.Color = new Color(1f, 0.2f, 0.2f, 0.5f);
                t.MovementSmoothing = 60; // плавно между обновлениями (каждые 2 тика Minecraft)
                t.Spawn();
                _boxes[id] = t;
            }
            foreach (var id in new List<int>(_boxes.Keys))
            {
                if (seen.Contains(id)) continue;
                var t = _boxes[id];
                if (t != null && !t.IsDestroyed) t.Destroy();
                _boxes.Remove(id);
            }
        }

        public void Clear()
        {
            foreach (var t in _boxes.Values) if (t != null && !t.IsDestroyed) t.Destroy();
            _boxes.Clear();
        }
    }
}
