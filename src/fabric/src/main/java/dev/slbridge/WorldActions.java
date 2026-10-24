package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.projectile.ProjectileUtil;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.entity.BlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.EntityHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;

/** Действия в мире, которые выполняются на потоке встроенного сервера Minecraft. */
public final class WorldActions {
    private static final class Damage { float hits; long lastNanos; int breakerId; }
    private final Map<BlockPos, Damage> blockDamage = new HashMap<>();
    private int nextBreakerId = 1_000_000;
    /** Сила «добычи» следующего удара: сколько попаданий засчитать блоку (мультитул: дерево 1.5 … незерит 6.5). */
    public float mine = 1f;
    /** Чары предмета SL в руке («sharpness:3,looting:1»), пусто — без чар. */
    public String ench = "";

    /**
     * Удар ближнего боя как в SCP:SL (Jailbird, SCP-1509): шар радиусом radius на offset м перед камерой, только цели
     * впереди. Главная цель (ближе всех к прицелу) получает полный урон, остальные — splash (1509 — половину,
     * Jailbird — полный). Никого нет — обычный удар лучом (ломает блок).
     */
    public void melee(ServerLevel level, ServerPlayer player, Vec3 from, Vec3 dir, double range, double radius, double offset,
                      float slDamage, float splash, boolean breaks, BridgeConfig cfg, BridgeServer bridge) {
        Vec3 f = dir.normalize();
        Vec3 center = from.add(f.scale(offset));
        List<Entity> found = level.getEntities(player, new AABB(center, center).inflate(radius),
                e -> e != player && e.isAlive() && e.isPickable() && !e.isSpectator() && e instanceof LivingEntity);
        Entity primary = null; double best = -2;
        List<Entity> targets = new java.util.ArrayList<>();
        for (Entity e : found) {
            AABB b = e.getBoundingBox();
            double cx = Math.max(b.minX, Math.min(center.x, b.maxX)), cy = Math.max(b.minY, Math.min(center.y, b.maxY)), cz = Math.max(b.minZ, Math.min(center.z, b.maxZ));
            if (center.distanceToSqr(cx, cy, cz) > radius * radius) continue;
            Vec3 to = b.getCenter().subtract(from);
            double dot = to.lengthSqr() < 1e-6 ? 1 : to.normalize().dot(f);
            if (dot <= 0.1) continue; // за спиной/сбоку не бьём
            // сквозь стену не бьём
            if (level.clip(new ClipContext(from, b.getCenter(), ClipContext.Block.COLLIDER, ClipContext.Fluid.NONE, player)).getType() == HitResult.Type.BLOCK) continue;
            targets.add(e);
            if (dot > best) { best = dot; primary = e; }
        }
        if (primary == null) { shot(level, player, from, dir, range, slDamage, breaks, true, cfg, bridge); return; }
        // «Разящий клинок»: остальные цели в области получают больше (как у меча: I — ½, II — ⅔, III — ¾ от удара)
        int sweep = SlEnchants.level(ench, "sweeping_edge");
        if (sweep > 0) splash = splash + (1f - splash) * sweep / (sweep + 1f);
        JsonObject res = new JsonObject();
        res.addProperty("t", "hit");
        res.addProperty("kind", "entity");
        res.addProperty("targets", targets.size());
        for (Entity e : targets) {
            float dmg = e == primary ? slDamage : slDamage * splash;
            if (dmg <= 0) continue;
            hitEntity(level, player, e, dmg, true, cfg, e == primary ? res : null);
        }
        bridge.send(res);
    }

