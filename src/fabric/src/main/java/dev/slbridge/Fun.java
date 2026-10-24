package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import dev.slbridge.mixin.DisplayAccessors;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.particles.DustParticleOptions;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.tags.BlockTags;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.Display;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.LightningBolt;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.PathfinderMob;
import net.minecraft.world.entity.animal.Animal;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.item.BoneMealItem;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.block.BaseFireBlock;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.CampfireBlock;
import net.minecraft.world.level.block.TntBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;
import org.joml.Quaternionf;
import org.joml.Vector3f;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;

/**
 * «Фишки» моста на стороне Minecraft: конфеты SCP-330, рентген SCP-1344, SCP-173 по табличке, тотем, зелья,
 * транспорт и «прошлый век» (лук, щит…), элитры, крик SCP-096 у эндерменов, достижения.
 */
public final class Fun {
    public static final int FX_SPICY = 1, FX_WHITE = 2, FX_ORANGE = 4, FX_METAL = 8, FX_SUGAR_HIGH = 16, FX_SUGAR_RUSH = 32, FX_SLOW_METAB = 64, FX_XRAY = 128;

    private static Fun instance;
    private final BridgeServer bridge;
    private final ServerBridge sb;

    Fun(BridgeServer bridge, ServerBridge sb) {
        this.bridge = bridge;
        this.sb = sb;
        instance = this;
    }

    private static boolean has(int bit) { return (Pose.fx & bit) != 0; }

    // ───────────── каждый тик сервера ─────────────

    void tick(ServerPlayer sp, ServerLevel level, int tick) {
        if (has(FX_SPICY) && tick % 4 == 0) breath(sp, level);
        if (has(FX_ORANGE) && tick % 20 == 0) glowMobs(sp, level, 32);
        if (has(FX_SUGAR_HIGH) && tick % 8 == 0) bloom(sp, level);
        if (has(FX_SUGAR_RUSH) && tick % 2 == 0) ram(sp, level, tick);
        if (has(FX_SLOW_METAB) && tick % 20 == 0) {
            sp.addEffect(new MobEffectInstance(MobEffects.FIRE_RESISTANCE, 60, 0, true, false));
            sp.clearFire();
        }
        if (has(FX_XRAY)) { if (tick % 20 == 0) { xray(sp, level); glowMobs(sp, level, 24); } }
        else if (!xrayDisplays.isEmpty()) clearXray();
        if (tick % 10 == 0) puddles(level, tick);
        scp173(sp, level, tick);
    }

    // ── красная конфета: огонь изо рта ──
    private void breath(ServerPlayer sp, ServerLevel level) {
        Vec3 eye = new Vec3(Pose.x, Pose.y, Pose.z);
        Vec3 dir = Vec3.directionFromRotation(Pose.pitch, Pose.yaw);
        // мобы в конусе 5 блоков
        for (LivingEntity e : level.getEntitiesOfClass(LivingEntity.class, new AABB(eye, eye).inflate(5.5), e -> e != sp && e.isAlive())) {
            Vec3 to = e.getBoundingBox().getCenter().subtract(eye);
            double dist = to.length();
            if (dist > 5.0 || (dist > 0.7 && to.normalize().dot(dir) < 0.75)) continue;
            if (level.clip(new ClipContext(eye, e.getBoundingBox().getCenter(), ClipContext.Block.COLLIDER, ClipContext.Fluid.NONE, sp)).getType() == HitResult.Type.BLOCK) continue;
            e.setInvulnerableTime(0);
            e.hurtServer(level, sp.damageSources().playerAttack(sp), 2f);
            e.igniteForSeconds(4f);
        }
        // блоки на пути пламени
        BlockPos prev = null;
        for (double t = 0.5; t <= 5.0; t += 0.5) {
            Vec3 p = eye.add(dir.scale(t));
            BlockPos bp = BlockPos.containing(p);
            if (bp.equals(prev)) continue;
            BlockState st = level.getBlockState(bp);
            if (st.is(Blocks.TNT)) { TntBlock.prime(level, bp); level.removeBlock(bp, false); break; }
            if (st.is(BlockTags.ICE) && level.dimension() != net.minecraft.world.level.Level.NETHER) { level.setBlockAndUpdate(bp, Blocks.WATER.defaultBlockState()); break; }
            if (st.is(Blocks.SNOW) || st.is(Blocks.SNOW_BLOCK) || st.is(Blocks.POWDER_SNOW)) { level.destroyBlock(bp, false); break; }
            if (CampfireBlock.canLight(st)) { level.setBlockAndUpdate(bp, st.setValue(CampfireBlock.LIT, true)); break; }
            if (!st.getCollisionShape(level, bp).isEmpty() || st.ignitedByLava()) {
                if (st.ignitedByLava() && prev != null && level.getRandom().nextFloat() < 0.35f) {
                    Direction face = Direction.getApproximateNearest(dir.reverse());
                    if (level.getBlockState(prev).isAir() && BaseFireBlock.canBePlacedAt(level, prev, face))
                        level.setBlockAndUpdate(prev, BaseFireBlock.getState(level, prev));
                }
                break;
            }
            level.sendParticles(ParticleTypes.FLAME, p.x, p.y, p.z, 3, 0.15 * t / 5, 0.15 * t / 5, 0.15 * t / 5, 0.01);
            prev = bp;
        }
    }

