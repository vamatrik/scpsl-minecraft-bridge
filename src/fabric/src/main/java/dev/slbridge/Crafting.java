package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.ItemStackTemplate;
import net.minecraft.world.item.crafting.AbstractCookingRecipe;
import net.minecraft.world.item.crafting.CraftingRecipe;
import net.minecraft.world.item.crafting.Ingredient;
import net.minecraft.world.item.crafting.PlacementInfo;
import net.minecraft.world.item.crafting.RecipeHolder;
import net.minecraft.world.item.crafting.RecipeType;
import net.minecraft.world.item.crafting.ShapedRecipe;
import net.minecraft.world.item.crafting.display.RecipeDisplay;
import net.minecraft.world.item.crafting.display.SlotDisplayContext;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Крафт и плавка по настоящим рецептам Minecraft. Инвентарь хранится в SL,
 * поэтому сюда приходит его снимок, а отсюда — что списать и что выдать.
 * Расположение в сетке не важно: раскладываем сами (как книга рецептов).
 */
public final class Crafting {

    private static Map<Item, Integer> counts(List<ItemStack> inv) {
        Map<Item, Integer> m = new LinkedHashMap<>();
        for (ItemStack s : inv) m.merge(s.getItem(), s.getCount(), Integer::sum);
        return m;
    }

    /** Подобрать предметы под ингредиенты. null — не хватает. Списывает из counts. */
    private static Map<Item, Integer> allocate(List<Ingredient> ings, Map<Item, Integer> counts) {
        Map<Item, Integer> used = new HashMap<>();
        for (Ingredient ing : ings) {
            Item best = null;
            int bestCount = 0;
            for (var e : counts.entrySet()) {
                if (e.getValue() > bestCount && ing.test(new ItemStack(e.getKey()))) { best = e.getKey(); bestCount = e.getValue(); }
            }
            if (best == null) return null;
            counts.merge(best, -1, Integer::sum);
            used.merge(best, 1, Integer::sum);
        }
        return used;
    }

    private static boolean near(ServerPlayer sp, ServerLevel level, Block... blocks) {
        BlockPos c = sp.blockPosition();
        for (BlockPos p : BlockPos.betweenClosed(c.offset(-4, -3, -4), c.offset(4, 3, 4))) {
            var st = level.getBlockState(p);
            for (Block b : blocks) if (st.is(b)) return true;
        }
        return false;
    }

    private static ItemStack result(RecipeHolder<?> h, ServerLevel level) {
        List<RecipeDisplay> d = h.value().display();
        if (d.isEmpty()) return ItemStack.EMPTY;
        return d.get(0).result().resolveForFirstStack(SlotDisplayContext.fromLevel(level));
    }

    private static boolean needsTable(CraftingRecipe r) {
        if (r instanceof ShapedRecipe s) return s.getWidth() > 2 || s.getHeight() > 2;
        return r.placementInfo().ingredients().size() > 4;
    }

    private static String rid(RecipeHolder<?> h) { return h.id().identifier().toString(); }

    private static JsonArray costJson(Map<Item, Integer> used) {
        JsonArray a = new JsonArray();
        for (var e : used.entrySet()) {
            if (e.getValue() <= 0) continue;
            JsonObject o = new JsonObject();
            o.addProperty("id", BuiltInRegistries.ITEM.getKey(e.getKey()).toString());
            o.addProperty("count", e.getValue());
            a.add(o);
        }
        return a;
    }

    // ───────────── крафт ─────────────

    public JsonObject craftList(ServerPlayer sp, ServerLevel level, List<ItemStack> inv) {
        boolean table = near(sp, level, Blocks.CRAFTING_TABLE);
        JsonArray list = new JsonArray();
        List<Object[]> found = new ArrayList<>();
        for (RecipeHolder<?> h : level.getServer().getRecipeManager().getRecipes()) {
            if (!(h.value() instanceof CraftingRecipe cr) || cr.isSpecial()) continue;
            PlacementInfo pi = cr.placementInfo();
            if (pi.isImpossibleToPlace()) continue;
            if (needsTable(cr) && !table) continue;
            Map<Item, Integer> c = counts(inv);
            int times = 0;
            while (times < 64 && allocate(pi.ingredients(), c) != null) times++;
            if (times == 0) continue;
            ItemStack res = result(h, level);
            if (res.isEmpty()) continue;
            found.add(new Object[]{h, res, times});
        }
        found.sort(Comparator.comparing(o -> ((ItemStack) o[1]).getHoverName().getString()));
        for (Object[] o : found) {
            JsonObject e = new JsonObject();
            e.addProperty("rid", rid((RecipeHolder<?>) o[0]));
            e.add("result", ItemJson.toJson((ItemStack) o[1]));
            e.addProperty("max", (int) o[2]);
            list.add(e);
        }
        JsonObject r = new JsonObject();
        r.addProperty("t", "craftList");
        r.addProperty("table", table);
        r.add("recipes", list);
        return r;
    }

