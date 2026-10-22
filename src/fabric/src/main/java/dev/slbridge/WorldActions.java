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
            target.hurtServer(level, level.damageSources().playerAttack(player), mcDamage);
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
                d.hits += 1f;
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
