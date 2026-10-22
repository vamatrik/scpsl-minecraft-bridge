package dev.slbridge.mixin;

import dev.slbridge.Pose;
import dev.slbridge.SlBridgeClient;
import net.minecraft.client.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Каждый кадр ставим камеру Minecraft туда, куда смотрит камера SCP:SL. */
@Mixin(Camera.class)
public abstract class CameraMixin {
    @Shadow protected abstract void setPosition(double x, double y, double z);
    @Shadow protected abstract void setRotation(float yRot, float xRot);

    @Inject(method = "alignWithEntity", at = @At("TAIL"))
    private void slbridge$followSl(float partialTicks, CallbackInfo ci) {
        if (!SlBridgeClient.isBridgeActive()) return;
        setRotation(Pose.yaw, Pose.pitch);
        setPosition(Pose.x, Pose.y, Pose.z);
    }

    @Inject(method = "calculateFov", at = @At("RETURN"), cancellable = true)
    private void slbridge$fov(float partialTicks, CallbackInfoReturnable<Float> cir) {
        if (SlBridgeClient.isBridgeActive()) cir.setReturnValue(SlBridgeClient.config.fov);
    }
}
