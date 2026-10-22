package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.commands.CommandSource;
import net.minecraft.commands.CommandSourceStack;
import net.minecraft.core.BlockPos;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.permissions.LevelBasedPermissionSet;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.food.FoodProperties;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;

/**
 * Всё, что мост делает на потоке встроенного сервера Minecraft:
 * держит тело игрока там, где камера SL, считает падение, пускает в порталы, ловит телепорты,
 * отдаёт предметы на земле, выполняет «использовать», крафт, плавку и команды.
 */
public final class ServerBridge {
    private final BridgeServer bridge;
    private final BridgeConfig cfg;
    private final Crafting crafting = new Crafting();

    private Vec3 lastSnap;
    private Level lastLevel;
    private int teleportSeq;
    private double lastFeetY = Double.NaN;
    private double fallDist;
    private int tickNo;
    private String lastItemsSig = "";
    private String lastMobsSig = "";
    /** Открыт интерфейс Minecraft (верстак, печь, сундук, инвентарь) — предметы сейчас «живут» в инвентаре MC. */
    public volatile boolean uiOpen;
    /** Уровень брони из SL: 0 — нет, 1 — железо (лёгкая), 2 — алмаз (боевая), 3 — незерит (тяжёлая). */
    private int armorTier;

    public ServerBridge(BridgeServer bridge, BridgeConfig cfg) {
        this.bridge = bridge;
        this.cfg = cfg;
    }

    public static ServerPlayer player(MinecraftServer server) {
        var list = server.getPlayerList().getPlayers();
        return list.isEmpty() ? null : list.get(0);
    }

    /** SL пересоздал свои объекты — прислать предметы и мобов заново. */
    public void resetStreams() { lastItemsSig = ""; lastMobsSig = ""; }

    public void reset() {
        lastSnap = null;
        lastLevel = null;
        lastFeetY = Double.NaN;
        fallDist = 0;
        lastItemsSig = "";
        lastMobsSig = "";
    }

    // ───────────── каждый тик сервера ─────────────

    public void tick(MinecraftServer server) {
        ServerPlayer sp = player(server);
        if (sp == null) return;
        tickNo++;
        ServerLevel level = sp.level();

        // Голод не нужен: еда лечит в SL. Держим сытость полной, чтобы не было урона от голода.
        sp.getFoodData().setFoodLevel(20);
        if (!sp.getAbilities().mayfly || !sp.getAbilities().flying) {
            sp.getAbilities().mayfly = true;
            sp.getAbilities().flying = true;
            sp.onUpdateAbilities();
        }

        // 1) Minecraft сам переместил игрока (портал, жемчуг Эндера, /tp, возрождение) → сообщить SL.
        if (lastSnap != null && (level != lastLevel || sp.position().distanceToSqr(lastSnap) > 9.0)) {
            sendTeleport(sp);
            return;
        }
        // ждём, пока SL подтвердит телепорт (придёт поза с новым номером), иначе утащим игрока назад
        if (Pose.lastSeq < Pose.minSeq) return;

        // 2) Тело игрока — там, где камера SL. От этого зависят чанки, цели мобов, порталы.
        Vec3 prev = sp.position();
        double feetY = Pose.feetY;
        sp.snapTo(Pose.x, feetY, Pose.z, Pose.yaw, Pose.pitch);
        sp.setDeltaMovement(Vec3.ZERO);
        Vec3 now = sp.position();
        sp.applyEffectsFromBlocks(prev, now); // порталы, ягоды, паутина, лава...
        level.getChunkSource().move(sp);      // сервер подгружает чанки вокруг новой позиции (пакетов движения от клиента нет)
        lastSnap = now;
        lastLevel = level;

        // Страховка: если игрок SL всё-таки оказался внутри земли — поднимаем на ближайший воздух и сообщаем SL.
        if (unstick(sp, level, feetY)) return;

        // 3) Урон от падения по правилам Minecraft (вода, сено, слизь уменьшают/отменяют).
        fall(sp, level, feetY);

        // 4) Предметы на земле рядом — в SL (там их подбирают на E).
        if (tickNo % 4 == 0) streamItems(sp, level);
        // 4б) Хитбоксы мобов рядом — в SL твёрдыми невидимыми коробками (в них упирается Jailbird, нельзя пройти сквозь).
        if (tickNo % 2 == 0) streamMobs(sp, level);

        // 5) Броня SL → броня Minecraft (её защита учитывается в уроне).
        equipArmor(sp);

        // 6) Предметы, появившиеся в инвентаре Minecraft сами (/give, награды) — переносим в инвентарь SL.
        if (!uiOpen && tickNo % 5 == 0) {
            List<ItemStack> extra = takeInventory(sp);
            if (!extra.isEmpty()) {
                JsonObject m = new JsonObject();
                m.addProperty("t", "invAdd");
                m.add("stacks", ItemJson.toJson(extra));
                bridge.send(m);
            }
        }
    }

