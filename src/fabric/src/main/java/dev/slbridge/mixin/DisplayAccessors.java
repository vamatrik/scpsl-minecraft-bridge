package dev.slbridge.mixin;

import net.minecraft.world.entity.Display;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

/** Доступ к закрытым сеттерам «дисплеев» — ими рисуется рентген (SCP-1344): руды, подсвеченные сквозь стены. */
public final class DisplayAccessors {
    private DisplayAccessors() {}

    @Mixin(Display.class)
    public interface Base {
        @Invoker("setGlowColorOverride") void slbridge$setGlowColorOverride(int color);
        @Invoker("setTransformation") void slbridge$setTransformation(com.mojang.math.Transformation t);
        @Invoker("setViewRange") void slbridge$setViewRange(float range);
    }

    @Mixin(Display.BlockDisplay.class)
    public interface Block {
        @Invoker("setBlockState") void slbridge$setBlockState(BlockState state);
    }
}
