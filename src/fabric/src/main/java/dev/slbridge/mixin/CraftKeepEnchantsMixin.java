package dev.slbridge.mixin;

import dev.slbridge.ItemJson;
import net.minecraft.core.component.DataComponents;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.AbstractContainerMenu;
import net.minecraft.world.inventory.CraftingContainer;
import net.minecraft.world.inventory.CraftingMenu;
import net.minecraft.world.inventory.ResultContainer;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.crafting.CraftingRecipe;
import net.minecraft.world.item.crafting.RecipeHolder;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Перезарядка на верстаке (Micro HID + редстоун, оружие + патроны…): результат — новый предмет SL того же типа.
 * Чары, имя с наковальни и цена ремонта переходят на него с исходного предмета, иначе подзарядка их стирала.
 * Работает и для верстака, и для сетки 2×2 в инвентаре (InventoryMenu вызывает тот же метод).
 */
@Mixin(CraftingMenu.class)
public abstract class CraftKeepEnchantsMixin {
    @Inject(method = "slotChangedCraftingGrid", at = @At("TAIL"))
    private static void slbridge$keepEnchants(AbstractContainerMenu menu, ServerLevel level, Player player, CraftingContainer grid,
                                              ResultContainer results, RecipeHolder<CraftingRecipe> hint, CallbackInfo ci) {
        ItemStack res = results.getItem(0);
        String sl = ItemJson.slKey(res);
        if (sl == null) return;
        for (int i = 0; i < grid.getContainerSize(); i++) {
            ItemStack in = grid.getItem(i);
            if (in.isEmpty() || !sl.equals(ItemJson.slKey(in))) continue;
            var en = in.get(DataComponents.ENCHANTMENTS);
            boolean changed = false;
            if (en != null && !en.isEmpty()) { res.set(DataComponents.ENCHANTMENTS, en); changed = true; }
            Integer cost = in.get(DataComponents.REPAIR_COST);
            if (cost != null && cost > 0) { res.set(DataComponents.REPAIR_COST, cost); changed = true; }
            if (in.has(DataComponents.CUSTOM_NAME)) { res.set(DataComponents.CUSTOM_NAME, in.get(DataComponents.CUSTOM_NAME)); changed = true; }
            if (changed) {
                results.setItem(0, res);
                if (player instanceof net.minecraft.server.level.ServerPlayer sp)
                    sp.connection.send(new net.minecraft.network.protocol.game.ClientboundContainerSetSlotPacket(menu.containerId, menu.incrementStateId(), 0, res));
            }
            return;
        }
    }
}
