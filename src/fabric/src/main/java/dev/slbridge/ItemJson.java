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
        if (sl != null) {
            o.addProperty("sl", sl);
            CompoundTag tag = s.get(DataComponents.CUSTOM_DATA).copyTag();
            int ammo = tag.getIntOr("slAmmo", 0);
            if (ammo > 0) o.addProperty("slAmmo", ammo);
            String state = tag.getStringOr("slState", "");
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
        }
        return st;
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
