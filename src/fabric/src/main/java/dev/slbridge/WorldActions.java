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
        boolean hurt = target.hurtServer(level, src, mcDamage);
        if (!knockback) target.setDeltaMovement(velBefore);
        if (target instanceof LivingEntity le1) le1.damageCooldownTime = 0;
        target.setInvulnerableTime(0);
        if (res != null) {
            if (!hurt && !knockback && target instanceof net.minecraft.world.entity.monster.Enderman) res.addProperty("dodged", true);
            res.addProperty("entity", BuiltInRegistries.ENTITY_TYPE.getKey(target.getType()).toString());
            res.addProperty("dmg", mcDamage);
            if (target instanceof LivingEntity living) {
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
        EntityHitResult entityHit = ProjectileUtil.getEntityHitResult(level, player, from, solidEnd, sweep,
                e -> e != player && e.isAlive() && e.isPickable() && !e.isSpectator(), 0f); // строго по хитбоксу
        // Блок для разрушения — любой, включая траву (по контуру).
        BlockHitResult blockHit = level.clip(new ClipContext(from, to, ClipContext.Block.OUTLINE, ClipContext.Fluid.NONE, player));

        JsonObject res = new JsonObject();
        res.addProperty("t", "hit");
        if (entityHit != null) {
            Entity target = entityHit.getEntity();
            float mcDamage = slDamage / cfg.damageScale;
            target.setInvulnerableTime(0); // автомат стреляет чаще 10 тиков
            // У мобов нет «бессмертия» после попадания: в 26.x это damageCooldownTime (10 тиков) — обнуляем до и после.
            if (target instanceof LivingEntity le0) { le0.damageCooldownTime = 0; le0.hurtTime = 0; }
            target.setInvulnerableTime(0);
            Vec3 velBefore = target.getDeltaMovement();
            // Эндермен уворачивается от пуль, как от стрел: снаряд → он телепортируется и урона не получает.
            // Холодное оружие (Jailbird, мачете — с отбрасыванием) бьёт его обычным ударом.
            var src = !knockback && target instanceof net.minecraft.world.entity.monster.Enderman
                    ? level.damageSources().thrown(player, player)
                    : level.damageSources().playerAttack(player);
            boolean hurt = target.hurtServer(level, src, mcDamage);
            // эндермен увернулся (телепортировался) — в SL без хитмаркера
            if (!hurt && !knockback && target instanceof net.minecraft.world.entity.monster.Enderman) res.addProperty("dodged", true);
            // отбрасывание — только от холодного оружия (Jailbird, SCP-1509); пули и Micro HID моба не толкают
            if (!knockback) target.setDeltaMovement(velBefore);
            if (target instanceof LivingEntity le1) le1.damageCooldownTime = 0;
            target.setInvulnerableTime(0);
            res.addProperty("kind", "entity");
            res.addProperty("entity", BuiltInRegistries.ENTITY_TYPE.getKey(target.getType()).toString());
            res.addProperty("dmg", mcDamage);
            if (target instanceof LivingEntity living) {
                if (Float.isFinite(living.getHealth())) res.addProperty("hp", living.getHealth());
                res.addProperty("dead", living.isDeadOrDying());
            }
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
                d.hits += Math.max(0.1f, mine);
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

    private void breakBlock(ServerLevel level, ServerPlayer player, BlockPos pos, BlockState state, BridgeServer bridge) {
        BlockEntity be = level.getBlockEntity(pos);
        // Дроп считаем как будто ломали алмазной киркой: камень → булыжник, руды → ресурсы.
        List<ItemStack> drops = Block.getDrops(state, level, pos, be, player, new ItemStack(Items.DIAMOND_PICKAXE));
        level.destroyBlock(pos, false, player, 512);
        // Выпавшее — настоящими предметами в мире Minecraft; в SL их подбирают на E.
        for (ItemStack s : drops) {
            if (s.isEmpty()) continue;
            net.minecraft.world.entity.item.ItemEntity ie = new net.minecraft.world.entity.item.ItemEntity(level,
                    pos.getX() + 0.5, pos.getY() + 0.3, pos.getZ() + 0.5, s.copy(), 0, 0.15, 0);
            ie.setDefaultPickUpDelay();
            level.addFreshEntity(ie);
        }
        state.spawnAfterBreak(level, pos, new ItemStack(Items.DIAMOND_PICKAXE), true); // опыт из руд и т.п.

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
