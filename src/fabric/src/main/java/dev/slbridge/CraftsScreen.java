package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Книга крафтов предметов SCP:SL прямо в Minecraft, в стиле его окон: слева разделы, справа рецепты —
 * сетка 3×3 с настоящими предметами, стрелка и результат. Наведи на предмет — подсказка с названием.
 * Данные — assets/slbridge/crafts.json (генерирует tools/gen_recipes.py вместе с рецептами).
 */
public final class CraftsScreen extends Screen {
    private record Craft(String name, List<List<ItemStack>> cells, List<ItemStack> result, boolean shaped) {}

    private static Map<String, List<Craft>> groups;
    private String group;
    private int scroll;

    // размеры (в пикселях интерфейса)
    private static final int SLOT = 18, CARD_W = 136, CARD_H = 80, SIDE_W = 104;
    private int px, py, pw, ph;

    public CraftsScreen() { super(Component.literal(Lang.t("Крафты SCP:SL", "SCP:SL crafting"))); }

    // ───────── данные ─────────

    private static ItemStack stack(String id, int count) {
        Item it = BuiltInRegistries.ITEM.getValue(Identifier.parse(id));
        return new ItemStack(it == null ? Items.BARRIER : it, Math.max(1, count));
    }

    private static void load() {
        if (groups != null) return;
        groups = new LinkedHashMap<>();
        try (var in = CraftsScreen.class.getResourceAsStream("/assets/slbridge/crafts.json")) {
            if (in == null) return;
            JsonObject root = JsonParser.parseReader(new InputStreamReader(in, StandardCharsets.UTF_8)).getAsJsonObject();
            boolean en = Lang.en();
            for (JsonElement e : root.getAsJsonArray("crafts")) {
                JsonObject o = e.getAsJsonObject();
                String g = o.get(en ? "groupEn" : "groupRu").getAsString();
                String name = o.get(en ? "en" : "ru").getAsString();
                List<List<ItemStack>> cells = new ArrayList<>();
                for (JsonElement c : o.getAsJsonArray("cells")) {
                    List<ItemStack> l = new ArrayList<>();
                    for (JsonElement id : c.getAsJsonArray()) l.add(stack(id.getAsString(), 1));
                    cells.add(l);
                }
                int count = o.get("count").getAsInt();
                List<ItemStack> result = new ArrayList<>();
                for (JsonElement id : o.getAsJsonArray("result")) {
                    ItemStack s = stack(id.getAsString(), count);
                    s.set(DataComponents.ITEM_NAME, Component.literal(name));
                    s.set(DataComponents.ENCHANTMENT_GLINT_OVERRIDE, true);
                    result.add(s);
                }
                groups.computeIfAbsent(g, k -> new ArrayList<>()).add(new Craft(name, cells, result, o.get("shaped").getAsBoolean()));
            }
            // аптечки из еды: N одинаковых продуктов → аптечки
            String mg = Lang.t("Аптечки из еды", "Medkits from food");
            for (JsonElement e : root.getAsJsonArray("medkits")) {
                JsonObject o = e.getAsJsonObject();
                int need = o.get("need").getAsInt(), out = o.get("out").getAsInt();
                ItemStack food = stack(o.get("food").getAsString(), 1);
                List<List<ItemStack>> cells = new ArrayList<>();
                for (int i = 0; i < 9; i++) cells.add(i < need ? List.of(food) : List.of());
                ItemStack res = stack("minecraft:red_dye", out);
                String nm = Lang.t("Аптечка", "Medkit") + " ×" + out;
                res.set(DataComponents.ITEM_NAME, Component.literal(nm));
                res.set(DataComponents.ENCHANTMENT_GLINT_OVERRIDE, true);
                groups.computeIfAbsent(mg, k -> new ArrayList<>()).add(new Craft(food.getHoverName().getString() + " ×" + need, cells, List.of(res), false));
            }
        } catch (Exception ex) {
            SlBridgeClient.LOG.warn("[SLBridge] crafts.json: {}", ex.toString());
        }
    }

    // ───────── раскладка ─────────

    @Override
    protected void init() {
        load();
        if (group == null && !groups.isEmpty()) group = groups.keySet().iterator().next();
        pw = Math.min(width - 16, SIDE_W + 3 * (CARD_W + 4) + 14);
        ph = Math.min(height - 16, 260);
        px = (width - pw) / 2;
        py = (height - ph) / 2;
        int y = py + 22;
        for (String g : groups.keySet()) {
            addRenderableWidget(Button.builder(Component.literal(g), b -> { group = g; scroll = 0; })
                    .bounds(px + 6, y, SIDE_W - 10, 18).build());
            y += 20;
        }
    }

    private int cols() { return Math.max(1, (pw - SIDE_W - 10) / (CARD_W + 4)); }

    private int maxScroll() {
        List<Craft> list = groups.getOrDefault(group, List.of());
        int rows = (list.size() + cols() - 1) / cols();
        return Math.max(0, rows * (CARD_H + 4) - (ph - 30));
    }

    @Override
    public boolean mouseScrolled(double mx, double my, double dx, double dy) {
        scroll = Math.max(0, Math.min(maxScroll(), scroll - (int) (dy * 24)));
        return true;
    }


    @Override
    public boolean isPauseScreen() { return false; }

    // ───────── рисование в стиле окон Minecraft ─────────

