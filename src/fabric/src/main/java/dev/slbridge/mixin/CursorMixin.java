package dev.slbridge.mixin;

import dev.slbridge.SlBridgeClient;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Курсор в окнах Minecraft (верстак, инвентарь, сундук): системный курсор там не виден (его прячет SCP:SL),
 * поэтому рисуем стрелку прямо в интерфейсе Minecraft — поверх всего, в точке мыши.
 */
@Mixin(Screen.class)
public abstract class CursorMixin {
    @Inject(method = "extractRenderStateWithTooltipAndSubtitles", at = @At("RETURN"))
    private void slbridge$cursor(GuiGraphicsExtractor g, int mx, int my, float pt, CallbackInfo ci) {
        if (!SlBridgeClient.isBridgeActive() || !SlBridgeClient.config.drawCursor) return;
        g.nextStratum();
        final int black = 0xFF000000, white = 0xFFFFFFFF;
        // стрелка 12 строк: треугольник с хвостиком, чёрная обводка и белая заливка
        for (int y = 0; y < 11; y++) {
            int w = Math.min(y + 1, 8);
            if (y >= 8) w = 0;
            if (w > 0) {
                g.fill(mx, my + y, mx + w, my + y + 1, black);
                if (y >= 2 && w > 2) g.fill(mx + 1, my + y, mx + w - 1, my + y + 1, white);
            }
        }
        g.fill(mx, my + 7, mx + 8, my + 8, black);          // низ треугольника
        g.fill(mx + 3, my + 8, mx + 6, my + 12, black);      // хвостик
        g.fill(mx + 4, my + 8, mx + 5, my + 11, white);
    }
}
