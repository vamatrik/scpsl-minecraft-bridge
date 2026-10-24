package dev.slbridge.mixin;

import dev.slbridge.Fun;
import dev.slbridge.SlBridgeClient;
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.server.PlayerAdvancements;
import net.minecraft.server.level.ServerPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Достижение Minecraft получено — показать его подсказкой в SL (чат и всплывашки Minecraft в мосте не видны). */
@Mixin(PlayerAdvancements.class)
public abstract class AdvancementMixin {
    @Shadow private ServerPlayer player;

    @Inject(method = "award", at = @At("RETURN"))
    private void slbridge$award(AdvancementHolder holder, String criterion, CallbackInfoReturnable<Boolean> cir) {
        if (!cir.getReturnValue() || !SlBridgeClient.isBridgeActive()) return;
        PlayerAdvancements self = (PlayerAdvancements) (Object) this;
        if (!self.getOrStartProgress(holder).isDone()) return;
        holder.value().display().ifPresent(d -> {
            if (d.announceToChat() || d.showToast()) Fun.advancement(player, d.title().getString(), d.type().getSerializedName());
        });
    }
}