    // ── оранжевая конфета и рентген: мобы подсвечены сквозь стены ──
    private void glowMobs(ServerPlayer sp, ServerLevel level, double r) {
        for (Mob m : level.getEntitiesOfClass(Mob.class, sp.getBoundingBox().inflate(r), Entity::isAlive))
            m.addEffect(new MobEffectInstance(MobEffects.GLOWING, 40, 0, true, false));
    }

    // ── зелёная конфета: под ногами всё растёт ──
    private void bloom(ServerPlayer sp, ServerLevel level) {
        BlockPos feet = BlockPos.containing(sp.getX(), Pose.feetY + 0.1, sp.getZ());
        for (int i = 0; i < 4; i++) {
            BlockPos p = feet.offset(level.getRandom().nextInt(5) - 2, 0, level.getRandom().nextInt(5) - 2);
            for (BlockPos q : new BlockPos[]{p, p.below()}) {
                if (BoneMealItem.growCrop(new ItemStack(Items.BONE_MEAL), level, q)) {
                    level.levelEvent(1505, q, 15); // частицы роста, как от костной муки
                    break;
                }
            }
        }
    }

    // ── жёлтая конфета: таран ──
    private final Map<Integer, Integer> rammedAt = new HashMap<>();

    private void ram(ServerPlayer sp, ServerLevel level, int tick) {
        Vec3 dir = Vec3.directionFromRotation(0, Pose.yaw);
        Vec3 c = new Vec3(sp.getX(), Pose.feetY + 0.9, sp.getZ()).add(dir.scale(0.9));
        for (LivingEntity e : level.getEntitiesOfClass(LivingEntity.class, new AABB(c, c).inflate(0.8, 0.9, 0.8), e -> e != sp && e.isAlive())) {
            Integer last = rammedAt.get(e.getId());
            if (last != null && tick - last < 10) continue;
            rammedAt.put(e.getId(), tick);
            e.setInvulnerableTime(0);
            e.hurtServer(level, sp.damageSources().playerAttack(sp), 2f);
            e.knockback(1.4, -dir.x, -dir.z, sp.damageSources().playerAttack(sp), 0f);
            level.playSound(null, e.getX(), e.getY(), e.getZ(), SoundEvents.PLAYER_ATTACK_KNOCKBACK, SoundSource.PLAYERS, 1f, 1f);
        }
        // листва и трава на пути сносятся
        for (int dy = 0; dy <= 1; dy++) {
            BlockPos bp = BlockPos.containing(c.x + dir.x * 0.3, Pose.feetY + 0.2 + dy, c.z + dir.z * 0.3);
            BlockState st = level.getBlockState(bp);
            if (st.is(BlockTags.LEAVES) || (st.canBeReplaced() && st.getFluidState().isEmpty() && !st.isAir())) level.destroyBlock(bp, true, sp);
        }
        if (rammedAt.size() > 200) rammedAt.clear();
    }

