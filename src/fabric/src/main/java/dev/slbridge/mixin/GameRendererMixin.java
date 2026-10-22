package dev.slbridge.mixin;

import dev.slbridge.SlBridgeClient;
import net.minecraft.client.renderer.GameRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Руки и предмет в руке Minecraft не рисуем — в руках оружие SCP:SL. */
@Mixin(GameRenderer.class)
public abstract class GameRendererMixin {
    @Inject(method = "renderItemInHand", at = @At("HEAD"), cancellable = true)
    private void slbridge$noHand(CallbackInfo ci) {
        if (SlBridgeClient.isBridgeActive()) ci.cancel();
    }
}