    /** Слоты 0..35 и левая рука; броню (36..39) не трогаем — её задаёт SL. */
    private static boolean isItemSlot(int i) { return i < 36 || i == 40; }

    private static List<ItemStack> takeInventory(ServerPlayer sp) {
        List<ItemStack> r = new ArrayList<>();
        var inv = sp.getInventory();
        for (int i = 0; i < inv.getContainerSize(); i++) {
            if (!isItemSlot(i)) continue;
            ItemStack s = inv.getItem(i);
            if (!s.isEmpty()) { r.add(s.copy()); inv.setItem(i, ItemStack.EMPTY); }
        }
        return r;
    }

    private static void fillInventory(ServerPlayer sp, JsonObject m) {
        takeInventory(sp);
        var inv = sp.getInventory();
        if (m.has("inv") && m.get("inv").isJsonArray()) {
            int slot = 0;
            for (var e : m.get("inv").getAsJsonArray()) {
                ItemStack s = ItemJson.fromJson(e);
                if (!s.isEmpty() && slot < 36) inv.setItem(slot, s);
                slot++;
            }
        }
        int held = m.has("heldSlot") ? m.get("heldSlot").getAsInt() : -1;
        inv.setSelectedSlot(held >= 0 && held < 9 ? held : 8);
    }

    private void sendInvSync(ServerPlayer sp, JsonObject extra) {
        JsonObject r = extra != null ? extra : new JsonObject();
        r.addProperty("t", "invSync");
        // по слотам, чтобы SL понял, какой стак во что превратился (ведро → ведро воды)
        JsonArray arr = new JsonArray();
        var inv = sp.getInventory();
        for (int i = 0; i < 36; i++) {
            ItemStack s = inv.getItem(i);
            if (s.isEmpty()) { arr.add(com.google.gson.JsonNull.INSTANCE); continue; }
            arr.add(ItemJson.toJson(s));
        }
        ItemStack off = inv.getItem(40);
        if (!off.isEmpty()) arr.add(ItemJson.toJson(off));
        while (arr.size() > 0 && arr.get(arr.size() - 1).isJsonNull()) arr.remove(arr.size() - 1);
        r.add("slots", arr);
        takeInventory(sp);
        bridge.send(r);
    }

    /** Интерфейс Minecraft закрыт — вернуть всё из инвентаря MC в SL. */
    public void uiClosed(MinecraftServer server) {
        ServerPlayer sp = player(server);
        if (sp == null || !uiOpen) return;
        if (sp.containerMenu != sp.inventoryMenu) sp.doCloseContainer();
        // 2×2 сетка инвентаря — вернуть предметы в инвентарь
        var grid = sp.inventoryMenu.getCraftSlots();
        for (int i = 0; i < grid.getContainerSize(); i++) {
            ItemStack s = grid.removeItemNoUpdate(i);
            if (!s.isEmpty() && !sp.getInventory().add(s)) dropAt(sp, s);
        }
        ItemStack carried = sp.containerMenu.getCarried();
        if (!carried.isEmpty()) { if (!sp.getInventory().add(carried)) dropAt(sp, carried); sp.containerMenu.setCarried(ItemStack.EMPTY); }
        uiOpen = false;
        JsonObject r = new JsonObject();
        r.addProperty("ui", true);
        sendInvSync(sp, r);
    }

