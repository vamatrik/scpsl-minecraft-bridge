package dev.slbridge.mixin;

import dev.slbridge.CollisionStreamer;
import dev.slbridge.SlBridgeClient;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

public final class PlayerMixins {
    private PlayerMixins() {}

    /** Позицию игрока задаёт сервер (по позе SL) — клиент не шлёт свою, иначе «moved wrongly» и откаты. */
    @Mixin(LocalPlayer.class)
    public abstract static class NoSendPosition {
        @Inject(method = "sendPosition", at = @At("HEAD"), cancellable = true)
        private void slbridge$noSend(CallbackInfo ci) { if (SlBridgeClient.isBridgeActive()) ci.cancel(); }
    }

    /** Игрок Minecraft не подбирает предметы сам — их подбирают в SL на E. */
    @Mixin(ItemEntity.class)
    public abstract static class NoAutoPickup {
        @Inject(method = "playerTouch", at = @At("HEAD"), cancellable = true)
        private void slbridge$noTouch(Player p, CallbackInfo ci) { if (SlBridgeClient.isBridgeActive()) ci.cancel(); }
    }

    /** Изменился блок — пересчитать коллизии только его ячейки. */
    @Mixin(ClientLevel.class)
    public abstract static class BlockChanged {
        @Inject(method = "sendBlockUpdated", at = @At("HEAD"))
        private void slbridge$changed(BlockPos pos, BlockState oldState, BlockState newState, int flags, CallbackInfo ci) {
            CollisionStreamer.markDirty(pos);
        }
    }
}