    // ── серая конфета: удар о землю после падения ──
    void groundPound(ServerPlayer sp, ServerLevel level, double fall) {
        if (!has(FX_METAL) || fall < 3) return;
        float dmg = (float) Math.min(20, fall);
        Vec3 at = new Vec3(sp.getX(), Pose.feetY, sp.getZ());
        for (LivingEntity e : level.getEntitiesOfClass(LivingEntity.class, new AABB(at, at).inflate(3.5, 2, 3.5), e -> e != sp && e.isAlive())) {
            e.setInvulnerableTime(0);
            e.hurtServer(level, sp.damageSources().playerAttack(sp), dmg);
            Vec3 away = e.position().subtract(at);
            e.knockback(1.0, -away.x, -away.z, sp.damageSources().playerAttack(sp), 0f);
            e.setDeltaMovement(e.getDeltaMovement().add(0, 0.5, 0));
        }
        BlockPos base = BlockPos.containing(at.x, at.y - 0.5, at.z);
        for (BlockPos p : BlockPos.betweenClosed(base.offset(-1, 0, -1), base.offset(1, 0, 1))) {
            BlockState st = level.getBlockState(p);
            if (st.is(BlockTags.LEAVES) || st.is(BlockTags.ICE) || st.getSoundType().equals(net.minecraft.world.level.block.SoundType.GLASS))
                level.destroyBlock(p.immutable(), true, sp);
        }
        level.sendParticles(ParticleTypes.EXPLOSION, at.x, at.y + 0.2, at.z, 3, 1, 0.1, 1, 0);
        level.playSound(null, at.x, at.y, at.z, SoundEvents.ANVIL_LAND, SoundSource.PLAYERS, 1.2f, 0.6f);
    }

    // ── разовые конфеты ──
    private record Puddle(Vec3 at, int until) {}
    private final List<Puddle> puddleList = new ArrayList<>();
    private int lastTick;

    void candy(ServerPlayer sp, ServerLevel level, String kind) {
        SlBridgeClient.LOG.info("[SLBridge] Конфета SCP-330: {}", kind);
        Vec3 at = new Vec3(sp.getX(), Pose.feetY, sp.getZ());
        switch (kind) {
            case "Blue" -> { sp.removeAllEffects(); sp.clearFire(); }
            case "Rainbow" -> {
                // животные влюбляются, а овцы становятся радужными (пасхалка jeb_)
                for (Animal a : level.getEntitiesOfClass(Animal.class, sp.getBoundingBox().inflate(10), Entity::isAlive)) {
                    if (a.canFallInLove()) a.setInLove(sp);
                    if (a instanceof net.minecraft.world.entity.animal.sheep.Sheep && !a.hasCustomName()) a.setCustomName(Component.literal("jeb_"));
                    level.sendParticles(ParticleTypes.HEART, a.getX(), a.getY() + a.getBbHeight(), a.getZ(), 3, 0.3, 0.3, 0.3, 0);
                }
            }
            case "Brown" -> puddleList.add(new Puddle(at, lastTick + 400));
            case "Evil" -> evilStorm(sp, level);
        }
    }

    private void puddles(ServerLevel level, int tick) {
        lastTick = tick;
        for (Iterator<Puddle> it = puddleList.iterator(); it.hasNext(); ) {
            Puddle p = it.next();
            if (tick > p.until()) { it.remove(); continue; }
            level.sendParticles(new DustParticleOptions(0x5A3A1E, 1.6f), p.at().x, p.at().y + 0.05, p.at().z, 6, 1.0, 0.02, 1.0, 0);
            for (Mob m : level.getEntitiesOfClass(Mob.class, new AABB(p.at(), p.at()).inflate(2.5, 1.5, 2.5), Entity::isAlive))
                m.addEffect(new MobEffectInstance(MobEffects.SLOWNESS, 30, 4, true, false));
        }
    }

