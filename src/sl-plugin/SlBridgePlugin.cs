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
        public override string Description => "Мост SCP:SL ↔ Minecraft: Minecraft рисует мир, SL — персонажа и интерфейс.";
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
            PlayerEvents.Dying += OnDying;
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
            PlayerEvents.Dying -= OnDying;
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
            _collision?.Clear();
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
            NewRoundObjects();
            _inv.Clear();
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
            if (ev.Player == _player) { _player = null; _inv.Clear(); }
        }

        private void OnSpawned(PlayerSpawnedEventArgs ev)
        {
            if (ev.Player != _player) return;
            if (ev.Role.RoleTypeId != Config.Role) return;
            if (_needRespawn) { _needRespawn = false; _hasSpawn = false; _mc.Send("{\"t\":\"respawnReq\"}"); }
            else _needSpawn = true;
            GiveLoadout(ev.Player);
            ApplyMovement(ev.Player);
            _armorTier = -1;
            _lastGoodPos = Vector3.zero;
            _frozen = false;
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
            // Рывок SL останавливает сам, когда Jailbird врезается в хитбокс моба (как в стену), и делает удар —
            // нам приходит AttackPerformed. Если перед этим был рывок — урон заряженного удара.
            if (ev.Message == JailbirdMessageType.ChargeStarted) _chargeUntil = Time.time + 1.6f;
            if (ev.Message == JailbirdMessageType.AttackPerformed)
            {
                bool charged = _chargeUntil > 0 && Time.time < _chargeUntil;
                _chargeUntil = -1f;
                SendShot(Config.MeleeRange, charged ? Config.JailbirdChargeDamage : Config.JailbirdDamage, true, true);
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
                    SendShot(Config.MeleeRange, Config.Scp1509Damage, true, true);
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

        private void SendShot(float range, float dmg, bool breaks, bool knockback)
        {
            if (_player == null || !_mc.Connected) return;
            var cam = _player.Camera;
            _mc.Send("{\"t\":\"shot\"," + Vec("", CoordMap.SlToMc(cam.position)) + "," + Vec("d", CoordMap.DirSlToMc(cam.forward))
                     + ",\"dmg\":" + F(dmg) + ",\"range\":" + F(range) + ",\"breaks\":" + (breaks ? "true" : "false") + ",\"kb\":" + (knockback ? "true" : "false") + "}");
        }

        private void OnSearchedToy(PlayerSearchedToyEventArgs ev)
        {
            if (ev.Player != _player) return;
            if (!_worldItems.TryGetEntity(ev.Interactable, out int id, out var stack)) return;
            bool hasRoom = _player.Items.Count() < 8
                           || (stack != null && _inv.All.Any(s => s.Id == stack.Id && s.Count < s.Max));
            if (!hasRoom) { _player.SendHint(L.T("<size=22>Инвентарь полон</size>", "<size=22>Inventory is full</size>"), 1.5f); return; }
            _mc.Send("{\"t\":\"pickup\",\"e\":" + id + "}");
        }

        private void OnDropping(PlayerDroppingItemEventArgs ev)
        {
            if (ev.Player != _player || !_inv.IsStack(ev.Item)) return;
            ev.IsAllowed = false;
            var s = _inv.Take(_player, ev.Item);
            if (s != null) DropToMinecraft(s, true);
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
            foreach (var s in _inv.TakeAll(_player)) DropToMinecraft(s, false);
            var ammo = new Dictionary<ItemType, int>();
            foreach (var it in _player.Items.ToList())
            {
                if (_inv.IsStack(it)) continue;
                if (it is FirearmItem fa)
                {
                    try { int a = fa.StoredAmmo + fa.ChamberedAmmo; if (a > 0) ammo[fa.AmmoType] = (ammo.TryGetValue(fa.AmmoType, out var x) ? x : 0) + a; } catch { }
                }
                DropToMinecraft(McStack.ForSl(it.Type), false);
            }
            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
            {
                if (!t.ToString().StartsWith("Ammo")) continue;
                int n = _player.GetAmmo(t);
                if (n > 0) ammo[t] = (ammo.TryGetValue(t, out var x) ? x : 0) + n;
            }
            foreach (var kv in ammo) DropToMinecraft(McStack.ForSl(kv.Key, kv.Value), false);
            try { _player.ClearInventory(); } catch (Exception e) { Logger.Warn("ClearInventory: " + e.Message); }
            _respawnAt = Time.time + Config.RespawnDelay;
            _needRespawn = true;
            _hasSpawn = false;
        }

        private void OnSetting(ReferenceHub hub, ServerSpecificSettingBase setting)
        {
            if (_player == null || hub != _player.ReferenceHub) return;
            switch (setting)
            {
                case SSKeybindSetting k when k.SettingId == McMenu.KeyUse && k.SyncIsPressed:
                    DoUse();
                    break;
                case SSKeybindSetting k when k.SettingId == McMenu.KeyInv && k.SyncIsPressed:
                    DoOpenInventory();
                    break;
                case SSButton b when b.SettingId == McMenu.QuitBtn:
                    RequestQuit();
                    break;
            }
        }

        // ───────────── действия ─────────────

        /// <summary>Инвентарь SL (стаки Minecraft) по слотам + какой слот в руке.</summary>
        private string InvForMinecraft(out int heldSlot)
        {
            var ordered = _inv.Ordered(_player);
            _useSerials = ordered.Select(k => k.Key).ToList();
            heldSlot = _player.CurrentItem != null ? _useSerials.IndexOf(_player.CurrentItem.Serial) : -1;
            return "[" + string.Join(",", ordered.Select(k => k.Value.ToJson())) + "]";
        }

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
                if (_player.Items.Count() >= 8 || _player.AddItem(type, InventorySystem.Items.ItemAddReason.AdminCommand) == null)
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

            if (_player == null || !_player.IsOnline)
            {
                _player = Player.List.FirstOrDefault(p => p.IsPlayer && p.IsReady && !p.IsHost && !p.IsDummy && !p.IsNpc);
                if (_player == null) return;
                _inv.Clear();
                if (_player.Role != Config.Role) _player.SetRole(Config.Role, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                else { _needSpawn = true; GiveLoadout(_player); ApplyMovement(_player); }
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

            // Заморозка: пока коллизии под ногами ещё не построены — не даём упасть/уйти в пустоту.
            if (_hasSpawn) FreezeIfNotBuilt(pos);

            if (Time.time >= _nextHousekeeping)
            {
                _nextHousekeeping = Time.time + 1f;
                // Lightweight (прыжок как в Minecraft) — проверяем, что не слетел после возрождения/эффектов
                if (Config.JumpBoostPercent > 0 && (!_player.TryGetEffect<Lightweight>(out var lw) || lw.Intensity != Config.JumpBoostPercent))
                    _player.EnableEffect<Lightweight>(Config.JumpBoostPercent, 0f);
                // броня SL → броня Minecraft
                int tier = _player.Items.Any(i => i.Type == ItemType.ArmorHeavy) ? 3 : _player.Items.Any(i => i.Type == ItemType.ArmorCombat) ? 2 : _player.Items.Any(i => i.Type == ItemType.ArmorLight) ? 1 : 0;
                if (tier != _armorTier && _mc.Connected) { _armorTier = tier; _mc.Send("{\"t\":\"armor\",\"tier\":" + tier + "}"); }
            }

            if (_hasSpawn && pos.y < CoordMap.Origin.y + Config.VoidY)
                TeleportTo(_spawnSl, L.T("Ты выпал из мира — возвращаю на точку появления.", "You fell out of the world — returning you to spawn."));

            PollMelee();

            bool wantSolid = _player.CurrentItem is LabApi.Features.Wrappers.JailbirdItem;
            if (wantSolid != _mobsSolid)
            {
                _mobsSolid = wantSolid;
                if (wantSolid) _mc.Send("{\"t\":\"streams\"}"); // прислать мобов заново
                else _mobs.Clear();
            }


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

        private string KeyBelow(int cx, int cy, int cz) => $"{cx},{cy - 1},{cz}";

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
                + ",\"fy\":" + F(feet) + ",\"yaw\":" + F(yaw) + ",\"pitch\":" + F(pitch) + ",\"seq\":" + _poseSeq + ",\"lamp\":" + LampLevel() + ",\"beam\":" + (_player.CurrentItem?.Type == ItemType.Flashlight ? 1 : 0) + "}");
        }

        /// <summary>Лампа (Lantern) или фонарик в руке и включены — светят в Minecraft.</summary>
        private int LampLevel()
        {
            if (!(_player.CurrentItem is LightItem li) || !li.IsEmitting) return 0;
            return li.Type == ItemType.Lantern ? Config.LanternLight : li.Type == ItemType.Flashlight ? Config.FlashlightLight : 0;
        }

        private void GiveLoadout(Player p)
        {
            // Предметы Minecraft не трогаем — только оружие/патроны из конфига, если их нет.
            foreach (var it in Config.Loadout)
                if (!p.Items.Any(i => i.Type == it)) p.AddItem(it);
            foreach (var kv in Config.Ammo) p.SetAmmo(kv.Key, kv.Value);
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
                    if (!m.Bool("ready") || _player == null) break; // мир ещё грузится — спросим позже
                    _spawnSl = CoordMap.McToSl(m.Num("x"), m.Num("y") + 0.05, m.Num("z")) + Vector3.up * FeetOffset(_player);
                    _hasSpawn = true;
                    _needSpawn = false;
                    TeleportTo(_spawnSl, L.T("Добро пожаловать в мир Minecraft!", "Welcome to the Minecraft world!"));
                    break;

                case "teleport":
                {
                    if (_player == null) break;
                    _poseSeq = (int)m.Num("seq");
                    var target = CoordMap.McToSl(m.Num("x"), m.Num("y") + 0.05, m.Num("z")) + Vector3.up * FeetOffset(_player);
                    _collision.Clear();
                    _worldItems.Clear();
                    _mobs.Clear();
                    _mc.Send("{\"t\":\"resync\"}");
                    string dim = m.Str("dim", "");
                    string hint = dim.EndsWith("the_nether") ? L.T("Незер", "Nether") : dim.EndsWith("the_end") ? L.T("Энд", "The End") : null;
                    if (!_hasSpawn) { _spawnSl = target; _hasSpawn = true; _needSpawn = false; }
                    if (dim.EndsWith("overworld")) _spawnSl = target;
                    _lastGoodPos = Vector3.zero;
                    _frozen = false;
                    TeleportTo(target, hint);
                    break;
                }

                case "sec":
                {
                    var arr = m.Arr("b");
                    var b = new float[arr.Count];
                    for (int i = 0; i < b.Length; i++) b[i] = (float)(double)arr[i];
                    _collision.Enqueue(m.Str("k"), b);
                    break;
                }

                case "secDel":
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
                    if (_player != null && m.Str("kind") == "entity")
                    {
                        _player.SendHitMarker(m.Bool("dead") ? 2.5f : 1f);
                    }
                    break;

                case "items":
                    _worldItems.Update(m.Arr("list"));
                    break;

                case "mobs":
                    // Твёрдые хитбоксы мобов нужны только для Jailbird (в них упирается рывок).
                    // Без него мобы «мягкие», как в Minecraft: их расталкивает тело игрока в Minecraft.
                    if (_mobsSolid) _mobs.Update(m.Arr("list"));
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
                _inv.TakeAll(_player); // на всякий случай
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
