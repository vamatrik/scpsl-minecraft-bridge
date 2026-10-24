package dev.slbridge;

import net.minecraft.world.level.block.LightBlock;
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
    final Fun fun;
    private boolean xrayPurged;

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
    private String armorEnch = "";
    private int lastXp = -1;

    public ServerBridge(BridgeServer bridge, BridgeConfig cfg) {
        this.bridge = bridge;
        this.cfg = cfg;
        this.fun = new Fun(bridge, this);
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

    // ───────────── лампа ─────────────

    /** Наши блоки света: позиция → уровень. Лампа — один у головы; фонарик — пятно там, куда смотришь. */
    private final java.util.Map<BlockPos, Integer> lamps = new java.util.HashMap<>();
    private ServerLevel lampLevel;
    private Boolean lastKeepInv;

    private static boolean lampFits(ServerLevel level, BlockPos p) {
        var st = level.getBlockState(p);
        return st.isAir() || st.is(Blocks.LIGHT);
    }

    private void updateLamp(ServerPlayer sp, ServerLevel level) {
        int want = Math.max(0, Math.min(15, Pose.lamp));
        java.util.Map<BlockPos, Integer> target = new java.util.HashMap<>();
        if (want > 0) {
            Vec3 eye = new Vec3(Pose.x, Pose.y, Pose.z);
            if (Pose.beam) {
                // Фонарик: один источник света на несколько метров впереди — или у блока, если он ближе.
                Vec3 dir = Vec3.directionFromRotation(Pose.pitch, Pose.yaw);
                double ahead = Math.min(6.0, cfg.flashlightRange);
                var hit = level.clip(new net.minecraft.world.level.ClipContext(eye, eye.add(dir.scale(ahead)),
                        net.minecraft.world.level.ClipContext.Block.COLLIDER, net.minecraft.world.level.ClipContext.Fluid.NONE, sp));
                double dist = hit.getType() == net.minecraft.world.phys.HitResult.Type.BLOCK ? hit.getLocation().distanceTo(eye) - 0.3 : ahead;
                // от этой точки назад к глазам — первая свободная клетка (не в воде/листве/стене)
                for (double dd = Math.max(0.0, dist); dd >= 0.0; dd -= 0.5) {
                    BlockPos spot = BlockPos.containing(eye.add(dir.scale(dd)));
                    if (lampFits(level, spot)) { target.put(spot.immutable(), want); break; }
                }
            } else {
                BlockPos head = BlockPos.containing(eye);
                for (BlockPos c : new BlockPos[]{head, head.below(), head.above()})
                    if (lampFits(level, c)) { target.put(c.immutable(), want); break; }
            }
        }
        if (level == lampLevel && target.equals(lamps)) return;
        // убрать лишние, поставить новые (не трогая совпадающие — меньше пересчёта освещения)
        if (lampLevel != level) clearLamp();
        for (var it = lamps.entrySet().iterator(); it.hasNext(); ) {
            var e = it.next();
            if (!e.getValue().equals(target.get(e.getKey()))) {
                if (lampLevel != null && lampLevel.getBlockState(e.getKey()).is(Blocks.LIGHT))
                    lampLevel.setBlock(e.getKey(), Blocks.AIR.defaultBlockState(), 3);
                it.remove();
            }
        }
        for (var e : target.entrySet()) {
            if (lamps.containsKey(e.getKey())) continue;
            level.setBlock(e.getKey(), Blocks.LIGHT.defaultBlockState().setValue(LightBlock.LEVEL, e.getValue()), 3);
            lamps.put(e.getKey(), e.getValue());
        }
        lampLevel = level;
    }

    /** Убрать все наши блоки света. Вызывать в потоке сервера. */
    public void clearLamp() {
        if (lampLevel != null)
            for (BlockPos p : lamps.keySet())
                if (lampLevel.getBlockState(p).is(Blocks.LIGHT)) lampLevel.setBlock(p, Blocks.AIR.defaultBlockState(), 3);
        lamps.clear();
        lampLevel = null;
    }

    // ───────────── каждый тик сервера ─────────────

    public void tick(MinecraftServer server) {
        ServerPlayer sp = player(server);
        if (sp == null) return;
        tickNo++;
        ServerLevel level = sp.level();
        SlEnchants.regs = server.registryAccess();
        if (sp.isPassenger()) sp.stopRiding(); // на всякий случай (вагонетка на рельсах, команда /ride)
        if (tickNo % 10 == 0) {
            // предметы SL в инвентаре (в т.ч. только что скрафченные) — зачаровываемые по своему типу
            var inv = sp.getInventory();
            for (int i = 0; i < inv.getContainerSize(); i++) {
                ItemStack st = inv.getItem(i);
                if (!st.isEmpty() && ItemJson.slKey(st) != null) SlEnchants.ensureEnchantable(st);
            }
        }
        // опыт, который подобрал игрок, — SL чинит им Jailbird с «Починкой» (и вернёт, сколько потратил)
        int xpNow = sp.totalExperience;
        if (lastXp >= 0 && xpNow > lastXp) {
            JsonObject x = new JsonObject();
            x.addProperty("t", "xp");
            x.addProperty("n", xpNow - lastXp);
            bridge.send(x);
        }
        lastXp = xpNow;

        // Правила мира, которые важны SL: keep_inventory (сохранять инвентарь при смерти).
        boolean keep = level.getGameRules().get(net.minecraft.world.level.gamerules.GameRules.KEEP_INVENTORY);
        if (!Boolean.valueOf(keep).equals(lastKeepInv) || tickNo % 100 == 0) {
            lastKeepInv = keep;
            JsonObject r = new JsonObject();
            r.addProperty("t", "rules");
            r.addProperty("keepInv", keep);
            bridge.send(r);
        }

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

        // 1б) Сон в кровати: тело Minecraft лежит в кровати (не тащим его за камерой). Ушёл в SL от кровати — проснулся.
        if (sp.isSleeping()) {
            if (sleepFrom == null) sleepFrom = new Vec3(Pose.x, Pose.feetY, Pose.z);
            if (new Vec3(Pose.x, Pose.feetY, Pose.z).distanceToSqr(sleepFrom) > 2.25) sp.stopSleepInBed(true, true);
            else { lastSnap = sp.position(); lastLevel = level; return; }
        }
        sleepFrom = null;

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

        if (Pose.noclip) {
            // ноуклип SL — как наблюдатель в Minecraft: сквозь блоки, без падения и без выталкивания
            fallDist = 0;
            lastFeetY = feetY;
            stuckTicks = 0;
        } else {
            // Страховка: если игрок SL всё-таки оказался внутри блоков — выталкиваем в ближайшее свободное место.
            if ((Pose.fx & Fun.FX_WHITE) == 0 && unstick(sp, level, feetY)) return; // белая конфета: сквозь стены можно
            // 3) Урон от падения по правилам Minecraft (вода, сено, слизь уменьшают/отменяют).
            fall(sp, level, feetY);
        }
        // 3а) Жидкости: SL всплывает/тонет по ним; у берега — помогаем выбраться, как в Minecraft.
        fluids(sp, level, prev, now, feetY);

        // 3а') SCP-268: невидим — мобы рядом забывают игрока как цель (и обычные, и «мозговые» вроде пиглинов и хранителя)
        if (Pose.invisible) hideFromMobs(sp, level);
        sp.setInvisible(Pose.invisible);

        // 3б) Лампа/фонарик в руке SL светят и в Minecraft: невидимый блок света у головы, едет за игроком.
        updateLamp(sp, level);

        // 3в) Фишки: конфеты SCP-330, рентген SCP-1344, SCP-173 по табличке…
        if (!xrayPurged) { xrayPurged = true; fun.purgeStaleXray(level, sp); }
        fun.tick(sp, level, tickNo);

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
            else if (cur.getItem() != set[i] || !armorEnch.equals(cur.getOrDefault(net.minecraft.core.component.DataComponents.CUSTOM_DATA, net.minecraft.world.item.component.CustomData.EMPTY).copyTag().getStringOr("slArmorEnch", ""))) {
                // чары бронежилета SL — на каждую часть брони, которой они подходят (Невесомость — ботинкам, Подводное дыхание — шлему)
                ItemStack piece = new ItemStack(set[i]);
                var all = SlEnchants.fromString(armorEnch);
                var mut = new net.minecraft.world.item.enchantment.ItemEnchantments.Mutable(net.minecraft.world.item.enchantment.ItemEnchantments.EMPTY);
                for (var e : all.entrySet()) if (e.getKey().value().canEnchant(piece)) mut.set(e.getKey(), e.getIntValue());
                piece.set(net.minecraft.core.component.DataComponents.ENCHANTMENTS, mut.toImmutable());
                net.minecraft.nbt.CompoundTag t = new net.minecraft.nbt.CompoundTag();
                t.putString("slArmorEnch", armorEnch);
                piece.set(net.minecraft.core.component.DataComponents.CUSTOM_DATA, net.minecraft.world.item.component.CustomData.of(t));
                sp.setItemSlot(slots[i], piece);
            }
        }
    }

    void sendTeleport(ServerPlayer sp) {
        teleportSeq++;
        Vec3 p = sp.position();
        Pose.dim = sp.level().dimension();
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
        SlBridgeClient.LOG.info(Lang.t("[SLBridge] Телепорт Minecraft → {} {}", "[SLBridge] Minecraft teleport → {} {}"), m.get("dim"), p);
    }

    private int stuckTicks;

    private boolean free(ServerLevel level, BlockPos p) {
        return level.getBlockState(p).getCollisionShape(level, p).isEmpty()
                && level.getBlockState(p.above()).getCollisionShape(level, p.above()).isEmpty();
    }

    /**
     * Игрок целиком внутри блоков дольше секунды — выталкиваем в ближайшее свободное место (сначала вбок на 1 блок,
     * как Minecraft выталкивает игрока, потом вверх). Ноуклип сюда не попадает.
     */
    private boolean unstick(ServerPlayer sp, ServerLevel level, double feetY) {
        BlockPos feet = BlockPos.containing(sp.getX(), feetY + 0.3, sp.getZ());
        BlockPos head = feet.above();
        boolean solidFeet = level.getBlockState(feet).isCollisionShapeFullBlock(level, feet);
        boolean solidHead = level.getBlockState(head).isCollisionShapeFullBlock(level, head);
        if (!(solidFeet && solidHead)) { stuckTicks = 0; return false; }
        if (++stuckTicks < 20) return false;
        stuckTicks = 0;
        BlockPos target = null;
        for (var d : net.minecraft.core.Direction.Plane.HORIZONTAL) {
            BlockPos q = feet.relative(d);
            if (free(level, q)) { target = q; break; }
        }
        if (target == null) {
            BlockPos.MutableBlockPos p = feet.mutable();
            for (int i = 0; i < 96 && p.getY() < level.getMaxY(); i++, p.move(0, 1, 0))
                if (free(level, p) && !level.getBlockState(p.below()).getCollisionShape(level, p.below()).isEmpty()) { target = p.immutable(); break; }
        }
        if (target == null) return false;
        sp.snapTo(target.getX() + 0.5, target.getY() + 0.05, target.getZ() + 0.5, sp.getYRot(), sp.getXRot());
        SlBridgeClient.LOG.info(Lang.t("[SLBridge] Игрок застрял в блоках — выталкиваю на {}", "[SLBridge] Player stuck in blocks — pushing out to {}"), target);
        // Не «телепорт» (он заново строит все коллизии и замораживает) — просто переставить игрока SL рядом.
        teleportSeq++;
        Vec3 p = sp.position();
        Pose.teleport(p.x, p.y + SlBridgeClient.EYE_HEIGHT, p.z, teleportSeq);
        lastSnap = p;
        lastLevel = level;
        lastFeetY = Double.NaN;
        fallDist = 0;
        JsonObject m = new JsonObject();
        m.addProperty("t", "unstick");
        m.addProperty("x", p.x);
        m.addProperty("y", p.y);
        m.addProperty("z", p.z);
        m.addProperty("seq", teleportSeq);
        bridge.send(m);
        return true;
    }

    // ───────────── жидкости ─────────────

    private int lastFluidCode = -1;
    private int lastLedgeTick = -100;
    private Vec3 sleepFrom;

    /** Взрыв в мире Minecraft (граната, SCP-018) — блоки и мобы; игроку урон уже нанёс SL, второй раз не считаем. */
    public static volatile boolean ownExplosion;

    public void explode(ServerPlayer sp, double x, double y, double z, float power) {
        ownExplosion = true;
        try {
            sp.level().explode(sp, x, y, z, power, net.minecraft.world.level.Level.ExplosionInteraction.TNT);
        } finally {
            ownExplosion = false;
        }
    }

    private static boolean climbable(ServerLevel level, BlockPos p) {
        return level.getBlockState(p).is(net.minecraft.tags.BlockTags.CLIMBABLE);
    }

    private static int fluidAt(ServerLevel level, BlockPos p) {
        var fs = level.getFluidState(p);
        if (fs.isEmpty()) return 0;
        return fs.is(net.minecraft.tags.FluidTags.LAVA) ? 2 : 1;
    }

    private void fluids(ServerPlayer sp, ServerLevel level, Vec3 prev, Vec3 now, double feetY) {
        int f = fluidAt(level, BlockPos.containing(now.x, feetY + 0.2, now.z));
        int e = fluidAt(level, BlockPos.containing(Pose.x, Pose.y, Pose.z));
        // лестница, лианы, строительные леса — у ног или на уровне тела
        int c = climbable(level, BlockPos.containing(now.x, feetY + 0.2, now.z)) || climbable(level, BlockPos.containing(now.x, feetY + 1.0, now.z)) ? 1 : 0;
        int code = (f * 3 + e) * 2 + c;
        if (code != lastFluidCode || tickNo % 20 == 0) {
            lastFluidCode = code;
            JsonObject m = new JsonObject();
            m.addProperty("t", "fluid");
            m.addProperty("f", f);
            m.addProperty("e", e);
            m.addProperty("c", c);
            bridge.send(m);
        }
        // Выбраться на берег / с лестницы наверх: блок впереди, верх которого чуть выше ног, над ним свободно —
        // ставим ноги на его верх (как Minecraft выпускает из воды и с лестницы).
        boolean swimming = f != 0 && e == 0;
        if (!(swimming || c != 0) || tickNo - lastLedgeTick < 8 || Pose.noclip) return;
        double vx = now.x - prev.x, vz = now.z - prev.z, hs = Math.hypot(vx, vz);
        if (hs < 0.03) {
            if (c == 0) return;
            // на лестнице упираемся в неё — берём направление взгляда
            double yaw = Math.toRadians(Pose.yaw);
            vx = -Math.sin(yaw); vz = Math.cos(yaw); hs = 1;
        }
        BlockPos ahead = BlockPos.containing(now.x + vx / hs * 0.6, feetY + 0.2, now.z + vz / hs * 0.6);
        for (int dy = 0; dy <= 1; dy++) {
            BlockPos b = ahead.above(dy);
            var shape = level.getBlockState(b).getCollisionShape(level, b);
            if (shape.isEmpty()) continue;
            double top = b.getY() + shape.max(net.minecraft.core.Direction.Axis.Y);
            if (top <= feetY + 0.05 || top - feetY > 1.3 || !free(level, b.above())) return;
            lastLedgeTick = tickNo;
            JsonObject n = new JsonObject();
            n.addProperty("t", "nudge");
            n.addProperty("fy", top);
            bridge.send(n);
            return;
        }
    }

    private void fall(ServerPlayer sp, ServerLevel level, double feetY) {
        if (Double.isNaN(lastFeetY)) { lastFeetY = feetY; return; }
        double dy = lastFeetY - feetY;
        lastFeetY = feetY;
        if (sp.isInWater() || sp.onClimbable() || sp.isInLava()) { fallDist = 0; return; }
        // лестницы и лианы проверяем сами: у «приклеенного» к камере SL тела Minecraft onClimbable() часто не срабатывает
        BlockPos cf = BlockPos.containing(sp.getX(), feetY + 0.1, sp.getZ());
        if (climbable(level, cf) || climbable(level, cf.above()) || climbable(level, cf.below())) { fallDist = 0; lastFeetY = feetY; return; }
        // Вода гасит падение, как в Minecraft. В мосте игрок SL «стоит» на поверхности воды (там коллизия-пол),
        // поэтому тело Minecraft может воды не касаться — проверяем блоки у ног и под ногами сами.
        BlockPos feetPos = BlockPos.containing(sp.getX(), feetY + 0.1, sp.getZ());
        for (BlockPos q : new BlockPos[]{feetPos, feetPos.below()}) {
            BlockState qs = level.getBlockState(q);
            if (!level.getFluidState(q).isEmpty() && level.getFluidState(q).is(net.minecraft.tags.FluidTags.WATER)
                    || qs.is(net.minecraft.world.level.block.Blocks.COBWEB) || qs.is(net.minecraft.world.level.block.Blocks.POWDER_SNOW)
                    || qs.is(net.minecraft.world.level.block.Blocks.BUBBLE_COLUMN)) { fallDist = 0; return; }
        }
        if (dy < -0.0005) { fallDist = 0; return; } // идём вверх (прыжок, лестница)
        // Стоим ли на чём-то: коробка игрока, опущенная на 6 см, задевает блок. Проверяем КАЖДЫЙ тик —
        // иначе спуск по ступенькам/склону копился бы как одно длинное падение.
        AABB feetBox = new AABB(sp.getX() - 0.3, feetY - 0.06, sp.getZ() - 0.3, sp.getX() + 0.3, feetY + 0.5, sp.getZ() + 0.3);
        boolean onGround = !level.noBlockCollision(sp, feetBox);
        if (!onGround) { if (dy > 0.0005) fallDist += dy; return; }
        double d = fallDist;
        fallDist = 0;
        if (d <= 0) return;
        fun.groundPound(sp, level, d);
        BlockPos below = BlockPos.containing(sp.getX(), feetY - 0.2, sp.getZ());
        BlockState st = level.getBlockState(below);
        // Как в Minecraft: урон = высота − 3 блока (сено и мёд — 20 %, кровать — 50 %, слизь — 0, сталагмит — ×2 и +2).
        double mult = 1.0;
        if (st.is(Blocks.SLIME_BLOCK)) mult = 0.0;
        else if (st.is(Blocks.HAY_BLOCK) || st.is(Blocks.HONEY_BLOCK)) mult = 0.2;
        else if (st.is(net.minecraft.tags.BlockTags.BEDS)) mult = 0.5;
        else if (st.is(Blocks.POINTED_DRIPSTONE)) { d += 2.0; mult = 2.0; }
        double safe = sp.getAttributeValue(net.minecraft.world.entity.ai.attributes.Attributes.SAFE_FALL_DISTANCE);
        int dmg = (int) Math.floor((d - safe) * mult * sp.getAttributeValue(net.minecraft.world.entity.ai.attributes.Attributes.FALL_DAMAGE_MULTIPLIER));
        if (dmg <= 0) return;
        SlBridgeClient.LOG.info(Lang.t("[SLBridge] Падение {} бл. → урон {} (в SL ×{})", "[SLBridge] Fell {} blocks → damage {} (×{} in SL)"),
                String.format("%.1f", d), dmg, SlBridgeClient.config.damageScale);
        sp.hurtServer(level, st.is(Blocks.POINTED_DRIPSTONE) ? sp.damageSources().stalagmite() : sp.damageSources().fall(), dmg);
    }

    private void hideFromMobs(ServerPlayer sp, ServerLevel level) {
        for (var mob : level.getEntitiesOfClass(net.minecraft.world.entity.Mob.class, sp.getBoundingBox().inflate(64.0))) {
            if (mob.getTarget() == sp) mob.setTarget(null);
            var brain = mob.getBrain();
            if (brain.hasMemoryValue(net.minecraft.world.entity.ai.memory.MemoryModuleType.ATTACK_TARGET)
                    && brain.getMemory(net.minecraft.world.entity.ai.memory.MemoryModuleType.ATTACK_TARGET).orElse(null) == sp)
                brain.eraseMemory(net.minecraft.world.entity.ai.memory.MemoryModuleType.ATTACK_TARGET);
            if (mob instanceof net.minecraft.world.entity.NeutralMob nm && nm.getTarget() == sp) nm.stopBeingAngry();
        }
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
            case "armor" -> { armorTier = m.get("tier").getAsInt(); armorEnch = m.has("ench") ? m.get("ench").getAsString() : ""; }
            case "xpTake" -> { int n = m.get("n").getAsInt(); if (n > 0) { sp.giveExperiencePoints(-n); lastXp = sp.totalExperience; } }
            case "craftList" -> bridge.send(crafting.craftList(sp, level, ItemJson.listFromJson(m.get("inv"))));
            case "craft" -> bridge.send(crafting.craft(sp, level, m.get("rid").getAsString(), m.get("times").getAsInt(), ItemJson.listFromJson(m.get("inv"))));
            case "smeltList" -> bridge.send(crafting.smeltList(sp, level, ItemJson.listFromJson(m.get("inv"))));
            case "smelt" -> bridge.send(crafting.smelt(sp, level, m.get("rid").getAsString(), m.get("times").getAsInt(), ItemJson.listFromJson(m.get("inv"))));
            case "cmd" -> command(server, sp, m.get("text").getAsString());
            case "respawnReq" -> respawn(server, sp);
            case "candy" -> fun.candy(sp, level, m.get("kind").getAsString());
            case "chorus" -> fun.chorus(sp, level);
            case "phaseEnd" -> fun.phaseEnd(sp, level);
            case "totem" -> fun.totem(sp, level);
            case "evilKill" -> fun.evilKill(sp, level);
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
        // Сначала существо под прицелом (ближе блока): житель — торговля (или «нет» головой), овца — стрижка,
        // корова — молоко, бирка, поводок, седло, кормёжка для размножения… — всё как ПКМ по мобу в Minecraft.
        Vec3 blockEnd = hit.getType() == HitResult.Type.BLOCK ? hit.getLocation() : from.add(dir.scale(cfg.reach));
        var eh = net.minecraft.world.entity.projectile.ProjectileUtil.getEntityHitResult(level, sp, from, blockEnd,
                new AABB(from, blockEnd).inflate(1.0), e -> e != sp && e.isAlive() && e.isPickable() && !e.isSpectator(), 0f);
        if (eh != null) {
            Entity target = eh.getEntity();
            InteractionResult res = sp.interactOn(target, InteractionHand.MAIN_HAND, eh.getLocation().subtract(target.position()));
            done = res.consumesAction();
            // лодки, вагонетки, лошади: сесть нельзя — движением в мосте управляет SL, а не транспорт Minecraft
            if (sp.isPassenger()) { Entity v = sp.getVehicle(); sp.stopRiding(); if (v != null) fun.refuseRide(v, level.getRandom()); }
            if (done) hit = BlockHitResult.miss(blockEnd, net.minecraft.core.Direction.UP, BlockPos.containing(blockEnd)); // блок за мобом не трогаем
        }
        if (!done && hit.getType() == HitResult.Type.BLOCK) {
            // Стройка под себя: блок ставится туда, где стоит тело игрока, а игрок в прыжке (ноги выше пола).
            // Minecraft бы отказал (место занято игроком) — поднимаем тело над новым блоком, а SL сообщаем подняться.
            BlockPos place = level.getBlockState(hit.getBlockPos()).canBeReplaced() ? hit.getBlockPos() : hit.getBlockPos().relative(hit.getDirection());
            Vec3 before = sp.position();
            boolean under = sp.getMainHandItem().getItem() instanceof net.minecraft.world.item.BlockItem
                    && sp.getBoundingBox().intersects(new net.minecraft.world.phys.AABB(place))
                    && before.y > place.getY() + 0.1 && before.y < place.getY() + 1.0;
            if (under) sp.setPos(before.x, place.getY() + 1.0, before.z);
            InteractionResult res = sp.gameMode.useItemOn(sp, level, sp.getMainHandItem(), InteractionHand.MAIN_HAND, hit);
            done = res.consumesAction();
            if (under) {
                if (done && !level.getBlockState(place).getCollisionShape(level, place).isEmpty()) {
                    lastSnap = sp.position();
                    JsonObject n = new JsonObject();
                    n.addProperty("t", "nudge");
                    n.addProperty("fy", place.getY() + 1.0);
                    bridge.send(n);
                } else sp.setPos(before.x, before.y, before.z);
            }
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
            } else if (h.is(net.minecraft.world.item.Items.ELYTRA)) {
                // элитры: «сила небес» — ноуклип SL вкл/выкл
                JsonObject e = new JsonObject();
                e.addProperty("t", "elytra");
                bridge.send(e);
                level.playSound(null, sp.getX(), sp.getY(), sp.getZ(), SoundEvents.ARMOR_EQUIP_ELYTRA.value(), SoundSource.PLAYERS, 1f, 1f);
            } else if (fun.oldWeapon(h, level.getRandom())) {
                // лук, щит, удочка… — фраза вместо действия
            } else if (h.is(net.minecraft.world.item.Items.POTION) || h.is(net.minecraft.world.item.Items.MILK_BUCKET)
                    || h.is(net.minecraft.world.item.Items.HONEY_BOTTLE) || h.is(net.minecraft.world.item.Items.OMINOUS_BOTTLE)) {
                // выпить сразу (удерживать кнопку в SL нельзя): эффекты Minecraft + то, что SL применит сам
                boolean milk = h.is(net.minecraft.world.item.Items.MILK_BUCKET);
                var contents = h.get(DataComponents.POTION_CONTENTS);
                List<net.minecraft.world.effect.MobEffectInstance> effs = new ArrayList<>();
                if (contents != null) contents.forEachEffect(effs::add, 1f);
                ItemStack rest = h.finishUsingItem(level, sp);
                sp.setItemInHand(InteractionHand.MAIN_HAND, rest);
                level.playSound(null, sp.getX(), sp.getY(), sp.getZ(), milk ? SoundEvents.GENERIC_DRINK.value() : SoundEvents.GENERIC_DRINK.value(), SoundSource.PLAYERS, 1f, 1f);
                if (milk) { JsonObject k = new JsonObject(); k.addProperty("t", "milk"); bridge.send(k); }
                else fun.potionToSl(effs);
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
            lines.add(Lang.t("Ошибка: ", "Error: ") + e.getMessage());
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
        // Игрок Minecraft сам не умирает (умирает SL) — новая жизнь без огня, эффектов и накопленного падения.
        sp.clearFire();
        sp.removeAllEffects();
        sp.setAirSupply(sp.getMaxAirSupply());
        sp.resetFallDistance();
        fallDist = 0;
        lastFeetY = Double.NaN;
        // Кровать / якорь возрождения — как в Minecraft; нет их (или кровать сломана) — точка появления мира.
        try {
            var tt = sp.findRespawnPositionAndUseSpawnBlock(true, net.minecraft.world.level.portal.TeleportTransition.DO_NOTHING);
            if (tt != null && sp.getRespawnConfig() != null && !tt.missingRespawnBlock()) {
                Vec3 p = tt.position();
                sp.teleportTo(tt.newLevel(), p.x, p.y, p.z, Set.of(), tt.yRot(), 0f, true);
                sendTeleport(sp);
                return;
            }
        } catch (Exception e) { SlBridgeClient.LOG.warn("[SLBridge] respawn point", e); }
        ServerLevel ow = server.overworld();
        BlockPos sp0 = ow.getRespawnData().pos();
        ow.getChunk(sp0.getX() >> 4, sp0.getZ() >> 4); // загрузить чанк, иначе высота посчитается как дно мира
        int y = ow.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES, sp0.getX(), sp0.getZ());
        if (y <= ow.getMinY() + 1) y = sp0.getY();
        sp.teleportTo(ow, sp0.getX() + 0.5, y + 0.1, sp0.getZ() + 0.5, Set.of(), sp.getYRot(), 0f, true);
        sendTeleport(sp);
    }
}