    public JsonObject craft(ServerPlayer sp, ServerLevel level, String id, int times, List<ItemStack> inv) {
        JsonObject r = new JsonObject();
        r.addProperty("t", "crafted");
        r.addProperty("rid", id);
        boolean table = near(sp, level, Blocks.CRAFTING_TABLE);
        for (RecipeHolder<?> h : level.getServer().getRecipeManager().getRecipes()) {
            if (!rid(h).equals(id) || !(h.value() instanceof CraftingRecipe cr)) continue;
            if (needsTable(cr) && !table) { r.addProperty("ok", false); r.addProperty("msg", Lang.t("Нужен верстак рядом", "You need a crafting table nearby")); return r; }
            Map<Item, Integer> c = counts(inv);
            Map<Item, Integer> total = new HashMap<>();
            int done = 0;
            for (; done < Math.max(1, times); done++) {
                Map<Item, Integer> u = allocate(cr.placementInfo().ingredients(), c);
                if (u == null) break;
                u.forEach((k, v) -> total.merge(k, v, Integer::sum));
            }
            if (done == 0) { r.addProperty("ok", false); r.addProperty("msg", Lang.t("Не хватает предметов", "Not enough items")); return r; }
            ItemStack res = result(h, level);
            List<ItemStack> give = new ArrayList<>();
            int n = res.getCount() * done;
            while (n > 0) { int k = Math.min(n, res.getMaxStackSize()); give.add(res.copyWithCount(k)); n -= k; }
            // остатки: ведро от ведра молока и т.п.
            total.forEach((item, cnt) -> {
                ItemStackTemplate rem = item.getCraftingRemainder();
                if (rem != null) for (int i = 0; i < cnt; i++) give.add(rem.create());
            });
            r.addProperty("ok", true);
            r.addProperty("times", done);
            r.add("consume", costJson(total));
            r.add("give", ItemJson.toJson(give));
            level.playSound(null, sp.blockPosition(), net.minecraft.sounds.SoundEvents.UI_LOOM_TAKE_RESULT, net.minecraft.sounds.SoundSource.PLAYERS, 0.6f, 1f);
            return r;
        }
        r.addProperty("ok", false);
        r.addProperty("msg", Lang.t("Рецепт не найден", "Recipe not found"));
        return r;
    }

    // ───────────── плавка ─────────────

    /** Сколько предметов переплавляет одна единица топлива. */
    public static double fuelUnits(Item item) {
        String id = BuiltInRegistries.ITEM.getKey(item).getPath();
        return switch (id) {
            case "coal", "charcoal" -> 8;
            case "coal_block" -> 80;
            case "blaze_rod" -> 12;
            case "lava_bucket" -> 100;
            case "dried_kelp_block" -> 20;
            case "stick", "bamboo", "scaffolding" -> 0.5;
            case "crafting_table", "chest", "bookshelf", "barrel", "ladder", "note_block", "jukebox" -> 1.5;
            default -> {
                if (id.endsWith("_planks") || id.endsWith("_log") || id.endsWith("_wood") || id.endsWith("_stem")
                        || id.endsWith("_hyphae") || id.endsWith("_fence") || id.endsWith("_fence_gate")
                        || id.endsWith("_stairs") && id.contains("wood") ) yield 1.5;
                if (id.endsWith("_slab") && (id.contains("oak") || id.contains("spruce") || id.contains("birch")
                        || id.contains("jungle") || id.contains("acacia") || id.contains("cherry") || id.contains("mangrove"))) yield 0.75;
                if (id.endsWith("_sapling") || id.startsWith("wooden_")) yield id.startsWith("wooden_") ? 1 : 0.5;
                yield 0;
            }
        };
    }

    private static boolean isCooking(RecipeHolder<?> h) {
        return h.value() instanceof AbstractCookingRecipe && h.value().getType() == RecipeType.SMELTING;
    }

