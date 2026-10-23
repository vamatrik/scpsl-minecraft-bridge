package dev.slbridge;

import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.io.Reader;
import java.io.Writer;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

/** Настройки моста: config/slbridge.properties (создаётся при первом запуске). */
public final class BridgeConfig {
    public int port = 25599;
    /** Вертикальный FOV камеры SCP:SL в градусах (в SL — 70). */
    public float fov = 70f;
    /** Рисовать свою стрелку-курсор в окнах Minecraft (системный курсор там не виден). */
    public boolean drawCursor = true;
    /** Показывать полоску здоровья босса Minecraft (дракон, иссушитель) вверху экрана. */
    public boolean bossBar = true;
    /** Во сколько раз урон Minecraft больше переводится в урон SL (20 HP → 100 HP). */
    public float damageScale = 5f;
    /** Сколько выстрелов нужно на единицу твёрдости блока (камень 1.5 → 3 выстрела). */
    public float shotsPerHardness = 2f;
    /** Радиус коллизий в ячейках 8×8×8 вокруг игрока. */
    public int collisionRadiusCells = 1;
    /** Сколько ячеек коллизий пересчитывать за тик (больше — быстрее подгрузка, но тяжелее). */
    public int collisionCellsPerTick = 6;
    /** Дальность «использовать» (E), блоков. */
    public double reach = 5.0;
    /** Дальность луча фонарика, блоков. */
    public double flashlightRange = 28.0;
    /** Сколько HP SL лечит одно очко сытости еды (хлеб = 5 → 12.5 HP). */
    public float healPerFoodPoint = 2.5f;


    public static BridgeConfig load() {
        BridgeConfig c = new BridgeConfig();
        Path file = FabricLoader.getInstance().getConfigDir().resolve("slbridge.properties");
        Properties p = new Properties();
        if (Files.exists(file)) {
            try (Reader r = Files.newBufferedReader(file)) { p.load(r); } catch (IOException e) { SlBridgeClient.LOG.warn("config read failed", e); }
        }
        c.port = Integer.parseInt(p.getProperty("port", String.valueOf(c.port)));
        c.fov = Float.parseFloat(p.getProperty("fov", String.valueOf(c.fov)));
        c.drawCursor = Boolean.parseBoolean(p.getProperty("drawCursor", String.valueOf(c.drawCursor)));
        c.bossBar = Boolean.parseBoolean(p.getProperty("bossBar", String.valueOf(c.bossBar)));
        c.damageScale = Float.parseFloat(p.getProperty("damageScale", String.valueOf(c.damageScale)));
        c.shotsPerHardness = Float.parseFloat(p.getProperty("shotsPerHardness", String.valueOf(c.shotsPerHardness)));
        c.collisionRadiusCells = Integer.parseInt(p.getProperty("collisionRadiusCells", String.valueOf(c.collisionRadiusCells)));
        c.collisionCellsPerTick = Integer.parseInt(p.getProperty("collisionCellsPerTick", String.valueOf(c.collisionCellsPerTick)));
        c.reach = Double.parseDouble(p.getProperty("reach", String.valueOf(c.reach)));
        c.flashlightRange = Double.parseDouble(p.getProperty("flashlightRange", String.valueOf(c.flashlightRange)));
        c.healPerFoodPoint = Float.parseFloat(p.getProperty("healPerFoodPoint", String.valueOf(c.healPerFoodPoint)));

        Properties out = new Properties();
        out.setProperty("port", String.valueOf(c.port));
        out.setProperty("fov", String.valueOf(c.fov));
        out.setProperty("drawCursor", String.valueOf(c.drawCursor));
        out.setProperty("bossBar", String.valueOf(c.bossBar));
        out.setProperty("damageScale", String.valueOf(c.damageScale));
        out.setProperty("shotsPerHardness", String.valueOf(c.shotsPerHardness));
        out.setProperty("collisionRadiusCells", String.valueOf(c.collisionRadiusCells));
        out.setProperty("collisionCellsPerTick", String.valueOf(c.collisionCellsPerTick));
        out.setProperty("reach", String.valueOf(c.reach));
        out.setProperty("flashlightRange", String.valueOf(c.flashlightRange));
        out.setProperty("healPerFoodPoint", String.valueOf(c.healPerFoodPoint));

        try (Writer w = Files.newBufferedWriter(file)) { out.store(w, "SCP:SL <-> Minecraft bridge"); } catch (IOException e) { SlBridgeClient.LOG.warn("config write failed", e); }
        return c;
    }
}
