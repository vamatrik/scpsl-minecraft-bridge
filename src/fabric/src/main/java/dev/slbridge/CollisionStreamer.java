package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.shapes.VoxelShape;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Превращает блоки Minecraft вокруг игрока в коллизионные параллелепипеды и отправляет их в SCP:SL.
 *
 * Оптимизации:
 *  • мир режется на ячейки 8×8×8, отправляется только куб 3×3×3 ячеек вокруг игрока;
 *  • ячейка пересчитывается, только когда в ней изменился блок или она впервые попала в радиус;
 *  • за тик считается не больше нескольких ячеек, ближайшие — первыми;
 *  • полные блоки с открытой гранью жадно склеиваются в крупные боксы, внутренние не отправляются.
 */
public final class CollisionStreamer {
    public static final int C = 8; // размер ячейки
    private static final Set<Long> dirty = ConcurrentHashMap.newKeySet();
    private final Map<Long, float[]> sent = new HashMap<>();
    private Level lastLevel;

    public static void markDirty(BlockPos p) {
        int x = p.getX(), y = p.getY(), z = p.getZ();
        // соседние ячейки тоже: изменилась «открытость» граничных блоков
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    dirty.add(BlockPos.asLong(Math.floorDiv(x + dx, C), Math.floorDiv(y + dy, C), Math.floorDiv(z + dz, C)));
    }

    public void reset() { sent.clear(); dirty.clear(); }

    public void tick(Level level, BridgeServer bridge, int radius, int budget) {
        // Клиент ещё в старом измерении (экран загрузки после портала), а позиция уже новая — не строим
        // коллизии нового места из блоков старого измерения.
        if (Pose.dim != null && level.dimension() != Pose.dim) return;
        if (!Pose.active) return;
        if (level != lastLevel) { // другое измерение / новый мир
            lastLevel = level;
            reset();
        }
        int pcx = Math.floorDiv((int) Math.floor(Pose.x), C);
        int pcy = Math.floorDiv((int) Math.floor(Pose.feetY), C);
        int pcz = Math.floorDiv((int) Math.floor(Pose.z), C);

        List<long[]> todo = new ArrayList<>(); // {key, dist²}
        Set<Long> wanted = new HashSet<>();
        for (int cx = pcx - radius; cx <= pcx + radius; cx++)
            for (int cy = pcy - radius; cy <= pcy + radius; cy++)
                for (int cz = pcz - radius; cz <= pcz + radius; cz++) {
                    // ячейки выше/ниже мира тоже отправляем (пустыми) — SL знает, что там «построено» и не замораживает
                    long key = BlockPos.asLong(cx, cy, cz);
                    wanted.add(key);
                    if (!sent.containsKey(key) || dirty.contains(key)) {
                        int dx = cx - pcx, dy = cy - pcy, dz = cz - pcz;
                        todo.add(new long[]{key, (long) dx * dx + (long) dy * dy + (long) dz * dz});
                    }
                }
        todo.sort((a, b) -> Long.compare(a[1], b[1]));

        int done = 0;
        for (long[] t : todo) {
            if (done >= budget) break;
            long key = t[0];
            int cx = BlockPos.getX(key), cy = BlockPos.getY(key), cz = BlockPos.getZ(key);
            // чанк ещё не загружен клиентом — подождём, иначе отправим «пустоту»
            if (!level.hasChunk(Math.floorDiv(cx * C, 16), Math.floorDiv(cz * C, 16))) continue;
            dirty.remove(key);
            done++;
            float[] boxes = buildCell(level, cx, cy, cz);
            float[] old = sent.get(key);
            if (old != null && Arrays.equals(old, boxes)) continue;
            sent.put(key, boxes);
            JsonObject m = new JsonObject();
            m.addProperty("t", "sec");
            m.addProperty("k", cx + "," + cy + "," + cz);
            JsonArray arr = new JsonArray(boxes.length);
            for (float f : boxes) arr.add(f);
            m.add("b", arr);
            bridge.send(m);
        }

        sent.keySet().removeIf(key -> {
            if (wanted.contains(key)) return false;
            JsonObject m = new JsonObject();
            m.addProperty("t", "secDel");
            m.addProperty("k", BlockPos.getX(key) + "," + BlockPos.getY(key) + "," + BlockPos.getZ(key));
            bridge.send(m);
            return true;
        });
    }

