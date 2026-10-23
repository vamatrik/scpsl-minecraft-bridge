using System.Collections.Generic;
using System.ComponentModel;
using PlayerRoles;

namespace SlBridge
{
    public sealed class Config
    {
        [Description("Язык / language: auto (как выбрано в лаунчере), ru или en.")]
        public string Language { get; set; } = "auto";

        [Description("Адрес и порт мода Minecraft (config/slbridge.properties → port).")]
        public string MinecraftHost { get; set; } = "127.0.0.1";
        public int MinecraftPort { get; set; } = 25599;

        [Description("Точка в мире SCP:SL, которая соответствует координатам Minecraft (0, 0, 0). Должна быть вдали от карты комплекса.")]
        public float OriginX { get; set; } = 3000f;
        public float OriginY { get; set; } = 1000f;
        public float OriginZ { get; set; } = 3000f;

        [Description("Роль, которой играет игрок в мире Minecraft.")]
        public RoleTypeId Role { get; set; } = RoleTypeId.Tutorial;

        [Description("Стартовые предметы (и после смерти): мачете — ломать и бить, аптечка — один раз отхилиться. Остальное крафтится.")]
        public List<ItemType> Loadout { get; set; } = new List<ItemType> { ItemType.SCP1509, ItemType.Medkit };

        [Description("Стартовые патроны: тип → количество.")]
        public Dictionary<ItemType, ushort> Ammo { get; set; } = new Dictionary<ItemType, ushort>();

        [Description("Сам запускать раунд, когда заходит игрок, и не давать ему закончиться.")]
        public bool AutoStartRound { get; set; } = true;
        public bool LockRound { get; set; } = true;

        [Description("Группа из config_remoteadmin, которую выдать игроку при входе (админ-панель на M). Пусто — не выдавать.")]
        public string AutoGrantGroup { get; set; } = "owner";

        [Description("Урон оружия SL за выстрел (до деления на DamageScale в моде Minecraft).")]
        public Dictionary<ItemType, float> WeaponDamage { get; set; } = new Dictionary<ItemType, float>
        {
            { ItemType.GunCOM15, 20f }, { ItemType.GunCOM18, 22f }, { ItemType.GunCom45, 25f },
            { ItemType.GunFSP9, 20f }, { ItemType.GunCrossvec, 23f }, { ItemType.GunE11SR, 25f },
            { ItemType.GunAK, 29f }, { ItemType.GunLogicer, 25f }, { ItemType.GunFRMG0, 23f },
            { ItemType.GunA7, 25f }, { ItemType.GunRevolver, 70f }, { ItemType.GunShotgun, 80f },
            { ItemType.ParticleDisruptor, 200f },
        };
        public float DefaultWeaponDamage { get; set; } = 25f;
        public float ShotRange { get; set; } = 128f;

        [Description("Цвет-ключ фона (R,G,B 0..1). Где компоновщик видит этот цвет — он подставляет картинку Minecraft. Должен совпадать с KeyColor в compositor.json.")]
        public float KeyColorR { get; set; } = 0f;
        public float KeyColorG { get; set; } = 1f;
        public float KeyColorB { get; set; } = 0f;
        [Description("Половина размера «комнаты-фона» вокруг игрока, в метрах. Без тумана (no_fog) её можно отодвинуть — фонарик меньше засвечивает стены.")]
        public float BackdropHalfSize { get; set; } = 4f;
        [Description("Убрать туман SL (эффект FogControl), чтобы далёкие стены «комнаты-фона» не размывались.")]
        public bool NoFog { get; set; } = true;
        [Description("Высота центра комнаты над точкой игрока (примерно уровень глаз).")]
        public float BackdropCenterHeight { get; set; } = 0.6f;

        [Description("После телепорта (портал, /tp) игрок стоит на месте, пока пол под ним не построен и не простоял столько секунд (чтобы коробки дошли до клиента SL).")]
        public float UnfreezeDelay { get; set; } = 0.3f;
        [Description("Сколько секунд максимум ждать пола под ногами после телепорта (если там правда пустота — потом отпускаем).")]
        public float TeleportMaxWait { get; set; } = 10f;
        [Description("Плавание и лестницы: скорость (м/с) всплытия в воде, погружения (если присесть), всплытия в лаве и подъёма/спуска по лестнице.")]
        public float SwimRise { get; set; } = 2f;
        public float SwimSink { get; set; } = 2f;
        public float LavaRise { get; set; } = 1f;
        public float ClimbSpeed { get; set; } = 2.4f;