    private static void dropAt(ServerPlayer sp, ItemStack s) {
        ItemEntity ie = new ItemEntity(sp.level(), sp.getX(), sp.getY() + 1.0, sp.getZ(), s.copy());
        ie.setPickUpDelay(40);
        sp.level().addFreshEntity(ie);
    }

    private void equipArmor(ServerPlayer sp) {
        net.minecraft.world.item.Item[] set = switch (armorTier) {
            case 1 -> new net.minecraft.world.item.Item[]{net.minecraft.world.item.Items.IRON_HELMET, net.minecraft.world.item.Items.IRON_CHESTPLATE, net.minecraft.world.item.Items.IRON_LEGGINGS, net.minecraft.world.item.Items.IRON_BOOTS};
            case 2 -> new net.minecraft.world.item.Item[]{net.minecraft.world.item.Items.DIAMOND_HELMET, net.minecraft.world.item.Items.DIAMOND_CHESTPLATE, net.minecraft.world.item.Items.DIAMOND_LEGGINGS, net.minecraft.world.item.Items.DIAMOND_BOOTS};
            case 3 -> new net.minecraft.world.item.Item[]{net.minecraft.world.item.Items.NETHERITE_HELMET, net.minecraft.world.item.Items.NETHERITE_CHESTPLATE, net.minecraft.world.item.Items.NETHERITE_LEGGINGS, net.minecraft.world.item.Items.NETHERITE_BOOTS};
            default -> null;
        };
        net.minecraft.world.entity.EquipmentSlot[] slots = {net.minecraft.world.entity.EquipmentSlot.HEAD, net.minecraft.world.entity.EquipmentSlot.CHEST, net.minecraft.world.entity.EquipmentSlot.LEGS, net.minecraft.world.entity.EquipmentSlot.FEET};
        for (int i = 0; i < 4; i++) {
            ItemStack cur = sp.getItemBySlot(slots[i]);
            if (set == null) { if (!cur.isEmpty()) sp.setItemSlot(slots[i], ItemStack.EMPTY); }
            else if (cur.getItem() != set[i]) sp.setItemSlot(slots[i], new ItemStack(set[i]));
        }
    }

    private void sendTeleport(ServerPlayer sp) {
        teleportSeq++;
        Vec3 p = sp.position();
        Pose.teleport(p.x, p.y + SlBridgeClient.EYE_HEIGHT, p.z, teleportSeq);
        lastSnap = p;
        lastLevel = sp.level();
        lastFeetY = Double.NaN;
        fallDist = 0;
        JsonObject m = new JsonObject();
        m.addProperty("t", "teleport");
        m.addProperty("x", p.x);
        m.addProperty("y", p.y);
        m.addProperty("z", p.z);
        m.addProperty("yaw", sp.getYRot());
        m.addProperty("seq", teleportSeq);
        m.addProperty("dim", sp.level().dimension().identifier().toString());
        bridge.send(m);
        SlBridgeClient.LOG.info("[SLBridge] Телепорт Minecraft → {} {}", m.get("dim"), p);
    }

    private int stuckTicks;

    private boolean unstick(ServerPlayer sp, ServerLevel level, double feetY) {
        BlockPos feet = BlockPos.containing(sp.getX(), feetY + 0.3, sp.getZ());
        BlockPos head = feet.above();
        boolean solidFeet = level.getBlockState(feet).isCollisionShapeFullBlock(level, feet);
        boolean solidHead = level.getBlockState(head).isCollisionShapeFullBlock(level, head);
        if (!(solidFeet && solidHead)) { stuckTicks = 0; return false; }
        if (++stuckTicks < 10) return false;
        stuckTicks = 0;
        BlockPos.MutableBlockPos p = feet.mutable();
        for (int i = 0; i < 96 && p.getY() < level.getMaxY(); i++, p.move(0, 1, 0)) {
            BlockPos below = p.below();
            if (level.getBlockState(p).getCollisionShape(level, p).isEmpty()
                    && level.getBlockState(p.above()).getCollisionShape(level, p.above()).isEmpty()
                    && !level.getBlockState(below).getCollisionShape(level, below).isEmpty()) {
                sp.snapTo(sp.getX(), p.getY() + 0.05, sp.getZ(), sp.getYRot(), sp.getXRot());
                SlBridgeClient.LOG.info("[SLBridge] Игрок застрял в блоках — поднимаю на {}", p.getY());
                sendTeleport(sp);
                return true;
            }
        }
        return false;
    }

