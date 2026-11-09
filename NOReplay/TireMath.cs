using System;

namespace NOReplay
{
    /// <summary>
    /// LandingGear.FixedUpdate decides dust, tire smoke and the two wheel clips
    /// from contact, surface and slip. Replay does not integrate wheel spin, so a
    /// touchdown is "airborne a moment ago and on the ground now": longitudinal
    /// slip is then the ground speed, which is what the game has before the tyre
    /// spins up. Established rolling uses no longitudinal slip. Lateral drift is
    /// the sideways part of the velocity. This is an approximation of wheelSpeed.
    /// </summary>
    public static class TireMath
    {
        public const float SmokeSlip = 20f;
        public const float DustSpeed = 2f;
        public const float RollSpeed = 1f;
        public const float SkidLateral = 6f;
        public const float SmokeLateral = 8f;
        public const float TouchdownAgl = 3f;
        public const float Lookback = 0.4f;

        /// <summary>LandingGear adds at most this much wheel speed per second.</summary>
        public const float SpinRate = 100f;

        /// <summary>A sample this far above the current height is still in the descent.</summary>
        public const float DescentStep = 0.2f;

        /// <summary>How far back the height trace is trusted when every sample is level.</summary>
        public const float SurfaceWindow = 1.6f;

        public readonly struct Result
        {
            public readonly bool Rolling;
            public readonly bool Skid;
            public readonly bool Smoke;
            public readonly bool Dust;
            public readonly float NoiseVolume;
            public readonly float NoisePitch;
            public readonly float SkidVolume;
            public readonly float SkidPitch;
            public readonly int SmokeCount;

            public Result(bool rolling, bool skid, bool smoke, bool dust,
                float noiseVolume, float noisePitch, float skidVolume, float skidPitch, int smokeCount)
            {
                Rolling = rolling;
                Skid = skid;
                Smoke = smoke;
                Dust = dust;
                NoiseVolume = noiseVolume;
                NoisePitch = noisePitch;
                SkidVolume = skidVolume;
                SkidPitch = skidPitch;
                SmokeCount = smokeCount;
            }
        }

        public static bool RecentAir(float aglNow, bool hasNow, float aglThen, bool hasThen)
        {
            if (!hasNow || !hasThen) return false;
            return aglThen > TouchdownAgl && aglNow <= TouchdownAgl;
        }

        /// <summary>
        /// Slip after the tyre has been on the surface for <paramref name="secondsOnSurface"/>.
        /// The wheel starts at rest and catches the ground speed at <see cref="SpinRate"/>.
        /// </summary>
        public static float SpinSlip(float groundSpeed, float secondsOnSurface)
        {
            float spun = MathF.Min(MathF.Abs(groundSpeed), SpinRate * MathF.Max(secondsOnSurface, 0f));
            float slip = MathF.Abs(groundSpeed) - spun;
            return groundSpeed < 0f ? -slip : slip;
        }

        public static bool TouchdownSlip(float groundSpeed, float secondsOnSurface)
        {
            return MathF.Abs(SpinSlip(groundSpeed, secondsOnSurface)) > SmokeSlip;
        }

        /// <summary>
        /// Time since the aircraft was still descending. A level trace, such as a
        /// rollout or a deck that is not the sea, returns the whole window so the
        /// tyre is treated as spun up. Rolling sound does not use this.
        /// </summary>
        public static float SecondsSinceDescent(float now, float yNow, float[] times, float[] ys, int n)
        {
            if (times == null || ys == null || n <= 0) return SurfaceWindow;
            int count = n;
            if (count > times.Length) count = times.Length;
            if (count > ys.Length) count = ys.Length;
            float crossed = -1f;
            float oldest = times[0];
            for (int i = 0; i < count; i++)
            {
                float dt = now - times[i];
                if (dt < 0.02f) continue;
                if (times[i] < oldest) oldest = times[i];
                float need = DescentStep * (dt / 0.2f);
                if (need < DescentStep) need = DescentStep;
                if (ys[i] > yNow + need && times[i] > crossed)
                    crossed = times[i];
            }
            if (crossed < 0f) return MathF.Max(0f, now - oldest);
            return MathF.Max(0f, now - crossed);
        }

        public static Result At(bool contact, bool tarmac, float groundSpeed, float lateral, bool recentAir, bool running, float replayDt)
        {
            if (!contact || !running)
                return new Result(false, false, false, false, 0f, 1f, 0f, 1f, 0);
            float slip = recentAir ? groundSpeed : 0f;
            bool smoke = MathF.Abs(slip) > SmokeSlip || MathF.Abs(lateral) > SmokeLateral;
            bool dust = !tarmac && MathF.Abs(groundSpeed) > DustSpeed;
            bool skid = smoke || MathF.Abs(lateral) > SkidLateral;
            bool rolling = MathF.Abs(groundSpeed) > RollSpeed;
            float noiseVol = rolling ? MathF.Min(MathF.Abs(groundSpeed) / 40f, 0.8f) : 0f;
            float noisePitch = 0.5f + MathF.Abs(groundSpeed) * 0.015f;
            float skidVol = 0f;
            if (skid)
            {
                float fromLat = MathF.Min(MathF.Abs(lateral) * 0.04f, 0.9f);
                float fromTouch = recentAir ? MathF.Min(MathF.Abs(groundSpeed) * 0.02f, 0.9f) : 0f;
                skidVol = MathF.Max(fromLat, fromTouch);
            }
            float skidPitch = MathF.Min(0.75f + MathF.Abs(lateral) * 0.01f + MathF.Abs(slip) * 0.003f, 3f);
            int count = 0;
            if (smoke && replayDt > 0f)
            {
                float mag = recentAir ? MathF.Abs(groundSpeed) : MathF.Abs(lateral);
                count = (int)(mag * 0.03f * (replayDt / 0.02f));
                if (count < 1) count = 1;
                if (count > 12) count = 12;
            }
            return new Result(rolling, skid, smoke, dust, noiseVol, noisePitch, skidVol, skidPitch, count);
        }
    }
}
