using System;

namespace NOReplay.OrientationTests
{
    static class Program
    {
        static int _failed;

        static void Main()
        {
            YawWrapsShort();
            RollWrapsShort();
            SignFlipStaysPut();
            UnityYaw90();
            LevelFlightHasNoAoa();
            NoseUpIsPositiveAoa();
            NoseDownIsNegativeAoa();
            WingVaporUsesAoaCurve();
            ContrailUsesAltitudeBand();
            ReplayGatesKillVapor();
            GLoadMatchesAircraft();
            OpacityFadesBothWays();
            SpeedOfSoundFloor();
            WheelRollsWithTheNose();
            ReverseRollsBackward();
            PivotSplitsTheSides();
            SeekIgnoresATimelineJump();
            if (_failed > 0)
            {
                Console.Error.WriteLine("FAILED " + _failed);
                Environment.Exit(1);
            }
            Console.WriteLine("OK");
        }

        static void YawWrapsShort()
        {
            OrientationMath.FromPlaybackEuler(0f, 350f, 0f, out float ax, out float ay, out float az, out float aw);
            OrientationMath.FromPlaybackEuler(0f, 10f, 0f, out float bx, out float by, out float bz, out float bw);
            OrientationMath.Slerp(ax, ay, az, aw, bx, by, bz, bw, 0.5f, out float x, out float y, out float z, out float w);
            float yaw = YawOf(x, y, z, w);
            // Short arc passes through north (0°). The long arc would sit near 180°.
            Check(MathF.Abs(yaw) < 8f, "yaw 350→10 midpoint " + yaw.ToString("0.0") + " expected near 0");
        }

        static void RollWrapsShort()
        {
            OrientationMath.FromPlaybackEuler(0f, 0f, 179f, out float ax, out float ay, out float az, out float aw);
            OrientationMath.FromPlaybackEuler(0f, 0f, -179f, out float bx, out float by, out float bz, out float bw);
            OrientationMath.Slerp(ax, ay, az, aw, bx, by, bz, bw, 0.5f, out float x, out float y, out float z, out float w);
            Rotate(x, y, z, w, 0f, 1f, 0f, out _, out float upY, out _);
            // 179° and -179° are 2° apart, both inverted. Linear lerp of the degrees lands upright.
            Check(upY < -0.95f, "roll 179→-179 up.y " + upY.ToString("0.00") + " expected inverted");
        }

        static void SignFlipStaysPut()
        {
            OrientationMath.FromPlaybackEuler(0f, 40f, 0f, out float x, out float y, out float z, out float w);
            OrientationMath.Slerp(x, y, z, w, -x, -y, -z, -w, 0.5f, out float ox, out float oy, out float oz, out float ow);
            float dot = MathF.Abs(x * ox + y * oy + z * oz + w * ow);
            Check(dot > 0.999f, "sign-flipped quat slerp dot " + dot.ToString("0.000") + " expected ~1");
        }

        static void UnityYaw90()
        {
            OrientationMath.FromUnityEuler(0f, 90f, 0f, out float x, out float y, out float z, out float w);
            Check(MathF.Abs(x) < 0.01f && MathF.Abs(z) < 0.01f, "yaw90 x/z");
            Check(MathF.Abs(y - 0.70710678f) < 0.01f && MathF.Abs(w - 0.70710678f) < 0.01f, "yaw90 y/w");
            Check(MathF.Abs(YawOf(x, y, z, w) - 90f) < 1f, "yaw90 extracted");
        }

        static void WheelRollsWithTheNose()
        {
            float travel = GroundRoll.Travel(2f * MathF.PI * 0.3f, 0f, 1.2f);
            float deg = GroundRoll.WheelDegrees(travel, 0.3f);
            Check(MathF.Abs(deg - 360f) < 0.05f, "one circumference is 360 deg, got " + deg.ToString("0.00"));
            Check(MathF.Abs(GroundRoll.TrackOffset(10f) - (-2f)) < 0.0001f, "track scroll is -0.2 per meter");
            Check(MathF.Abs(GroundRoll.WheelDegrees(1f, 0f) - GroundRoll.WheelDegrees(1f, 0.3f)) < 0.0001f,
                "missing radius falls back to 0.3");
        }

