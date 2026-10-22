using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using PrimitiveFlags = AdminToys.PrimitiveFlags;
using UnityEngine;

namespace SlBridge
{
    /// <summary>Невидимые твёрдые примитивы, повторяющие блоки Minecraft вокруг игрока (по секциям 16³).</summary>
    public sealed class CollisionWorld
    {
        private readonly Dictionary<string, List<PrimitiveObjectToy>> _sections = new Dictionary<string, List<PrimitiveObjectToy>>();
        private readonly Config _cfg;
        public int SectionCount => _sections.Count;
        public bool Has(string key) => _sections.ContainsKey(key);
        private readonly Dictionary<string, float> _builtAt = new Dictionary<string, float>();
        /// <summary>Секция построена и прошло не меньше delay секунд — клиент SL точно успел получить её коробки.</summary>
        public bool HasStable(string key, float delay) => _builtAt.TryGetValue(key, out var t) && Time.time - t >= delay;

        /// <summary>
        /// Есть ли под ногами (точка в координатах SL) настоящая опора: коробка, верх которой чуть ниже ног,
        /// в секции, которая не менялась delay секунд. Нужна после порталов: платформа Энда и новый портал
        /// появляются в мире Minecraft уже после прибытия — пустая секция «построена», но пола в ней ещё нет.
        /// </summary>
        public bool HasGroundUnder(Vector3 feet, float delay)
        {
            foreach (var kv in _sections)
            {
                if (!HasStable(kv.Key, delay)) continue;
                foreach (var t in kv.Value)
                {
                    if (t == null || t.IsDestroyed) continue;
                    Vector3 c = t.Position, sz = t.Scale;
                    float top = c.y + Mathf.Abs(sz.y) * 0.5f;
                    if (top > feet.y + 0.5f || top < feet.y - 1.2f) continue;
                    if (Mathf.Abs(feet.x - c.x) > Mathf.Abs(sz.x) * 0.5f + 0.3f) continue;
                    if (Mathf.Abs(feet.z - c.z) > Mathf.Abs(sz.z) * 0.5f + 0.3f) continue;
                    return true;
                }
            }
            return false;
        }
        public int BoxCount { get { int n = 0; foreach (var l in _sections.Values) n += l.Count; return n; } }
        public int ReceivedSinceReset;

        public CollisionWorld(Config cfg) { _cfg = cfg; }

        private readonly List<string> _pending = new List<string>();
        private readonly Dictionary<string, float[]> _latest = new Dictionary<string, float[]>();

        /// <summary>Поставить секцию в очередь: примитивы создаются порциями, чтобы не было рывков.</summary>
        public void Enqueue(string key, float[] b)
        {
            if (!_latest.ContainsKey(key)) _pending.Add(key);
            _latest[key] = b;
        }

        public int Pending => _latest.Count;

        /// <summary>Обработать очередь: не больше budget примитивов за кадр.</summary>
        public void Process(int budget)
        {
            while (budget > 0 && _pending.Count > 0)
            {
                var key = _pending[0];
                _pending.RemoveAt(0);
                if (!_latest.TryGetValue(key, out var b)) continue;
                _latest.Remove(key);
                if (b == null) { Remove(key); continue; }
                Apply(key, b);
                budget -= Math.Max(1, b.Length / 6);
            }
        }

        public void EnqueueRemove(string key)
        {
            if (!_latest.ContainsKey(key)) _pending.Add(key);
            _latest[key] = null;
        }

        public void Apply(string key, float[] b)
        {
            Remove(key);
            var list = new List<PrimitiveObjectToy>(b.Length / 6);
            for (int i = 0; i + 5 < b.Length; i += 6)
            {
                CoordMap.McBoxToSl(b[i], b[i + 1], b[i + 2], b[i + 3], b[i + 4], b[i + 5], out var c, out var s);
                var toy = PrimitiveObjectToy.Create(c, Quaternion.identity, s, null, false);
                toy.Type = PrimitiveType.Cube;
                toy.Flags = _cfg.DebugShowCollision ? (PrimitiveFlags.Collidable | PrimitiveFlags.Visible) : PrimitiveFlags.Collidable;
                toy.Color = new Color(1f, 0.55f, 0.1f, 1f); // отладка: оранжевые (не зелёные — зелёный это ключ)
                toy.IsStatic = true;
                toy.Spawn();
                list.Add(toy);
            }
            _builtAt[key] = Time.time; // время последнего обновления: новые коробки ещё должны дойти до клиента
            _sections[key] = list;
            ReceivedSinceReset++;
        }