    private void fall(ServerPlayer sp, ServerLevel level, double feetY) {
        if (Double.isNaN(lastFeetY)) { lastFeetY = feetY; return; }
        double dy = lastFeetY - feetY;
        lastFeetY = feetY;
        if (sp.isInWater() || sp.onClimbable() || sp.isInLava()) { fallDist = 0; return; }
        if (dy > 0.0005) { fallDist += dy; return; }
        if (dy < -0.0005) { fallDist = 0; return; } // идём вверх (прыжок)
        if (fallDist <= 0) return;
        BlockPos below = BlockPos.containing(sp.getX(), feetY - 0.2, sp.getZ());
        BlockState st = level.getBlockState(below);
        if (st.getCollisionShape(level, below).isEmpty()) return; // ещё в воздухе (зависли на месте)
        double d = fallDist;
        fallDist = 0;
        if (d > 3.0) st.getBlock().fallOn(level, st, below, sp, d); // сам Minecraft посчитает урон (сено, слизь...)
    }

    private void streamMobs(ServerPlayer sp, ServerLevel level) {
        var list = level.getEntitiesOfClass(net.minecraft.world.entity.LivingEntity.class, sp.getBoundingBox().inflate(20.0),
                e -> e.isAlive() && !(e instanceof net.minecraft.world.entity.player.Player) && !e.isSpectator());
        StringBuilder sig = new StringBuilder();
        JsonArray arr = new JsonArray();
        // Эндер-дракон — составной: его хитбоксы — это части (голова, шея, тело, хвост, крылья), а не огромная коробка.
        List<net.minecraft.world.entity.Entity> boxes = new ArrayList<>();
        for (var e : list) {
            if (e instanceof net.minecraft.world.entity.boss.enderdragon.EnderDragon dragon) {
                java.util.Collections.addAll(boxes, dragon.getSubEntities());
            } else boxes.add(e);
        }
        for (var e : boxes) {
            AABB b = e.getBoundingBox();
            double x = Math.round((b.minX + b.maxX) * 5) / 10.0, y = Math.round(b.minY * 10) / 10.0, z = Math.round((b.minZ + b.maxZ) * 5) / 10.0;
            double w = Math.round(b.getXsize() * 20) / 20.0, h = Math.round(b.getYsize() * 20) / 20.0, d = Math.round(b.getZsize() * 20) / 20.0;
            JsonObject o = new JsonObject();
            o.addProperty("e", e.getId());
            o.addProperty("x", x); o.addProperty("y", y); o.addProperty("z", z);
            o.addProperty("w", w); o.addProperty("h", h); o.addProperty("d", d);
            arr.add(o);
            sig.append(e.getId()).append(':').append(x).append(',').append(y).append(',').append(z).append(',').append(w).append(',').append(h).append(';');
        }
        String s = sig.toString();
        if (s.equals(lastMobsSig)) return;
        lastMobsSig = s;
        JsonObject m = new JsonObject();
        m.addProperty("t", "mobs");
        m.add("list", arr);
        bridge.send(m);
    }

