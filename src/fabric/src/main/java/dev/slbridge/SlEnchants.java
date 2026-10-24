package dev.slbridge;

import net.minecraft.core.Holder;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.contents.TranslatableContents;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.enchantment.Enchantable;
import net.minecraft.world.item.enchantment.Enchantment;
import net.minecraft.world.item.enchantment.ItemEnchantments;
import it.unimi.dsi.fastutil.objects.Object2IntMap;

import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;

/**
 * Зачарования предметов SCP:SL в Minecraft. Какие чары можно наложить, решаем сами по метке предмета SL
 * (см. EnchantMixin): огнестрел и Micro HID — чары лука/арбалета (без «Отдачи»), мачете/мультитулы и Jailbird —
 * чары меча (+ инструменты у мачете, + прочность и починка у Jailbird), бронежилеты — чары брони.
 * Расходники не зачаровываются. Чары хранятся в состоянии предмета SL (slState: «ench=sharpness:3,looting:1»),
 * а в Minecraft — обычным компонентом зачарований, так что работают стол зачарований, книги на наковальне и точило.
 */
public final class SlEnchants {
    private SlEnchants() {}

    /** Реестры для поиска чар по имени (ставится при старте/тике встроенного сервера). */
    public static volatile HolderLookup.Provider regs;

    private static final Set<String> MELEE = Set.of("sharpness", "smite", "bane_of_arthropods", "fire_aspect", "knockback", "sweeping_edge", "looting");
    private static final Set<String> TOOL = Set.of("efficiency", "fortune", "silk_touch");
    private static final Set<String> RANGED = Set.of("power", "flame", "piercing", "infinity");
    private static final Set<String> ARMOR = Set.of("protection", "fire_protection", "blast_protection", "projectile_protection",
            "thorns", "feather_falling", "respiration", "depth_strider");

    /** Категория предмета SL: gun, hid, melee (мачете/мультитул), jailbird, armor; null — не зачаровывается. */
    public static String category(String sl) {
        if (sl == null) return null;
        if (sl.startsWith("Gun") || sl.equals("ParticleDisruptor")) return "gun";
        if (sl.equals("MicroHID")) return "hid";
        if (sl.equals("SCP1509")) return "melee";
        if (sl.equals("Jailbird")) return "jailbird";
        if (sl.startsWith("Armor")) return "armor";
        return null;
    }

    public static boolean allowed(String sl, String ench) {
        String c = category(sl);
        if (c == null || ench == null) return false;
        return switch (c) {
            case "gun" -> RANGED.contains(ench);
            case "hid" -> RANGED.contains(ench) || ench.equals("unbreaking") || ench.equals("mending");
            case "melee" -> MELEE.contains(ench) || TOOL.contains(ench);
            case "jailbird" -> MELEE.contains(ench) || ench.equals("unbreaking") || ench.equals("mending");
            case "armor" -> ARMOR.contains(ench);
            default -> false;
        };
    }

    /** «Зачаровываемость» для стола (как у материала: выше — чаще хорошие чары). */
    public static int enchantability(String sl) {
        String c = category(sl);
        if (c == null) return 0;
        return switch (c) {
            case "gun", "hid" -> 12;
            case "jailbird" -> 15;
            case "armor" -> 12;
            default -> 14;
        };
    }

    /** Имя чар (без minecraft:) — у ванильных чар оно есть в ключе перевода «enchantment.minecraft.sharpness». */
    public static String name(Enchantment e) {
        if (e.description().getContents() instanceof TranslatableContents tc) {
            String k = tc.getKey();
            if (k.startsWith("enchantment.minecraft.")) return k.substring("enchantment.minecraft.".length());
        }
        return null;
    }

    public static String name(Holder<Enchantment> h) {
        return h.unwrapKey().map(k -> k.identifier().getNamespace().equals("minecraft") ? k.identifier().getPath() : null).orElse(null);
    }

    /** Предмет SL, к которому чары вообще применимы, — добавить ему «зачаровываемость» (иначе стол его не примет). */
    public static void ensureEnchantable(ItemStack st) {
        String sl = ItemJson.slKey(st);
        int v = enchantability(sl);
        if (v <= 0) { if (st.has(DataComponents.ENCHANTABLE) && sl != null) st.remove(DataComponents.ENCHANTABLE); return; }
        Enchantable cur = st.get(DataComponents.ENCHANTABLE);
        if (cur == null || cur.value() != v) st.set(DataComponents.ENCHANTABLE, new Enchantable(v));
    }

    // ── строка чар «sharpness:3,looting:1» ⇄ компонент ──

    public static String toString(ItemEnchantments en) {
        if (en == null || en.isEmpty()) return "";
        StringBuilder sb = new StringBuilder();
        for (Object2IntMap.Entry<Holder<Enchantment>> e : en.entrySet()) {
            String n = name(e.getKey());
            if (n == null) continue;
            if (sb.length() > 0) sb.append(',');
            sb.append(n).append(':').append(e.getIntValue());
        }
        return sb.toString();
    }

    public static Map<String, Integer> parse(String s) {
        Map<String, Integer> r = new LinkedHashMap<>();
        if (s == null || s.isEmpty()) return r;
        for (String p : s.split(",")) {
            int i = p.indexOf(':');
            if (i <= 0) continue;
            try { r.put(p.substring(0, i), Integer.parseInt(p.substring(i + 1))); } catch (NumberFormatException ignored) { }
        }
        return r;
    }

    public static ItemEnchantments fromString(String s) {
        HolderLookup.Provider p = regs;
        if (p == null || s == null || s.isEmpty()) return ItemEnchantments.EMPTY;
        var look = p.lookupOrThrow(Registries.ENCHANTMENT);
        ItemEnchantments.Mutable m = new ItemEnchantments.Mutable(ItemEnchantments.EMPTY);
        for (var e : parse(s).entrySet()) {
            Identifier id = Identifier.tryParse("minecraft:" + e.getKey());
            if (id == null) continue;
            look.get(ResourceKey.create(Registries.ENCHANTMENT, id)).ifPresent(h -> m.set(h, Math.max(1, e.getValue())));
        }
        return m.toImmutable();
    }

    // ── слот «ench=…» в строке состояния SL ──

    public static String stateEnch(String state) {
        if (state == null) return "";
        for (String part : state.split(";")) if (part.startsWith("ench=")) return part.substring(5);
        return "";
    }

    public static String withEnch(String state, String ench) {
        StringBuilder sb = new StringBuilder();
        if (state != null) for (String part : state.split(";")) {
            if (part.isEmpty() || part.startsWith("ench=")) continue;
            if (sb.length() > 0) sb.append(';');
            sb.append(part);
        }
        if (ench != null && !ench.isEmpty()) { if (sb.length() > 0) sb.append(';'); sb.append("ench=").append(ench); }
        return sb.toString();
    }

    /** Временный «предмет в руке» с этими чарами — чтобы ванильные правила (добыча, урон, огонь) их увидели. */
    public static ItemStack weapon(String ench) {
        ItemStack st = new ItemStack(Items.TRIAL_KEY);
        ItemEnchantments en = fromString(ench);
        if (!en.isEmpty()) st.set(DataComponents.ENCHANTMENTS, en);
        return st;
    }

    public static int level(String ench, String name) {
        Integer v = parse(ench).get(name);
        return v == null ? 0 : v;
    }
}
