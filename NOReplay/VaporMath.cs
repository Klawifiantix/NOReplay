using System;

namespace NOReplay
{
    /// <summary>
    /// Wing condensation and engine contrails, matching Assembly-CSharp VaporEffect / VaporEmitter.
    /// AoA is TargetCalc.GetAngleOnAxis(forward, velocity, right). Wing vapor shows when
    /// displayDetail &gt; 0.5 and |AoA| exceeds alphaThresholdVelocity(airspeed). Contrails show
    /// when altitude is between the emitter's min and max (prefab default 7500–12500 m).
    /// Aircraft.gForce is |Δv| / (dt · 9.81) from the same smoothed velocity samples.
    /// Replay also forces the effect off while paused, below the emitter min speed, or on the ground.
    /// </summary>
    public static class VaporMath
    {
        public const float DetailCutoff = 0.5f;
        public const float DefaultContrailMin = 7500f;
        public const float DefaultContrailMax = 12500f;
        public const float DefaultMinSpeed = 20f;

        public static float SpeedOfSound(float altitude)
        {
            float v = -0.005f * altitude + 340f;
            return v > 290f ? v : 290f;
        }

        /// <summary>
        /// Aircraft.LocalSimFixedUpdate: accel = (velocity - velocityPrev) / (dt * 9.81), gForce = |accel|.
        /// </summary>
        public static float GLoad(float vx, float vy, float vz, float px, float py, float pz, float dt)
        {
            if (dt < 1e-4f) return 0f;
            float ax = (vx - px) / dt;
            float ay = (vy - py) / dt;
            float az = (vz - pz) / dt;
            return MathF.Sqrt(ax * ax + ay * ay + az * az) / 9.81f;
        }

        /// <summary>TargetCalc.GetAngleOnAxis. Degrees, signed around axis.</summary>
        public static float AngleOnAxis(
            float sx, float sy, float sz,
            float ox, float oy, float oz,
            float ax, float ay, float az)
        {
            float fx = ay * sz - az * sy;
            float fy = az * sx - ax * sz;
            float fz = ax * sy - ay * sx;
            float tx = ay * oz - az * oy;
            float ty = az * ox - ax * oz;
            float tz = ax * oy - ay * ox;
            return SignedAngle(fx, fy, fz, tx, ty, tz, ax, ay, az);
        }

        public static float SignedAngle(
            float fx, float fy, float fz,
            float tx, float ty, float tz,
            float ax, float ay, float az)
        {
            float f2 = fx * fx + fy * fy + fz * fz;
            float t2 = tx * tx + ty * ty + tz * tz;
            float denom = MathF.Sqrt(f2 * t2);
            if (denom < 1e-8f) return 0f;
            float dot = fx * tx + fy * ty + fz * tz;
            float cos = dot / denom;
            if (cos > 1f) cos = 1f;
            else if (cos < -1f) cos = -1f;
            float deg = MathF.Acos(cos) * (180f / MathF.PI);
            float cx = fy * tz - fz * ty;
            float cy = fz * tx - fx * tz;
            float cz = fx * ty - fy * tx;
            float side = ax * cx + ay * cy + az * cz;
            if (side < 0f) deg = -deg;
            return deg;
        }

        public static bool WingCondensation(
            float alphaDeg, float thresholdDeg, float detail,
            float airspeed, float minSpeed, bool paused, bool onGround)
        {
            if (paused || onGround) return false;
            if (!(airspeed >= minSpeed)) return false;
            if (!(detail > DetailCutoff)) return false;
            if (float.IsNaN(thresholdDeg) || float.IsNaN(alphaDeg)) return false;
            return MathF.Abs(alphaDeg) > thresholdDeg;
        }

        public static bool EngineContrail(
            float altitude, float minAlt, float maxAlt,
            float airspeed, float minSpeed, bool paused, bool onGround)
        {
            if (paused || onGround) return false;
            if (!(airspeed >= minSpeed)) return false;
            return altitude > minAlt && altitude < maxAlt;
        }

        /// <summary>VaporEmitter opacity slew: 1 when active, -0.5 when not, at most dt per step.</summary>
        public static float StepOpacity(float current, bool active, float dt)
        {
            if (dt < 0f) dt = 0f;
            float target = active ? 1f : -0.5f;
            float d = target - current;
            if (d > dt) d = dt;
            else if (d < -dt) d = -dt;
            return current + d;
        }

        public static bool FadeStillVisible(bool active, float opacity)
        {
            return active || opacity > -0.5f;
        }

        public static float EmitRate(float emitFrequency, float airspeed, float minSpeed)
        {
            if (emitFrequency <= 0f || !(airspeed > 0f)) return 0f;
            float scaled = minSpeed > 0.01f ? airspeed / minSpeed * emitFrequency : emitFrequency;
            return scaled < emitFrequency ? scaled : emitFrequency;
        }
    }
}