    private void streamItems(ServerPlayer sp, ServerLevel level) {
        List<ItemEntity> list = level.getEntitiesOfClass(ItemEntity.class, sp.getBoundingBox().inflate(12.0), Entity::isAlive);
        StringBuilder sig = new StringBuilder();
        JsonArray arr = new JsonArray();
        for (ItemEntity ie : list) {
            ItemStack s = ie.getItem();
            JsonObject o = ItemJson.toJson(s);
            o.addProperty("e", ie.getId());
            double x = Math.round(ie.getX() * 10) / 10.0, y = Math.round(ie.getY() * 10) / 10.0, z = Math.round(ie.getZ() * 10) / 10.0;
            o.addProperty("x", x);
            o.addProperty("y", y);
            o.addProperty("z", z);
            arr.add(o);
            sig.append(ie.getId()).append(':').append(s.getCount()).append('@').append(x).append(',').append(y).append(',').append(z).append(';');
        }
        String s = sig.toString();
        if (s.equals(lastItemsSig)) return;
        lastItemsSig = s;
        JsonObject m = new JsonObject();
        m.addProperty("t", "items");
        m.add("list", arr);
        bridge.send(m);
    }

    // ───────────── сообщения от SL ─────────────

    public void handle(MinecraftServer server, JsonObject m) {
        ServerPlayer sp = player(server);
        if (sp == null) return;
        ServerLevel level = sp.level();
        String t = m.get("t").getAsString();
        switch (t) {
            case "pickup" -> pickup(sp, level, m);
            case "drop" -> drop(level, m);
            case "use" -> use(sp, level, m);
            case "openInv" -> {
                fillInventory(sp, m);
                uiOpen = true;
                net.minecraft.client.Minecraft mc = net.minecraft.client.Minecraft.getInstance();
                mc.execute(() -> { if (mc.player != null) mc.gui.setScreen(new net.minecraft.client.gui.screens.inventory.InventoryScreen(mc.player)); });
                JsonObject r = new JsonObject();
                r.addProperty("t", "ui");
                r.addProperty("open", true);
                bridge.send(r);
            }
            case "armor" -> armorTier = m.get("tier").getAsInt();
            case "craftList" -> bridge.send(crafting.craftList(sp, level, ItemJson.listFromJson(m.get("inv"))));
            case "craft" -> bridge.send(crafting.craft(sp, level, m.get("rid").getAsString(), m.get("times").getAsInt(), ItemJson.listFromJson(m.get("inv"))));
            case "smeltList" -> bridge.send(crafting.smeltList(sp, level, ItemJson.listFromJson(m.get("inv"))));
            case "smelt" -> bridge.send(crafting.smelt(sp, level, m.get("rid").getAsString(), m.get("times").getAsInt(), ItemJson.listFromJson(m.get("inv"))));
            case "cmd" -> command(server, sp, m.get("text").getAsString());
            case "respawnReq" -> respawn(server, sp);
            default -> {}
        }
    }

    private void pickup(ServerPlayer sp, ServerLevel level, JsonObject m) {
        int id = m.get("e").getAsInt();
        Entity e = level.getEntity(id);
        JsonObject r = new JsonObject();
        r.addProperty("t", "picked");
        r.addProperty("e", id);
        if (e instanceof ItemEntity ie && ie.isAlive()) {
            ItemStack s = ie.getItem().copy();
            ie.discard();
            level.playSound(null, ie.getX(), ie.getY(), ie.getZ(), SoundEvents.ITEM_PICKUP, SoundSource.PLAYERS, 0.3f, 1.4f);
            r.add("item", ItemJson.toJson(s));
            lastItemsSig = "";
        }
        bridge.send(r);
    }

    private void drop(ServerLevel level, JsonObject m) {
        ItemStack s = ItemJson.fromJson(m.get("item"));
        if (s.isEmpty()) return;
        double x = m.get("x").getAsDouble(), y = m.get("y").getAsDouble(), z = m.get("z").getAsDouble();
        double dx = m.has("dx") ? m.get("dx").getAsDouble() : 0, dy = m.has("dy") ? m.get("dy").getAsDouble() : 0, dz = m.has("dz") ? m.get("dz").getAsDouble() : 0;
        ItemEntity ie = new ItemEntity(level, x, y - 0.3, z, s, dx * 0.3, dy * 0.3 + 0.1, dz * 0.3);
        ie.setPickUpDelay(m.has("delay") ? m.get("delay").getAsInt() : 40);
        level.addFreshEntity(ie);
    }

