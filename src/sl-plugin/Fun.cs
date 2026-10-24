using System;
using System.Collections.Generic;
using System.Linq;
using CustomPlayerEffects;
using InventorySystem.Items.Usables.Scp330;
using LabApi.Features.Wrappers;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace SlBridge
{
    /// <summary>
    /// «Фишки» моста: хэллуинские конфеты SCP-330 в мире Minecraft, SCP-1344 как рентген, тотем бессмертия,
    /// зелья и молоко, элитры (ноуклип), крик SCP-096 у эндерменов, достижения и фразы Minecraft на экране SL.
    /// </summary>
    public sealed partial class SlBridgePlugin
    {
        // ── эффекты конфет, которые Minecraft применяет каждый тик (битовая маска в позе) ──
        public const int FxSpicy = 1, FxWhite = 2, FxOrange = 4, FxMetal = 8, FxSugarHigh = 16, FxSugarRush = 32, FxSlowMetab = 64, FxXray = 128;

        private bool On<T>() where T : StatusEffectBase
        {
            try { return _player.TryGetEffect<T>(out var e) && e.IsEnabled; } catch { return false; }
        }

        private int FunFx()
        {
            int fx = 0;
            if (On<Spicy>()) fx |= FxSpicy;
            if (On<WhiteCandy>()) fx |= FxWhite;
            if (On<OrangeCandy>()) fx |= FxOrange;
            if (On<Metal>()) fx |= FxMetal;
            if (On<SugarHigh>()) fx |= FxSugarHigh;
            if (On<SugarRush>()) fx |= FxSugarRush;
            if (On<SlowMetabolism>()) fx |= FxSlowMetab;
            if (_xray) fx |= FxXray;
            return fx;
        }

        /// <summary>Каждый кадр (из OnUpdate): конфеты, рентген, тотем, проход сквозь стены.</summary>
        private void FunTick(Vector3 pos)
        {
            try { TickCandyBag(); } catch (Exception e) { Logger.Warn("[SlBridge] конфеты: " + e.Message); }
            try { TickBlackTeleport(pos); } catch { }
            try { TickXray(); } catch (Exception e) { Logger.Warn("[SlBridge] SCP-1344: " + e.Message); }
            try { TickTotemRegen(); TickPotionRegen(); } catch { }
            try { TickPhase(pos); } catch (Exception e) { Logger.Warn("[SlBridge] белая конфета: " + e.Message); }
        }

        // ───────────── SCP-330: какую конфету съели ─────────────
        private ushort _bagSerial;
        private List<CandyKindID> _bagCandies = new List<CandyKindID>();
        private float _candyDroppedAt = -10f;

        /// <summary>Снимок мешочка, пока он в руке: после еды SL сразу убирает его из рук, и разницу считаем уже в событии.</summary>
        private void TickCandyBag()
        {
            if (!(_player.CurrentItem is Scp330Item bag)) return;
            _bagSerial = bag.Serial;
            _bagCandies = bag.Candies.ToList();
        }

        private CandyKindID _pendingCandy = CandyKindID.None;

        /// <summary>SL вот-вот применит эффект конфеты: какая выбрана — ещё видно в мешочке.</summary>
        private void OnUsageApplyingFun(LabApi.Events.Arguments.PlayerEvents.PlayerItemUsageEffectsApplyingEventArgs ev)
        {
            try
            {
                if (ev.Player?.ReferenceHub != _player?.ReferenceHub) return;
                if (!(ev.UsableItem?.Base is InventorySystem.Items.Usables.Scp330.Scp330Bag bag)) return;
                if (bag.SelectedCandyId >= 0 && bag.SelectedCandyId < bag.Candies.Count) _pendingCandy = bag.Candies[bag.SelectedCandyId];
                Logger.Info("[SlBridge] конфета выбрана: " + _pendingCandy);
            }
            catch (Exception e) { Logger.Warn("[SlBridge] конфета: " + e.Message); }
        }

        /// <summary>Съедена конфета (эффекты SL уже применены — злые 450 AHP тоже).</summary>
        private void OnUsedItemFun(LabApi.Events.Arguments.PlayerEvents.PlayerUsedItemEventArgs ev)
        {
            try
            {
                if (ev.Player?.ReferenceHub != _player?.ReferenceHub) return;
                if (!(ev.UsableItem?.Base is InventorySystem.Items.Usables.Scp330.Scp330Bag)) return;
                var kind = _pendingCandy;
                _pendingCandy = CandyKindID.None;
                Logger.Info("[SlBridge] съедена конфета: " + kind);
                if (kind != CandyKindID.None) OnCandyEaten(kind);
            }
            catch (Exception e) { Logger.Warn("[SlBridge] конфета: " + e.Message); }
        }

        // ── злая конфета: +100 AHP за каждое убийство (как у злого зефирного человека в SL), до 450 ──
        private int _evilKillCode = -1;
        private const float EvilAhpOnKill = 100f;

        private PlayerStatsSystem.AhpStat Ahp => _player.ReferenceHub.playerStats.GetModule<PlayerStatsSystem.AhpStat>();

        private int FindEvilProcess()
        {
            try
            {
                var f = typeof(PlayerStatsSystem.AhpStat).GetField("_activeProcesses", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(Ahp) is List<PlayerStatsSystem.AhpProcess> list)
                    foreach (var p in list)
                        if (p.Persistant && p.Limit >= 449f) return p.KillCode;
            }
            catch (Exception e) { Logger.Warn("[SlBridge] злая конфета: " + e.Message); }
            return -1;
        }

        private void OnMcKill()
        {
            if (_evilKillCode < 0 || _player == null || !_player.IsAlive) return;
            if (!Ahp.ServerTryGetProcess(_evilKillCode, out var proc)) { _evilKillCode = -1; return; }
            float before = proc.CurrentAmount;
            proc.CurrentAmount = Mathf.Min(proc.Limit, proc.CurrentAmount + EvilAhpOnKill);
            if (proc.CurrentAmount > before + 0.5f)
                _player.SendHint(L.T($"<size=22><color=#B01E1E>Зло насыщается… +{Mathf.RoundToInt(proc.CurrentAmount - before)} AHP</color></size>",
                                     $"<size=22><color=#B01E1E>The evil feeds… +{Mathf.RoundToInt(proc.CurrentAmount - before)} AHP</color></size>"), 2f);
            if (_mc.Connected) _mc.Send("{\"t\":\"evilKill\"}");
        }

        private Vector3 _blackFrom;
        private float _blackUntil;

        private void OnCandyEaten(CandyKindID kind)
        {
            if (Config.DebugLog) Logger.Info("[SlBridge] съедена конфета " + kind);
            switch (kind)
            {
                case CandyKindID.Black:
                    // одна из случайностей — телепорт в комплекс SL: мост вернёт назад, а в Minecraft — прыжок, как от хоруса
                    _blackFrom = _player.Position;
                    _blackUntil = Time.time + 1.5f;
                    break;
                case CandyKindID.Evil:
                    // SL уже выдал вечные 450 AHP — запоминаем этот процесс: убийства в Minecraft пополняют его
                    _evilKillCode = FindEvilProcess();
                    if (_mc.Connected) _mc.Send("{\"t\":\"candy\",\"kind\":\"Evil\"}");
                    break;
                case CandyKindID.Blue:
                case CandyKindID.Rainbow:
                case CandyKindID.Brown:
                    if (_mc.Connected) _mc.Send("{\"t\":\"candy\",\"kind\":\"" + kind + "\"}");
                    break;
            }
        }

        private void TickBlackTeleport(Vector3 pos)
        {
            if (_blackUntil <= 0f) return;
            if (Time.time > _blackUntil) { _blackUntil = 0f; return; }
            if ((pos - _blackFrom).sqrMagnitude < 25f * 25f) return;
            _blackUntil = 0f;
            _player.Position = _blackFrom; // в комплекс SL не пускаем
            if (_mc.Connected) _mc.Send("{\"t\":\"chorus\"}");
        }

        // ───────────── белая конфета: сквозь стены (по горизонтали) ─────────────
        private bool _phasing;
        private PrimitiveObjectToy _phasePad;

        private void TickPhase(Vector3 pos)
        {
            // Сам проход сквозь стены делает Minecraft: пока действует конфета, он не присылает коллизии
            // выше уровня ног (пол остаётся). Здесь — только «конфета кончилась»: застрял в стене — вытолкнет наверх.
            bool on = On<WhiteCandy>() && _hasSpawn && !_arriving;
            if (on) { _phasing = true; return; }
            if (!_phasing) return;
            _phasing = false;
            if (_mc.Connected) _mc.Send("{\"t\":\"phaseEnd\"}");
        }

        private float _phaseFloor;
        private bool _vyGrounded() => Mathf.Abs(_vy) < 0.3f;

        // ───────────── SCP-1344: рентген вместо затемнения ─────────────
        private bool _xray;

        /// <summary>Очки SCP-1344 надеты (или надеваются/снимаются) — такой предмет нельзя пересоздавать.</summary>
        private static bool IsWorn1344(Item i) =>
            i?.Base is InventorySystem.Items.Usables.Scp1344.Scp1344Item g && g.Status != InventorySystem.Items.Usables.Scp1344.Scp1344Status.Idle;
        private float _severedUntil;

        private float _nextHolster1344;
        private string _last1344 = "";

        private void TickXray()
        {
            InventorySystem.Items.Usables.Scp1344.Scp1344Item worn = null;
            foreach (var it in _player.Items)
                if (it.Base is InventorySystem.Items.Usables.Scp1344.Scp1344Item g
                    && g.Status != InventorySystem.Items.Usables.Scp1344.Scp1344Status.Idle) { worn = g; break; }
            var st = worn?.Status ?? InventorySystem.Items.Usables.Scp1344.Scp1344Status.Idle;
            if (st.ToString() != _last1344) { Logger.Info("[SlBridge] SCP-1344: " + st); _last1344 = st.ToString(); }

            // Снимать очки в мосте — только выбросить их (Q): тогда предмет просто пропадает из инвентаря.
            // Любое другое «снятие» (SL сам запускает его при смене предметов) отменяем — очки остаются на глазах.
            if (_xray && worn != null && (st == InventorySystem.Items.Usables.Scp1344.Scp1344Status.Dropping
                                          || st == InventorySystem.Items.Usables.Scp1344.Scp1344Status.Deactivating
                                          || st == InventorySystem.Items.Usables.Scp1344.Scp1344Status.CancelingDeactivation))
            {
                try { worn.Status = InventorySystem.Items.Usables.Scp1344.Scp1344Status.Active; st = worn.Status; } catch (Exception e) { Logger.Warn("SCP-1344 status: " + e.Message); }
            }

            if (worn != null && st == InventorySystem.Items.Usables.Scp1344.Scp1344Status.Active)
            {
                // вместо тёмной рамки и «зрения сквозь стены» SL (в мосте это чёрный экран) — рентген Minecraft
                if (On<Scp1344>()) _player.DisableEffect<Scp1344>();
                if (_player.TryGetEffect<Blindness>(out var bl) && bl.IsEnabled) _player.DisableEffect<Blindness>();
                // анимации надевания в мосте нет — очки сами убираются из рук (SL делал бы это по слепоте, а её мы сняли)
                if (worn.IsEquipped && Time.time >= _nextHolster1344)
                {
                    _nextHolster1344 = Time.time + 0.25f;
                    try { _player.ReferenceHub.inventory.ServerSelectItem(0); } catch { }
                }
                if (!_xray) { _xray = true; _player.SendHint(L.T("<size=24><color=#7FE3FF>Глаза видят сквозь камень…</color></size>", "<size=24><color=#7FE3FF>Your eyes see through stone…</color></size>"), 3f); }
                return;
            }
            if (_xray && worn == null)
            {
                // очки сняли (выбросили) — как и положено, глаза повреждены
                _xray = false;
                _severedUntil = Time.time + 2f;
                try { _player.EnableEffect<SeveredEyes>(); } catch { }
            }
            // SL при снятии ослепляет полностью (в мосте — чёрный экран): оставляем только «повреждённые глаза»
            if (Time.time < _severedUntil && _player.TryGetEffect<Blindness>(out var b2) && b2.IsEnabled)
                _player.DisableEffect<Blindness>();
            if (Time.time < _severedUntil && !On<SeveredEyes>()) try { _player.EnableEffect<SeveredEyes>(); } catch { }
        }

        // ───────────── тотем бессмертия ─────────────
        private float _totemRegenUntil, _nextTotemRegen;

        /// <summary>Смерть, а в руке тотем Minecraft: как в игре — спасает, тратится, лечит.</summary>
        private bool TryTotem(LabApi.Events.Arguments.PlayerEvents.PlayerDyingEventArgs ev)
        {
            var held = _player.CurrentItem;
            var st = _inv.Get(held);
            if (st == null || st.Id != "minecraft:totem_of_undying") return false;
            if (ev.DamageHandler is PlayerStatsSystem.UniversalDamageHandler u && u.TranslationId == PlayerStatsSystem.DeathTranslations.Crushed.Id) return false;
            ev.IsAllowed = false;
            _inv.Replace(_player, held.Serial, st.Count > 1 ? new McStack { Id = st.Id, Name = st.Name, Count = st.Count - 1, Max = st.Max, Comp = st.Comp } : null);
            // как в Minecraft: 1 сердце, «поглощение» (щит) и регенерация на 45 секунд; плохие эффекты снимаются
            _player.Health = 5f * 1f;
            try { Ahp.ServerAddProcess(40f, 40f, 0f, 1f, 0f, false); } catch { } // отдельный щит — злые 450 AHP не трогаем
            foreach (var e in _player.ActiveEffects.ToList())
                if (e.Classification == StatusEffectBase.EffectClassification.Negative) _player.DisableEffect(e);
            _totemRegenUntil = Time.time + 45f;
            _player.SendHint(L.T("<size=28><color=#F6D66B>Тотем бессмертия спас тебя!</color></size>", "<size=28><color=#F6D66B>The Totem of Undying saved you!</color></size>"), 3f);
            if (_mc.Connected) _mc.Send("{\"t\":\"totem\"}");
            return true;
        }

        private void TickTotemRegen()
        {
            if (Time.time > _totemRegenUntil || Time.time < _nextTotemRegen || !_player.IsAlive) return;
            _nextTotemRegen = Time.time + 0.5f;
            _player.Heal(2f); // регенерация II: ~0.8 HP Minecraft в секунду ×5
        }

        // ───────────── сообщения от Minecraft ─────────────
        private float _regenUntil, _regenPerSec, _nextRegen;

        /// <summary>true — сообщение обработано здесь.</summary>
        private bool HandleFun(string t, Dictionary<string, object> m)
        {
            switch (t)
            {
                case "say":
                    if (_player != null) _player.SendHint("<size=22>" + m.Str("text", "") + "</size>", (float)m.Num("time", 3));
                    return true;
                case "adv":
                    if (_player != null)
                    {
                        string kind = m.Str("kind", "task");
                        string col = kind == "challenge" ? "#C77DFF" : kind == "goal" ? "#7FE3FF" : "#F6D66B";
                        string head = kind == "challenge" ? L.T("Испытание выполнено!", "Challenge complete!") : kind == "goal" ? L.T("Цель достигнута!", "Goal reached!") : L.T("Достижение получено!", "Advancement made!");
                        _player.SendHint($"<size=20><color={col}>{head}</color></size>\n<size=26><b>{m.Str("title", "")}</b></size>", 4f);
                    }
                    return true;
                case "elytra":
                    if (_player != null)
                    {
                        bool fly = !_player.IsNoclipEnabled;
                        _player.IsNoclipEnabled = fly;
                        _player.SendHint(fly ? L.T("<size=26><color=#B5E3FF>Ты постиг силу небес и научился летать!</color></size>", "<size=26><color=#B5E3FF>You have grasped the power of the skies and learned to fly!</color></size>")
                                             : L.T("<size=24>Ты спустился с небес на землю.</size>", "<size=24>You came back down to earth.</size>"), 3f);
                    }
                    return true;
                case "kill":
                    OnMcKill();
                    return true;
                case "milk":
                    if (_player != null)
                    {
                        foreach (var e in _player.ActiveEffects.ToList())
                            if (e.Classification == StatusEffectBase.EffectClassification.Negative) _player.DisableEffect(e);
                        _player.SendHint(L.T("<size=22>Молоко сняло все дурные эффекты</size>", "<size=22>The milk cleared all bad effects</size>"), 2f);
                    }
                    return true;
                case "potion":
                    if (_player != null) ApplyPotion(m);
                    return true;
                case "scream096":
                    PlayScream096();
                    return true;
            }
            return false;
        }

        /// <summary>Выпитое зелье Minecraft → эффекты SL (то, что Minecraft не может сам: лечение, скорость, невидимость).</summary>
        private void ApplyPotion(Dictionary<string, object> m)
        {
            foreach (var o in m.Arr("effects").OfType<Dictionary<string, object>>())
            {
                string id = o.Str("id", "");
                int amp = (int)o.Num("amp", 0);
                float dur = (float)o.Num("sec", 0);
                switch (id)
                {
                    case "instant_health": _player.Heal(4f * (1 << amp) * 5f); break;
                    case "regeneration":
                        _regenUntil = Time.time + dur;
                        _regenPerSec = (amp + 1) * 2f; // ~0.4 HP Minecraft/с на уровень ×5
                        break;
                    case "speed": _player.EnableEffect<MovementBoost>((byte)Mathf.Clamp(20 * (amp + 1), 1, 255), dur); break;
                    case "slowness": _player.EnableEffect<Slowness>((byte)Mathf.Clamp(15 * (amp + 1), 1, 255), dur); break;
                    case "invisibility": _player.EnableEffect<Invisible>(1, dur); break;
                    case "instant_damage": _player.Damage(6f * (1 << amp) * 5f, L.T("Зелье вреда", "Potion of Harming")); break;
                    case "poison": _player.EnableEffect<Poisoned>(1, dur); break;
                    case "absorption": { float a = 4f * (amp + 1) * 5f; Ahp.ServerAddProcess(a, a, 0f, 1f, 0f, false); break; }
                    case "resistance": _player.EnableEffect<DamageReduction>((byte)Mathf.Clamp(40 * (amp + 1), 1, 200), dur); break;
                }
            }
        }

        private void TickPotionRegen()
        {
            if (Time.time > _regenUntil || Time.time < _nextRegen || !_player.IsAlive) return;
            _nextRegen = Time.time + 1f;
            _player.Heal(_regenPerSec);
        }

        // ───────────── крик SCP-096 (эндермен разозлился) ─────────────
        private static float[] _scream;
        private SpeakerToy _speaker;
        private float _nextScream;

        private void PlayScream096()
        {
            if (_player == null || Time.time < _nextScream) return;
            _nextScream = Time.time + 8f;
            try
            {
                _scream ??= LoadWav("SlBridge.sounds.scp096_rage.wav");
                if (_scream == null) return;
                if (_speaker == null || _speaker.IsDestroyed)
                {
                    _speaker = SpeakerToy.Create(_player.Position, null, true);
                    _speaker.ControllerId = 96;
                    _speaker.IsSpatial = false;
                    _speaker.Volume = 1f;
                }
                _speaker.Position = _player.Position;
                _speaker.Stop();
                _speaker.Play(_scream, false);
            }
            catch (Exception e) { Logger.Warn("[SlBridge] крик SCP-096: " + e.Message); }
        }

        /// <summary>WAV (16 бит, моно, 48 кГц) из ресурсов плагина → отсчёты для динамика SL.</summary>
        private static float[] LoadWav(string resource)
        {
            using var s = typeof(SlBridgePlugin).Assembly.GetManifestResourceStream(resource);
            if (s == null) { Logger.Warn("[SlBridge] нет звука " + resource); return null; }
            var bytes = new byte[s.Length];
            int read = 0;
            while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
            // ищем блок «data»
            int p = 12;
            while (p + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, p, 4);
                int len = BitConverter.ToInt32(bytes, p + 4);
                if (id == "data")
                {
                    int count = Math.Min(len, bytes.Length - p - 8) / 2;
                    var f = new float[count];
                    for (int i = 0; i < count; i++) f[i] = BitConverter.ToInt16(bytes, p + 8 + i * 2) / 32768f;
                    return f;
                }
                p += 8 + len + (len & 1);
            }
            return null;
        }

        private void FunCleanup()
        {
            _xray = false; _phasing = false; _blackUntil = 0f; _totemRegenUntil = 0f; _regenUntil = 0f;
            try { _collision.Unphase(); } catch { }
            if (_phasePad != null && !_phasePad.IsDestroyed) _phasePad.Destroy();
            _phasePad = null;
        }
    }
}