    /** Злая конфета: гроза — молнии бьют в мобов вокруг (но не в самого игрока). */
    private void evilStorm(ServerPlayer sp, ServerLevel level) {
        List<Mob> targets = level.getEntitiesOfClass(Mob.class, sp.getBoundingBox().inflate(20), m -> m.isAlive() && m instanceof Enemy && m.distanceTo(sp) > 4);
        targets.sort((a, b) -> Double.compare(a.distanceTo(sp), b.distanceTo(sp)));
        int n = 0;
        for (Mob m : targets) { if (n++ >= 6) break; strike(level, m.position(), sp); }
        while (n++ < 4) {
            double ang = level.getRandom().nextDouble() * Math.PI * 2, r = 6 + level.getRandom().nextDouble() * 6;
            double x = sp.getX() + Math.cos(ang) * r, z = sp.getZ() + Math.sin(ang) * r;
            strike(level, new Vec3(x, level.getHeight(net.minecraft.world.level.levelgen.Heightmap.Types.MOTION_BLOCKING, (int) Math.floor(x), (int) Math.floor(z)), z), sp);
        }
        say(Lang.t("Небо откликнулось на зло…", "The sky answers the evil…"));
    }

    private static void strike(ServerLevel level, Vec3 at, ServerPlayer cause) {
        LightningBolt bolt = net.minecraft.world.entity.EntityTypes.LIGHTNING_BOLT.create(level, EntitySpawnReason.TRIGGERED);
        if (bolt == null) return;
        bolt.setPos(at.x, at.y, at.z);
        bolt.setCause(cause);
        level.addFreshEntity(bolt);
    }

    // ── ведьмы роняют конфеты SCP-330 (1–2 случайных) ──
    private static final String[][] CANDIES = {
            {"Rainbow", "Радужная", "Rainbow"}, {"Yellow", "Жёлтая", "Yellow"}, {"Purple", "Фиолетовая", "Purple"},
            {"Red", "Красная", "Red"}, {"Green", "Зелёная", "Green"}, {"Blue", "Синяя", "Blue"}, {"Pink", "Розовая", "Pink"},
            {"Orange", "Оранжевая", "Orange"}, {"White", "Белая", "White"}, {"Gray", "Серая", "Gray"},
            {"Black", "Чёрная", "Black"}, {"Brown", "Коричневая", "Brown"}, {"Evil", "Злая", "Evil"}};

    void witchCandy(ServerLevel level, LivingEntity witch) {
        var r = level.getRandom();
        int n = 1 + r.nextInt(2);
        List<String> kinds = new ArrayList<>(), names = new ArrayList<>();
        for (int i = 0; i < n; i++) {
            // злая — редкая (как в SL), розовая — тоже
            String[] c;
            do { c = CANDIES[r.nextInt(CANDIES.length)]; }
            while ((c[0].equals("Evil") && r.nextFloat() > 0.25f) || (c[0].equals("Pink") && r.nextFloat() > 0.5f));
            kinds.add(c[0]);
            names.add(Lang.t(c[1], c[2]));
        }
        JsonObject o = new JsonObject();
        o.addProperty("id", "minecraft:trial_key");
        o.addProperty("count", 1);
        o.addProperty("max", 1);
        o.addProperty("name", Lang.t("SCP-330 (конфеты): ", "SCP-330 (candy): ") + String.join(", ", names));
        o.addProperty("sl", "SCP330");
        o.addProperty("model", "minecraft:bowl");
        o.addProperty("slState", "c330=" + String.join(",", kinds));
        ItemStack st = ItemJson.fromJson(o);
        if (st.isEmpty()) return;
        net.minecraft.world.entity.item.ItemEntity ie = new net.minecraft.world.entity.item.ItemEntity(level, witch.getX(), witch.getY() + 0.5, witch.getZ(), st);
        ie.setDefaultPickUpDelay();
        level.addFreshEntity(ie);
        level.sendParticles(ParticleTypes.WITCH, witch.getX(), witch.getY() + 1, witch.getZ(), 15, 0.4, 0.5, 0.4, 0.05);
    }