    private static boolean isFull(Level level, BlockPos.MutableBlockPos p) {
        BlockState s = level.getBlockState(p);
        return !s.isAir() && s.isCollisionShapeFullBlock(level, p);
    }

    private static float[] buildCell(Level level, int cx, int cy, int cz) {
        int ox = cx * C, oy = cy * C, oz = cz * C;
        boolean[] solid = new boolean[C * C * C];
        List<Float> out = new ArrayList<>();
        BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
        BlockPos.MutableBlockPos n = new BlockPos.MutableBlockPos();

        for (int y = 0; y < C; y++)
            for (int z = 0; z < C; z++)
                for (int x = 0; x < C; x++) {
                    p.set(ox + x, oy + y, oz + z);
                    BlockState s = level.getBlockState(p);
                    if (s.isAir()) continue;
                    // Плавание: у поверхности глубокой воды/лавы — тонкий «пол» на уровне низа верхнего блока жидкости.
                    // Голова над поверхностью, на берег (1 блок) можно запрыгнуть.
                    var fluid = s.getFluidState();
                    if (!fluid.isEmpty()) {
                        if (level.getFluidState(n.set(p.getX(), p.getY() + 1, p.getZ())).isEmpty()
                                && !level.getFluidState(n.set(p.getX(), p.getY() - 1, p.getZ())).isEmpty()) {
                            add(out, p.getX(), p.getY() - 0.05, p.getZ(), p.getX() + 1, p.getY(), p.getZ() + 1);
                        }
                    }
                    VoxelShape shape = s.getCollisionShape(level, p);
                    if (shape.isEmpty()) continue;
                    if (s.isCollisionShapeFullBlock(level, p)) {
                        boolean exposed = !isFull(level, n.set(p.getX() + 1, p.getY(), p.getZ()))
                                || !isFull(level, n.set(p.getX() - 1, p.getY(), p.getZ()))
                                || !isFull(level, n.set(p.getX(), p.getY() + 1, p.getZ()))
                                || !isFull(level, n.set(p.getX(), p.getY() - 1, p.getZ()))
                                || !isFull(level, n.set(p.getX(), p.getY(), p.getZ() + 1))
                                || !isFull(level, n.set(p.getX(), p.getY(), p.getZ() - 1));
                        if (exposed) solid[(y * C + z) * C + x] = true;
                    } else {
                        for (AABB b : shape.toAabbs()) {
                            add(out, p.getX() + b.minX, p.getY() + b.minY, p.getZ() + b.minZ,
                                    p.getX() + b.maxX, p.getY() + b.maxY, p.getZ() + b.maxZ);
                        }
                    }
                }

        for (int y = 0; y < C; y++)
            for (int z = 0; z < C; z++)
                for (int x = 0; x < C; x++) {
                    if (!solid[(y * C + z) * C + x]) continue;
                    int x2 = x;
                    while (x2 + 1 < C && solid[(y * C + z) * C + x2 + 1]) x2++;
                    int z2 = z;
                    outerZ:
                    while (z2 + 1 < C) {
                        for (int xi = x; xi <= x2; xi++) if (!solid[(y * C + z2 + 1) * C + xi]) break outerZ;
                        z2++;
                    }
                    int y2 = y;
                    outerY:
                    while (y2 + 1 < C) {
                        for (int zi = z; zi <= z2; zi++)
                            for (int xi = x; xi <= x2; xi++) if (!solid[((y2 + 1) * C + zi) * C + xi]) break outerY;
                        y2++;
                    }
                    for (int yi = y; yi <= y2; yi++)
                        for (int zi = z; zi <= z2; zi++)
                            for (int xi = x; xi <= x2; xi++) solid[(yi * C + zi) * C + xi] = false;
                    add(out, ox + x, oy + y, oz + z, ox + x2 + 1, oy + y2 + 1, oz + z2 + 1);
                }

        float[] r = new float[out.size()];
        for (int i = 0; i < r.length; i++) r[i] = out.get(i);
        return r;
    }

    private static void add(List<Float> out, double x0, double y0, double z0, double x1, double y1, double z1) {
        out.add(round(x0)); out.add(round(y0)); out.add(round(z0));
        out.add(round(x1)); out.add(round(y1)); out.add(round(z1));
    }

    private static float round(double v) { return Math.round(v * 1000.0) / 1000f; }
}
