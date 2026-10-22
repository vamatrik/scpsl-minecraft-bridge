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
    @Inject(method = "pick", at = @At("HEAD"), cancellable = true)
    private void slbridge$noPick(float partialTicks, CallbackInfo ci) {
        if (SlBridgeClient.isBridgeActive()) {
            ((Minecraft) (Object) this).hitResult = null;
            ((Minecraft) (Object) this).crosshairPickEntity = null;
            ci.cancel();
        }
    }
}
