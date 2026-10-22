package dev.slbridge.mixin;

import dev.slbridge.SlBridgeClient;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Интерфейс Minecraft (хотбар, сердца, прицел, чат) целиком прячем — интерфейс рисует SCP:SL. */
@Mixin(Hud.class)
public abstract class HudMixin {
    @Inject(method = "isHidden", at = @At("HEAD"), cancellable = true)
    private void slbridge$hide(CallbackInfoReturnable<Boolean> cir) {
        if (SlBridgeClient.isBridgeActive()) cir.setReturnValue(true);
    }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void slbridge$noHud(GuiGraphicsExtractor g, DeltaTracker d, CallbackInfo ci) {
        if (SlBridgeClient.isBridgeActive()) ci.cancel();
    }
}