    /** «Использовать» (E в SL): открыть верстак/печь/сундук, нажать дверь/рычаг, поставить блок, применить предмет, съесть. */
    private void use(ServerPlayer sp, ServerLevel level, JsonObject m) {
        Vec3 from = new Vec3(m.get("x").getAsDouble(), m.get("y").getAsDouble(), m.get("z").getAsDouble());
        Vec3 dir = new Vec3(m.get("dx").getAsDouble(), m.get("dy").getAsDouble(), m.get("dz").getAsDouble()).normalize();
        // весь инвентарь SL (стаки Minecraft) — в инвентарь MC, предмет в руке — выбранный слот
        fillInventory(sp, m);

        JsonObject r = new JsonObject();
        BlockHitResult hit = level.clip(new ClipContext(from, from.add(dir.scale(cfg.reach)), ClipContext.Block.OUTLINE, ClipContext.Fluid.NONE, sp));
        boolean done = false;
        if (hit.getType() == HitResult.Type.BLOCK) {
            InteractionResult res = sp.gameMode.useItemOn(sp, level, sp.getMainHandItem(), InteractionHand.MAIN_HAND, hit);
            done = res.consumesAction();
        }
        if (sp.containerMenu != sp.inventoryMenu) {
            // открылся настоящий интерфейс Minecraft (верстак, печь, сундук…) — предметы остаются в MC до закрытия
            uiOpen = true;
            JsonObject u = new JsonObject();
            u.addProperty("t", "ui");
            u.addProperty("open", true);
            bridge.send(u);
            return;
        }
        if (!done && !sp.getMainHandItem().isEmpty()) {
            ItemStack h = sp.getMainHandItem();
            FoodProperties food = h.get(DataComponents.FOOD);
            if (food != null) {
                h.shrink(1);
                r.addProperty("heal", food.nutrition() * cfg.healPerFoodPoint);
                level.playSound(null, sp.getX(), sp.getY(), sp.getZ(), SoundEvents.GENERIC_EAT.value(), SoundSource.PLAYERS, 1f, 1f);
            } else {
                sp.gameMode.useItem(sp, level, h, InteractionHand.MAIN_HAND);
            }
        }
        if (sp.isUsingItem()) sp.stopUsingItem();
        sendInvSync(sp, r);
    }

    private void command(MinecraftServer server, ServerPlayer sp, String text) {
        List<String> lines = new ArrayList<>();
        CommandSource collector = new CommandSource() {
            @Override public void sendSystemMessage(Component c) { lines.add(c.getString()); }
            @Override public boolean acceptsSuccess() { return true; }
            @Override public boolean acceptsFailure() { return true; }
            @Override public boolean shouldInformAdmins() { return false; }
        };
        CommandSourceStack src = sp.createCommandSourceStack().withSource(collector).withPermission(LevelBasedPermissionSet.OWNER);
        String cmd = text.startsWith("/") ? text.substring(1) : text;
        try {
            server.getCommands().performPrefixedCommand(src, cmd);
        } catch (Exception e) {
            lines.add("Ошибка: " + e.getMessage());
        }
        JsonObject r = new JsonObject();
        r.addProperty("t", "cmdOut");
        r.addProperty("cmd", cmd);
        JsonArray a = new JsonArray();
        for (String l : lines) a.add(l);
        r.add("lines", a);
        bridge.send(r);
    }

    /** Возрождение после смерти в SL: точка возрождения мира (в Верхнем мире). */
    private void respawn(MinecraftServer server, ServerPlayer sp) {
        ServerLevel ow = server.overworld();
        BlockPos sp0 = ow.getRespawnData().pos();
        ow.getChunk(sp0.getX() >> 4, sp0.getZ() >> 4); // загрузить чанк, иначе высота посчитается как дно мира
        int y = ow.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES, sp0.getX(), sp0.getZ());
        if (y <= ow.getMinY() + 1) y = sp0.getY();
        sp.teleportTo(ow, sp0.getX() + 0.5, y + 0.1, sp0.getZ() + 0.5, Set.of(), sp.getYRot(), 0f, true);
        sendTeleport(sp);
    }
}
