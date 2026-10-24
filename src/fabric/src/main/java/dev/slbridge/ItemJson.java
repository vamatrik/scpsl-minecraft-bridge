package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.resources.Identifier;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;

import java.util.ArrayList;
import java.util.List;

/** Перевод предметов Minecraft ⇄ JSON протокола: {id, count, name, max}. */
public final class ItemJson {
    private ItemJson() {}

    public static JsonObject toJson(ItemStack s) {
        JsonObject o = new JsonObject();
        o.addProperty("id", BuiltInRegistries.ITEM.getKey(s.getItem()).toString());
        o.addProperty("count", s.getCount());
        o.addProperty("name", s.getHoverName().getString());
        o.addProperty("max", s.getMaxStackSize());
        String sl = slKey(s);
        if (sl == null) {
            // обычный предмет Minecraft с «особенностями» (книга с чарами, зачарованный меч, зелье, имя, износ) —
            // переносим все изменённые компоненты, чтобы в инвентаре SL они не терялись
            String comp = encodeComponents(s);
            if (comp != null) o.addProperty("comp", comp);
            // в названии карты SL — какие чары (у книги — запасённые)
            var en = s.getOrDefault(DataComponents.STORED_ENCHANTMENTS, net.minecraft.world.item.enchantment.ItemEnchantments.EMPTY);
            if (en.isEmpty()) en = s.getOrDefault(DataComponents.ENCHANTMENTS, net.minecraft.world.item.enchantment.ItemEnchantments.EMPTY);
            if (!en.isEmpty()) {
                List<String> parts = new ArrayList<>();
                for (var e : en.entrySet()) parts.add(net.minecraft.world.item.enchantment.Enchantment.getFullname(e.getKey(), e.getIntValue()).getString());
                o.addProperty("name", s.getHoverName().getString() + " (" + String.join(", ", parts) + ")");
            }
        }
        if (sl != null) {
            o.addProperty("sl", sl);
            CompoundTag tag = s.get(DataComponents.CUSTOM_DATA).copyTag();
            int ammo = tag.getIntOr("slAmmo", 0);
            if (ammo > 0) o.addProperty("slAmmo", ammo);
            // чары (стол зачарований, книга на наковальне) — в состояние предмета SL
            String state = SlEnchants.withEnch(tag.getStringOr("slState", ""),
                    SlEnchants.toString(s.getOrDefault(DataComponents.ENCHANTMENTS, net.minecraft.world.item.enchantment.ItemEnchantments.EMPTY)));
            if (!state.isEmpty()) o.addProperty("slState", state); // заряд, патроны в магазине, обвесы
            Identifier model = s.get(DataComponents.ITEM_MODEL);
            if (model != null) o.addProperty("model", model.toString());
        }
        return o;
    }

    public static JsonArray toJson(List<ItemStack> stacks) {
        JsonArray a = new JsonArray();
        for (ItemStack s : stacks) if (!s.isEmpty()) a.add(toJson(s));
        return a;
    }

    /** Ключ предмета SCP:SL (ItemType), если это результат SL-рецепта. */
    public static String slKey(ItemStack s) {
        CustomData cd = s.get(DataComponents.CUSTOM_DATA);
        if (cd == null) return null;
        return cd.copyTag().getString("slItem").orElse(null);
    }

    public static Item item(String id) {
        Identifier rl = Identifier.tryParse(id);
        if (rl == null) return net.minecraft.world.item.Items.AIR;
        return BuiltInRegistries.ITEM.getValue(rl);
    }

    public static ItemStack fromJson(JsonElement e) {
        if (e == null || e.isJsonNull()) return ItemStack.EMPTY;
        JsonObject o = e.getAsJsonObject();
        Item it = item(o.get("id").getAsString());
        if (it == net.minecraft.world.item.Items.AIR) return ItemStack.EMPTY;
        ItemStack st = new ItemStack(it, Math.max(1, o.get("count").getAsInt()));
        if (o.has("comp") && !o.has("sl")) applyComponents(st, o.get("comp").getAsString());
        if (o.has("sl")) {
            // предмет SCP:SL, лежащий в мире Minecraft (не влез в инвентарь) — восстанавливаем метку
            CompoundTag tag = new CompoundTag();
            tag.putString("slItem", o.get("sl").getAsString());
            if (o.has("slState")) tag.putString("slState", o.get("slState").getAsString());
            if (o.has("slAmmo")) tag.putInt("slAmmo", o.get("slAmmo").getAsInt());
            st.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
            if (o.has("name")) st.set(DataComponents.ITEM_NAME, Component.literal(o.get("name").getAsString()));
            if (o.has("model")) { Identifier m = Identifier.tryParse(o.get("model").getAsString()); if (m != null) st.set(DataComponents.ITEM_MODEL, m); }
            st.set(DataComponents.ENCHANTMENT_GLINT_OVERRIDE, true);
            String ench = o.has("slState") ? SlEnchants.stateEnch(o.get("slState").getAsString()) : "";
            if (!ench.isEmpty()) st.set(DataComponents.ENCHANTMENTS, SlEnchants.fromString(ench));
            SlEnchants.ensureEnchantable(st);
        }
        return st;
    }

    /** Изменённые компоненты предмета (как в /give: чары, зелье, имя…) строкой JSON; null — предмет обычный. */
    public static String encodeComponents(ItemStack s) {
        var patch = s.getComponentsPatch();
        var regs = SlEnchants.regs;
        if (patch.isEmpty() || regs == null) return null;
        try {
            return net.minecraft.core.component.DataComponentPatch.CODEC
                    .encodeStart(regs.createSerializationContext(com.mojang.serialization.JsonOps.INSTANCE), patch)
                    .result().map(JsonElement::toString).orElse(null);
        } catch (Exception ex) { return null; }
    }

    public static void applyComponents(ItemStack st, String comp) {
        var regs = SlEnchants.regs;
        if (regs == null || comp == null || comp.isEmpty()) return;
        try {
            JsonElement j = com.google.gson.JsonParser.parseString(comp);
            net.minecraft.core.component.DataComponentPatch.CODEC
                    .parse(regs.createSerializationContext(com.mojang.serialization.JsonOps.INSTANCE), j)
                    .result().ifPresent(st::applyComponents);
        } catch (Exception ex) { SlBridgeClient.LOG.warn("[SLBridge] компоненты предмета: {}", ex.toString()); }
    }

    public static List<ItemStack> listFromJson(JsonElement e) {
        List<ItemStack> r = new ArrayList<>();
        if (e == null || !e.isJsonArray()) return r;
        for (JsonElement x : e.getAsJsonArray()) {
            ItemStack s = fromJson(x);
            if (!s.isEmpty()) r.add(s);
        }
        return r;
    }
}
