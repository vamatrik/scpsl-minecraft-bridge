package dev.slbridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import net.minecraft.core.registries.BuiltInRegistries;
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
        return o;
    }

    public static JsonArray toJson(List<ItemStack> stacks) {
        JsonArray a = new JsonArray();
        for (ItemStack s : stacks) if (!s.isEmpty()) a.add(toJson(s));
        return a;
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
        return new ItemStack(it, Math.max(1, o.get("count").getAsInt()));
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
