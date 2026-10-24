package dev.slbridge.mixin;

import dev.slbridge.ItemJson;
import dev.slbridge.SlEnchants;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.enchantment.Enchantment;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Предметы SCP:SL (ключ испытаний с меткой slItem): какие чары на них можно наложить — решает SlEnchants по типу
 * предмета, а не ванильные теги. Работает и для стола зачарований (isPrimaryItem), и для книг на наковальне (canEnchant).
 */
@Mixin(Enchantment.class)
public abstract class EnchantMixin {
    private Boolean slbridge$decide(ItemStack stack) {
        String sl = ItemJson.slKey(stack);
        if (sl == null) return null;
        String n = SlEnchants.name((Enchantment) (Object) this);
        return SlEnchants.allowed(sl, n);
    }

    @Inject(method = "isSupportedItem", at = @At("HEAD"), cancellable = true)
    private void slbridge$supported(ItemStack stack, CallbackInfoReturnable<Boolean> cir) {
        Boolean d = slbridge$decide(stack);
        if (d != null) cir.setReturnValue(d);
    }

    @Inject(method = "isPrimaryItem", at = @At("HEAD"), cancellable = true)
    private void slbridge$primary(ItemStack stack, CallbackInfoReturnable<Boolean> cir) {
        Boolean d = slbridge$decide(stack);
        if (d != null) cir.setReturnValue(d);
    }

    @Inject(method = "canEnchant", at = @At("HEAD"), cancellable = true)
    private void slbridge$can(ItemStack stack, CallbackInfoReturnable<Boolean> cir) {
        Boolean d = slbridge$decide(stack);
        if (d != null) cir.setReturnValue(d);
    }
}