    /** Злая конфета: убийство пополнило AHP — злой смех, а мобы рядом «травмированы злом» (замедление и слабость). */
    void evilKill(ServerPlayer sp, ServerLevel level) {
        level.playSound(null, sp.getX(), sp.getY(), sp.getZ(), SoundEvents.WITCH_CELEBRATE, SoundSource.PLAYERS, 1.2f, 0.7f);
        level.sendParticles(ParticleTypes.SOUL_FIRE_FLAME, sp.getX(), Pose.feetY + 1.0, sp.getZ(), 20, 0.4, 0.6, 0.4, 0.02);
        for (Mob m : level.getEntitiesOfClass(Mob.class, sp.getBoundingBox().inflate(5), Entity::isAlive)) {
            m.addEffect(new MobEffectInstance(MobEffects.SLOWNESS, 100, 1));
            m.addEffect(new MobEffectInstance(MobEffects.WEAKNESS, 100, 0));
        }
    }

    /** Чёрная конфета (исход «телепорт»): прыжок, как от плода хоруса. */
    void chorus(ServerPlayer sp, ServerLevel level) {
        for (int i = 0; i < 24; i++) {
            double x = sp.getX() + (level.getRandom().nextDouble() - 0.5) * 16;
            double z = sp.getZ() + (level.getRandom().nextDouble() - 0.5) * 16;
            int top = (int) Math.floor(sp.getY()) + 8;
            for (int y = top; y > top - 16; y--) {
                BlockPos below = BlockPos.containing(x, y - 1, z);
                if (level.getBlockState(below).getCollisionShape(level, below).isEmpty()) continue;
                AABB box = sp.getBoundingBox().move(x - sp.getX(), y - sp.getY(), z - sp.getZ());
                if (!level.noCollision(sp, box) || level.containsAnyLiquid(box)) continue;
                if (Math.hypot(x - sp.getX(), z - sp.getZ()) < 3) break;
                level.playSound(null, sp.getX(), sp.getY(), sp.getZ(), SoundEvents.CHORUS_FRUIT_TELEPORT, SoundSource.PLAYERS, 1f, 1f);
                sp.teleportTo(x, y, z);
                sb.sendTeleport(sp);
                return;
            }
        }
    }

    /** Белая конфета кончилась внутри блоков — вытолкнуть наверх, в первое свободное место. */
    void phaseEnd(ServerPlayer sp, ServerLevel level) {
        if (level.noCollision(sp, sp.getBoundingBox())) return;
        for (int dy = 1; dy <= 48; dy++) {
            AABB box = sp.getBoundingBox().move(0, dy, 0);
            if (level.noCollision(sp, box)) {
                sp.teleportTo(sp.getX(), Math.floor(sp.getY()) + dy, sp.getZ());
                sb.sendTeleport(sp);
                return;
            }
        }
    }

    /** Тотем бессмертия сработал (SL отменил смерть): анимация и огнестойкость, как в Minecraft. */
    void totem(ServerPlayer sp, ServerLevel level) {
        level.broadcastEntityEvent(sp, (byte) 35);
        sp.clearFire();
        sp.addEffect(new MobEffectInstance(MobEffects.FIRE_RESISTANCE, 800, 0));
    }

    // ── SCP-1344: рентген — руды подсвечены сквозь камень ──
    private final Map<BlockPos, Display.BlockDisplay> xrayDisplays = new HashMap<>();
    private static final String XRAY_TAG = "slbridge_xray";

    private static int oreColor(BlockState st) {
        if (st.is(net.minecraft.tags.BlockItemTags.DIAMOND_ORES.block())) return 0x4AEDD9;
        if (st.is(net.minecraft.tags.BlockItemTags.EMERALD_ORES.block())) return 0x17DD62;
        if (st.is(net.minecraft.tags.BlockItemTags.GOLD_ORES.block()) || st.is(Blocks.NETHER_GOLD_ORE)) return 0xFCEE4B;
        if (st.is(net.minecraft.tags.BlockItemTags.IRON_ORES.block())) return 0xD8AF93;
        if (st.is(net.minecraft.tags.BlockItemTags.REDSTONE_ORES.block())) return 0xFF2020;
        if (st.is(net.minecraft.tags.BlockItemTags.LAPIS_ORES.block())) return 0x2A5BFF;
        if (st.is(net.minecraft.tags.BlockItemTags.COPPER_ORES.block())) return 0xE77C56;
        if (st.is(net.minecraft.tags.BlockItemTags.COAL_ORES.block())) return 0x777777;
        if (st.is(Blocks.NETHER_QUARTZ_ORE)) return 0xF0EAE0;
        if (st.is(Blocks.ANCIENT_DEBRIS)) return 0xA0522D;
        return -1;
    }

