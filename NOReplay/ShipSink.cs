using System;

namespace NOReplay
{
    /// <summary>
    /// Deterministic hull motion for a ship whose recording ends at the kill.
    /// Pose is a function of seconds since removal, so pause, reverse, speed
    /// and seek all land on the same place. This is not the game's buoyancy
    /// job. A recording that keeps samples after Sinking=1 plays those instead.
    /// </summary>
    public static class ShipSink
    {
        /// <summary>Seconds from the kill until the hull is hidden. Long enough to slip under, short of a carrier's real despawn.</summary>
        public const float Duration = 28f;

        /// <summary>How far under the sea the origin is driven, in metres.</summary>
        public const float Depth = 24f;

        /// <summary>Extra starboard list, degrees, blended in across <see cref="Duration"/>.</summary>
        public const float ListRollDegrees = 18f;

        /// <summary>Extra bow-down pitch, degrees, in the playback euler convention (positive pitch is nose up).</summary>
        public const float BowDownDegrees = 8f;

        /// <summary>
        /// A post-kill track shorter than this is treated as "no sink recording".
        /// One sample in the same frame as the kill must still use the scripted curve.
        /// </summary>
        public const float MinRecordedSeconds = 0.5f;

        public readonly struct Pose
        {
            public readonly float X, Y, Z;
            public readonly float Qx, Qy, Qz, Qw;

            public Pose(float x, float y, float z, float qx, float qy, float qz, float qw)
            {
                X = x;
                Y = y;
                Z = z;
                Qx = qx;
                Qy = qy;
                Qz = qz;
                Qw = qw;
            }
        }

        public static float Progress(float secondsSinceRemoval)
        {
            if (secondsSinceRemoval <= 0f) return 0f;
            if (secondsSinceRemoval >= Duration) return 1f;
            float u = secondsSinceRemoval / Duration;
            return u * u * (3f - 2f * u);
        }

        /// <summary>
        /// Height of the origin. Already deeper than the target stays put, so a hull that died underwater is not lifted.
        /// </summary>
        public static float Height(float startY, float seaY, float secondsSinceRemoval)
        {
            float target = seaY - Depth;
            if (target >= startY) return startY;
            return startY + (target - startY) * Progress(secondsSinceRemoval);
        }

        /// <summary>
        /// Seconds until the origin crosses the sea, or -1 when it does not cross during the curve.
        /// </summary>
        public static float CrossSeconds(float startY, float seaY)
        {
            if (startY <= seaY) return -1f;
            if (Height(startY, seaY, Duration) > seaY) return -1f;
            float lo = 0f;
            float hi = Duration;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Height(startY, seaY, mid) > seaY) lo = mid;
                else hi = mid;
            }
            return hi;
        }

        public static bool HasRecordedSink(float killedAt, float lastSampleTime)
        {
            return killedAt >= 0f && lastSampleTime > killedAt + MinRecordedSeconds;
        }

        /// <summary>
        /// <paramref name="havePrev"/> continues the roll or pitch from the last two samples for a few seconds, then lets it die away.
        /// The list and bow-down are added in the hull's local axes. At zero seconds the pose is the last sample.
        /// </summary>
        public static Pose At(
            float x, float y, float z,
            float qx, float qy, float qz, float qw,
            float seaY,
            float secondsSinceRemoval,
            bool havePrev,
            float px, float py, float pz, float pw,
            float prevDt)
        {
            Normalize(ref qx, ref qy, ref qz, ref qw);
            float spinX = 0f, spinY = 0f, spinZ = 0f, spinW = 1f;
            if (havePrev && secondsSinceRemoval > 0f && prevDt >= 0.05f && prevDt <= 2.5f)
            {
                Normalize(ref px, ref py, ref pz, ref pw);
                Inverse(px, py, pz, pw, out float ix, out float iy, out float iz, out float iw);
                Mul(ix, iy, iz, iw, qx, qy, qz, qw, out float dx, out float dy, out float dz, out float dw);
                float horizon = secondsSinceRemoval < 3f ? secondsSinceRemoval : 3f;
                float fade = MathF.Exp(-secondsSinceRemoval / 4f);
                float steps = horizon / prevDt;
                if (steps > 2f) steps = 2f;
                steps *= fade;
                OrientationMath.Slerp(0f, 0f, 0f, 1f, dx, dy, dz, dw, steps,
                    out spinX, out spinY, out spinZ, out spinW);
            }
            Mul(qx, qy, qz, qw, spinX, spinY, spinZ, spinW, out float sx, out float sy, out float sz, out float sw);

            OrientationMath.FromPlaybackEuler(-BowDownDegrees, 0f, ListRollDegrees,
                out float lx, out float ly, out float lz, out float lw);
            float list = Progress(secondsSinceRemoval);
            OrientationMath.Slerp(0f, 0f, 0f, 1f, lx, ly, lz, lw, list,
                out float ux, out float uy, out float uz, out float uw);
            Mul(sx, sy, sz, sw, ux, uy, uz, uw, out float rx, out float ry, out float rz, out float rw);
            Normalize(ref rx, ref ry, ref rz, ref rw);
            return new Pose(x, Height(y, seaY, secondsSinceRemoval), z, rx, ry, rz, rw);
        }

        static void Mul(
            float ax, float ay, float az, float aw,
            float bx, float by, float bz, float bw,
            out float x, out float y, out float z, out float w)
        {
            x = aw * bx + ax * bw + ay * bz - az * by;
            y = aw * by - ax * bz + ay * bw + az * bx;
            z = aw * bz + ax * by - ay * bx + az * bw;
            w = aw * bw - ax * bx - ay * by - az * bz;
        }

        static void Inverse(float x, float y, float z, float w, out float ix, out float iy, out float iz, out float iw)
        {
            ix = -x;
            iy = -y;
            iz = -z;
            iw = w;
        }

        static void Normalize(ref float x, ref float y, ref float z, ref float w)
        {
            OrientationMath.Normalize(ref x, ref y, ref z, ref w);
        }
    }
}
