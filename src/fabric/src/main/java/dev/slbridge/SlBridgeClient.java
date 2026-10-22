package dev.slbridge;

import com.google.gson.JsonObject;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.client.InactivityFpsLimit;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.client.server.IntegratedServer;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.damagesource.DamageTypes;
import net.minecraft.world.phys.Vec3;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Сторона Minecraft. Minecraft здесь — «рендерер мира»: камеру ведёт SCP:SL,
 * интерфейс и руки скрыты, а мир, блоки и мобы живут как обычно в одиночной игре.
 */
public final class SlBridgeClient implements ClientModInitializer {
    public static final Logger LOG = LoggerFactory.getLogger("slbridge");
    /** Рост глаз игрока Minecraft над ногами. */
    public static final double EYE_HEIGHT = 1.62;

    public static BridgeConfig config;
    public static BridgeServer bridge;
    private static final CollisionStreamer collisions = new CollisionStreamer();
    private static final WorldActions actions = new WorldActions();
    private static ServerBridge serverBridge;
    /** Где стоял игрок Minecraft до того, как мост взял его под контроль — это точка появления для SL. */
    private static volatile Vec3 home;

    // «Неуязвимость после урона» как у игрока Minecraft (10 тиков): иначе лава/огонь бьют каждый тик.
    private static int lastHurtTick = -1000;
    private static float lastHurtAmount;

    /** Открыт интерфейс-контейнер Minecraft (верстак, печь, инвентарь…): ввод идёт в Minecraft. */
    public static volatile boolean uiMode;
    public static final String UI_TITLE = "Minecraft* SLBRIDGE-UI";

    /** Ввод Minecraft заблокирован (управляет SL), кроме моментов, когда открыт интерфейс Minecraft. */
    public static boolean isInputBlocked() { return isBridgeActive() && !uiMode; }

    /** true, когда SCP:SL подключён и шлёт позу — тогда камера и интерфейс MC переходят под мост. */
    public static boolean isBridgeActive() {
        return Pose.active && bridge != null && bridge.isConnected();
    }

    @Override
    public void onInitializeClient() {
        config = BridgeConfig.load();
        bridge = new BridgeServer(config.port, this::onMessage, this::onConnect, this::onDisconnect);
        serverBridge = new ServerBridge(bridge, config);
        bridge.start();

        ClientTickEvents.END_CLIENT_TICK.register(this::clientTick);
        ServerTickEvents.END_SERVER_TICK.register(server -> {
            if (!isBridgeActive()) return;
            serverBridge.tick(server);
            ServerPlayer sp = ServerBridge.player(server);
            if (sp != null) actions.tick(sp.level());
        });

        // Урон игроку Minecraft не наносится — он пересылается в SCP:SL (×5) и снимается там.
        ServerLivingEntityEvents.ALLOW_DAMAGE.register((entity, source, amount) -> {
            if (!(entity instanceof ServerPlayer) || !isBridgeActive()) return true;
            boolean ignored = source.is(DamageTypes.IN_WALL) || source.is(DamageTypes.FLY_INTO_WALL)
                    || source.is(DamageTypes.CRAMMING) || source.is(DamageTypes.STARVE);
            if (!ignored && amount > 0 && !source.is(net.minecraft.tags.DamageTypeTags.BYPASSES_COOLDOWN)) {
                int since = entity.tickCount - lastHurtTick;
                if (since >= 0 && since < 10) {
                    if (amount <= lastHurtAmount) return false;      // в окне неуязвимости — только «добивка» сверх прошлого удара
                    float extra = amount - lastHurtAmount;
                    lastHurtAmount = amount;
                    amount = extra;
                } else {
                    lastHurtTick = entity.tickCount;
                    lastHurtAmount = amount;
                }
            }
            if (!ignored && amount > 0) {
                // броня (из SL) уменьшает урон по правилам Minecraft
                if (!source.is(net.minecraft.tags.DamageTypeTags.BYPASSES_ARMOR)) {
                    amount = net.minecraft.world.damagesource.CombatRules.getDamageAfterAbsorb(entity, amount, source,
                            entity.getArmorValue(), (float) entity.getAttributeValue(net.minecraft.world.entity.ai.attributes.Attributes.ARMOR_TOUGHNESS));
                }
                JsonObject m = new JsonObject();
                m.addProperty("t", "hurt");
                m.addProperty("amount", amount * config.damageScale);
                m.addProperty("mcAmount", amount);
                m.addProperty("source", source.getMsgId());
                if (source.getEntity() != null) {
                    m.addProperty("attacker", BuiltInRegistries.ENTITY_TYPE.getKey(source.getEntity().getType()).toString());
                }
                bridge.send(m);
            }
            return false;
        });
    }