    private void xray(ServerPlayer sp, ServerLevel level) {
        BlockPos c = sp.blockPosition();
        int r = 12;
        List<BlockPos> found = new ArrayList<>();
        for (BlockPos p : BlockPos.betweenClosed(c.offset(-r, -r, -r), c.offset(r, r, r))) {
            if (oreColor(level.getBlockState(p)) >= 0) found.add(p.immutable());
        }
        found.sort((a, b) -> Double.compare(a.distSqr(c), b.distSqr(c)));
        Set<BlockPos> want = new HashSet<>(found.subList(0, Math.min(found.size(), 256)));
        for (Iterator<Map.Entry<BlockPos, Display.BlockDisplay>> it = xrayDisplays.entrySet().iterator(); it.hasNext(); ) {
            var e = it.next();
            if (!want.contains(e.getKey()) || !e.getValue().isAlive()) { e.getValue().discard(); it.remove(); }
        }
        for (BlockPos p : want) {
            if (xrayDisplays.containsKey(p)) continue;
            BlockState st = level.getBlockState(p);
            Display.BlockDisplay d = net.minecraft.world.entity.EntityTypes.BLOCK_DISPLAY.create(level, EntitySpawnReason.COMMAND);
            if (d == null) continue;
            ((DisplayAccessors.Block) d).slbridge$setBlockState(st);
            ((DisplayAccessors.Base) d).slbridge$setTransformation(new com.mojang.math.Transformation(
                    new Vector3f(0.04f, 0.04f, 0.04f), new Quaternionf(), new Vector3f(0.92f, 0.92f, 0.92f), new Quaternionf()));
            ((DisplayAccessors.Base) d).slbridge$setGlowColorOverride(oreColor(st));
            d.setGlowingTag(true);
            d.addTag(XRAY_TAG);
            d.setPos(p.getX(), p.getY(), p.getZ());
            level.addFreshEntity(d);
            xrayDisplays.put(p, d);
        }
    }

    void clearXray() {
        for (Display.BlockDisplay d : xrayDisplays.values()) d.discard();
        xrayDisplays.clear();
    }

    /** Подсветка, оставшаяся в сохранении мира (вылет игры с рентгеном), — убрать. */
    void purgeStaleXray(ServerLevel level, ServerPlayer sp) {
        for (Display.BlockDisplay d : level.getEntitiesOfClass(Display.BlockDisplay.class, sp.getBoundingBox().inflate(64), e -> e.entityTags().contains(XRAY_TAG)))
            if (!xrayDisplays.containsValue(d)) d.discard();
    }

    // ── SCP-173: моб с табличкой «SCP-173» двигается, только пока на него не смотрят ──
    private final Set<UUID> frozen173 = new HashSet<>();
    private int nextSnap;

    private static boolean is173(Mob m) {
        if (!m.hasCustomName()) return false;
        String n = m.getCustomName().getString().toLowerCase(java.util.Locale.ROOT);
        return n.contains("173") || n.contains("peanut") || n.contains("арахис") || n.contains("скульптур");
    }