        public void Remove(string key)
        {
            if (!_sections.TryGetValue(key, out var list)) return;
            foreach (var t in list) if (!t.IsDestroyed) t.Destroy();
            _sections.Remove(key);
            _builtAt.Remove(key);
        }

        public void Clear()
        {
            foreach (var k in new List<string>(_sections.Keys)) Remove(k);
            _pending.Clear();
            _latest.Clear();
            ReceivedSinceReset = 0;
        }
    }

    /// <summary>
    /// Маленькая «комната» цвета-ключа вплотную вокруг игрока + свет. Привязана к игроку как к родителю,
    /// поэтому у клиента она движется вместе с камерой без задержки. Стены близко — туман SL их не тронет.
    /// SCP:SL видит только её, оружие и интерфейс; компоновщик заменяет цвет-ключ картинкой Minecraft.
    /// </summary>
    public sealed class Backdrop
    {
        private readonly Config _cfg;
        private readonly List<PrimitiveObjectToy> _walls = new List<PrimitiveObjectToy>();
        private LightSourceToy _light;
        private Transform _owner;

        public Backdrop(Config cfg) { _cfg = cfg; }

        public void Attach(Player player)
        {
            var root = player.GameObject != null ? player.GameObject.transform : null;
            if (root == null) return;
            if (_owner == root && _walls.Count > 0 && !_walls[0].IsDestroyed) return;
            Destroy();
            _owner = root;

            var color = new Color(_cfg.KeyColorR, _cfg.KeyColorG, _cfg.KeyColorB, 1f);
            float h = _cfg.BackdropHalfSize, d = 0.05f, L = h * 2f + 0.2f;
            var c = Vector3.up * _cfg.BackdropCenterHeight;
            Vector3[] pos = { c + Vector3.right * h, c + Vector3.left * h, c + Vector3.up * h, c + Vector3.down * h, c + Vector3.forward * h, c + Vector3.back * h };
            Vector3[] scl = { new Vector3(d, L, L), new Vector3(d, L, L), new Vector3(L, d, L), new Vector3(L, d, L), new Vector3(L, L, d), new Vector3(L, L, d) };
            for (int i = 0; i < 6; i++)
            {
                var t = PrimitiveObjectToy.Create(root);
                t.Type = PrimitiveType.Cube;
                t.Flags = PrimitiveFlags.Visible;
                t.Color = color;
                t.MovementSmoothing = 0;
                t.Transform.localPosition = pos[i];
                t.Transform.localRotation = Quaternion.identity;
                t.Transform.localScale = scl[i];
                _walls.Add(t);
            }
            _light = LightSourceToy.Create(root, true);
            // Свет в центре комнаты (на уровне груди), а не у стены — иначе на потолке «горячее пятно», которое не вырезается.
            _light.Transform.localPosition = c + new Vector3(0f, 0.2f, 0.4f);
            _light.Intensity = _cfg.LightIntensity;
            _light.Range = _cfg.LightRange;
            _light.Color = Color.white;
        }

        public void Destroy()
        {
            foreach (var w in _walls) if (!w.IsDestroyed) w.Destroy();
            _walls.Clear();
            if (_light != null && !_light.IsDestroyed) _light.Destroy();
            _light = null;
            _owner = null;
        }
    }

    /// <summary>
    /// Вне карты комплекса SCP:SL не умеет точно синхронизировать позиции — для этого нужны WaypointToy.
    /// Ставим их сеткой 256 м вокруг игрока.
    /// </summary>
    public sealed class Waypoints
    {
        private const float Cell = 250f;
        private readonly Dictionary<Vector3Int, WaypointToy> _cells = new Dictionary<Vector3Int, WaypointToy>();

        public void Ensure(Vector3 p)
        {
            var c = new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var k = new Vector3Int(c.x + dx, c.y + dy, c.z + dz);
                if (_cells.ContainsKey(k)) continue;
                var center = new Vector3((k.x + 0.5f) * Cell, (k.y + 0.5f) * Cell, (k.z + 0.5f) * Cell);
                var w = WaypointToy.Create(center, null, false);
                w.BoundsSize = Vector3.one * WaypointToy.MaxBounds;
                w.PriorityBias = 0f;
                w.Spawn();
                _cells[k] = w;
            }
        }

        public void Clear()
        {
            foreach (var w in _cells.Values) if (!w.IsDestroyed) w.Destroy();
            _cells.Clear();
        }
    }
}