    /** Урон по мобу из SL (общая часть выстрела и удара). res — куда записать результат (для главной цели). */
    private boolean hitEntity(ServerLevel level, ServerPlayer player, Entity target, float slDamage, boolean knockback, BridgeConfig cfg, JsonObject res) {
        float mcDamage = slDamage / cfg.damageScale;
        target.setInvulnerableTime(0);
        if (target instanceof LivingEntity le0) { le0.damageCooldownTime = 0; le0.hurtTime = 0; }
        Vec3 velBefore = target.getDeltaMovement();
        var src = !knockback && target instanceof net.minecraft.world.entity.monster.Enderman
                ? level.damageSources().thrown(player, player)
                : level.damageSources().playerAttack(player);
        ItemStack weapon = ench.isEmpty() ? ItemStack.EMPTY : SlEnchants.weapon(ench);
        if (!weapon.isEmpty()) {
            if (knockback) mcDamage = net.minecraft.world.item.enchantment.EnchantmentHelper.modifyDamage(level, weapon, target, src, mcDamage); // Острота, Небесная кара, Бич членистоногих
            else mcDamage *= 1f + 0.10f * SlEnchants.level(ench, "power"); // Сила: +10% за уровень
        }
        boolean hurt;
        // на время удара предмет с чарами — «в руке» игрока Minecraft: так работают Добыча и Заговор огня
        ItemStack prevHand = player.getMainHandItem();
        if (!weapon.isEmpty()) player.setItemSlot(net.minecraft.world.entity.EquipmentSlot.MAINHAND, weapon);
        try {
            hurt = target.hurtServer(level, src, mcDamage);
            if (hurt && !weapon.isEmpty()) {
                if (knockback) {
                    float kb = net.minecraft.world.item.enchantment.EnchantmentHelper.modifyKnockback(level, weapon, target, src, 0f);
                    if (kb > 0 && target instanceof LivingEntity lk) {
                        float yaw = player.getYRot() * ((float) Math.PI / 180f);
                        lk.knockback(kb * 0.5f, Math.sin(yaw), -Math.cos(yaw), src, 0f);
                    }
                    net.minecraft.world.item.enchantment.EnchantmentHelper.doPostAttackEffectsWithItemSource(level, target, src, weapon);
                } else if (SlEnchants.level(ench, "flame") > 0) {
                    target.igniteForSeconds(5f); // Горящая стрела
                }
            }
        } finally {
            if (!weapon.isEmpty()) player.setItemSlot(net.minecraft.world.entity.EquipmentSlot.MAINHAND, prevHand);
        }
        if (!knockback) target.setDeltaMovement(velBefore);
        if (target instanceof LivingEntity le1) le1.damageCooldownTime = 0;
        target.setInvulnerableTime(0);
        if (res != null) {
            if (!hurt && !knockback && target instanceof net.minecraft.world.entity.monster.Enderman) res.addProperty("dodged", true);
            res.addProperty("entity", BuiltInRegistries.ENTITY_TYPE.getKey(target.getType()).toString());
            res.addProperty("dmg", mcDamage);
            // дракон Края получает урон частями тела — здоровье и смерть берём у него самого
            Entity owner = target instanceof net.minecraft.world.entity.boss.enderdragon.EnderDragonPart part ? part.parentMob : target;
            if (owner instanceof LivingEntity living) {
                if (Float.isFinite(living.getHealth())) res.addProperty("hp", living.getHealth());
                res.addProperty("dead", living.isDeadOrDying());
            }
        }
        return hurt;
    }

    /** Выстрел из SCP:SL: луч из камеры. Попал в моба — урон, попал в блок — копим «трещины» и ломаем. */
    public void shot(ServerLevel level, ServerPlayer player, Vec3 from, Vec3 dir, double range, float slDamage, boolean breaks, boolean knockback,
                     BridgeConfig cfg, BridgeServer bridge) {
        Vec3 to = from.add(dir.normalize().scale(range));

        // Мобов ищем до первого ТВЁРДОГО блока: трава и цветы пулю не останавливают.
        BlockHitResult solidHit = level.clip(new ClipContext(from, to, ClipContext.Block.COLLIDER, ClipContext.Fluid.NONE, player));
        Vec3 solidEnd = solidHit.getType() == HitResult.Type.MISS ? to : solidHit.getLocation();
        AABB sweep = new AABB(from, solidEnd).inflate(1.0);
        // «Пронзание»: пуля проходит сквозь столько мобов подряд (+1 за уровень)
        List<Entity> hits = rayEntities(level, player, from, solidEnd, sweep, 1 + SlEnchants.level(ench, "piercing"));
        // Блок для разрушения — любой, включая траву (по контуру).
        BlockHitResult blockHit = level.clip(new ClipContext(from, to, ClipContext.Block.OUTLINE, ClipContext.Fluid.NONE, player));

        JsonObject res = new JsonObject();
        res.addProperty("t", "hit");
        if (!hits.isEmpty()) {
            res.addProperty("kind", "entity");
            for (int i = 0; i < hits.size(); i++) hitEntity(level, player, hits.get(i), slDamage, knockback, cfg, i == 0 ? res : null);
        } else if (breaks && blockHit.getType() == HitResult.Type.BLOCK
                && level.getBlockState(blockHit.getBlockPos()).is(net.minecraft.world.level.block.Blocks.DRAGON_EGG)) {
            // яйцо дракона не ломается, а отпрыгивает — как от удара в Minecraft
            BlockPos pos = blockHit.getBlockPos().immutable();
            level.getBlockState(pos).attack(level, pos, player);
            res.addProperty("kind", "block");
            res.addProperty("block", "minecraft:dragon_egg");
        } else if (breaks && blockHit.getType() == HitResult.Type.BLOCK) {
            BlockPos pos = blockHit.getBlockPos().immutable();
            BlockState state = level.getBlockState(pos);
            float hardness = state.getDestroySpeed(level, pos);
            res.addProperty("kind", "block");
            res.addProperty("block", BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString());
            if (hardness < 0) {
                res.addProperty("unbreakable", true); // бедрок и т.п.
            } else {
                int needed = Math.max(1, (int) Math.ceil(hardness * cfg.shotsPerHardness));
                Damage d = blockDamage.computeIfAbsent(pos, p -> { Damage n = new Damage(); n.breakerId = nextBreakerId++; return n; });
                d.hits += Math.max(0.1f, mine) * (1f + 0.25f * SlEnchants.level(ench, "efficiency")); // Эффективность: +25% за уровень
                d.lastNanos = System.nanoTime();
                res.addProperty("progress", Math.min(1f, d.hits / needed));
                if (d.hits >= needed) {
                    level.destroyBlockProgress(d.breakerId, pos, -1);
                    blockDamage.remove(pos);
                    breakBlock(level, player, pos, state, bridge);
                    res.addProperty("broken", true);
                } else {
                    level.destroyBlockProgress(d.breakerId, pos, (int) (d.hits / needed * 10f));
                }
            }
        } else {
            res.addProperty("kind", "none");
        }
        bridge.send(res);
    }