    private void scp173(ServerPlayer sp, ServerLevel level, int tick) {
        if (tick % 2 != 0) return;
        Vec3 eye = new Vec3(Pose.x, Pose.y, Pose.z);
        Vec3 look = Vec3.directionFromRotation(Pose.pitch, Pose.yaw);
        Set<UUID> seenNow = new HashSet<>();
        for (Mob m : level.getEntitiesOfClass(Mob.class, sp.getBoundingBox().inflate(48), m -> m.isAlive() && is173(m))) {
            Vec3 c = m.getBoundingBox().getCenter();
            Vec3 to = c.subtract(eye);
            boolean watched = to.normalize().dot(look) > 0.55
                    && level.clip(new ClipContext(eye, c, ClipContext.Block.VISUAL, ClipContext.Fluid.NONE, sp)).getType() != HitResult.Type.BLOCK;
            if (watched) {
                // смотрят — каменеет
                if (!m.isNoAi()) { m.setNoAi(true); m.setDeltaMovement(Vec3.ZERO); }
                seenNow.add(m.getUUID());
                frozen173.add(m.getUUID());
                continue;
            }
            if (m.isNoAi() && frozen173.contains(m.getUUID())) m.setNoAi(false);
            frozen173.remove(m.getUUID());
            if (m instanceof PathfinderMob pm) pm.getNavigation().moveTo(sp, 1.8);
            if (tick % 10 == 0) level.playSound(null, m.getX(), m.getY(), m.getZ(), SoundEvents.GRINDSTONE_USE, SoundSource.HOSTILE, 0.35f, 0.5f);
            // догнал, пока ты моргнул, — сворачивает шею
            if (m.distanceTo(sp) < 1.6 && tick >= nextSnap && !Pose.noclip) {
                nextSnap = tick + 60;
                level.playSound(null, sp.getX(), sp.getY(), sp.getZ(), SoundEvents.SKELETON_HURT, SoundSource.HOSTILE, 1.5f, 0.6f);
                sp.setInvulnerableTime(0);
                sp.hurtServer(level, sp.damageSources().mobAttack(m), 1000f);
            }
        }
        // тех, кого мы «заморозили», а у них сменилось имя или они далеко, — разморозить
        for (Iterator<UUID> it = frozen173.iterator(); it.hasNext(); ) {
            UUID u = it.next();
            if (seenNow.contains(u)) continue;
            Entity e = level.getEntity(u);
            if (e instanceof Mob m && m.isNoAi() && (!is173(m) || m.distanceTo(sp) > 48)) m.setNoAi(false);
            if (!(e instanceof Mob) || !is173((Mob) e) || e.distanceTo(sp) > 48) it.remove();
        }
    }

    // ── фразы на экран SL ──
    void say(String text) {
        JsonObject m = new JsonObject();
        m.addProperty("t", "say");
        m.addProperty("text", text);
        bridge.send(m);
    }

    private static String pick(net.minecraft.util.RandomSource r, String... options) { return options[r.nextInt(options.length)]; }

    /** Попытка сесть на транспорт — вежливый отказ. */
    void refuseRide(Entity v, net.minecraft.util.RandomSource r) {
        String id = BuiltInRegistries.ENTITY_TYPE.getKey(v.getType()).getPath();
        String text;
        if (id.contains("boat") || id.contains("raft")) text = pick(r,
                Lang.t("Вы бы сели в лодку, но в бронежилете сразу пойдёте ко дну", "You'd get in the boat, but with your vest on you'd sink straight away"),
                Lang.t("Лодка рассчитана на одного жителя, а не на сотрудника Фонда со снаряжением", "The boat holds one villager, not a Foundation employee with gear"),
                Lang.t("Инструкция Фонда запрещает пользоваться неучтёнными плавсредствами", "Foundation protocol forbids unregistered watercraft"));
        else if (id.contains("minecart")) text = pick(r,
                Lang.t("Вагонетка не рассчитана на сотрудников Фонда", "The minecart is not rated for Foundation staff"),
                Lang.t("Последний, кто сел в такую, оказался в карманном измерении", "The last one who rode one of these ended up in the pocket dimension"));
        else if (id.equals("horse") || id.equals("donkey") || id.equals("mule") || id.contains("skeleton_horse") || id.contains("zombie_horse")) text = pick(r,
                Lang.t("Лошадь окинула вас взглядом и отвергла", "The horse looked you over and refused"),
                Lang.t("Лошадь фыркнула: от вас пахнет комплексом", "The horse snorted: you smell like the facility"),
                Lang.t("Лошадь не подписывала соглашение о неразглашении", "The horse didn't sign the NDA"));
        else if (id.equals("pig")) text = pick(r,
                Lang.t("Свинья против. Категорически", "The pig objects. Strongly"),
                Lang.t("Свинья вас не узнала и отказалась везти", "The pig doesn't know you and won't carry you"));
        else if (id.equals("strider")) text = Lang.t("Лавомерка не доверяет людям в оранжевом", "The strider doesn't trust people in orange");
        else if (id.equals("camel")) text = Lang.t("Верблюд плюнул в вашу сторону. Намёк понят", "The camel spat in your direction. Message received");
        else if (id.contains("llama")) text = Lang.t("Лама плюнула вам в бронежилет", "The llama spat on your vest");
        else text = pick(r,
                Lang.t("Это существо не готово к такой ответственности", "This creature isn't ready for that responsibility"),
                Lang.t("Оно смотрит на вас с сомнением", "It looks at you with doubt"));
        say(text);
    }

