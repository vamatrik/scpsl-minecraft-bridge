using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CustomPlayerEffects;
using InventorySystem.Items.Jailbird;
using InventorySystem.Items.MicroHID.Modules;
using InventorySystem.Items.Scp1509;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Arguments.WarheadEvents;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using UserSettings.ServerSpecific;
using Logger = LabApi.Features.Console.Logger;

namespace SlBridge
{
    public sealed class SlBridgePlugin : Plugin<Config>
    {
        public static SlBridgePlugin Instance { get; private set; }

        public override string Name => "SlBridge";
        public override string Description => "SCP:SL ↔ Minecraft bridge: Minecraft draws the world, SL draws the character and UI. / Мост SCP:SL ↔ Minecraft.";
        public override string Author => "Claude x Vamatrik";
        public override Version Version => new Version(0, 2, 0);
        // Берём версию LabAPI, которая сейчас запущена, — плагин не придётся пересобирать ради номера версии.
        public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;

        private BridgeClient _mc;
        private CollisionWorld _collision;
        private Backdrop _backdrop;
        private Waypoints _waypoints;
        private McWorldItems _worldItems;
        private McMobs _mobs;
        private bool _mobsSolid;
        private readonly McInventory _inv = new McInventory();
        private McMenu _menu;

        private Player _player;
        private Vector3 _spawnSl;
        private bool _hasSpawn;
        private bool _needSpawn;
        private bool _needRespawn;
        private float _respawnAt = -1f;
        private int _poseSeq;
        private float _gravityHoldUntil = -1f;
        private float _nextSpawnReq;
        private float _nextDebug;
        private float _nextUse;
        private float _nextHid;
        private double _lastMarshNext;
        private List<ushort> _useSerials = new List<ushort>();
        private bool _uiOpen;
        private float _chargeUntil = -1f;
        private float _nextCharge;
        private float _last1509;
        private int _armorTier = -1;
        private float _nextHousekeeping;
        private bool _frozen;
        // Жидкость у ног и у глаз (0 — нет, 1 — вода, 2 — лава), присылает Minecraft
        private int _fluidFeet, _fluidEye;
        private bool _f3Held, _f3Combo;
        // отложенный телепорт из Minecraft
        // ── Прибытие после телепорта Minecraft ──
        // Игрок SL при телепорте Minecraft НЕ переносится: сдвигается привязка координат (CoordMap.Origin), так что
        // точка прибытия в Minecraft оказывается ровно под ногами игрока SL. Игрок стоит на месте (без движения),
        // пока коллизии нового места не построены и не дошли до клиента, — никаких дальних телепортов SL.
        private bool _arriving;
        private float _arriveAt;
        private Vector3 _arriveLock;
        private string _arriveHint;
        private string _dim;                  // измерение Minecraft, чьи коллизии сейчас в SL
        private bool _inFacility = true;      // игрок только что появился в комплексе SL — его надо один раз перенести в «пустоту»
        private Vector3 _anchor;              // последнее место игрока в «пустоте», где строится мир Minecraft
        private Vector3 _spawnMc;             // точка появления в Minecraft
        private bool _hasSpawnMc;
        private float _nextVoidTp;
        private float _teleportFreezeAt = -1f;
        private Vector3 _lastGoodPos;
        private Player _cmdPlayer;
        private static readonly Vector3 DefaultGravity = new Vector3(0f, -19.6f, 0f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Число для JSON. Не "R": Mono в SL печатает его буквально как «R».</summary>
        private static string F(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "0";
            return ((double)v).ToString("0.######", Inv);
        }

        /// <summary>
        /// Сколько от Player.Position (центр капсулы) до ног. Position в SL — центр, а не ноги:
        /// раньше ставил центр на уровень ног Minecraft → игрок появлялся наполовину в земле, а Minecraft считал его на блок ниже.
        /// </summary>
        private static float FeetOffset(Player p)
        {
            try
            {
                if (p.RoleBase is IFpcRole fpc && fpc.FpcModule.CharControllerSet)
                {
                    var cc = fpc.FpcModule.CharController;
                    float scale = cc.transform.lossyScale.y;
                    return (cc.height * 0.5f - cc.center.y) * scale;
                }
            }
            catch { }
            return 0.96f;
        }

        private static string Vec(string px, Vector3 v) => $"\"{px}x\":{F(v.x)},\"{px}y\":{F(v.y)},\"{px}z\":{F(v.z)}";

        public override void Enable()
        {
            Instance = this;
            L.Load(Config.Language);
            CoordMap.Origin = new Vector3(Config.OriginX, Config.OriginY, Config.OriginZ);
            NewRoundObjects();
            _menu = new McMenu();
            _menu.Install();

            _mc = new BridgeClient(Config.MinecraftHost, Config.MinecraftPort);
            _mc.OnConnected += () =>
            {
                _mc.Send("{\"t\":\"hello\",\"ver\":2,\"side\":\"scpsl\"}");
                _collision.Clear();
                _mc.Send("{\"t\":\"resync\"}");
                _needSpawn = true;
            };
            _mc.Start();

            PlayerEvents.Joined += OnJoined;
            PlayerEvents.Left += OnLeft;
            PlayerEvents.Spawned += OnSpawned;
            PlayerEvents.ShotWeapon += OnShot;
            PlayerEvents.ProcessedJailbirdMessage += OnJailbird;
            PlayerEvents.SearchedToy += OnSearchedToy;
            PlayerEvents.DroppingItem += OnDropping;
            PlayerEvents.DroppingAmmo += OnDroppingAmmo;
            PlayerEvents.ThrewProjectile += OnThrewProjectile;
            ServerEvents.ExplosionSpawning += OnExplosion;
            PlayerEvents.Dying += OnDying;
            PlayerEvents.Hurting += OnHurting;
            ServerEvents.PickupCreated += OnPickupCreated;
            PlayerEvents.ChangedItem += OnChangedItem;
            PlayerEvents.ProcessedScp1509Message += On1509;
            ServerEvents.WaitingForPlayers += OnWaiting;
            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.WaveRespawning += OnWave;
            ServerEvents.DeadmanSequenceActivating += OnDeadman;
            ServerEvents.LczDecontaminationStarting += OnDecon;
            WarheadEvents.Starting += OnWarhead;
            ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSetting;
            StaticUnityMethods.OnUpdate += OnUpdate;
            SetLimits();
            Logger.Info(L.T("[SlBridge] Включён", "[SlBridge] Enabled"));
        }

        public override void Disable()
        {
            PlayerEvents.Joined -= OnJoined;
            PlayerEvents.Left -= OnLeft;
            PlayerEvents.Spawned -= OnSpawned;
            PlayerEvents.ShotWeapon -= OnShot;
            PlayerEvents.ProcessedJailbirdMessage -= OnJailbird;
            PlayerEvents.SearchedToy -= OnSearchedToy;
            PlayerEvents.DroppingItem -= OnDropping;
            PlayerEvents.DroppingAmmo -= OnDroppingAmmo;
            PlayerEvents.ThrewProjectile -= OnThrewProjectile;
            ServerEvents.ExplosionSpawning -= OnExplosion;
            PlayerEvents.Dying -= OnDying;
            PlayerEvents.Hurting -= OnHurting;
            ServerEvents.PickupCreated -= OnPickupCreated;
            PlayerEvents.ChangedItem -= OnChangedItem;
            PlayerEvents.ProcessedScp1509Message -= On1509;
            ServerEvents.WaitingForPlayers -= OnWaiting;
            ServerEvents.RoundStarted -= OnRoundStarted;
            ServerEvents.WaveRespawning -= OnWave;
            ServerEvents.DeadmanSequenceActivating -= OnDeadman;
            ServerEvents.LczDecontaminationStarting -= OnDecon;
            WarheadEvents.Starting -= OnWarhead;
            ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnSetting;
            StaticUnityMethods.OnUpdate -= OnUpdate;
            _mc?.Stop();
            _collision?.DestroyAll();
            DestroyPad();
            _backdrop?.Destroy();
            _waypoints?.Clear();
            _worldItems?.Clear();
            _mobs?.Clear();
        }

        private void NewRoundObjects()
        {
            _collision = new CollisionWorld(Config);
            _backdrop = new Backdrop(Config);
            _waypoints = new Waypoints();
            _worldItems = new McWorldItems(Config);
            _mobs = new McMobs(Config);
        }

        private static void SetLimits()
        {
            // Карты-заглушки предметов Minecraft — категория «ключ-карты»: разрешаем все 8 слотов.
            try { Server.CategoryLimits[ItemCategory.Keycard] = 8; } catch (Exception e) { Logger.Warn("[SlBridge] лимит карт: " + e.Message); }
        }

        // ───────────── события SL ─────────────

        private void OnWaiting()
        {
            _deconPending = true; // новая сцена — новый контроллер обеззараживания
            _arriving = false;
            _inFacility = true;
            _anchor = Vector3.zero;
            _dim = null;
            CoordMap.Origin = new Vector3(Config.OriginX, Config.OriginY, Config.OriginZ);
            NewRoundObjects();
            _inv.Clear();
            _invWorld = null;
            _player = null;
            _hasSpawn = false;
            _needSpawn = true;
            _gravityHoldUntil = -1f;
            SetLimits();
            if (Config.LockRound) Round.IsLocked = true;
            _mc?.Send("{\"t\":\"resync\"}");
        }

        private void OnJoined(PlayerJoinedEventArgs ev)
        {
            if (ev.Player.IsHost || ev.Player.IsDummy || ev.Player.IsNpc) return;
            Logger.Info("[SlBridge] Player joined the server"); // по этой строке лаунчер запускает оверлей
            if (!string.IsNullOrEmpty(Config.AutoGrantGroup))
            {
                var group = ServerStatic.PermissionsHandler?.GetGroup(Config.AutoGrantGroup);
                if (group != null) ev.Player.UserGroup = group;
                else Logger.Warn(L.T($"[SlBridge] Группа '{Config.AutoGrantGroup}' не найдена в config_remoteadmin.txt", $"[SlBridge] Group '{Config.AutoGrantGroup}' not found in config_remoteadmin.txt"));
            }
            _menu.SendTo(ev.Player.ReferenceHub);
            if (Config.AutoStartRound && !Round.IsRoundStarted)
            {
                if (Config.LockRound) Round.IsLocked = true;
                Round.Start();
            }
        }

        private void OnLeft(PlayerLeftEventArgs ev)
        {
            if (ev.Player == _player) { _player = null; _inv.Clear(); _invWorld = null; }
        }

        private void OnSpawned(PlayerSpawnedEventArgs ev)
        {
            if (ev.Player != _player) return;
            if (ev.Role.RoleTypeId != Config.Role) return;
            if (_needRespawn) { _needRespawn = false; _hasSpawn = false; _mc.Send("{\"t\":\"respawnReq\"}"); }
            else _needSpawn = true;
            // стартовый набор — только при первом появлении в мире (см. spawn) или командой kit start;
            // после смерти — пусто, а с keep_inventory — то, что было
            RestoreKeptInventory();
            ApplyMovement(ev.Player);
            _armorTier = -1;
            _lastGoodPos = Vector3.zero;
            _frozen = false;
            _arriving = false;
            _inFacility = true; // появился в комплексе SL — при следующем прибытии перенесём в «пустоту»
        }

        private void ApplyMovement(Player p)
        {
            p.Gravity = DefaultGravity * Config.GravityScale;
            if (Config.JumpBoostPercent > 0) p.EnableEffect<Lightweight>(Config.JumpBoostPercent, 0f);
        }

        private void OnShot(PlayerShotWeaponEventArgs ev)
        {
            if (ev.Player != _player) return;
            float dmg = Config.WeaponDamage.TryGetValue(ev.FirearmItem.Type, out var v) ? v : Config.DefaultWeaponDamage;
            SendShot(Config.ShotRange, dmg, true);
        }

        private void OnJailbird(PlayerProcessedJailbirdMessageEventArgs ev)
        {
            if (ev.Player != _player) return;
            // Рывок: врезался в моба — бьём заряженным ударом и на миг связываем игрока (рывок останавливается),
            // см. TickJailbirdCharge. Обычный удар — как раньше.
            if (ev.Message == JailbirdMessageType.ChargeStarted) { _chargeUntil = Time.time + 1.6f; return; }
            if (ev.Message == JailbirdMessageType.AttackPerformed)
            {
                if (Time.time < _chargeHitIgnoreUntil) return; // удар рывка уже засчитан
                bool charged = _chargeUntil > 0 && Time.time < _chargeUntil;
                _chargeUntil = -1f;
                _lastMeleeCharged = charged;
                SendMelee(charged ? JailbirdCharge() : MeleeDmg(Config.JailbirdDamage), Config.JailbirdSplash);
            }
        }

        private float _chargeHitIgnoreUntil;
        // мобы рядом (координаты Minecraft): центр по x/z, низ по y, размеры — для попадания рывком Jailbird
        private readonly List<float[]> _mobBoxes = new List<float[]>();

        private void TickJailbirdCharge()
        {
            if (_chargeUntil <= 0f) return;
            if (Time.time > _chargeUntil) { _chargeUntil = -1f; return; }
            var cam = _player.Camera;
            var tip = CoordMap.SlToMc(cam.position + cam.forward * 1.1f);
            var body = CoordMap.SlToMc(_player.Position);
            foreach (var b in _mobBoxes)
            {
                float hx = b[3] * 0.5f + 0.45f, hz = b[5] * 0.5f + 0.45f;
                bool vert = body.y + 0.9f >= b[1] && body.y - 1.0f <= b[1] + b[4];
                bool near = (Mathf.Abs(tip.x - b[0]) <= hx && Mathf.Abs(tip.z - b[2]) <= hz)
                            || (Mathf.Abs(body.x - b[0]) <= hx && Mathf.Abs(body.z - b[2]) <= hz);
                if (!vert || !near) continue;
                _chargeUntil = -1f;
                _chargeHitIgnoreUntil = Time.time + 1.2f;
                try { _player.EnableEffect<Ensnared>(1, Config.JailbirdHitStun); } catch { }
                // удар по направлению на моба (камера может смотреть чуть мимо)
                var from = CoordMap.SlToMc(cam.position);
                var dir = new Vector3(b[0] - from.x, b[1] + b[4] * 0.5f - from.y, b[2] - from.z).normalized;
                float chargeDmg = JailbirdCharge();
                _lastMeleeCharged = true;
                _lastShotDmg = chargeDmg;
                _mc.Send("{\"t\":\"shot\"," + Vec("", from) + "," + Vec("d", dir) + ",\"dmg\":" + F(chargeDmg)
                         + ",\"range\":" + F(Config.MeleeRange + 1.5f) + ",\"breaks\":false,\"kb\":true"
                         + ",\"mr\":" + F(MeleeRadius()) + ",\"moff\":" + F(MeleeOffset()) + ",\"splash\":" + F(Config.JailbirdSplash) + "}");
                return;
            }
        }

        private void On1509(PlayerProcessedScp1509MessageEventArgs ev)
        {
            if (ev.Player != _player) return;
            switch (ev.Message)
            {
                case Scp1509MessageType.AttackPerformed:
                case Scp1509MessageType.AttackPreformedHitWall:
                case Scp1509MessageType.AttackPreformedHitFlesh:
                case Scp1509MessageType.AttackPreformedHitMissed:
                    if (Time.time - _last1509 < 0.25f) return; // одно попадание — одно сообщение
                    _last1509 = Time.time;
                    var tst = SlItemState.TierStats(SlItemState.TierOf(_player.CurrentItem));
                    SendMelee(MeleeDmg(Config.Scp1509Damage) * tst.Dmg, Config.Scp1509Splash, tst.Mine);
                    break;
            }
        }

        // ───── всё, что мешает: обеззараживание, Dead Man's Sequence, боеголовка, отряды ─────
        private void OnRoundStarted()
        {
            SetLimits();
            if (!Config.DisableFacilityEvents) return;
            _deconPending = true; // выключим обеззараживание, как только контроллер готов
        }

        private bool _deconPending = true;

        /// <summary>Один раз за раунд, когда контроллер обеззараживания готов: выключаем его через LabAPI.</summary>
        private void DisableDeconOnce()
        {
            if (!_deconPending || !Config.DisableFacilityEvents) return;
            var dc = LightContainmentZoneDecontamination.DecontaminationController.Singleton;
            if (dc == null) return;
            _deconPending = false;
            try
            {
                Decontamination.Status = LightContainmentZoneDecontamination.DecontaminationController.DecontaminationStatus.Disabled;
                Logger.Info(L.T("[SlBridge] Обеззараживание LCZ выключено: ", "[SlBridge] LCZ decontamination disabled: ") + Decontamination.Status);
            }
            catch (Exception e) { Logger.Warn("[SlBridge] Decontamination.Status: " + e.Message); _deconPending = true; }
        }
        private void OnWave(WaveRespawningEventArgs ev) { if (Config.DisableFacilityEvents) ev.IsAllowed = false; }
        private void OnDeadman(DeadmanSequenceActivatingEventArgs ev) { if (Config.DisableFacilityEvents) ev.IsAllowed = false; }
        private void OnDecon(LczDecontaminationStartingEventArgs ev) { if (Config.DisableFacilityEvents) ev.IsAllowed = false; }
        private void OnWarhead(WarheadStartingEventArgs ev) { if (Config.DisableFacilityEvents) ev.IsAllowed = false; }

        private void SendShot(float range, float dmg, bool breaks) => SendShot(range, dmg, breaks, false);

        /// <summary>Удар ближнего боя по области, как в SL: шар перед камерой; главная цель — полный урон, остальные — splash.</summary>
        private void SendMelee(float dmg, float splash, float mine = 1f)
        {
            if (_player == null || !_mc.Connected) return;
            LogMeleeOnce();
            _lastShotDmg = dmg;
            var cam = _player.Camera;
            _mc.Send("{\"t\":\"shot\"," + Vec("", CoordMap.SlToMc(cam.position)) + "," + Vec("d", CoordMap.DirSlToMc(cam.forward))
                     + ",\"dmg\":" + F(dmg) + ",\"range\":" + F(Config.MeleeRange) + ",\"breaks\":true,\"kb\":true"
                     + ",\"mr\":" + F(MeleeRadius()) + ",\"moff\":" + F(MeleeOffset()) + ",\"splash\":" + F(splash) + ",\"mine\":" + F(mine) + "}");
        }
        private float _lastShotDmg;
        private bool _lastMeleeCharged;

        /// <summary>Сломанный Jailbird SL уберёт сам (после анимации) — он не выпадает при смерти и не сохраняется.</summary>
        private static bool IsBrokenJailbird(Item it)
        {
            try
            {
                if (!(it?.Base is InventorySystem.Items.Jailbird.JailbirdItem jb)) return false;
                var det = typeof(InventorySystem.Items.Jailbird.JailbirdItem).GetField("_deterioration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(jb)
                          as InventorySystem.Items.Jailbird.JailbirdDeteriorationTracker;
                return det != null && det.IsBroken;
            }
            catch { return false; }
        }

        /// <summary>
        /// Jailbird изнашивается и от ударов по мобам, как по игрокам в SL: каждый урон копится (обычный удар — немного,
        /// рывок — сильнее), а если рывок попал, когда дубинка уже сломана, — она взрывается.
        /// </summary>
        private void JailbirdWear(float damage, bool charged)
        {
            try
            {
                if (!(_player.CurrentItem?.Base is InventorySystem.Items.Jailbird.JailbirdItem jb)) return;
                var t = typeof(InventorySystem.Items.Jailbird.JailbirdItem);
                var det = t.GetField("_deterioration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(jb)
                          as InventorySystem.Items.Jailbird.JailbirdDeteriorationTracker;
                // взрывается только дубинка, которая уже светилась красным ДО этого удара (последний рывок),
                // а не та, что сломалась от урона по толпе мобов в этот же удар
                bool wasRed = det != null && det.WearState >= InventorySystem.Items.Jailbird.JailbirdWearState.AlmostBroken;
                var prop = t.GetProperty("TotalMeleeDamageDealt");
                prop?.SetValue(jb, (float)prop.GetValue(jb) + damage);
                det?.RecheckUsage();
                if (charged && wasRed && det != null && det.IsBroken)
                {
                    Logger.Info("[SlBridge] Jailbird сломан — рывок в моба: взрыв");
                    Utils.ExplosionUtils.ServerExplode(_player.ReferenceHub, ExplosionType.Jailbird);
                }
            }
            catch (Exception e) { Logger.Warn("[SlBridge] износ Jailbird: " + e.Message); }
        }

        // ── настоящие числа оружия ближнего боя — прямо из предмета SL (урон, радиус и вынос шара удара) ──
        private InventorySystem.Items.Autosync.MeleeAutoSync HeldMelee => _player?.CurrentItem?.Base as InventorySystem.Items.Autosync.MeleeAutoSync;
        private float MeleeDmg(float cfg) { var m = HeldMelee; return cfg > 0f || m == null ? cfg : m.MeleeDamage; }
        private float MeleeRadius() { var m = HeldMelee; return Config.MeleeHitRadius > 0f || m == null ? Mathf.Max(0.5f, Config.MeleeHitRadius) : m.HitregRadius; }
        private float MeleeOffset() { var m = HeldMelee; return Config.MeleeHitOffset > 0f || m == null ? Mathf.Max(0.5f, Config.MeleeHitOffset) : m.HitregOffset; }
        private float JailbirdCharge()
        {
            if (Config.JailbirdChargeDamage > 0f) return Config.JailbirdChargeDamage;
            try
            {
                if (_player.CurrentItem?.Base is InventorySystem.Items.Jailbird.JailbirdItem jb)
                {
                    var f = typeof(InventorySystem.Items.Jailbird.JailbirdItem).GetField("_chargeDamage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (f != null) return (float)f.GetValue(jb);
                }
            }
            catch { }
            return 200f;
        }
        private bool _meleeLogged;
        private void LogMeleeOnce()
        {
            if (_meleeLogged || HeldMelee == null) return;
            _meleeLogged = true;
            Logger.Info($"[SlBridge] ближний бой из игры: {_player.CurrentItem.Type} урон {HeldMelee.MeleeDamage}, радиус {HeldMelee.HitregRadius}, вынос {HeldMelee.HitregOffset}, заряд Jailbird {JailbirdCharge()}");
        }

        private void SendShot(float range, float dmg, bool breaks, bool knockback)
        {
            if (_player == null || !_mc.Connected) return;
            _lastShotDmg = dmg;
            var cam = _player.Camera;
            _mc.Send("{\"t\":\"shot\"," + Vec("", CoordMap.SlToMc(cam.position)) + "," + Vec("d", CoordMap.DirSlToMc(cam.forward))
                     + ",\"dmg\":" + F(dmg) + ",\"range\":" + F(range) + ",\"breaks\":" + (breaks ? "true" : "false") + ",\"kb\":" + (knockback ? "true" : "false") + "}");
        }

        private void OnSearchedToy(PlayerSearchedToyEventArgs ev)
        {
            if (ev.Player != _player) return;
            if (!_worldItems.TryGetEntity(ev.Interactable, out int id, out var stack)) return;
            // 8 слотов SL; патроны SL слота не занимают, стак Minecraft может долиться в такой же неполный
            bool hasRoom = _player.Items.Count() < 8
                           || (stack != null && stack.Sl != null && stack.Sl.StartsWith("Ammo"))
                           || (stack != null && stack.Sl == null && _inv.All.Any(s => s.Id == stack.Id && s.Count < s.Max));
            if (!hasRoom) { _player.SendHint(L.T("<size=22>Инвентарь полон</size>", "<size=22>Inventory is full</size>"), 1.5f); return; }
            if (stack != null && stack.Sl != null && Enum.TryParse<ItemType>(stack.Sl, out var slType) && OverCategoryLimit(slType))
            { _player.SendHint(L.T("<size=22>Больше таких не унести (лимит SL)</size>", "<size=22>Can't carry more of these (SL limit)</size>"), 1.5f); return; }
            _mc.Send("{\"t\":\"pickup\",\"e\":" + id + "}");
        }

        private void OnDropping(PlayerDroppingItemEventArgs ev)
        {
            if (ev.Player != _player || ev.Item == null) return;
            ev.IsAllowed = false;
            if (_inv.IsStack(ev.Item))
            {
                var s = _inv.Take(_player, ev.Item);
                if (s != null) DropToMinecraft(s, true);
                return;
            }
            // Предмет SL выпадает предметом Minecraft (его иконка из рецептов, иначе мешок) с меткой «что это».
            // Обычный «пикап» SL летел бы криво и проваливался сквозь коллизии. Подобрал на E — снова предмет SL.
            var stack = McStack.ForSl(ev.Item); // с состоянием: обвесы, магазин, заряд
            _player.RemoveItem(ev.Item);
            DropToMinecraft(stack, true);
        }

        // ───── гранаты и SCP-018 ─────
        // Пока летят — Minecraft строит коллизии и вокруг них (иначе за пределами куба вокруг игрока они проваливаются,
        // а SCP-018 не от чего отскакивать). Взрыв SL повторяется в мире Minecraft: ломает блоки, ранит мобов.
        private readonly List<Projectile> _flying = new List<Projectile>();
        private float _nextFocus, _lastFocusAt;
        private readonly Dictionary<Projectile, Vector3> _flyPrev = new Dictionary<Projectile, Vector3>();

        private void OnThrewProjectile(PlayerThrewProjectileEventArgs ev)
        {
            if (ev.Player != _player || ev.Projectile == null) return;
            _flying.Add(ev.Projectile);
            if (_flying.Count > 6) _flying.RemoveAt(0);
        }

        private void TickFlying()
        {
            if (_flying.Count == 0 || Time.time < _nextFocus || !_mc.Connected) return;
            float dt = Time.time - _lastFocusAt;
            _lastFocusAt = Time.time;
            _nextFocus = Time.time + 0.05f;
            _flying.RemoveAll(p => p == null || p.IsDestroyed);
            var pts = new List<string>();
            var seen = new HashSet<Projectile>();
            foreach (var p in _flying)
            {
                seen.Add(p);
                Vector3 now = p.Position;
                // где снаряд будет через 0.25 и 0.5 с — коллизии там строятся заранее, а не когда он уже пролетел
                Vector3 v = _flyPrev.TryGetValue(p, out var prev) && dt > 0.01f && dt < 0.5f ? (now - prev) / dt : Vector3.zero;
                _flyPrev[p] = now;
                foreach (float t in new[] { 0f, 0.25f, 0.5f })
                {
                    if (t > 0f && v.sqrMagnitude < 1f) break;
                    var mc = CoordMap.SlToMc(now + v * t + 0.5f * Physics.gravity * t * t);
                    pts.Add("[" + F(mc.x) + "," + F(mc.y) + "," + F(mc.z) + "]");
                }
            }
            foreach (var k in new List<Projectile>(_flyPrev.Keys)) if (!seen.Contains(k)) _flyPrev.Remove(k);
            _mc.Send("{\"t\":\"focus\",\"p\":[" + string.Join(",", pts) + "]}");
        }

        private void OnExplosion(ExplosionSpawningEventArgs ev)
        {
            if (_player == null || !_hasSpawn || !_mc.Connected) return;
            float power = ev.ExplosionType == ExplosionType.Grenade ? Config.GrenadeExplosionPower
                        : ev.ExplosionType == ExplosionType.SCP018 ? Config.Scp018ExplosionPower
                        : ev.ExplosionType == ExplosionType.Jailbird ? Config.JailbirdExplosionPower : 0f;
            if (power <= 0f) return;
            var mc = CoordMap.SlToMc(ev.Position);
            _mc.Send("{\"t\":\"explode\"," + Vec("", mc) + ",\"power\":" + F(power) + "}");
        }

        // Предметы, которые SL выбросил сам (лимит категории: второе спец. оружие, лишние патроны сверх лимита) —
        // в мире Minecraft пикап SL упал бы сквозь коллизии. Превращаем их в предметы Minecraft на следующем кадре.
        private readonly List<Pickup> _strayPickups = new List<Pickup>();

        /// <summary>Взять ещё один предмет этого типа — превысит лимит категории SL (спец. оружие, гранаты, SCP...)?</summary>
        private bool OverCategoryLimit(ItemType type)
        {
            try
            {
                if (!InventorySystem.InventoryItemLoader.AvailableItems.TryGetValue(type, out var tmpl) || !tmpl.CountsTowardsCategoryLimit) return false;
                var cat = tmpl.Category;
                if (cat == ItemCategory.Armor || cat == ItemCategory.Ammo) return false;
                int limit = Math.Abs(InventorySystem.Configs.InventoryLimits.GetCategoryLimit(cat, _player.ReferenceHub));
                int have = _player.Items.Count(i => !_inv.IsStack(i) && i.Base.Category == cat && i.Base.CountsTowardsCategoryLimit);
                return have + 1 > limit;
            }
            catch { return false; }
        }

        /// <summary>
        /// SCP-127 говорит, как в SL: за обычного моба — фраза об убийстве (зомби — как за SCP-049-2, остальные — как за людей),
        /// за босса — «комментарий» к уничтожению похожего по духу SCP: хранитель → SCP-939 (охотится на звук во тьме),
        /// дракон Края → SCP-106 (хозяин своего «измерения»), иссушитель → SCP-049 (нежить и мор). Как в SL — только если 127 «подружился».
        /// </summary>
        private static readonly RoleTypeId[] HumanRoles = { RoleTypeId.ClassD, RoleTypeId.Scientist, RoleTypeId.FacilityGuard, RoleTypeId.NtfSergeant, RoleTypeId.ChaosRifleman };

        private void Scp127Speak(FirearmItem f127, string entity)
        {
            try
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                var play = typeof(InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase).GetMethod("ServerPlayVoiceLine", flags);
                if (play == null) return;
                RoleTypeId boss = entity == "minecraft:warden" ? RoleTypeId.Scp939
                                : entity == "minecraft:ender_dragon" ? RoleTypeId.Scp106
                                : entity == "minecraft:wither" ? RoleTypeId.Scp049 : RoleTypeId.None;
                if (boss != RoleTypeId.None
                    && InventorySystem.Items.Firearms.Modules.ModulesUtils.TryGetModule(f127.Base, out InventorySystem.Items.Firearms.Modules.Scp127.Scp127TerminationCommentaryVoiceTrigger term, false))
                {
                    UnityEngine.AudioClip clip = null;
                    var t = term.GetType();
                    if (t.GetField("_commentary", flags)?.GetValue(term) is Array arr)
                        foreach (var c in arr)
                        {
                            var ct = c.GetType();
                            if ((RoleTypeId)ct.GetField("Role").GetValue(c) != boss) continue;
                            if (ct.GetField("Lines").GetValue(c) is UnityEngine.AudioClip[] ls && ls.Length > 0) clip = ls[UnityEngine.Random.Range(0, ls.Length)];
                        }
                    if (clip == null && t.GetField("_genericLines", flags)?.GetValue(term) is UnityEngine.AudioClip[] gl && gl.Length > 0)
                        clip = gl[UnityEngine.Random.Range(0, gl.Length)];
                    if (clip != null) { play.Invoke(term, new object[] { clip, null, InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority.Normal }); return; }
                }
                if (InventorySystem.Items.Firearms.Modules.ModulesUtils.TryGetModule(f127.Base, out InventorySystem.Items.Firearms.Modules.Scp127.Scp127OnKillVoiceTrigger kill, false)
                    && kill.GetType().GetField("_onKillLines", flags)?.GetValue(kill) is InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceLineCollection lines)
                {
                    bool zombie = entity.Contains("zombie") || entity.Contains("husk") || entity.Contains("drowned");
                    var role = zombie ? RoleTypeId.Scp0492 : HumanRoles[UnityEngine.Random.Range(0, HumanRoles.Length)];
                    if (lines.TryGetRandom(role, out var clip) || lines.TryGetRandom(RoleTypeId.ClassD, out clip))
                        play.Invoke(kill, new object[] { clip, null, InventorySystem.Items.Firearms.Modules.Scp127.Scp127VoiceTriggerBase.VoiceLinePriority.Normal });
                }
            }
            catch (Exception e) { Logger.Warn("[SlBridge] SCP-127 фраза: " + e.Message); }
        }

        private void Scp127KillAmmo(FirearmItem f127)
        {
            try
            {
                if (!InventorySystem.Items.Firearms.Modules.ModulesUtils.TryGetModule(f127.Base, out InventorySystem.Items.Firearms.Modules.Scp127.Scp127MagazineModule mag)) return;
                typeof(InventorySystem.Items.Firearms.Modules.Scp127.Scp127MagazineModule)
                    .GetMethod("ServerGrantRewardSafe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(mag, new object[] { mag.KillBonus });
            }
            catch (Exception e) { Logger.Warn("[SlBridge] SCP-127 патроны за убийство: " + e.Message); }
        }

        /// <summary>Взял в руку мультитул — на пару секунд его название цветом материала и характеристики (SL сам их не покажет).</summary>
        private void OnChangedItem(PlayerChangedItemEventArgs ev)
        {
            if (ev.Player != _player || ev.NewItem == null || ev.NewItem.Type != ItemType.SCP1509) return;
            string tier = SlItemState.TierOf(ev.NewItem);
            if (tier == null) return;
            var st = SlItemState.TierStats(tier);
            string name = L.T(st.Ru, st.En);
            name = char.ToUpper(name[0]) + name.Substring(1);
            _player.SendHint($"<size=26><b><color={SlItemState.TierColor(tier)}>{name}</color></b></size>\n" +
                             $"<size=17><color=#9A9A9A>ID: SCP-1509 · {L.T("урон", "damage")} ×{st.Dmg:0.##} · {L.T("копание", "digging")} ×{st.Mine:0.#}</color></size>", 2.5f);
        }

        private void OnPickupCreated(PickupCreatedEventArgs ev)
        {
            if (_player == null || !_hasSpawn || _inFacility || ev.Pickup == null || ev.Pickup is Projectile) return;
            _strayPickups.Add(ev.Pickup);
        }

        private void ConvertStrayPickups()
        {
            if (_strayPickups.Count == 0 || !_mc.Connected) return;
            foreach (var p in _strayPickups)
            {
                try
                {
                    if (p == null || p.IsDestroyed || p is Projectile) continue;
                    McStack st = p is AmmoPickup ap ? McStack.ForSl(p.Type, ap.Ammo) : McStack.ForSl(p.Type, 0, null);
                    var mc = CoordMap.SlToMc(p.Position);
                    p.Destroy();
                    _mc.Send("{\"t\":\"drop\",\"item\":" + st.ToJson() + "," + Vec("", mc) + "," + Vec("d", new Vector3(0f, 0.2f, 0f)) + "}");
                    Logger.Info($"[SlBridge] SL выбросил {p.Type} (лимит инвентаря) — стал предметом Minecraft");
                }
                catch (Exception e) { Logger.Warn("[SlBridge] пикап SL → Minecraft: " + e.Message); }
            }
            _strayPickups.Clear();
        }

        private void OnDroppingAmmo(PlayerDroppingAmmoEventArgs ev)
        {
            if (ev.Player != _player) return;
            ev.IsAllowed = false;
            int have = _player.GetAmmo(ev.Type);
            int n = Math.Min(have, ev.Amount);
            if (n <= 0) return;
            _player.SetAmmo(ev.Type, (ushort)(have - n));
            DropToMinecraft(McStack.ForSl(ev.Type, n), true);
        }

        private void DropToMinecraft(McStack s, bool thrown)
        {
            var cam = _player.Camera;
            Vector3 from = CoordMap.SlToMc(cam.position + cam.forward * 0.4f);
            Vector3 d = thrown ? CoordMap.DirSlToMc(cam.forward) : new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0.2f, UnityEngine.Random.Range(-0.3f, 0.3f));
            _mc.Send("{\"t\":\"drop\",\"item\":" + s.ToJson() + "," + Vec("", from) + "," + Vec("d", d) + "}");
        }

        private void OnDying(PlayerDyingEventArgs ev)
        {
            if (ev.Player != _player) return;
            // Как в Minecraft: всё выпадает там, где умер — и стаки Minecraft, и предметы SL (как предметы Minecraft:
            // обычные «пикапы» SL упали бы сквозь коллизии и пропали). Подбираются на E и снова становятся предметами SL.
            // всё, что было, по порядку слотов: стаки Minecraft и предметы SL (с состоянием: обвесы, магазин, заряд)
            var all = new List<McStack>();
            foreach (var it in _player.Items.ToList())
                if (!IsBrokenJailbird(it)) all.Add(_inv.IsStack(it) ? _inv.Get(it).Clone() : McStack.ForSl(it));
            _inv.TakeAll(_player);
            var ammo = new Dictionary<ItemType, ushort>();
            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
            {
                if (!t.ToString().StartsWith("Ammo")) continue;
                ushort n = _player.GetAmmo(t);
                if (n > 0) ammo[t] = n;
            }
            if (_keepInventory)
            {
                // gamerule keep_inventory: вернём всё при возрождении
                _keptItems = all;
                _keptAmmo = ammo;
            }
            else
            {
                foreach (var s in all) DropToMinecraft(s, false);
                foreach (var kv in ammo) DropToMinecraft(McStack.ForSl(kv.Key, kv.Value), false);
            }
            try { _player.ClearInventory(); } catch (Exception e) { Logger.Warn("ClearInventory: " + e.Message); }
            _respawnAt = Time.time + Config.RespawnDelay;
            _needRespawn = true;
            _hasSpawn = false;
            _arriving = false;
        }

        /// <summary>Урон от падения считает Minecraft (вода, сено, слизь его гасят), собственный урон SL от падения отключаем.</summary>
        private void OnHurting(PlayerHurtingEventArgs ev)
        {
            if (ev.Player != _player) return;
            if (ev.DamageHandler is PlayerStatsSystem.UniversalDamageHandler u && u.TranslationId == PlayerStatsSystem.DeathTranslations.Falldown.Id)
            {
                ev.IsAllowed = false;
                if (Config.DebugLog) Logger.Info($"[SlBridge] свой урон SL от падения ({u.Damage:0}) отменён — считает Minecraft");
            }
        }

        private void OnSetting(ReferenceHub hub, ServerSpecificSettingBase setting)
        {
            if (_player == null || hub != _player.ReferenceHub) return;
            switch (setting)
            {
                case SSKeybindSetting k when k.SettingId == McMenu.KeyUse && k.SyncIsPressed:
                    DoUse();
                    break;
                case SSKeybindSetting k when k.SettingId == McMenu.KeyCrafts && k.SyncIsPressed:
                    if (_mc != null && _mc.Connected && !_uiOpen) _mc.Send("{\"t\":\"crafts\"}");
                    break;
                case SSKeybindSetting k when k.SettingId == McMenu.KeyOptions && k.SyncIsPressed:
                    if (_mc != null && _mc.Connected && !_uiOpen) _mc.Send("{\"t\":\"options\"}");
                    break;
                case SSKeybindSetting k when k.SettingId == McMenu.KeyInv && k.SyncIsPressed:
                    DoOpenInventory();
                    break;
                case SSKeybindSetting k when k.SettingId == McMenu.KeyDebug:
                    // F3 сам по себе (без B/G) — показать/скрыть экран отладки Minecraft, как в игре: по отпусканию
                    if (k.SyncIsPressed) { _f3Held = true; _f3Combo = false; }
                    else if (_f3Held)
                    {
                        _f3Held = false;
                        if (!_f3Combo) _mc?.Send("{\"t\":\"debug\",\"what\":\"overlay\"}");
                    }
                    break;
                case SSKeybindSetting k when k.SettingId == McMenu.KeyHitboxes && k.SyncIsPressed && _f3Held:
                    _f3Combo = true;
                    _mc?.Send("{\"t\":\"debug\",\"what\":\"hitboxes\"}");
                    break;
                case SSKeybindSetting k when k.SettingId == McMenu.KeyChunks && k.SyncIsPressed && _f3Held:
                    _f3Combo = true;
                    _mc?.Send("{\"t\":\"debug\",\"what\":\"chunks\"}");
                    break;
                case SSButton b when b.SettingId == McMenu.QuitBtn:
                    RequestQuit();
                    break;
            }
        }

        // ───────────── действия ─────────────

        /// <summary>
        /// Весь инвентарь SL по слотам для Minecraft: стаки Minecraft как есть, предметы SL — бумагой-заглушкой
        /// с меткой и состоянием (их видно в окнах Minecraft и можно класть в верстак, например на перезарядку).
        /// </summary>
        private string InvForMinecraft(out int heldSlot)
        {
            var items = _player.Items.ToList();
            _useSerials = items.Select(i => i.Serial).ToList();
            _slSentSerials = items.Where(i => !_inv.IsStack(i)).Select(i => i.Serial).ToList();
            // в руке Minecraft — только стак Minecraft; предмет SL в руке = пустая рука (иначе заглушку можно «применить»)
            heldSlot = _player.CurrentItem != null && _inv.IsStack(_player.CurrentItem) ? _useSerials.IndexOf(_player.CurrentItem.Serial) : -1;
            return "[" + string.Join(",", items.Select(i => (_inv.IsStack(i) ? _inv.Get(i) : McStack.ForSl(i)).ToJson())) + "]";
        }

        /// <summary>Предметы SL, отправленные в Minecraft вместе с инвентарём (при закрытии окна вернутся оттуда).</summary>
        private List<ushort> _slSentSerials = new List<ushort>();

        private void DoUse()
        {
            if (!_mc.Connected || !_hasSpawn || _uiOpen || Time.time < _nextUse) return;
            _nextUse = Time.time + 0.2f;
            var cam = _player.Camera;
            string inv = InvForMinecraft(out int held);
            _mc.Send("{\"t\":\"use\"," + Vec("", CoordMap.SlToMc(cam.position)) + "," + Vec("d", CoordMap.DirSlToMc(cam.forward))
                     + ",\"inv\":" + inv + ",\"heldSlot\":" + held + "}");
        }

        private void DoOpenInventory()
        {
            if (!_mc.Connected || !_hasSpawn || _uiOpen) return;
            string inv = InvForMinecraft(out int held);
            _mc.Send("{\"t\":\"openInv\",\"inv\":" + inv + ",\"heldSlot\":" + held + "}");
        }

        /// <summary>Попросить лаунчер закрыть всё (SL, Minecraft, сервер, оверлей). Лаунчер следит за этим файлом.</summary>
        private void RequestQuit()
        {
            try { SaveInventoryIfChanged(true); } catch { }
            try
            {
                string f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(L.FilePath), "quit.req");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
                System.IO.File.WriteAllText(f, DateTime.Now.ToString("O"));
                _player.SendHint(L.T("<size=22>Закрываю всё…</size>", "<size=22>Closing everything…</size>"), 5f);
            }
            catch (Exception e) { Logger.Warn("quit.req: " + e.Message); }
        }

        public bool SendMinecraftCommand(string text, Player sender)
        {
            if (_mc == null || !_mc.Connected) return false;
            _cmdPlayer = sender;
            _mc.Send("{\"t\":\"cmd\",\"text\":\"" + MiniJson.Escape(text) + "\"}");
            return true;
        }

        private void GiveBack(McStack s)
        {
            if (s == null) return;
            if (!string.IsNullOrEmpty(s.Sl)) { GiveSl(s); return; }
            int left = _inv.Add(_player, s);
            if (left > 0)
            {
                var rest = s.Clone();
                rest.Count = left;
                DropToMinecraft(rest, false);
                _player.SendHint(L.T("<size=22>Инвентарь полон — часть выпала</size>", "<size=22>Inventory is full — some items dropped</size>"), 1.5f);
            }
        }

        /// <summary>Предмет SCP:SL, скрафченный в Minecraft: выдаём настоящий предмет (патроны — по 80 за штуку).</summary>
        private readonly List<(Item item, string state, float at)> _reapply = new List<(Item, string, float)>();

        private void TickReapply()
        {
            if (_reapply.Count == 0) return;
            for (int i = _reapply.Count - 1; i >= 0; i--)
            {
                var r = _reapply[i];
                if (Time.time < r.at) continue;
                _reapply.RemoveAt(i);
                try
                {
                    if (r.item is MicroHIDItem hid && !r.item.IsDestroyed)
                    {
                        float e = hid.Energy;
                        hid.Energy = e > 0.5f ? e - 0.01f : e + 0.01f; // заметное изменение (заряд шлётся байтом) — чтобы ушло клиенту
                        hid.Energy = e;
                    }
                }
                catch { }
            }
        }

        private void GiveSl(McStack s)
        {
            if (!Enum.TryParse(s.Sl, out ItemType type) || type == ItemType.None)
            {
                Logger.Warn("Неизвестный предмет SL из рецепта: " + s.Sl);
                return;
            }
            if (type.ToString().StartsWith("Ammo"))
            {
                int add = (s.Ammo > 0 ? s.Ammo : 80) * s.Count;
                int now = _player.GetAmmo(type);
                _player.SetAmmo(type, (ushort)Math.Min(ushort.MaxValue, now + add));
                _player.SendHint($"<size=22>+{add} {s.Name}</size>", 2f);
                return;
            }
            int given = 0;
            for (int i = 0; i < s.Count; i++)
            {
                Item added = _player.Items.Count() >= 8 ? null : _player.AddItem(type, InventorySystem.Items.ItemAddReason.AdminCommand);
                if (added != null)
                {
                    SlItemState.Apply(added, s.State); // заряд, магазин, обвесы — как были
                    if (added.Type == ItemType.SCP1509 && SlItemState.TierOf(added) is string tier)
                        _player.SendHint("<size=22>" + L.T("Мультитул: ", "Multitool: ") + L.T(SlItemState.TierStats(tier).Ru, SlItemState.TierStats(tier).En) + "</size>", 2.5f);
                    // заряд Micro HID клиент SL иногда не получает сразу у нового предмета (показывает 100%) — повторим чуть позже
                    if (added is MicroHIDItem && !string.IsNullOrEmpty(s.State)) _reapply.Add((added, s.State, Time.time + 0.5f));
                }
                if (added == null)
                {
                    var rest = s.Clone();
                    rest.Count = s.Count - i;
                    DropToMinecraft(rest, false);
                    _player.SendHint(L.T("<size=22>Инвентарь полон — предмет выпал рядом</size>", "<size=22>Inventory is full — the item dropped nearby</size>"), 2f);
                    break;
                }
                given++;
            }
            if (given > 0 && Config.DebugLog) Logger.Info($"Выдан предмет SL {type} ×{given}");
        }

        // ───────────── каждый кадр ─────────────

        private void OnUpdate()
        {
            if (_mc == null) return;
            _mc.Drain(Handle);
            DisableDeconOnce();
            _collision.Process(Config.CollisionBudgetPerFrame);
            _collision.Tick();

            if (_player == null || !_player.IsOnline)
            {
                _player = Player.List.FirstOrDefault(p => p.IsPlayer && p.IsReady && !p.IsHost && !p.IsDummy && !p.IsNpc);
                if (_player == null) return;
                _inv.Clear();
                if (_player.Role != Config.Role) _player.SetRole(Config.Role, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                else { _needSpawn = true; ApplyMovement(_player); }
                return;
            }

            _worldItems.Tick();

            if (!_player.IsAlive)
            {
                if (_respawnAt < 0) { _respawnAt = Time.time + Config.RespawnDelay; _needRespawn = true; _hasSpawn = false; } // умер не через Dying
                if (Time.time >= _respawnAt)
                {
                    _respawnAt = -1f;
                    _player.SetRole(Config.Role, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                }
                return;
            }

            if (_needSpawn && _mc.Connected && Time.time >= _nextSpawnReq)
            {
                _nextSpawnReq = Time.time + 2f;
                _mc.Send("{\"t\":\"spawnReq\"}");
            }

            Vector3 pos = _player.Position;
            _waypoints.Ensure(pos);
            _backdrop.Attach(_player);

            // Прибытие после телепорта / заморозка над непостроенным миром / плавание.
            bool noclip = IsNoclip();
            if (_arriving) ArrivalTick(pos);
            else if (_hasSpawn)
            {
                if (!noclip) FreezeIfNotBuilt(pos); // в ноуклипе летаем где угодно, как наблюдатель в Minecraft
                else if (_frozen) _frozen = false;
                if (!_inFacility && !_frozen) _anchor = pos;
                if (!_frozen) ApplySwimGravity(noclip);
                PadTick(pos);
            }

            if (Time.time >= _nextHousekeeping)
            {
                _nextHousekeeping = Time.time + 1f;
                _waypoints.Prune(pos);
                SaveInventoryIfChanged();
                // трупы SL (их физика в мире Minecraft глючит), кровь и дырки от пуль
                if (Config.CleanupRagdolls)
                    foreach (var r in new List<Ragdoll>(Ragdoll.List)) try { r.Destroy(); } catch { }
                if (Config.CleanupDecals)
                    foreach (var t in new[] { Decals.DecalPoolType.Blood, Decals.DecalPoolType.Bullet, Decals.DecalPoolType.Buckshot, Decals.DecalPoolType.GlassCrack })
                        Utils.Networking.NetworkUtils.SendToAuthenticated(new CommandSystem.Commands.RemoteAdmin.Cleanup.DecalCleanupMessage(t, int.MaxValue));
                // Lightweight (прыжок как в Minecraft) — проверяем, что не слетел после возрождения/эффектов
                if (Config.JumpBoostPercent > 0 && (!_player.TryGetEffect<Lightweight>(out var lw) || lw.Intensity != Config.JumpBoostPercent))
                    _player.EnableEffect<Lightweight>(Config.JumpBoostPercent, 0f);
                // без тумана SL: «комнату-фон» можно отодвинуть, и фонарик меньше засвечивает её зелёные стены
                if (Config.NoFog && (!_player.TryGetEffect<FogControl>(out var fog) || fog.Intensity != 1))
                    _player.EnableEffect<FogControl>(1, 0f); // 1 = FogType.None
                // броня SL → броня Minecraft
                int tier = _player.Items.Any(i => i.Type == ItemType.ArmorHeavy) ? 3 : _player.Items.Any(i => i.Type == ItemType.ArmorCombat) ? 2 : _player.Items.Any(i => i.Type == ItemType.ArmorLight) ? 1 : 0;
                if (tier != _armorTier && _mc.Connected) { _armorTier = tier; _mc.Send("{\"t\":\"armor\",\"tier\":" + tier + "}"); }
            }

            if (_hasSpawn && !_arriving && _hasSpawnMc && pos.y < CoordMap.Origin.y + Config.VoidY && Time.time >= _nextVoidTp)
            {
                // выпал сквозь мир — просим Minecraft вернуть на точку появления (дальше — обычное «прибытие»)
                _nextVoidTp = Time.time + 3f;
                _player.SendHint(L.T("<size=22>Ты выпал из мира — возвращаю на точку появления.</size>", "<size=22>You fell out of the world — returning you to spawn.</size>"), 4f);
                SendMinecraftCommand($"execute in minecraft:overworld run tp @s {F(_spawnMc.x)} {F(_spawnMc.y)} {F(_spawnMc.z)}", null);
            }

            PollMelee();
            TickFlying();
            ConvertStrayPickups();
            TickReapply();

            TickJailbirdCharge();


            if (Config.DebugLog && Time.time >= _nextDebug)
            {
                _nextDebug = Time.time + 5f;
                var mcp = CoordMap.SlToMc(pos);
                Logger.Info($"[SlBridge] mc={_mc.Connected} spawn={_hasSpawn} секций={_collision.SectionCount} боксов={_collision.BoxCount} очередь={_collision.Pending} " +
                            $"MC=({mcp.x:0.0},{mcp.y:0.0},{mcp.z:0.0})");
            }

            // Позу шлём только после появления в мире Minecraft — иначе Minecraft утащит игрока в «комплекс SL».
            if (_mc.Connected && _hasSpawn) SendPose();
        }

        private static FieldInfo _marshCooldown;

        /// <summary>
        /// Minecraft переместил игрока (портал, жемчуг, /tp, возрождение, первый вход). Игрока SL НЕ телепортируем:
        /// сдвигаем привязку координат (CoordMap.Origin) так, чтобы точка прибытия оказалась прямо под ним, и держим его
        /// на месте (без движения), пока коллизии нового места не построены и не простояли UnfreezeDelay — то есть
        /// точно есть и у клиента SL. Единственный перенос в SL — после появления роли в комплексе: один раз в «пустоту».
        /// </summary>
        private void BeginArrival(double x, double y, double z, string dim, int seq, string hint)
        {
            if (_player == null) return;
            Vector3 p;
            if (_inFacility)
            {
                // из комплекса SL — в «пустоту»: на прошлое место или туда, где эта точка была бы при исходной привязке
                p = _anchor != Vector3.zero ? _anchor
                    : new Vector3(Config.OriginX - (float)x, Config.OriginY + (float)(y + 0.05), Config.OriginZ + (float)z) + Vector3.up * FeetOffset(_player);
                _waypoints.Ensure(p);
                _player.Position = p;
            }
            else p = _player.Position;

            float feet = p.y - FeetOffset(_player);
            CoordMap.Origin = new Vector3(p.x + (float)x, feet - (float)(y + 0.05), p.z - (float)z); // McToSl(x, y+0.05, z) == ноги игрока

            _collision.Clear();
            _worldItems.Clear();
            _mobs.Clear();
            _mobBoxes.Clear();
            if (!string.IsNullOrEmpty(dim)) _dim = dim;
            if (seq >= 0) _poseSeq = seq; // позы SL сразу соответствуют точке прибытия
            _mc.Send("{\"t\":\"resync\"}"); // Minecraft заново пришлёт все ячейки вокруг нового места

            _arriving = true;
            _arriveAt = Time.time;
            _arriveLock = p;
            _arriveHint = hint;
            _hasSpawn = true;
            _needSpawn = false;
            _frozen = true;
            _lastGoodPos = p;
            _player.Gravity = Vector3.zero;
            try { _player.EnableEffect<Ensnared>(1, 0f); } catch { } // не даём ходить, пока ждём — без рассинхрона
            _fluidFeet = _fluidEye = 0; _climb = false; // жидкость/лестницу у нового места Minecraft пришлёт заново
            // Страховочная площадка прямо под ногами: создаётся сразу, так что к моменту, когда игрока отпустят,
            // она точно есть и у клиента SL, даже если коробки мира ещё «в пути». Исчезает, когда отойдёшь.
            DestroyPad();
            _pad = PrimitiveObjectToy.Create(new Vector3(p.x, feet - 0.26f, p.z), Quaternion.identity, new Vector3(1.4f, 0.5f, 1.4f), null, false);
            _pad.Type = PrimitiveType.Cube;
            _pad.Flags = Config.DebugShowCollision ? (AdminToys.PrimitiveFlags.Collidable | AdminToys.PrimitiveFlags.Visible) : AdminToys.PrimitiveFlags.Collidable;
            _pad.Color = new Color(0.2f, 0.4f, 1f, 1f);
            _pad.IsStatic = true;
            _pad.Spawn();
            _padUntil = float.MaxValue;
            Logger.Info($"[SlBridge/TP] прибытие → {_dim} ({x:0.0}, {y:0.0}, {z:0.0}), игрок {(_inFacility ? "перенесён из комплекса" : "стоит на месте")}, ждём коллизии");
        }

        private PrimitiveObjectToy _pad;
        private float _padUntil, _watchFallUntil;

        private void DestroyPad()
        {
            if (_pad != null && !_pad.IsDestroyed) _pad.Destroy();
            _pad = null;
        }

        /// <summary>После прибытия: убрать площадку, когда отошёл; записать в лог, если всё-таки провалился.</summary>
        private void PadTick(Vector3 pos)
        {
            if (_pad != null)
            {
                var d = pos - _arriveLock; d.y = 0f;
                if (Time.time > _padUntil || d.sqrMagnitude > 2.25f) DestroyPad();
            }
            if (_watchFallUntil > 0f)
            {
                if (Time.time > _watchFallUntil) _watchFallUntil = -1f;
                else if (_arriveLock.y - pos.y > 2f)
                {
                    _watchFallUntil = -1f;
                    Logger.Warn($"[SlBridge/TP] упал после прибытия на {_arriveLock.y - pos.y:0.0} м; под ногами: {_collision.DescribeUnder(pos + Vector3.down * FeetOffset(_player))}; " +
                                $"на месте прибытия: {_collision.DescribeUnder(_arriveLock + Vector3.down * FeetOffset(_player))}; переезжают {_collision.SettlingCount}, в запасе {_collision.PoolSize}");
                }
            }
        }

        private void ArrivalTick(Vector3 pos)
        {
            if ((pos - _arriveLock).sqrMagnitude > 0.04f) _player.Position = _arriveLock;
            var mc = CoordMap.SlToMc(_arriveLock);
            float feetY = mc.y - FeetOffset(_player) - 0.05f;
            int cx = Mathf.FloorToInt(mc.x / 8f), cy = Mathf.FloorToInt(feetY / 8f), cz = Mathf.FloorToInt(mc.z / 8f);
            float d = Config.UnfreezeDelay;
            // Не только ячейка под ногами, а все 3×3 вокруг (и ряд ниже, если ноги у дна ячейки): сразу после
            // прибытия можно идти в любую сторону — коробки там уже есть и у клиента SL.
            bool cells = true;
            bool lowInCell = feetY - cy * 8f <= 1.5f;
            for (int ix = cx - 1; ix <= cx + 1 && cells; ix++)
                for (int iz = cz - 1; iz <= cz + 1 && cells; iz++)
                    for (int iy = lowInCell ? cy - 1 : cy; iy <= cy && cells; iy++)
                    {
                        string k = $"{ix},{iy},{iz}";
                        cells = _collision.HasStable(k, d, _arriveAt) && _collision.Settled(k);
                    }
            // опора: коробка под ногами — или вода/лава (в них не стоят, а плавают)
            bool ready = cells && (_fluidFeet != 0 || _collision.HasGroundUnder(_arriveLock + Vector3.down * FeetOffset(_player), d, _arriveAt));
            if (!ready && Time.time - _arriveAt < Config.TeleportMaxWait) return;
            if (_fluidFeet != 0) DestroyPad(); else _padUntil = Time.time + 8f;
            _watchFallUntil = Time.time + 3f;

            _arriving = false;
            _inFacility = false;
            _frozen = false;
            _teleportFreezeAt = -1f;
            _player.Gravity = DefaultGravity * Config.GravityScale;
            try { _player.DisableEffect<Ensnared>(); } catch { }
            _lastGoodPos = _arriveLock;
            _anchor = _arriveLock;
            Logger.Info($"[SlBridge/TP] готово через {Time.time - _arriveAt:0.0} с, пол {(ready ? "есть" : "НЕ найден (таймаут)")}, секций {_collision.SectionCount}" +
                        (ready ? "" : $", ячейка {cx},{cy},{cz}: {(_collision.Has($"{cx},{cy},{cz}") ? "есть" : "нет")}, под ногами: {_collision.DescribeUnder(_arriveLock + Vector3.down * FeetOffset(_player))}"));
            if (_arriveHint != null) _player.SendHint(_arriveHint, 4f);
        }

        private string KeyBelow(int cx, int cy, int cz) => $"{cx},{cy - 1},{cz}";

        /// <summary>SCP-268 (кепка) — эффект невидимости SL: в Minecraft мобы тоже теряют игрока.</summary>
        private bool IsInvisible()
        {
            try { return _player.TryGetEffect<CustomPlayerEffects.Invisible>(out var e) && e.IsEnabled; } catch { return false; }
        }

        private bool IsNoclip()
        {
            try { return _player.RoleBase is IFpcRole fpc && fpc.FpcModule.Noclip.IsActive; } catch { return false; }
        }

        private bool IsSneaking()
        {
            // присед (C) или красться — оба считаем «присел»
            try { return _player.RoleBase is IFpcRole fpc && (fpc.FpcModule.CurrentMovementState == PlayerMovementState.Sneaking || fpc.FpcModule.CurrentMovementState == PlayerMovementState.Crouching); } catch { return false; }
        }

        /// <summary>
        /// Плавание как в Minecraft: под водой всплываешь (присел — тонешь), у поверхности покачиваешься,
        /// в лаве всплываешь медленнее. Коллизий у жидкостей нет — только гравитация SL.
        /// </summary>
        private void ApplySwimGravity(bool noclip)
        {
            // вертикальная скорость по позиции (сглаженная)
            float now = Time.time, dt = now - _lastVyTime;
            if (dt > 0.02f)
            {
                float vy = (_player.Position.y - _lastVyY) / dt;
                _vy = dt > 0.5f ? 0f : Mathf.Lerp(_vy, vy, 0.5f);
                _lastVyY = _player.Position.y;
                _lastVyTime = now;
            }

            // Целевая вертикальная скорость: как в Minecraft — в воде всплываешь (присел — тонешь), у поверхности держишься,
            // на лестнице смотришь вверх — лезешь, вниз — спускаешься, прямо — висишь. Гравитация SL подгоняет к ней скорость.
            float? target = null;
            if (!noclip)
            {
                float look = _player.Camera.forward.y;
                // под водой: смотришь вниз (или присел) — ныряешь, иначе всплываешь
                if (_fluidEye != 0) target = IsSneaking() ? -Config.SwimSink : _fluidEye == 2 ? Config.LavaRise : Config.SwimRise;
                else if (_climb) target = IsSneaking() ? 0f : look > 0.3f ? Config.ClimbSpeed : look < -0.5f ? -Config.ClimbSpeed : 0f;
                else if (_fluidFeet != 0) target = -0.3f; // голова над водой — держимся на плаву
            }
            Vector3 g;
            if (target == null) g = DefaultGravity * Config.GravityScale;
            else g = new Vector3(0f, Mathf.Clamp((target.Value - _vy) * 8f, -25f, 25f), 0f);

            // не дёргаем сеть по мелочи: обновляем, если изменилось заметно, и не чаще 10 раз в секунду
            var cur = _player.Gravity;
            bool normal = target == null;
            if (normal ? (cur - g).sqrMagnitude < 0.0001f : (Mathf.Abs(cur.y - g.y) < 1f || now < _nextGravitySet)) return;
            _nextGravitySet = now + 0.1f;
            _player.Gravity = g;
        }

        private float _vy, _lastVyY, _lastVyTime, _nextGravitySet;
        private bool _climb;

        private void FreezeIfNotBuilt(Vector3 pos)
        {
            var mc = CoordMap.SlToMc(pos);
            int cx = Mathf.FloorToInt(mc.x / 8f), cz = Mathf.FloorToInt(mc.z / 8f);
            int cyFeet = Mathf.FloorToInt((mc.y - FeetOffset(_player) - 0.05f) / 8f);
            string key = $"{cx},{cyFeet},{cz}";
            // Размороженному хватает «построено»; замороженного (телепорт, портал) отпускаем, только когда коробки
            // успели дойти до клиента SL — иначе он падает сквозь ещё не появившийся у него пол.
            float feetInCell = (mc.y - FeetOffset(_player) - 0.05f) - cyFeet * 8f; // 0..8: высота ног внутри ячейки
            bool built = _frozen ? _collision.HasStable(key, Config.UnfreezeDelay)
                                   && (feetInCell > 1.5f || _collision.HasStable(KeyBelow(cx, cyFeet, cz), Config.UnfreezeDelay))
                                 : _collision.Has(key);
            if (built && _frozen && _teleportFreezeAt >= 0f)
            {
                // после телепорта/портала — ещё и настоящий пол под ногами (или прошло TeleportMaxWait: значит, там правда пусто)
                Vector3 feet = pos + Vector3.down * FeetOffset(_player);
                built = _collision.HasGroundUnder(feet, Config.UnfreezeDelay) || Time.time - _teleportFreezeAt > Config.TeleportMaxWait;
            }
            if (built) _teleportFreezeAt = -1f;
            if (built)
            {
                _lastGoodPos = pos;
                if (_frozen) { _frozen = false; _player.Gravity = DefaultGravity * Config.GravityScale; }
                return;
            }
            if (!_frozen)
            {
                Logger.Info($"[SlBridge/TP] заморозка: ячейка под ногами {key} не построена (секций {_collision.SectionCount}, очередь {_collision.Pending})");
                _frozen = true;
                _player.Gravity = Vector3.zero;
                if (_lastGoodPos != Vector3.zero) _player.Position = _lastGoodPos; // откат на последнее «построенное» место
            }
            else if (_lastGoodPos != Vector3.zero && (pos - _lastGoodPos).sqrMagnitude > 0.25f)
                _player.Position = _lastGoodPos; // заморожен — стоим на месте
        }

        private void PollMelee()
        {
            var item = _player.CurrentItem;
            if (item is MicroHIDItem hid)
            {
                // ЛКМ (PrimaryFire) — быстрый луч; ПКМ (ChargeFire) — долгий заряд, бьёт сильнее и дальше.
                if (hid.Phase == MicroHidPhase.Firing && Time.time >= _nextHid)
                {
                    _nextHid = Time.time + 0.1f;
                    bool charged = hid.FiringMode == MicroHidFiringMode.ChargeFire;
                    SendShot(charged ? Config.MicroHidChargeRange : Config.MicroHidRange,
                             charged ? Config.MicroHidChargeDamagePerTick : Config.MicroHidDamagePerTick, true);
                }
            }
            else if (item is MarshmallowItem mm)
            {
                try
                {
                    if (_marshCooldown == null) _marshCooldown = mm.Base.GetType().GetField("_cooldown", BindingFlags.Instance | BindingFlags.NonPublic);
                    var cd = _marshCooldown?.GetValue(mm.Base);
                    if (cd != null)
                    {
                        var prop = cd.GetType().GetProperty("NextUse");
                        double next = prop != null ? (double)prop.GetValue(cd) : 0;
                        if (next > _lastMarshNext + 0.01 && _lastMarshNext > 0) SendShot(Config.MeleeRange, Config.MarshmallowDamage, true, true);
                        _lastMarshNext = next;
                    }
                }
                catch { }
            }
        }

        private void SendPose()
        {
            var cam = _player.Camera;
            Vector3 eye = CoordMap.SlToMc(cam.position);
            CoordMap.DirToMcAngles(cam.forward, out float yaw, out float pitch);
            float feet = CoordMap.SlToMc(_player.Position).y - FeetOffset(_player);
            _mc.Send("{\"t\":\"pose\",\"x\":" + F(eye.x) + ",\"y\":" + F(eye.y) + ",\"z\":" + F(eye.z)
                + ",\"fy\":" + F(feet) + ",\"yaw\":" + F(yaw) + ",\"pitch\":" + F(pitch) + ",\"seq\":" + _poseSeq + ",\"lamp\":" + LampLevel() + ",\"beam\":" + (_player.CurrentItem?.Type == ItemType.Flashlight ? 1 : 0) + ",\"nc\":" + (IsNoclip() ? 1 : 0) + ",\"inv\":" + (IsInvisible() ? 1 : 0) + "}");
        }

        /// <summary>Лампа (Lantern) или фонарик в руке и включены — светят в Minecraft.</summary>
        private int LampLevel()
        {
            if (!(_player.CurrentItem is LightItem li) || !li.IsEmitting) return 0;
            return li.Type == ItemType.Lantern ? Config.LanternLight : li.Type == ItemType.Flashlight ? Config.FlashlightLight : 0;
        }

        /// <summary>Стартовый набор (команда kit start или самое первое появление в мире).</summary>
        public void GiveLoadoutTo(Player p)
        {
            foreach (var it in Config.Loadout)
            {
                if (p.Items.Count() >= 8) continue;
                var added = p.AddItem(it);
                // стартовый инструмент — деревянный мультитул (мачете SL слабого уровня)
                if (added != null && it == ItemType.SCP1509 && !string.IsNullOrEmpty(Config.StarterMultitoolTier))
                    SlItemState.Apply(added, "tier=" + Config.StarterMultitoolTier);
            }
            foreach (var kv in Config.Ammo) p.SetAmmo(kv.Key, (ushort)Math.Min(ushort.MaxValue, p.GetAmmo(kv.Key) + kv.Value));
        }

        // Мир, в котором стартовый набор уже выдан, — список в %APPDATA%\SlBridge\kits.txt (по папке мира Minecraft).
        private static string KitsFile => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(L.FilePath), "kits.txt");

        private void GiveFirstKitIfNew(string world)
        {
            if (_player == null || string.IsNullOrEmpty(world)) return;
            try
            {
                var lines = System.IO.File.Exists(KitsFile) ? System.IO.File.ReadAllLines(KitsFile).ToList() : new List<string>();
                if (lines.Contains(world)) return;
                lines.Add(world);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(KitsFile));
                System.IO.File.WriteAllLines(KitsFile, lines);
            }
            catch (Exception e) { Logger.Warn("[SlBridge] kits.txt: " + e.Message); }
            GiveLoadoutTo(_player);
            Logger.Info("[SlBridge] Стартовый набор выдан (первое появление в мире «" + world + "»)");
        }

        // ── Сохранение инвентаря при смерти (gamerule keep_inventory в Minecraft) ──
        private bool _keepInventory;
        private List<McStack> _keptItems;
        private Dictionary<ItemType, ushort> _keptAmmo;

        // ── инвентарь SL сохраняется в папке мира Minecraft (slbridge_inventory.json) ──
        private string _invWorld;      // мир, чей инвентарь сейчас у игрока (null — ещё не загружен)
        private string _invSavedSig;

        private void LoadSavedInventory(string world, Dictionary<string, object> inv)
        {
            if (_player == null || string.IsNullOrEmpty(world) || world == _invWorld) return;
            if (_invWorld != null)
            {
                // зашёл в другой мир, не перезапуская SL — вещи прошлого мира остались в его файле
                _inv.TakeAll(_player);
                try { _player.ClearInventory(); } catch { }
            }
            _invWorld = world;
            _invSavedSig = null;
            _keptItems = null; _keptAmmo = null;
            if (inv == null) return;
            int n = 0;
            foreach (var o in inv.Arr("items"))
                if (o is Dictionary<string, object> so) { var st = McStack.FromMsg(so); if (st != null) { GiveBack(st); n++; } }
            if (inv.TryGetValue("ammo", out var a) && a is Dictionary<string, object> ammo)
                foreach (var kv in ammo)
                    if (Enum.TryParse<ItemType>(kv.Key, out var at) && kv.Value is double cnt) _player.SetAmmo(at, (ushort)Math.Min(ushort.MaxValue, cnt));
            Logger.Info($"[SlBridge] инвентарь мира «{world}» восстановлен: предметов {n}");
        }

        /// <summary>Раз в секунду: если инвентарь изменился — отправить в Minecraft, он запишет в папку мира.</summary>
        private void SaveInventoryIfChanged(bool force = false)
        {
            if (_player == null || _invWorld == null || !_mc.Connected) return;
            var items = new List<string>();
            if (_keptItems != null) foreach (var s in _keptItems) items.Add(s.ToJson()); // умер с keep_inventory
            else foreach (var it in _player.Items.ToList()) if (!IsBrokenJailbird(it)) items.Add(_inv.IsStack(it) ? _inv.Get(it).ToJson() : McStack.ForSl(it).ToJson());
            var ammo = new List<string>();
            var src = _keptAmmo;
            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
            {
                if (!t.ToString().StartsWith("Ammo")) continue;
                ushort n = src != null ? (src.TryGetValue(t, out var k) ? k : (ushort)0) : _player.GetAmmo(t);
                if (n > 0) ammo.Add("\"" + t + "\":" + n);
            }
            string body = "\"items\":[" + string.Join(",", items) + "],\"ammo\":{" + string.Join(",", ammo) + "}";
            if (!force && body == _invSavedSig) return;
            _invSavedSig = body;
            _mc.Send("{\"t\":\"invSave\",\"world\":\"" + MiniJson.Escape(_invWorld) + "\"," + body + "}");
        }

        private void RestoreKeptInventory()
        {
            if (_keptItems == null) return;
            var items = _keptItems; var ammo = _keptAmmo;
            _keptItems = null; _keptAmmo = null;
            foreach (var s in items) GiveBack(s);
            if (ammo != null) foreach (var kv in ammo) _player.SetAmmo(kv.Key, kv.Value);
        }

        /// <summary>
        /// Телепорт с гарантией: игрок висит (гравитация 0) и не может уйти, пока под ногами не построены коллизии.
        /// Таймаута нет — упасть сквозь непостроенный мир нельзя.
        /// </summary>
        private void TeleportTo(Vector3 target, string hint)
        {
            if (_player == null) return;
            _collision.ReceivedSinceReset = 0;
            _frozen = true;
            _teleportFreezeAt = Time.time; // после телепорта отпускаем только на настоящий пол
            _player.Gravity = Vector3.zero;
            _lastGoodPos = target;
            _player.Position = target;
            if (hint != null) _player.SendHint(hint, 4f);
        }

        // ───────────── сообщения от Minecraft ─────────────

        private void Handle(Dictionary<string, object> m)
        {
            string t = m.Str("t");
            switch (t)
            {
                case "hello":
                    Logger.Info(L.T("[SlBridge] Minecraft подключён, версия протокола ", "[SlBridge] Minecraft connected, protocol version ") + m.Num("ver"));
                    break;

                case "spawn":
                {
                    if (!m.Bool("ready") || _player == null) break; // мир ещё грузится — спросим позже
                    _spawnMc = new Vector3((float)m.Num("x"), (float)m.Num("y"), (float)m.Num("z"));
                    _hasSpawnMc = true;
                    BeginArrival(m.Num("x"), m.Num("y"), m.Num("z"), m.Str("dim", "minecraft:overworld"), -1,
                        L.T("Добро пожаловать в мир Minecraft!", "Welcome to the Minecraft world!"));
                    LoadSavedInventory(m.Str("world", ""), m.TryGetValue("inv", out var inv) ? inv as Dictionary<string, object> : null);
                    GiveFirstKitIfNew(m.Str("world", ""));
                    break;
                }

                case "rules":
                    _keepInventory = m.Bool("keepInv");
                    break;

                case "teleport":
                {
                    // Minecraft переместил игрока (портал, жемчуг, /tp, возрождение) — см. BeginArrival
                    if (_player == null) break;
                    string dim = m.Str("dim", "");
                    string hint = dim != _dim ? (dim.EndsWith("the_nether") ? L.T("Незер", "Nether") : dim.EndsWith("the_end") ? L.T("Энд", "The End") : null) : null;
                    BeginArrival(m.Num("x"), m.Num("y"), m.Num("z"), dim, (int)m.Num("seq"), hint);
                    break;
                }

                case "unstick":
                {
                    // Minecraft вытолкнул игрока из блоков на соседнюю клетку — просто переставляем, без «телепорта»
                    // (коллизии вокруг остаются, заморозки нет).
                    if (_player == null || _arriving) break;
                    var to = CoordMap.McToSl(m.Num("x"), m.Num("y") + 0.02, m.Num("z")) + Vector3.up * FeetOffset(_player);
                    _player.Position = to;
                    _poseSeq = (int)m.Num("seq");
                    _lastGoodPos = to;
                    if (!_inFacility) _anchor = to;
                    Logger.Info("[SlBridge/TP] Minecraft вытолкнул из блоков — переставил рядом");
                    break;
                }

                case "nudge":
                {
                    // поставил блок под себя в прыжке (или выплываешь на берег) — ставим ноги ровно на верх блока.
                    // Гравитацию не трогаем: иначе инерция прыжка уносит вверх.
                    if (_player == null || _arriving) break;
                    var cur = CoordMap.SlToMc(_player.Position);
                    var up = CoordMap.McToSl(cur.x, m.Num("fy") + 0.02, cur.z) + Vector3.up * FeetOffset(_player);
                    if (up.y <= _player.Position.y) break;
                    _player.Position = up;
                    _lastGoodPos = up;
                    break;
                }

                case "fluid":
                    _fluidFeet = (int)m.Num("f");
                    _fluidEye = (int)m.Num("e");
                    _climb = m.Num("c") > 0;
                    break;

                case "sec":
                {
                    if (_dim != null && m.Has("d") && m.Str("d") != _dim) break; // запоздавшая ячейка другого измерения
                    var arr = m.Arr("b");
                    var b = new float[arr.Count];
                    for (int i = 0; i < b.Length; i++) b[i] = (float)(double)arr[i];
                    _collision.Enqueue(m.Str("k"), b);
                    break;
                }

                case "secDel":
                    if (_dim != null && m.Has("d") && m.Str("d") != _dim) break;
                    _collision.EnqueueRemove(m.Str("k"));
                    break;

                case "hurt":
                    if (_player != null && _player.IsAlive)
                    {
                        float amount = (float)m.Num("amount");
                        string who = m.Str("attacker") ?? m.Str("source", "?");
                        _player.Damage(amount, "Minecraft: " + who);
                    }
                    break;

                case "hit":
                    if (_player != null && m.Str("kind") == "entity" && !m.Bool("dodged")) // эндермен увернулся — это промах
                    {
                        _player.SendHitMarker(m.Bool("dead") ? 2.5f : 1f);
                        if (_player.CurrentItem is LabApi.Features.Wrappers.JailbirdItem && !m.Bool("dodged"))
                        {
                            JailbirdWear(_lastShotDmg, _lastMeleeCharged); // износ — по урону главной цели
                            _lastMeleeCharged = false;
                        }
                        // SCP-127 растёт от урона по живым целям — мобы Minecraft тоже считаются
                        if (_player.CurrentItem is FirearmItem f127 && f127.Type == ItemType.GunSCP127 && SlItemState.TryGet127(f127, out var m127))
                        {
                            // как в SL: опыт = урон; за убийство — ещё KillBonus оружия и патроны в магазин (KillBonus магазина)
                            float xp = _lastShotDmg;
                            if (m.Bool("dead")) xp += m127.KillBonus;
                            m127.ServerSetStats(m127.ServerExp + xp * Config.Scp127ExpMultiplier);
                            if (m.Bool("dead")) { Scp127KillAmmo(f127); Scp127Speak(f127, m.Str("entity", "")); }
                        }
                    }
                    break;

                case "items":
                    _worldItems.Update(m.Arr("list"));
                    break;

                case "mobs":
                    // Твёрдых хитбоксов мобов в SL нет (мешали) — только список для попаданий рывком Jailbird.
                    _mobBoxes.Clear();
                    foreach (var o in m.Arr("list"))
                        if (o is Dictionary<string, object> mo)
                            _mobBoxes.Add(new[] { (float)mo.Num("x"), (float)mo.Num("y"), (float)mo.Num("z"), (float)mo.Num("w"), (float)mo.Num("h"), (float)mo.Num("d") });
                    break;

                case "picked":
                    if (_player == null) break;
                    _worldItems.Remove((int)m.Num("e"));
                    if (m.TryGetValue("item", out var it) && it is Dictionary<string, object> io) GiveBack(McStack.FromMsg(io));
                    break;

                case "ui":
                    if (_player == null) break;
                    // предметы теперь в окне Minecraft — карты-заглушки убираем, вернутся при закрытии
                    _uiOpen = true;
                    _inv.TakeAll(_player);
                    _player.SendHint(L.T("<size=22>Окно Minecraft: перекладывай мышкой. Закрыть — E или Esc.</size>", "<size=22>Minecraft window: drag items with the mouse. Close with E or Esc.</size>"), 3f);
                    break;

                case "invSync":
                    OnInvSync(m);
                    break;

                case "invAdd":
                    if (_player == null) break;
                    foreach (var g in m.Arr("stacks").OfType<Dictionary<string, object>>()) GiveBack(McStack.FromMsg(g));
                    break;

                case "crafted":
                    OnCrafted(m);
                    break;

                case "cmdOut":
                {
                    var lines = m.Arr("lines").OfType<string>().ToList();
                    string text = lines.Count > 0 ? string.Join("\n", lines) : L.T("(без ответа)", "(no output)");
                    if (_cmdPlayer != null && _cmdPlayer.IsOnline)
                    {
                        _cmdPlayer.SendConsoleMessage("[Minecraft] /" + m.Str("cmd") + "\n" + text, "yellow");
                        _cmdPlayer.SendHint("<size=20>" + (lines.Count > 0 ? lines[0] : "/" + m.Str("cmd")) + "</size>", 3f);
                    }
                    else Logger.Info("[Minecraft] " + text);
                    break;
                }
            }
        }

        private void OnInvSync(Dictionary<string, object> m)
        {
            if (_player == null) return;
            if (m.Has("heal")) _player.Heal((float)m.Num("heal"));
            var slots = m.Arr("slots");
            if (m.Bool("ui") || _uiOpen)
            {
                _uiOpen = false;
                // Окно Minecraft закрыто: инвентарь Minecraft — теперь главный. Предметы SL, отданные в окно, убираем
                // и выдаём заново из того, что вернул Minecraft (с их состоянием; перезаряженные — новыми).
                foreach (var serial in _slSentSerials)
                {
                    var it = _player.Items.FirstOrDefault(x => x.Serial == serial);
                    if (it != null && !_inv.IsStack(it)) _player.RemoveItem(it);
                }
                _slSentSerials.Clear();
                _inv.TakeAll(_player);
                foreach (var o in slots) if (o is Dictionary<string, object> d) GiveBack(McStack.FromMsg(d));
                return;
            }
            // обычное «использовать»: сопоставляем слоты с картами, меняем только то, что изменилось
            for (int i = 0; i < Math.Max(slots.Count, _useSerials.Count); i++)
            {
                var now = i < slots.Count ? McStack.FromMsg(slots[i] as Dictionary<string, object>) : null;
                if (i < _useSerials.Count)
                {
                    var serial = _useSerials[i];
                    if (_slSentSerials.Contains(serial)) continue; // предмет SL — «использовать» его не меняет
                    var item = _player.Items.FirstOrDefault(x => x.Serial == serial);
                    var was = _inv.Get(item);
                    if (was == null) { if (now != null) GiveBack(now); continue; }
                    if (now == null) _inv.Replace(_player, serial, null);
                    else if (!string.IsNullOrEmpty(now.Sl)) { _inv.Replace(_player, serial, null); GiveSl(now); }
                    else if (now.Id != was.Id || now.Count != was.Count) _inv.Replace(_player, serial, now);
                }
                else if (now != null) GiveBack(now);
            }
        }

        private void OnCrafted(Dictionary<string, object> m)
        {
            if (_player == null) return;
            if (!m.Bool("ok"))
            {
                _player.SendHint("<size=22>" + m.Str("msg", L.T("Не получилось", "Failed")) + "</size>", 2f);
                return;
            }
            foreach (var c in m.Arr("consume").OfType<Dictionary<string, object>>())
                if (!_inv.Consume(_player, c.Str("id"), (int)c.Num("count")))
                {
                    _player.SendHint(L.T("<size=22>Инвентарь изменился — попробуй ещё раз</size>", "<size=22>Your inventory changed — try again</size>"), 2f);

                    return;
                }
            var names = new List<string>();
            foreach (var g in m.Arr("give").OfType<Dictionary<string, object>>())
            {
                var s = McStack.FromMsg(g);
                names.Add($"{s.Name} ×{s.Count}");
                GiveBack(s);
            }
            _player.SendHint(L.T("<size=22>Получено: ", "<size=22>Received: ") + string.Join(", ", names) + "</size>", 2.5f);
        }
    }
}
