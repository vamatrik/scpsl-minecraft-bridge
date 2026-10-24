using System;
using System.IO;
using System.Text.Json;

namespace SlBridge.Compositor
{
    /// <summary>compositor.json рядом с exe — создаётся при первом запуске.</summary>
    public sealed class Settings
    {
        public string SlProcessName { get; set; } = "SCPSL";
        public string SlWindowClass { get; set; } = "UnityWndClass";
        public string MinecraftTitlePrefix { get; set; } = "Minecraft";
        /// <summary>Подгонять окно Minecraft под размер окна SL (нужно, чтобы совпали пропорции и FOV).</summary>
        public bool ResizeMinecraft { get; set; } = true;
        /// <summary>Цвет-ключ (R,G,B 0..1) — тот же, что KeyColor в конфиге плагина SL. По умолчанию чистый зелёный.</summary>
        public float[] KeyColor { get; set; } = { 0f, 1f, 0f };
        /// <summary>Насколько ключевой канал должен преобладать над остальными (0..1), чтобы пиксель заменился на Minecraft.</summary>
        public float KeyThreshold { get; set; } = 0.25f;
        public float KeySoftness { get; set; } = 0.25f;
        /// <summary>Пиксели темнее этого (0..1) не считаются ключом — тени и чёрные детали оружия остаются.</summary>
        public float KeyMinBrightness { get; set; } = 0.06f;
        /// <summary>Подстраивать ключ под эффекты SL (низкое HP, ослепление): эталон фона берётся с краёв кадра.</summary>
        public bool AdaptiveKey { get; set; } = true;
        /// <summary>Пиксель, у которого ключевой канал темнее фона «сейчас» больше чем в 1/x раз, — не фон (тёмный металл оружия). 0 — выключить.</summary>
        public float KeyRelBrightness { get; set; } = 0.55f;
        /// <summary>
        /// Куда девать окно Minecraft: "offscreen" — за пределы экрана (не перехватывает мышь и фокус),
        /// "behind" — под окном SL.
        /// </summary>
        public string MinecraftPlacement { get; set; } = "behind";
        /// <summary>Сквозные клики (окно-оверлей не перехватывает мышь). Выключать только для отладки.</summary>
        public bool ClickThrough { get; set; } = true;
        /// <summary>Отдельное окно «SCP:SL x Minecraft (OBS)» с той же картинкой — его захватывает OBS («Захват окна»).</summary>
        public bool ObsWindow { get; set; } = false;
        /// <summary>Рисовать свой курсор в окнах Minecraft (верстак, инвентарь) — курсор Windows там не виден.</summary>
        public bool DrawCursorInUi { get; set; } = false;
        /// <summary>Окно Minecraft на 1 пиксель ниже экрана — чтобы OBS («Захват экрана») снимал окна Minecraft вживую.</summary>
        public bool AvoidFullscreenFlip { get; set; } = true;
        /// <summary>
        /// Размер картинки Minecraft относительно окна SL (0.4…1). 0.75 — Minecraft рисует почти вдвое меньше пикселей
        /// (заметно больше FPS), мир чуть мягче. Окна крафта всегда в полном размере.
        /// </summary>
        public float McRenderScale { get; set; } = 1f;
        /// <summary>Режим отладки: показывать маску ключа.</summary>
        public bool DebugShowMask { get; set; } = false;

        public static Settings Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "compositor.json");
            var opts = new JsonSerializerOptions { WriteIndented = true };
            Settings s = new Settings();
            try { if (File.Exists(path)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? s; } catch { }
            try { File.WriteAllText(path, JsonSerializer.Serialize(s, opts)); } catch { }
            return s;
        }
    }
}
