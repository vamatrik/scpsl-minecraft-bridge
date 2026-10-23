package dev.slbridge.mixin;

import dev.slbridge.SlBridgeClient;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.options.OptionsScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Настройки Minecraft, открытые кнопкой из SL: Esc на главном экране настроек их не закрывает
 * (закрыть — той же кнопкой в SL или «Готово»). Esc в подразделах по-прежнему возвращает назад.
 */
@Mixin(Screen.class)
public abstract class OptionsEscMixin {
    @Inject(method = "shouldCloseOnEsc", at = @At("HEAD"), cancellable = true)
    private void slbridge$noEsc(CallbackInfoReturnable<Boolean> cir) {
        if ((Object) this instanceof OptionsScreen && SlBridgeClient.isBridgeActive() && SlBridgeClient.optionsSessionActive())
            cir.setReturnValue(false);
    }
}
