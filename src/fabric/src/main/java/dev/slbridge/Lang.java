package dev.slbridge;

import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Язык мода (русский/английский). Выбирается в лаунчере: он пишет «ru» или «en» в
 * %APPDATA%\SlBridge\language.txt — этот же файл читает и плагин SCP:SL.
 */
public final class Lang {
    private static volatile boolean en;
    private static long checkedAt;

    private Lang() {}

    public static String t(String ru, String enText) { refresh(); return en ? enText : ru; }

    public static boolean en() { refresh(); return en; }

    /** Перечитываем файл не чаще раза в 5 секунд — язык можно сменить, не перезапуская Minecraft. */
    private static void refresh() {
        long now = System.currentTimeMillis();
        if (now - checkedAt < 5000) return;
        checkedAt = now;
        try {
            String appdata = System.getenv("APPDATA");
            if (appdata == null) return;
            Path f = Path.of(appdata, "SlBridge", "language.txt");
            if (Files.exists(f)) en = Files.readString(f).trim().toLowerCase().startsWith("en");
        } catch (Exception ignored) { }
    }
}