        static void ReverseRollsBackward()
        {
            float ahead = GroundRoll.Travel(4f, 0f, 0f);
            float back = GroundRoll.Travel(-4f, 0f, 0f);
            Check(back == -ahead, "reverse travel is the negation of forward");
            Check(GroundRoll.WheelDegrees(back, 0.5f) < 0f, "reverse spins the wheel backward");
        }

        static void PivotSplitsTheSides()
        {
            // Positive yaw is a right turn. The right side rolls backward, the left side forward.
            float right = GroundRoll.Travel(0f, 1f, 2f);
            float left = GroundRoll.Travel(0f, 1f, -2f);
            Check(MathF.Abs(right - (-2f)) < 0.0001f, "right wheel on a right pivot goes backward");
            Check(MathF.Abs(left - 2f) < 0.0001f, "left wheel on a right pivot goes forward");
            Check(MathF.Abs(GroundRoll.Travel(3f, 0.5f, 2f) - 2f) < 0.0001f, "forward and yaw add");
            Check(MathF.Abs(GroundRoll.Travel(5f, 1f, 100f) - 5f) < 0.0001f, "a wild lever is ignored");
        }

        static void SeekIgnoresATimelineJump()
        {
            Check(!GroundRoll.IsSeek(0.2f, 16f, 8f), "a playback frame is not a seek");
            Check(GroundRoll.IsSeek(1.5f, 0f, 0f), "a clock jump is a seek");
            Check(GroundRoll.IsSeek(-2f, 0f, 0f), "a rewind jump is a seek");
            Check(GroundRoll.IsSeek(0f, 401f, 0f), "a 20m teleport is a seek");
            Check(GroundRoll.IsSeek(0f, 1f, 40f), "a large yaw step is a seek");
        }

        static void LevelFlightHasNoAoa()
        {
            float a = VaporMath.AngleOnAxis(0f, 0f, 1f, 0f, 0f, 1f, 1f, 0f, 0f);
            Check(MathF.Abs(a) < 0.01f, "level AoA " + a.ToString("0.00"));
        }

        static void NoseUpIsPositiveAoa()
        {
            float s = 0.70710678f;
            float a = VaporMath.AngleOnAxis(0f, 0f, 1f, 0f, -s, s, 1f, 0f, 0f);
            Check(MathF.Abs(a - 45f) < 0.05f, "nose-up AoA " + a.ToString("0.00") + " expected 45");
        }

        static void NoseDownIsNegativeAoa()
        {
            float s = 0.70710678f;
            float a = VaporMath.AngleOnAxis(0f, 0f, 1f, 0f, s, s, 1f, 0f, 0f);
            Check(MathF.Abs(a + 45f) < 0.05f, "nose-down AoA " + a.ToString("0.00") + " expected -45");
        }

        static void WingVaporUsesAoaCurve()
        {
            Check(VaporMath.WingCondensation(12f, 8f, 2f, 200f, 20f, false, false), "AoA above curve");
            Check(!VaporMath.WingCondensation(4f, 8f, 2f, 200f, 20f, false, false), "AoA under curve");
            Check(!VaporMath.WingCondensation(12f, 8f, 0.5f, 200f, 20f, false, false), "detail cutoff");
            Check(!VaporMath.WingCondensation(-12f, 8f, 0.4f, 200f, 20f, false, false), "detail below cutoff");
            Check(VaporMath.WingCondensation(-12f, 8f, 0.51f, 200f, 20f, false, false), "negative AoA counts");
        }

        static void ContrailUsesAltitudeBand()
        {
            Check(VaporMath.EngineContrail(8000f, 7500f, 12500f, 200f, 20f, false, false), "inside band");
            Check(!VaporMath.EngineContrail(7500f, 7500f, 12500f, 200f, 20f, false, false), "min exclusive");
            Check(!VaporMath.EngineContrail(12500f, 7500f, 12500f, 200f, 20f, false, false), "max exclusive");
            Check(!VaporMath.EngineContrail(4000f, 7500f, 12500f, 250f, 20f, false, false), "below band");
            Check(VaporMath.EngineContrail(9000f, 7500f, 12500f, 200f, 20f, false, false) , "contrail ignores detail");
        }

