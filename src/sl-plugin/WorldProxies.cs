using System.Linq;
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
        public bool HasStable(string key, float delay, float since = float.NegativeInfinity)
            => _builtAt.TryGetValue(key, out var t) && t >= since && Time.time - t >= delay;

        /// <summary>Убрать секции, которые не обновлялись с момента t (остались от прошлого места/измерения).</summary>
        public void RemoveOlderThan(float t)
        {
            foreach (var k in new List<string>(_sections.Keys))
                if (!_builtAt.TryGetValue(k, out var bt) || bt < t) Remove(k);
        }

        /// <summary>
        /// Есть ли под ногами (точка в координатах SL) настоящая опора: коробка, верх которой чуть ниже ног,
        /// в секции, которая не менялась delay секунд. Нужна после порталов: платформа Энда и новый портал
        /// появляются в мире Minecraft уже после прибытия — пустая секция «построена», но пола в ней ещё нет.
        /// </summary>
        /// <summary>Для лога: что лежит под ногами (ближайший верх коробки ниже ног в пределах 4 м).</summary>
        public string DescribeUnder(Vector3 feet)
        {
            float best = float.NegativeInfinity; string where = "ничего";
            foreach (var kv in _sections)
                foreach (var t in kv.Value)
                {
                    if (t == null || t.IsDestroyed) continue;
                    Vector3 c = t.Position, sz = t.Scale;
                    if (Mathf.Abs(feet.x - c.x) > Mathf.Abs(sz.x) * 0.5f + 0.3f || Mathf.Abs(feet.z - c.z) > Mathf.Abs(sz.z) * 0.5f + 0.3f) continue;
                    float top = c.y + Mathf.Abs(sz.y) * 0.5f;
                    if (top > feet.y + 0.5f || top < feet.y - 4f || top <= best) continue;
                    best = top;
                    where = $"{kv.Key}: верх на {top - feet.y:0.00} м от ног, обновлена {(_builtAt.TryGetValue(kv.Key, out var bt) ? Time.time - bt : -1f):0.0} с назад";
                }
            return where;
        }

        public bool HasGroundUnder(Vector3 feet, float delay, float since = float.NegativeInfinity)
        {
            foreach (var kv in _sections)
            {
                if (!HasStable(kv.Key, delay, since)) continue;
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

        // ── Пул примитивов ──
        // Создать/удалить сетевой объект в SL дорого (особенно клиенту), а сдвинуть готовый — дёшево. Поэтому коробки
        // не удаляются, а «паркуются» далеко внизу и потом переезжают на новое место. Переезжающая коробка на полсекунды
        // становится нестатичной (чтобы клиент применил новую позицию), потом снова статичной.
        private static readonly Vector3 ParkPos = new Vector3(0f, -4000f, 0f);
        private readonly List<PrimitiveObjectToy> _pool = new List<PrimitiveObjectToy>(); // как стек (Stack в BCL игры нет)
        // коробка → когда снова сделать статичной (по ПОСЛЕДНЕМУ переезду: если переехала дважды подряд, ждём от второго)
        private readonly Dictionary<PrimitiveObjectToy, float> _settling = new Dictionary<PrimitiveObjectToy, float>();
        public int PoolSize => _pool.Count;
        public int SettlingCount => _settling.Count;

        private PrimitiveObjectToy NewToy(Vector3 c, Vector3 s)
        {
            var toy = PrimitiveObjectToy.Create(c, Quaternion.identity, s, null, false);
            toy.Type = PrimitiveType.Cube;
            toy.Flags = _cfg.DebugShowCollision ? (PrimitiveFlags.Collidable | PrimitiveFlags.Visible) : PrimitiveFlags.Collidable;
            toy.Color = new Color(1f, 0.55f, 0.1f, 1f); // отладка: оранжевые (не зелёные — зелёный это ключ)
            toy.MovementSmoothing = 0;
            toy.IsStatic = true;
            toy.Spawn();
            return toy;
        }

        private void Move(PrimitiveObjectToy toy, Vector3 c, Vector3 s)
        {
            if (_phased.Remove(toy)) SetCollidable(toy, true); // коробка переехала (новый блок) — снова твёрдая
            if ((toy.Position - c).sqrMagnitude < 1e-6f && (toy.Scale - s).sqrMagnitude < 1e-6f) return;
            if (toy.IsStatic) toy.IsStatic = false;
            toy.Position = c;
            toy.Scale = s;
            _settling[toy] = Time.time + 0.6f;
        }

        // ── «Сквозь стены» (белая конфета SCP-330): коробки рядом, выше ног, временно без коллизии ──
        private readonly HashSet<PrimitiveObjectToy> _phased = new HashSet<PrimitiveObjectToy>();

        private void SetCollidable(PrimitiveObjectToy t, bool on)
        {
            if (t == null || t.IsDestroyed) return;
            var vis = _cfg.DebugShowCollision ? PrimitiveFlags.Visible : 0;
            t.Flags = on ? (PrimitiveFlags.Collidable | vis) : vis;
        }

        /// <summary>Выключить коллизию у коробок в радиусе r от ног, которые выше ног (стены), — пол остаётся.</summary>
        public void Phase(Vector3 feet, float r)
        {
            var keep = new HashSet<PrimitiveObjectToy>();
            foreach (var list in _sections.Values)
                foreach (var t in list)
                {
                    if (t == null || t.IsDestroyed) continue;
                    Vector3 c = t.Position, h = new Vector3(Mathf.Abs(t.Scale.x), Mathf.Abs(t.Scale.y), Mathf.Abs(t.Scale.z)) * 0.5f;
                    float dx = Mathf.Max(0f, Mathf.Abs(feet.x - c.x) - h.x), dz = Mathf.Max(0f, Mathf.Abs(feet.z - c.z) - h.z);
                    if (dx * dx + dz * dz > r * r) continue;
                    if (c.y + h.y <= feet.y + 0.25f || c.y - h.y >= feet.y + 1.9f) continue;
                    keep.Add(t);
                    if (_phased.Add(t)) SetCollidable(t, false);
                }
            foreach (var t in _phased.ToList())
                if (!keep.Contains(t)) { _phased.Remove(t); SetCollidable(t, true); }
        }

        public void Unphase()
        {
            foreach (var t in _phased) SetCollidable(t, true);
            _phased.Clear();
        }

        /// <summary>Точка внутри какой-нибудь коробки (в том числе выключенной) — там в Minecraft твёрдый блок.</summary>
        public bool ContainsPoint(Vector3 p)
        {
            foreach (var list in _sections.Values)
                foreach (var t in list)
                {
                    if (t == null || t.IsDestroyed) continue;
                    Vector3 c = t.Position, h = new Vector3(Mathf.Abs(t.Scale.x), Mathf.Abs(t.Scale.y), Mathf.Abs(t.Scale.z)) * 0.5f;
                    if (Mathf.Abs(p.x - c.x) <= h.x && Mathf.Abs(p.y - c.y) <= h.y && Mathf.Abs(p.z - c.z) <= h.z) return true;
                }
            return false;
        }

        private void Park(PrimitiveObjectToy toy)
        {
            if (_phased.Remove(toy)) SetCollidable(toy, true);
            if (toy == null || toy.IsDestroyed) return;
            Move(toy, ParkPos, Vector3.one * 0.01f);
            _pool.Add(toy);
        }

        /// <summary>Каждый кадр: переехавшие коробки снова делаем статичными (клиенту не нужно их обновлять).</summary>
        public void Tick()
        {
            if (_settling.Count == 0) return;
            float now = Time.time;
            List<PrimitiveObjectToy> done = null;
            foreach (var kv in _settling)
                if (kv.Key == null || kv.Key.IsDestroyed || now >= kv.Value) (done ??= new List<PrimitiveObjectToy>()).Add(kv.Key);
            if (done == null) return;
            foreach (var t in done)
            {
                _settling.Remove(t);
                if (t != null && !t.IsDestroyed && !t.IsStatic) t.IsStatic = true;
            }
        }

        /// <summary>Коробки ячейки уже на месте у клиента (ни одна не «переезжает» прямо сейчас).</summary>
        public bool Settled(string key)
        {
            if (!_sections.TryGetValue(key, out var list)) return false;
            foreach (var t in list) if (t != null && _settling.ContainsKey(t)) return false;
            return true;
        }

        public void Apply(string key, float[] b)
        {
            _sections.TryGetValue(key, out var old);
            int n = b.Length / 6;
            var list = new List<PrimitiveObjectToy>(n);
            for (int i = 0; i < n; i++)
            {
                int j = i * 6;
                CoordMap.McBoxToSl(b[j], b[j + 1], b[j + 2], b[j + 3], b[j + 4], b[j + 5], out var c, out var s);
                PrimitiveObjectToy toy = null;
                if (old != null && i < old.Count && old[i] != null && !old[i].IsDestroyed) toy = old[i];
                while (toy == null && _pool.Count > 0) { var p = _pool[_pool.Count - 1]; _pool.RemoveAt(_pool.Count - 1); if (p != null && !p.IsDestroyed) toy = p; }
                if (toy == null) toy = NewToy(c, s);
                else Move(toy, c, s);
                list.Add(toy);
            }
            if (old != null) for (int i = n; i < old.Count; i++) Park(old[i]);
            _builtAt[key] = Time.time; // время последнего обновления: новые коробки ещё должны дойти до клиента
            _sections[key] = list;
            ReceivedSinceReset++;
        }

        public void Remove(string key)
        {
            if (!_sections.TryGetValue(key, out var list)) return;
            foreach (var t in list) Park(t);
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

        /// <summary>Конец раунда/выгрузка: удалить всё, включая пул.</summary>
        public void DestroyAll()
        {
            Clear();
            foreach (var t in _pool) if (t != null && !t.IsDestroyed) t.Destroy();
            _pool.Clear();
            _settling.Clear();
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

            // «неон»: цвет ярче 1 — стены светлее при любом освещении (компоновщику важен оттенок, не яркость)
            float glow = Mathf.Max(1f, _cfg.BackdropGlow);
            var color = new Color(_cfg.KeyColorR * glow, _cfg.KeyColorG * glow, _cfg.KeyColorB * glow, 1f);
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
            // Свет для оружия — мягкий, небольшого радиуса и без теней: не засвечивает руки.
            _light = LightSourceToy.Create(root, true);
            _light.Transform.localPosition = c + new Vector3(0f, 0.3f, 0.2f);
            _light.Intensity = _cfg.LightIntensity;
            _light.Range = _cfg.LightRange;
            _light.Color = Color.white;
            try { _light.ShadowType = LightShadows.None; } catch { }

            // Отдельная подсветка стен: по лампе у каждой стены, радиус — только до стены, до оружия в центре не достаёт.
            if (_cfg.WallLightIntensity > 0f)
            {
                float off = Mathf.Max(0.5f, h - _cfg.WallLightRange * 0.45f);
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var dir in dirs)
                {
                    var l = LightSourceToy.Create(root, true);
                    l.Transform.localPosition = c + dir * off;
                    l.Intensity = _cfg.WallLightIntensity;
                    l.Range = _cfg.WallLightRange;
                    l.Color = Color.white;
                    try { l.ShadowType = LightShadows.None; } catch { }
                    _wallLights.Add(l);
                }
            }
        }

        private readonly List<LightSourceToy> _wallLights = new List<LightSourceToy>();

        public void Destroy()
        {
            foreach (var w in _walls) if (!w.IsDestroyed) w.Destroy();
            _walls.Clear();
            foreach (var l in _wallLights) if (!l.IsDestroyed) l.Destroy();
            _wallLights.Clear();
            if (_light != null && !_light.IsDestroyed) _light.Destroy();
            _light = null;
            _owner = null;
        }
    }

    /// <summary>
    /// Вне карты комплекса SCP:SL не умеет точно синхронизировать позиции — для этого нужны WaypointToy.
    /// Ставим их сеткой 256 м вокруг игрока.
    /// </summary>
    /// <summary>
    /// Точки привязки координат SL (WaypointToy). Позиции игрока в сети SL передаются относительно ближайшей
    /// точки и только в её пределах (±128 м), а всего таких точек в игре не больше 255 (вместе с комнатами комплекса).
    /// Поэтому держим точки только вокруг игрока (и вокруг места, куда его сейчас перенесём), дальние удаляем.
    /// </summary>
    public sealed class Waypoints
    {
        private const float Cell = 250f;
        private readonly Dictionary<Vector3Int, WaypointToy> _cells = new Dictionary<Vector3Int, WaypointToy>();

        private static Vector3Int CellOf(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

        public void Ensure(Vector3 p)
        {
            var c = CellOf(p);
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

        /// <summary>Удалить точки дальше одной ячейки от всех переданных позиций (освобождает номера точек).</summary>
        public void Prune(params Vector3[] keepAround)
        {
            var keep = new List<Vector3Int>();
            foreach (var p in keepAround) keep.Add(CellOf(p));
            foreach (var k in new List<Vector3Int>(_cells.Keys))
            {
                bool near = false;
                foreach (var c in keep)
                    if (Mathf.Abs(k.x - c.x) <= 1 && Mathf.Abs(k.y - c.y) <= 1 && Mathf.Abs(k.z - c.z) <= 1) { near = true; break; }
                if (near) continue;
                var w = _cells[k];
                if (w != null && !w.IsDestroyed) w.Destroy();
                _cells.Remove(k);
            }
        }

        public int Count => _cells.Count;

        public void Clear()
        {
            foreach (var w in _cells.Values) if (!w.IsDestroyed) w.Destroy();
            _cells.Clear();
        }
    }
}
