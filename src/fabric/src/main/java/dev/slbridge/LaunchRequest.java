package dev.slbridge;

import com.google.gson.Gson;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPreset;
import net.minecraft.world.level.levelgen.presets.WorldPresets;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Запрос от лаунчера: config/slbridge-launch.json — «открой мир X» или «создай мир с настройками».
 * Выполняется один раз с главного меню и удаляется. Работает с любым лаунчером Minecraft.
 *
 * И обратно: config/slbridge-recent.json — недавние миры, в которых играли через мост (для вкладки «Недавние»).
 */
public final class LaunchRequest {
    private static boolean done;

    private static Path file(String name) { return FabricLoader.getInstance().getConfigDir().resolve(name); }

    public static void tick(Minecraft mc) {
        if (done || mc.level != null || !(mc.gui.screen() instanceof TitleScreen)) return;
        done = true;
        Path f = file("slbridge-launch.json");
        if (!Files.exists(f)) return;
        JsonObject req;
        try {
            req = JsonParser.parseString(Files.readString(f, StandardCharsets.UTF_8)).getAsJsonObject();
            Files.deleteIfExists(f); // одноразово: следующий ручной запуск Minecraft не уйдёт сам в мир
        } catch (Exception e) {
            SlBridgeClient.LOG.warn("[SLBridge] Не прочитал запрос лаунчера", e);
            return;
        }
        String action = req.has("action") ? req.get("action").getAsString() : "open";
        try {
            if ("create".equals(action)) create(mc, req);
            else open(mc, req.get("world").getAsString());
        } catch (Exception e) {
            SlBridgeClient.LOG.error("[SLBridge] Не удалось выполнить запрос лаунчера", e);
        }
    }

    private static void open(Minecraft mc, String folder) {
        if (!mc.getLevelSource().levelExists(folder)) {
            SlBridgeClient.LOG.warn("[SLBridge] Мир «{}» не найден", folder);
            return;
        }
        SlBridgeClient.LOG.info("[SLBridge] Открываю мир «{}» (по запросу лаунчера)", folder);
        mc.createWorldOpenFlows().openWorld(folder, () -> mc.gui.setScreen(new TitleScreen()));
    }

    private static void create(Minecraft mc, JsonObject r) {
        String name = r.has("name") ? r.get("name").getAsString() : "Мир SCP";
        // папка: имя без запрещённых символов, уникальное
        String folder = name.replaceAll("[\\\\/:*?\"<>|.]", "_").trim();
        if (folder.isEmpty()) folder = "World";
        String base = folder;
        for (int i = 2; mc.getLevelSource().levelExists(folder); i++) folder = base + " (" + i + ")";

        String mode = r.has("mode") ? r.get("mode").getAsString() : "survival";
        boolean hardcore = "hardcore".equals(mode);
        GameType type = "creative".equals(mode) ? GameType.CREATIVE : GameType.SURVIVAL;
        Difficulty diff = switch (r.has("difficulty") ? r.get("difficulty").getAsString() : "normal") {
            case "peaceful" -> Difficulty.PEACEFUL;
            case "easy" -> Difficulty.EASY;
            case "hard" -> Difficulty.HARD;
            default -> Difficulty.NORMAL;
        };
        if (hardcore) diff = Difficulty.HARD;
        boolean cheats = r.has("cheats") && r.get("cheats").getAsBoolean() && !hardcore;

        LevelSettings settings = new LevelSettings(name, type, new LevelSettings.DifficultySettings(diff, hardcore, false), cheats, WorldDataConfiguration.DEFAULT);
        WorldOptions options = WorldOptions.defaultWithRandomSeed();
        if (r.has("seed") && !r.get("seed").getAsString().isBlank()) options = options.withSeed(WorldOptions.parseSeed(r.get("seed").getAsString()));
        ResourceKey<WorldPreset> preset = switch (r.has("type") ? r.get("type").getAsString() : "normal") {
            case "flat" -> WorldPresets.FLAT;
            case "large_biomes" -> WorldPresets.LARGE_BIOMES;
            case "amplified" -> WorldPresets.AMPLIFIED;
            default -> WorldPresets.NORMAL;
        };
        SlBridgeClient.LOG.info("[SLBridge] Создаю мир «{}» (папка «{}»): {}, {}, читы={}, тип={}", name, folder, mode, diff, cheats, preset);
        final ResourceKey<WorldPreset> p = preset;
        mc.createWorldOpenFlows().createFreshLevel(folder, settings, options,
                provider -> provider.lookupOrThrow(Registries.WORLD_PRESET).getOrThrow(p).value().createWorldDimensions(),
                new TitleScreen());
    }

    /** Запомнить мир, в котором играем через мост. */
    public static void rememberWorld(String folder, String name) {
        try {
            Path f = file("slbridge-recent.json");
            JsonArray old = Files.exists(f) ? JsonParser.parseString(Files.readString(f, StandardCharsets.UTF_8)).getAsJsonArray() : new JsonArray();
            JsonArray out = new JsonArray();
            JsonObject me = new JsonObject();
            me.addProperty("folder", folder);
            me.addProperty("name", name);
            me.addProperty("time", System.currentTimeMillis());
            out.add(me);
            for (var e : old) {
                if (out.size() >= 10) break;
                if (e.getAsJsonObject().get("folder").getAsString().equals(folder)) continue;
                out.add(e);
            }
            Files.writeString(f, new Gson().toJson(out), StandardCharsets.UTF_8);
        } catch (Exception e) {
            SlBridgeClient.LOG.warn("[SLBridge] Не записал недавние миры", e);
        }
    }

    private LaunchRequest() {}
}
