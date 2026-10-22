package dev.slbridge.mixin;

import dev.slbridge.SlBridgeClient;
import net.minecraft.client.KeyboardHandler;
import net.minecraft.client.MouseHandler;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Пока мост активен, Minecraft не забирает мышь и не реагирует на ввод:
 * управление целиком у SCP:SL, а Minecraft только показывает мир.
 */
public final class InputMixin {
    private InputMixin() {}

    @Mixin(MouseHandler.class)
    public abstract static class Mouse {
        @Inject(method = "grabMouse", at = @At("HEAD"), cancellable = true)
        private void slbridge$noGrab(CallbackInfo ci) { if (SlBridgeClient.isInputBlocked()) ci.cancel(); }

        @Inject(method = "onButton", at = @At("HEAD"), cancellable = true)
        private void slbridge$noButton(CallbackInfo ci) { if (SlBridgeClient.isInputBlocked()) ci.cancel(); }

        @Inject(method = "onScroll", at = @At("HEAD"), cancellable = true)
        private void slbridge$noScroll(CallbackInfo ci) { if (SlBridgeClient.isInputBlocked()) ci.cancel(); }

        @Inject(method = "onMove", at = @At("HEAD"), cancellable = true)
        private void slbridge$noMove(CallbackInfo ci) { if (SlBridgeClient.isInputBlocked()) ci.cancel(); }
    }

    @Mixin(KeyboardHandler.class)
    public abstract static class Keyboard {
        @Inject(method = "keyPress", at = @At("HEAD"), cancellable = true)
        private void slbridge$noKeys(CallbackInfo ci) { if (SlBridgeClient.isInputBlocked()) ci.cancel(); }
    }
}
