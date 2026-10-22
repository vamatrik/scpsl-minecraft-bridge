using UnityEngine;

namespace SlBridge
{
    /// <summary>
    /// Перевод координат. Unity левосторонняя, Minecraft правосторонняя:
    /// ось X отражаем, Y и Z совпадают. При этом углы поворота (yaw/pitch) у обеих игр совпадают по смыслу.
    /// </summary>
    public static class CoordMap
    {
        public static Vector3 Origin;

        public static Vector3 McToSl(double x, double y, double z)
            => new Vector3(Origin.x - (float)x, Origin.y + (float)y, Origin.z + (float)z);

        public static Vector3 SlToMc(Vector3 u)
            => new Vector3(Origin.x - u.x, u.y - Origin.y, u.z - Origin.z);

        public static Vector3 DirSlToMc(Vector3 f) => new Vector3(-f.x, f.y, f.z);

        /// <summary>Направление взгляда Unity → yaw/pitch Minecraft (градусы).</summary>
        public static void DirToMcAngles(Vector3 unityForward, out float yaw, out float pitch)
        {
            Vector3 d = DirSlToMc(unityForward).normalized;
            yaw = Mathf.Atan2(-d.x, d.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Бокс Minecraft [min, max] → центр и размер в Unity.</summary>
        public static void McBoxToSl(float x0, float y0, float z0, float x1, float y1, float z1, out Vector3 center, out Vector3 size)
        {
            center = McToSl((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5);
            size = new Vector3(x1 - x0, y1 - y0, z1 - z0);
        }
    }
}
