package dev.slbridge;

/** Последняя поза камеры SCP:SL в координатах Minecraft (позиция глаз + углы в градусах MC). */
public final class Pose {
    public static volatile boolean active;
    public static volatile double x, y, z;
    public static volatile float yaw, pitch;
    /** Высота НОГ игрока SL (низ капсулы) — тело Minecraft ставим ровно туда, а не «глаза − 1.62». */
    public static volatile double feetY = Double.NaN;
    public static volatile long lastUpdateNanos;
    /** Номер телепорта: позы со старым номером (до телепорта) игнорируются. */
    public static volatile int minSeq;
    /** Номер последней принятой позы. */
    public static volatile int lastSeq;
    /** Свет от лампы/фонарика в руке SL (0 — нет, иначе уровень 1..15). */
    public static volatile int lamp;
    /** true — фонарик (узкий луч), false — лампа (свет вокруг). */
    public static volatile boolean beam;

    public static void set(double nx, double ny, double nz, float nyaw, float npitch, int seq, double nfeet) {
        if (seq < minSeq) return;
        lastSeq = seq;
        feetY = Double.isNaN(nfeet) ? ny - SlBridgeClient.EYE_HEIGHT : nfeet;
        x = nx; y = ny; z = nz; yaw = nyaw; pitch = npitch;
        lastUpdateNanos = System.nanoTime();
        active = true;
    }

    /** Телепорт, начатый Minecraft (портал, жемчуг, команда): камера сразу туда, старые позы SL не принимаем. */
    public static void teleport(double nx, double ny, double nz, int seq) {
        minSeq = seq;
        x = nx; y = ny; z = nz;
        feetY = ny - SlBridgeClient.EYE_HEIGHT;
    }

    private Pose() {}
}
