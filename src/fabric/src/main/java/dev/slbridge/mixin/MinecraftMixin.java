package dev.slbridge.mixin;

import dev.slbridge.SlBridgeClient;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Без рамки выделения блока под прицелом — прицел у SCP:SL свой. */
@Mixin(Minecraft.class)
public abstract class MinecraftMixin {
    /** Пока открыто окно Minecraft для моста, заголовок окна — метка для оверлея; Minecraft сбрасывает его при смене экрана. */
    @Inject(method = "updateTitle", at = @At("HEAD"), cancellable = true)
    private void slbridge$keepUiTitle(CallbackInfo ci) {
        if (SlBridgeClient.isBridgeActive() && SlBridgeClient.uiMode) {
            ((Minecraft) (Object) this).getWindow().setTitle(SlBridgeClient.UI_TITLE);
            ci.cancel();
        }
    }

    @Inject(method = "pick", at = @At("HEAD"), cancellable = true)
    private void slbridge$noPick(float partialTicks, CallbackInfo ci) {
        if (SlBridgeClient.isBridgeActive()) {
            ((Minecraft) (Object) this).hitResult = null;
            ((Minecraft) (Object) this).crosshairPickEntity = null;
            ci.cancel();
        }
    }
}
