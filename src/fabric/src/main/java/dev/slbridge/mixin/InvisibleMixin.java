package dev.slbridge.mixin;

import dev.slbridge.Pose;
import dev.slbridge.SlBridgeClient;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** SCP-268: пока игрок SL невидим, мобы Minecraft не могут взять его в цель. */
@Mixin(Mob.class)
public abstract class InvisibleMixin {
    @Inject(method = "setTarget", at = @At("HEAD"), cancellable = true)
    private void slbridge$noTarget(LivingEntity target, CallbackInfo ci) {
        if (target instanceof ServerPlayer && Pose.invisible && SlBridgeClient.isBridgeActive()) { ci.cancel(); return; }
        // эндермен впервые взял игрока в цель (посмотрел ему в глаза или получил удар) — крик SCP-096 в SL
        Mob self = (Mob) (Object) this;
        if (self instanceof net.minecraft.world.entity.monster.Enderman && target instanceof ServerPlayer
                && self.getTarget() != target && SlBridgeClient.isBridgeActive()) dev.slbridge.Fun.endermanAngry();
    }
}