    private void onConnect() {
        JsonObject hello = new JsonObject();
        hello.addProperty("t", "hello");
        hello.addProperty("ver", 2);
        hello.addProperty("side", "minecraft");
        bridge.send(hello);
    }

    private void onDisconnect() {
        Minecraft.getInstance().execute(() -> {
            var o = Minecraft.getInstance().options;
            if (savedBob != null) { o.bobView().set(savedBob); o.damageTiltStrength().set(savedTilt); savedBob = null; }
        });
        Pose.active = false;
        uiMode = false;
        Pose.minSeq = 0;
        Pose.lastSeq = 0;
        Minecraft.getInstance().execute(collisions::reset);
        serverBridge.reset();
    }

    private void onServer(Runnable r) {
        IntegratedServer server = Minecraft.getInstance().getSingleplayerServer();
        if (server != null) server.execute(r);
    }

    private void onMessage(JsonObject m) {
        String t = m.get("t").getAsString();
        Minecraft mc = Minecraft.getInstance();
        switch (t) {
            case "hello" -> LOG.info("[SLBridge] SCP:SL: {}", m);
            case "streams" -> serverBridge.resetStreams();
            case "resync" -> { // SL пересоздал коллизии/объекты — пришлём всё заново
                mc.execute(collisions::reset);
                serverBridge.resetStreams();
            }
            case "pose" -> Pose.set(m.get("x").getAsDouble(), m.get("y").getAsDouble(), m.get("z").getAsDouble(),
                    m.get("yaw").getAsFloat(), m.get("pitch").getAsFloat(), m.has("seq") ? m.get("seq").getAsInt() : 0,
                    m.has("fy") ? m.get("fy").getAsDouble() : Double.NaN);
            case "spawnReq" -> mc.execute(() -> {
                LocalPlayer p = mc.player;
                Vec3 h = home != null ? home : (p != null ? p.position() : null);
                JsonObject r = new JsonObject();
                r.addProperty("t", "spawn");
                r.addProperty("ready", p != null && h != null);
                if (h != null) {
                    r.addProperty("x", h.x);
                    r.addProperty("y", h.y);
                    r.addProperty("z", h.z);
                    r.addProperty("yaw", p != null ? p.getYRot() : 0f);
                }
                bridge.send(r);
            });
            case "shot" -> {
                Vec3 from = new Vec3(m.get("x").getAsDouble(), m.get("y").getAsDouble(), m.get("z").getAsDouble());
                Vec3 dir = new Vec3(m.get("dx").getAsDouble(), m.get("dy").getAsDouble(), m.get("dz").getAsDouble());
                double range = m.has("range") ? m.get("range").getAsDouble() : 128.0;
                float dmg = m.has("dmg") ? m.get("dmg").getAsFloat() : 20f;
                boolean breaks = !m.has("breaks") || m.get("breaks").getAsBoolean();
                boolean kb = m.has("kb") && m.get("kb").getAsBoolean();
                onServer(() -> {
                    IntegratedServer server = mc.getSingleplayerServer();
                    ServerPlayer sp = server == null ? null : ServerBridge.player(server);
                    if (sp != null) actions.shot(sp.level(), sp, from, dir, range, dmg, breaks, kb, config, bridge);
                });
            }
            case "pickup", "drop", "use", "openInv", "armor", "craftList", "craft", "smeltList", "smelt", "cmd", "respawnReq" -> onServer(() -> {
                IntegratedServer server = mc.getSingleplayerServer();
                if (server != null) serverBridge.handle(server, m);
            });
            default -> LOG.debug("[SLBridge] неизвестное сообщение {}", t);
        }
    }

