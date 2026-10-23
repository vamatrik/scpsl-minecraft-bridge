package dev.slbridge.mixin;

import dev.slbridge.SlBridgeClient;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Интерфейс Minecraft (хотбар, сердца, прицел, чат) целиком прячем — интерфейс рисует SCP:SL.
 * Исключение — экран отладки (F3): пока он рисуется, интерфейс «не скрыт».
 */
@Mixin(Hud.class)
public abstract class HudMixin {
    @Unique private static boolean slbridge$drawingDebug;

    @Inject(method = "isHidden", at = @At("HEAD"), cancellable = true)
    private void slbridge$hide(CallbackInfoReturnable<Boolean> cir) {
        if (SlBridgeClient.isBridgeActive() && !slbridge$drawingDebug) cir.setReturnValue(true);
    }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void slbridge$noHud(GuiGraphicsExtractor g, DeltaTracker d, CallbackInfo ci) {
        if (SlBridgeClient.isBridgeActive()) ci.cancel();
    }

    @Inject(method = "extractDebugOverlay", at = @At("HEAD"))
    private void slbridge$debugStart(GuiGraphicsExtractor g, CallbackInfo ci) { slbridge$drawingDebug = true; }

    @Inject(method = "extractDebugOverlay", at = @At("RETURN"))
    private void slbridge$debugEnd(GuiGraphicsExtractor g, CallbackInfo ci) { slbridge$drawingDebug = false; }
}