    /** Лук, арбалет, трезубец, щит, удочка… — у сотрудника Фонда своё снаряжение. */
    boolean oldWeapon(ItemStack h, net.minecraft.util.RandomSource r) {
        var it = h.getItem();
        String text = null;
        if (it instanceof net.minecraft.world.item.BowItem) text = pick(r,
                Lang.t("Бро, у тебя есть огнестрел. Лук — прошлый век", "Bro, you have firearms. Bows are so last century"),
                Lang.t("Натянуть тетиву? В Фонде этому не учили", "Draw a bowstring? The Foundation never taught that"));
        else if (it instanceof net.minecraft.world.item.CrossbowItem) text = Lang.t("Арбалет? У тебя же E-11, зачем тебе средневековье", "A crossbow? You have an E-11, why go medieval");
        else if (it instanceof net.minecraft.world.item.TridentItem) text = Lang.t("Трезубец — это для Посейдона, а не для класса D", "Tridents are for Poseidon, not Class-D");
        else if (it instanceof net.minecraft.world.item.ShieldItem) text = pick(r,
                Lang.t("Сотрудник Фонда никогда не держал щит — он не умеет", "A Foundation employee has never held a shield — he doesn't know how"),
                Lang.t("Щит? Лучшая защита — бронежилет и быстрые ноги", "A shield? The best defence is a vest and fast legs"));
        else if (it instanceof net.minecraft.world.item.FishingRodItem) text = Lang.t("Рыбалка не входит в должностные обязанности", "Fishing is not in your job description");
        else if (it instanceof net.minecraft.world.item.MaceItem) text = Lang.t("Булава? Для этого есть Jailbird", "A mace? That's what the Jailbird is for");
        if (text == null) return false;
        say(text);
        return true;
    }

    // ── достижения и эндермены (вызываются из миксинов) ──
    public static void advancement(ServerPlayer sp, String title, String kind) {
        Fun f = instance;
        if (f == null) return;
        JsonObject m = new JsonObject();
        m.addProperty("t", "adv");
        m.addProperty("title", title);
        m.addProperty("kind", kind);
        f.bridge.send(m);
    }

    public static void endermanAngry() {
        Fun f = instance;
        if (f == null) return;
        JsonObject m = new JsonObject();
        m.addProperty("t", "scream096");
        f.bridge.send(m);
    }

    /** Выпитое зелье: эффекты, которые SL должен применить сам (лечение, скорость, невидимость…). */
    void potionToSl(List<MobEffectInstance> effects) {
        JsonArray a = new JsonArray();
        for (MobEffectInstance e : effects) {
            var key = e.getEffect().unwrapKey();
            if (key.isEmpty()) continue;
            JsonObject o = new JsonObject();
            o.addProperty("id", key.get().identifier().getPath());
            o.addProperty("amp", e.getAmplifier());
            o.addProperty("sec", e.getDuration() / 20.0);
            a.add(o);
        }
        if (a.isEmpty()) return;
        JsonObject m = new JsonObject();
        m.addProperty("t", "potion");
        m.add("effects", a);
        bridge.send(m);
    }
}