    private static String rememberedWorld;
    private static Boolean savedBob;
    private static Double savedTilt;

    private void clientTick(Minecraft mc) {
        // Запрос лаунчера «открой/создай мир» — выполняется с главного меню, один раз.
        LaunchRequest.tick(mc);
        if (mc.player == null || mc.level == null) { home = null; return; }
        if (!isBridgeActive()) {
            // Пока SL не управляет — запоминаем, где стоит игрок (на земле), это будет точка появления.
            if (mc.player.onGround() || home == null) home = mc.player.position();
            return;
        }

        // Мир, в котором играем через мост, — в «недавние» лаунчера.
        IntegratedServer srv = mc.getSingleplayerServer();
        if (srv != null) {
            String folder = srv.getWorldPath(net.minecraft.world.level.storage.LevelResource.ROOT).normalize().getFileName().toString();
            if (!folder.equals(rememberedWorld)) {
                rememberedWorld = folder;
                String name = srv.getWorldData().getLevelName();
                java.util.concurrent.CompletableFuture.runAsync(() -> LaunchRequest.rememberWorld(folder, name));
            }
        }

        // Интерфейсы-контейнеры (верстак, печь, сундук, инвентарь) — оставляем: в них играешь мышкой.
        // Остальные меню (пауза, смерть, титры Энда) закрываем — их не видно, а игра стоит.
        var screen = mc.gui.screen();
        if (screen instanceof net.minecraft.client.gui.screens.inventory.AbstractContainerScreen<?>
                || screen instanceof net.minecraft.client.gui.screens.WinScreen) { // титры после Энда — показываем, их закрытие возвращает в Верхний мир
            if (!uiMode) { uiMode = true; mc.getWindow().setTitle(UI_TITLE); }
        } else {
            if (screen != null) mc.gui.setScreen(null);
            if (uiMode) {
                uiMode = false;
                mc.updateTitle();
                onServer(() -> {
                    IntegratedServer server = mc.getSingleplayerServer();
                    if (server != null) serverBridge.uiClosed(server);
                });
            }
        }

        // Minecraft работает в фоне под окном SCP:SL — он не должен ставить игру на паузу и снижать FPS.
        mc.options.pauseOnLostFocus = false;
        // Покачивание камеры при ходьбе и наклон при уроне сдвигают картинку Minecraft относительно прицела SL —
        // на дальних дистанциях это промах на целый блок. Выключаем, пока работает мост (и возвращаем потом).
        if (savedBob == null) { savedBob = mc.options.bobView().get(); savedTilt = mc.options.damageTiltStrength().get(); }
        if (mc.options.bobView().get()) mc.options.bobView().set(false);
        if (mc.options.damageTiltStrength().get() != 0.0) mc.options.damageTiltStrength().set(0.0);
        if (mc.options.inactivityFpsLimit().get() != InactivityFpsLimit.MINIMIZED)
            mc.options.inactivityFpsLimit().set(InactivityFpsLimit.MINIMIZED);
        mc.hitResult = null;
        if (!uiMode && mc.mouseHandler.isMouseGrabbed()) mc.mouseHandler.releaseMouse();

        // Тело игрока на клиенте — там же, где камера SL (позицию на сервер оно не шлёт, её ставит сервер).
        LocalPlayer p = mc.player;
        p.getAbilities().mayfly = true;
        p.getAbilities().flying = true;
        p.setDeltaMovement(Vec3.ZERO);
        p.snapTo(Pose.x, Pose.feetY, Pose.z, Pose.yaw, Pose.pitch);

        collisions.tick(mc.level, bridge, config.collisionRadiusCells, config.collisionCellsPerTick);
    }
}