    /** Мобы на луче from→to (строго по хитбоксу), ближние первыми, не больше max. */
    private static List<Entity> rayEntities(ServerLevel level, ServerPlayer player, Vec3 from, Vec3 to, AABB sweep, int max) {
        List<Entity> cand = level.getEntities(player, sweep, e -> e != player && e.isAlive() && e.isPickable() && !e.isSpectator());
        List<Map.Entry<Double, Entity>> hit = new java.util.ArrayList<>();
        for (Entity e : cand) {
            AABB b = e.getBoundingBox();
            if (b.contains(from)) { hit.add(Map.entry(0.0, e)); continue; }
            b.clip(from, to).ifPresent(p -> hit.add(Map.entry(from.distanceToSqr(p), e)));
        }
        hit.sort(Map.Entry.comparingByKey());
        List<Entity> r = new java.util.ArrayList<>();
        for (var h : hit) {
            Entity e = h.getValue();
            // части дракона — одна цель
            Entity owner = e instanceof net.minecraft.world.entity.boss.enderdragon.EnderDragonPart part ? part.parentMob : e;
            boolean dup = false;
            for (Entity x : r) { Entity ox = x instanceof net.minecraft.world.entity.boss.enderdragon.EnderDragonPart px ? px.parentMob : x; if (ox == owner) dup = true; }
            if (dup) continue;
            r.add(e);
            if (r.size() >= max) break;
        }
        return r;
    }

    private void breakBlock(ServerLevel level, ServerPlayer player, BlockPos pos, BlockState state, BridgeServer bridge) {
        BlockEntity be = level.getBlockEntity(pos);
        // Дроп считаем как будто ломали алмазной киркой: камень → булыжник, руды → ресурсы.
        ItemStack tool = new ItemStack(Items.DIAMOND_PICKAXE);
        // Удача и Шёлковое касание мультитула — на «кирку», которой считаем дроп
        if (!ench.isEmpty()) {
            var all = SlEnchants.fromString(ench);
            var mut = new net.minecraft.world.item.enchantment.ItemEnchantments.Mutable(net.minecraft.world.item.enchantment.ItemEnchantments.EMPTY);
            for (var e : all.entrySet()) {
                String n = SlEnchants.name(e.getKey());
                if ("fortune".equals(n) || "silk_touch".equals(n)) mut.set(e.getKey(), e.getIntValue());
            }
            tool.set(net.minecraft.core.component.DataComponents.ENCHANTMENTS, mut.toImmutable());
        }
        List<ItemStack> drops = Block.getDrops(state, level, pos, be, player, tool);
        level.destroyBlock(pos, false, player, 512);
        // Выпавшее — настоящими предметами в мире Minecraft; в SL их подбирают на E.
        for (ItemStack s : drops) {
            if (s.isEmpty()) continue;
            net.minecraft.world.entity.item.ItemEntity ie = new net.minecraft.world.entity.item.ItemEntity(level,
                    pos.getX() + 0.5, pos.getY() + 0.3, pos.getZ() + 0.5, s.copy(), 0, 0.15, 0);
            ie.setDefaultPickUpDelay();
            level.addFreshEntity(ie);
        }
        state.spawnAfterBreak(level, pos, tool, true); // опыт из руд и т.п.

        JsonObject m = new JsonObject();
        m.addProperty("t", "drops");
        m.addProperty("x", pos.getX() + 0.5);
        m.addProperty("y", pos.getY() + 0.5);
        m.addProperty("z", pos.getZ() + 0.5);
        m.addProperty("block", BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString());
        JsonArray items = new JsonArray();
        for (ItemStack s : drops) {
            if (s.isEmpty()) continue;
            JsonObject it = new JsonObject();
            it.addProperty("id", BuiltInRegistries.ITEM.getKey(s.getItem()).toString());
            it.addProperty("count", s.getCount());
            it.addProperty("name", s.getHoverName().getString());
            items.add(it);
        }
        m.add("items", items);
        bridge.send(m);
    }

    /** Трещины на блоках, по которым давно не стреляли, исчезают (как в Minecraft). */
    public void tick(ServerLevel level) {
        long now = System.nanoTime();
        for (Iterator<Map.Entry<BlockPos, Damage>> it = blockDamage.entrySet().iterator(); it.hasNext(); ) {
            Map.Entry<BlockPos, Damage> e = it.next();
            if (now - e.getValue().lastNanos > 8_000_000_000L) {
                level.destroyBlockProgress(e.getValue().breakerId, e.getKey(), -1);
                it.remove();
            }
        }
    }
}