        static void ReplayGatesKillVapor()
        {
            Check(!VaporMath.WingCondensation(20f, 5f, 10f, 200f, 20f, true, false), "paused wing");
            Check(!VaporMath.WingCondensation(20f, 5f, 10f, 200f, 20f, false, true), "ground wing");
            Check(!VaporMath.WingCondensation(20f, 5f, 10f, 10f, 20f, false, false), "slow wing");
            Check(!VaporMath.EngineContrail(9000f, 7500f, 12500f, 200f, 20f, true, false), "paused contrail");
            Check(!VaporMath.EngineContrail(9000f, 7500f, 12500f, 200f, 20f, false, true), "ground contrail");
            Check(!VaporMath.EngineContrail(9000f, 7500f, 12500f, 10f, 20f, false, false), "slow contrail");
        }

        static void GLoadMatchesAircraft()
        {
            float g = VaporMath.GLoad(0f, 9.81f, 0f, 0f, 0f, 0f, 1f);
            Check(MathF.Abs(g - 1f) < 0.001f, "1g step " + g.ToString("0.000"));
            float steady = VaporMath.GLoad(100f, 0f, 0f, 100f, 0f, 0f, 0.3f);
            Check(steady == 0f, "steady g " + steady.ToString("0.000"));
            float zeroDt = VaporMath.GLoad(1f, 0f, 0f, 0f, 0f, 0f, 0f);
            Check(zeroDt == 0f, "zero dt");
        }

        static void OpacityFadesBothWays()
        {
            float up = VaporMath.StepOpacity(-0.5f, true, 0.02f);
            Check(MathF.Abs(up - (-0.48f)) < 0.0001f, "fade in " + up.ToString("0.000"));
            float down = VaporMath.StepOpacity(1f, false, 0.02f);
            Check(MathF.Abs(down - 0.98f) < 0.0001f, "fade out " + down.ToString("0.000"));
            Check(VaporMath.FadeStillVisible(false, -0.4f), "still fading");
            Check(!VaporMath.FadeStillVisible(false, -0.5f), "fade done");
            Check(VaporMath.FadeStillVisible(true, -0.5f), "active emits");
            float rate = VaporMath.EmitRate(10f, 10f, 20f);
            Check(MathF.Abs(rate - 5f) < 0.001f, "half rate " + rate.ToString("0.00"));
        }

        static void SpeedOfSoundFloor()
        {
            Check(MathF.Abs(VaporMath.SpeedOfSound(0f) - 340f) < 0.01f, "sos sea");
            Check(MathF.Abs(VaporMath.SpeedOfSound(20000f) - 290f) < 0.01f, "sos floor");
            float sos = VaporMath.SpeedOfSound(4000f);
            Check(MathF.Abs(sos - 320f) < 0.01f, "sos 4km " + sos.ToString("0.0"));
        }

        static float YawOf(float x, float y, float z, float w)
        {
            Rotate(x, y, z, w, 0f, 0f, 1f, out float ox, out _, out float oz);
            return MathF.Atan2(ox, oz) * (180f / MathF.PI);
        }

        static void Rotate(float qx, float qy, float qz, float qw, float vx, float vy, float vz,
            out float ox, out float oy, out float oz)
        {
            float tx = 2f * (qy * vz - qz * vy);
            float ty = 2f * (qz * vx - qx * vz);
            float tz = 2f * (qx * vy - qy * vx);
            ox = vx + qw * tx + (qy * tz - qz * ty);
            oy = vy + qw * ty + (qz * tx - qx * tz);
            oz = vz + qw * tz + (qx * ty - qy * tx);
        }

        static void Check(bool ok, string message)
        {
            if (ok)
            {
                Console.WriteLine("ok  " + message);
                return;
            }
            _failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