    private static void panel(GuiGraphicsExtractor g, int x, int y, int w, int h) {
        g.fill(x, y, x + w, y + h, 0xFF000000);
        g.fill(x + 1, y + 1, x + w - 1, y + h - 1, 0xFFC6C6C6);
        g.fill(x + 1, y + 1, x + w - 2, y + 3, 0xFFFFFFFF);
        g.fill(x + 1, y + 1, x + 3, y + h - 2, 0xFFFFFFFF);
        g.fill(x + 2, y + h - 3, x + w - 1, y + h - 1, 0xFF555555);
        g.fill(x + w - 3, y + 2, x + w - 1, y + h - 1, 0xFF555555);
    }

    private static void slot(GuiGraphicsExtractor g, int x, int y) {
        g.fill(x, y, x + SLOT, y + SLOT, 0xFF8B8B8B);
        g.fill(x, y, x + SLOT - 1, y + 1, 0xFF373737);
        g.fill(x, y, x + 1, y + SLOT - 1, 0xFF373737);
        g.fill(x + 1, y + SLOT - 1, x + SLOT, y + SLOT, 0xFFFFFFFF);
        g.fill(x + SLOT - 1, y + 1, x + SLOT, y + SLOT, 0xFFFFFFFF);
    }

    private static void arrow(GuiGraphicsExtractor g, int x, int y) {
        g.fill(x, y + 5, x + 14, y + 9, 0xFF8B8B8B);
        for (int i = 0; i < 7; i++) g.fill(x + 14 + i, y + i, x + 15 + i, y + 14 - i, 0xFF8B8B8B);
    }

    private static <T> T cycle(List<T> l) { return l.get((int) ((System.currentTimeMillis() / 1000) % l.size())); }

    @Override
    public void extractRenderState(GuiGraphicsExtractor g, int mx, int my, float pt) {
        g.fill(0, 0, width, height, 0xA0101010);
        panel(g, px, py, pw, ph);
        g.text(font, title, px + 8, py + 7, 0xFF404040, false);
        String hint = Lang.t("Esc — закрыть", "Esc — close");
        g.text(font, hint, px + pw - 8 - font.width(hint), py + 7, 0xFF707070, false);

        super.extractRenderState(g, mx, my, pt); // кнопки разделов

        List<Craft> list = groups.getOrDefault(group, List.of());
        int ax = px + SIDE_W, ay = py + 20, aw = pw - SIDE_W - 6, ah = ph - 26;
        g.enableScissor(ax, ay, ax + aw, ay + ah);
        int cols = cols();
        ItemStack hover = null;
        for (int i = 0; i < list.size(); i++) {
            Craft c = list.get(i);
            int cx = ax + (i % cols) * (CARD_W + 4), cy = ay + (i / cols) * (CARD_H + 4) - scroll;
            if (cy + CARD_H < ay || cy > ay + ah) continue;
            g.fill(cx, cy, cx + CARD_W, cy + CARD_H, 0xFFB3B3B3);
            String nm = font.plainSubstrByWidth(c.name(), CARD_W - 6);
            g.text(font, nm, cx + 3, cy + 3, 0xFF303030, false);
            int gx = cx + 4, gy = cy + 14;
            for (int k = 0; k < 9; k++) {
                int sx = gx + (k % 3) * SLOT, sy = gy + (k / 3) * SLOT;
                slot(g, sx, sy);
                List<ItemStack> cell = k < c.cells().size() ? c.cells().get(k) : List.of();
                if (cell.isEmpty()) continue;
                ItemStack s = cycle(cell);
                g.item(s, sx + 1, sy + 1);
                g.itemDecorations(font, s, sx + 1, sy + 1);
                if (mx >= sx && mx < sx + SLOT && my >= sy && my < sy + SLOT && my >= ay && my < ay + ah) hover = s;
            }
            arrow(g, gx + 3 * SLOT + 6, gy + SLOT - 7 + 1);
            int rx = gx + 3 * SLOT + 32, ry = gy + SLOT - 4;
            g.fill(rx - 4, ry - 4, rx + SLOT + 4, ry + SLOT + 4, 0xFF8B8B8B);
            slot(g, rx, ry);
            ItemStack r = cycle(c.result());
            g.item(r, rx + 1, ry + 1);
            g.itemDecorations(font, r, rx + 1, ry + 1);
            if (mx >= rx && mx < rx + SLOT && my >= ry && my < ry + SLOT && my >= ay && my < ay + ah) hover = r;
            if (!c.shaped() && !c.cells().isEmpty())
                g.text(font, Lang.t("в любом порядке", "any order"), gx, gy + 3 * SLOT + 2, 0xFF606060, false);
        }
        g.disableScissor();
        if (maxScroll() > 0) { // полоса прокрутки
            int bh = Math.max(12, ah * ah / (ah + maxScroll()));
            int by = ay + (ah - bh) * scroll / maxScroll();
            g.fill(px + pw - 5, ay, px + pw - 3, ay + ah, 0xFF8B8B8B);
            g.fill(px + pw - 5, by, px + pw - 3, by + bh, 0xFF373737);
        }
        if (hover != null) g.setTooltipForNextFrame(font, hover, mx, my);
    }

    @Override
    public void extractBackground(GuiGraphicsExtractor g, int mx, int my, float pt) { } // фон рисуем сами (без размытия мира)
}