    public JsonObject smeltList(ServerPlayer sp, ServerLevel level, List<ItemStack> inv) {
        boolean furnace = near(sp, level, Blocks.FURNACE, Blocks.BLAST_FURNACE, Blocks.SMOKER);
        JsonObject r = new JsonObject();
        r.addProperty("t", "smeltList");
        r.addProperty("furnace", furnace);
        JsonArray list = new JsonArray();
        if (furnace) {
            Map<Item, Integer> c = counts(inv);
            double fuel = 0;
            for (var e : c.entrySet()) fuel += fuelUnits(e.getKey()) * e.getValue();
            for (RecipeHolder<?> h : level.getServer().getRecipeManager().getRecipes()) {
                if (!isCooking(h)) continue;
                AbstractCookingRecipe cr = (AbstractCookingRecipe) h.value();
                for (var e : c.entrySet()) {
                    if (!cr.input().test(new ItemStack(e.getKey()))) continue;
                    double fuelExcl = fuel - fuelUnits(e.getKey()) * e.getValue(); // само сырьё как топливо не тратим
                    int max = (int) Math.min(e.getValue(), Math.floor(fuelExcl));
                    if (max <= 0) continue;
                    ItemStack res = result(h, level);
                    if (res.isEmpty()) continue;
                    JsonObject o = new JsonObject();
                    o.addProperty("rid", rid(h) + "|" + BuiltInRegistries.ITEM.getKey(e.getKey()));
                    o.add("input", ItemJson.toJson(new ItemStack(e.getKey(), e.getValue())));
                    o.add("result", ItemJson.toJson(res));
                    o.addProperty("max", max);
                    list.add(o);
                }
            }
        }
        r.add("recipes", list);
        return r;
    }

    public JsonObject smelt(ServerPlayer sp, ServerLevel level, String key, int times, List<ItemStack> inv) {
        JsonObject r = new JsonObject();
        r.addProperty("t", "crafted");
        r.addProperty("rid", key);
        if (!near(sp, level, Blocks.FURNACE, Blocks.BLAST_FURNACE, Blocks.SMOKER)) { r.addProperty("ok", false); r.addProperty("msg", Lang.t("Нужна печь рядом", "You need a furnace nearby")); return r; }
        String[] parts = key.split("\\|");
        Item input = ItemJson.item(parts.length > 1 ? parts[1] : "");
        for (RecipeHolder<?> h : level.getServer().getRecipeManager().getRecipes()) {
            if (!rid(h).equals(parts[0]) || !isCooking(h)) continue;
            Map<Item, Integer> c = counts(inv);
            int have = c.getOrDefault(input, 0);
            int n = Math.min(Math.max(1, times), have);
            // топливо: сначала самое «ёмкое», сырьё не трогаем
            List<Map.Entry<Item, Integer>> fuels = new ArrayList<>(c.entrySet());
            fuels.removeIf(e -> e.getKey() == input || fuelUnits(e.getKey()) <= 0);
            fuels.sort((a, b) -> Double.compare(fuelUnits(b.getKey()), fuelUnits(a.getKey())));
            Map<Item, Integer> consume = new HashMap<>();
            double need = n;
            for (var e : fuels) {
                if (need <= 0) break;
                double u = fuelUnits(e.getKey());
                int k = (int) Math.min(e.getValue(), Math.ceil(need / u));
                consume.merge(e.getKey(), k, Integer::sum);
                need -= k * u;
            }
            if (need > 0) n -= (int) Math.ceil(need);
            if (n <= 0) { r.addProperty("ok", false); r.addProperty("msg", Lang.t("Нет топлива", "No fuel")); return r; }
            consume.merge(input, n, Integer::sum);
            ItemStack res = result(h, level);
            List<ItemStack> give = new ArrayList<>();
            int total = res.getCount() * n;
            while (total > 0) { int k = Math.min(total, res.getMaxStackSize()); give.add(res.copyWithCount(k)); total -= k; }
            consume.forEach((item, cnt) -> { // ведро от лавы
                ItemStackTemplate rem = item.getCraftingRemainder();
                if (rem != null && item != input) for (int i = 0; i < cnt; i++) give.add(rem.create());
            });
            r.addProperty("ok", true);
            r.addProperty("times", n);
            r.add("consume", costJson(consume));
            r.add("give", ItemJson.toJson(give));
            level.playSound(null, sp.blockPosition(), net.minecraft.sounds.SoundEvents.FURNACE_FIRE_CRACKLE, net.minecraft.sounds.SoundSource.BLOCKS, 1f, 1f);
            return r;
        }
        r.addProperty("ok", false);
        r.addProperty("msg", Lang.t("Рецепт не найден", "Recipe not found"));
        return r;
    }
}