        [Description("Сила взрыва в Minecraft (как у динамита — 4): осколочная граната и SCP-018. 0 — не взрывать мир Minecraft.")]
        public float GrenadeExplosionPower { get; set; } = 3f;
        public float Scp018ExplosionPower { get; set; } = 1.5f;
        public float JailbirdExplosionPower { get; set; } = 2f;

        [Description("Яркость света в Minecraft от лампы (Lantern) в руке, 0–15. Без лампы в темноте темно.")]
        public int LanternLight { get; set; } = 14;
        [Description("Яркость пятна от включённого фонарика в руке, 0–15: светит узким лучом туда, куда смотришь (дальше — тусклее).")]
        public int FlashlightLight { get; set; } = 15;

        [Description("Свет у камеры, чтобы оружие в руках не было чёрным (мягкий, без теней).")]
        public float LightIntensity { get; set; } = 1.5f;
        public float LightRange { get; set; } = 3f;
        [Description("Подсветка зелёных стен лампами (даёт яркие пятна). 0 — выключено: стены равномерные, яркость — через backdrop_glow.")]
        public float WallLightIntensity { get; set; } = 0f;
        public float WallLightRange { get; set; } = 2.2f;
        [Description("«Неон» зелёных стен: множитель яркости цвета (1 — обычный).")]
        public float BackdropGlow { get; set; } = 1.2f;

        [Description("Раз в секунду убирать трупы SL, кровь и следы от пуль.")]
        public bool CleanupRagdolls { get; set; } = true;
        public bool CleanupDecals { get; set; } = true;

        [Description("Раз в 5 секунд писать в лог состояние моста (секции, коллизии, позиция).")]
        public bool DebugLog { get; set; } = false;

        [Description("Множитель гравитации SL в мире Minecraft (1 — как в SL).")]
        public float GravityScale { get; set; } = 1f;
        [Description("Усиление прыжка в процентах (эффект Lightweight, без смены гравитации). 12 ≈ 1.25 м — как в Minecraft.")]
        public byte JumpBoostPercent { get; set; } = 12;

        [Description("Сколько секунд держать E, чтобы подобрать предмет Minecraft.")]
        public float PickupTime { get; set; } = 0.4f;
        [Description("Через сколько секунд после смерти возрождаться.")]
        public float RespawnDelay { get; set; } = 5f;
        [Description("Сколько коллизионных примитивов создавать за кадр (меньше — плавнее, но медленнее подгрузка).")]
        public int CollisionBudgetPerFrame { get; set; } = 120;

        [Description("Ближний бой и Micro HID: дальность (м) и урон SL. Урон Jailbird и SCP-1509: 0 — взять из предмета SL (как в игре). Micro HID — как в игре: 400/с на 6 м, заряженный 1000/с на 10 м.")]
        public float MeleeRange { get; set; } = 3f;
        public float JailbirdDamage { get; set; } = 0f;
        public float MarshmallowDamage { get; set; } = 40f;
        public float MicroHidRange { get; set; } = 6f;
        public float MicroHidDamagePerTick { get; set; } = 40f;
        [Description("Micro HID на ПКМ (заряженный выстрел): дальность и урон за 0.1 с.")]
        public float MicroHidChargeRange { get; set; } = 10f;
        public float MicroHidChargeDamagePerTick { get; set; } = 100f;
        public float JailbirdChargeDamage { get; set; } = 0f;
        [Description("Удар ближнего боя по области, как в SL: шар радиусом melee_hit_radius на melee_hit_offset м перед камерой. 0 — взять из предмета SL.")]
        public float MeleeHitRadius { get; set; } = 0f;
        public float MeleeHitOffset { get; set; } = 0f;
        [Description("Урон остальным целям в области (доля от урона по главной): SCP-1509 — половина, Jailbird — полный.")]
        public float Scp1509Splash { get; set; } = 0.5f;
        public float JailbirdSplash { get; set; } = 1f;
        [Description("Рывок Jailbird врезался в моба: на сколько секунд связать игрока (рывок останавливается).")]
        public float JailbirdHitStun { get; set; } = 0.4f;
        [Description("SCP-127 получает опыт за урон по мобам Minecraft: множитель (1 — как за урон по игрокам).")]
        public float Scp127ExpMultiplier { get; set; } = 1f;
        public float Scp1509Damage { get; set; } = 0f;

        [Description("Выключить всё, что мешает в мире Minecraft: обеззараживание ЛКЗ, Dead Man's Sequence, боеголовку, прибытие отрядов.")]
        public bool DisableFacilityEvents { get; set; } = true;

        [Description("Показывать коллизии (для отладки).")]
        public bool DebugShowCollision { get; set; } = false;

        [Description("Если упал ниже этой высоты Minecraft — вернуть на точку появления.")]
        public float VoidY { get; set; } = -80f;
    }
}
