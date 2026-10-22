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

        [Description("Стартовые предметы.")]
        public List<ItemType> Loadout { get; set; } = new List<ItemType> { ItemType.GunE11SR, ItemType.GunCOM18, ItemType.Medkit };

        [Description("Стартовые патроны: тип → количество.")]
        public Dictionary<ItemType, ushort> Ammo { get; set; } = new Dictionary<ItemType, ushort>
        {
            { ItemType.Ammo556x45, 200 },
            { ItemType.Ammo9x19, 120 },
        };

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
        [Description("Половина размера «комнаты-фона» вокруг игрока, в метрах. Маленькая — чтобы туман SL не размывал цвет-ключ.")]
        public float BackdropHalfSize { get; set; } = 2.5f;
        [Description("Высота центра комнаты над точкой игрока (примерно уровень глаз).")]
        public float BackdropCenterHeight { get; set; } = 0.6f;

        [Description("Яркость света в Minecraft от лампы (Lantern) в руке, 0–15. Без лампы в темноте темно.")]
        public int LanternLight { get; set; } = 14;
        [Description("Яркость света в Minecraft от включённого фонарика в руке, 0–15.")]
        public int FlashlightLight { get; set; } = 11;

        [Description("Свет над игроком, чтобы оружие в руках не было чёрным.")]
        public float LightIntensity { get; set; } = 1.5f;
        public float LightRange { get; set; } = 10f;

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

        [Description("Ближний бой и Micro HID: дальность (м) и урон SL.")]
        public float MeleeRange { get; set; } = 3f;
        public float JailbirdDamage { get; set; } = 60f;
        public float MarshmallowDamage { get; set; } = 40f;
        public float MicroHidRange { get; set; } = 8f;
        public float MicroHidDamagePerTick { get; set; } = 30f;
        [Description("Micro HID на ПКМ (заряженный выстрел): дальность и урон за 0.1 с.")]
        public float MicroHidChargeRange { get; set; } = 12f;
        public float MicroHidChargeDamagePerTick { get; set; } = 80f;
        public float JailbirdChargeDamage { get; set; } = 200f;
        public float Scp1509Damage { get; set; } = 50f;

        [Description("Выключить всё, что мешает в мире Minecraft: обеззараживание ЛКЗ, Dead Man's Sequence, боеголовку, прибытие отрядов.")]
        public bool DisableFacilityEvents { get; set; } = true;

        [Description("Показывать коллизии (для отладки).")]
        public bool DebugShowCollision { get; set; } = false;

        [Description("Если упал ниже этой высоты Minecraft — вернуть на точку появления.")]
        public float VoidY { get; set; } = -80f;
    }
}
