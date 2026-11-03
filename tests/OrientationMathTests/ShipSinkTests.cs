using System;

namespace NOReplay.OrientationTests
{
    static class ShipSinkTests
    {
        public static int Failed;

        public static void Run()
        {
            HoldsTheKillPose();
            SeekLandsOnTheSamePlace();
            DescendsAndDoesNotRise();
            AlreadyUnderStaysUnder();
            CrossesTheSeaOnce();
            LevelHullListsAndBowsDown();
            ContinuesTheLastRollThenStaysFinite();
            RecordedSinkNeedsHalfASecond();
        }

        static void HoldsTheKillPose()
        {
            var pose = ShipSink.At(4f, 2f, -6f, 0f, 0f, 0f, 1f, 0f, 0f, false, 0f, 0f, 0f, 1f, 1f);
            Check(pose.X == 4f && pose.Z == -6f, "xz stays on the kill sample");
            Check(MathF.Abs(pose.Y - 2f) < 0.0001f, "no drop at the kill instant");
            Check(Dot(pose, 0f, 0f, 0f, 1f) > 0.999f, "attitude at t=0 is the last sample");
        }

        static void SeekLandsOnTheSamePlace()
        {
            var a = At(10f);
            var b = At(20f);
            var again = At(10f);
            Check(MathF.Abs(a.Y - again.Y) < 0.0001f, "height at 10s does not depend on a later query");
            Check(Dot(a, again) > 0.999f, "attitude at 10s does not depend on a later query");
            Check(a.Y > b.Y, "earlier in the curve is higher than later");
        }

        static void DescendsAndDoesNotRise()
        {
            float mid = ShipSink.Height(0f, 0f, ShipSink.Duration * 0.5f);
            float end = ShipSink.Height(0f, 0f, ShipSink.Duration);
            // Smoothstep is 0.5 at the midpoint, so half of the 24 m drop.
            Check(MathF.Abs(mid - (-ShipSink.Depth * 0.5f)) < 0.0001f, "mid height " + mid.ToString("0.00"));
            Check(MathF.Abs(end - (-ShipSink.Depth)) < 0.0001f, "end height " + end.ToString("0.00"));
            Check(ShipSink.Progress(0f) == 0f && ShipSink.Progress(ShipSink.Duration) == 1f, "progress clamps");
        }

        static void AlreadyUnderStaysUnder()
        {
            float y = ShipSink.Height(-40f, 0f, ShipSink.Duration);
            Check(MathF.Abs(y - (-40f)) < 0.0001f, "a hull already below the target is not lifted");
            Check(ShipSink.CrossSeconds(-2f, 0f) < 0f, "already under does not cross again");
        }

        static void CrossesTheSeaOnce()
        {
            float cross = ShipSink.CrossSeconds(10f, 0f);
            Check(cross > 0f && cross < ShipSink.Duration, "cross is inside the window, got " + cross.ToString("0.00"));
            float y = ShipSink.Height(10f, 0f, cross);
            Check(MathF.Abs(y) < 0.05f, "height at the cross is the sea, got " + y.ToString("0.000"));
            Check(ShipSink.Height(10f, 0f, cross - 0.05f) > 0f, "just before the cross the deck is still up");
        }

        static void LevelHullListsAndBowsDown()
        {
            var pose = ShipSink.At(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, ShipSink.Duration, false, 0f, 0f, 0f, 1f, 1f);
            Rotate(pose, 0f, 1f, 0f, out _, out float upY, out _);
            Rotate(pose, 0f, 0f, 1f, out _, out float fwdY, out _);
            Check(upY < 0.98f, "the hull is no longer upright, up.y " + upY.ToString("0.00"));
            Check(fwdY < -0.05f, "the bow drops, forward.y " + fwdY.ToString("0.00"));
        }

        static void ContinuesTheLastRollThenStaysFinite()
        {
            OrientationMath.FromPlaybackEuler(0f, 0f, 6f, out float lx, out float ly, out float lz, out float lw);
            var held = ShipSink.At(0f, 5f, 0f, lx, ly, lz, lw, 0f, 0f, true, 0f, 0f, 0f, 1f, 1f);
            Check(Dot(held, lx, ly, lz, lw) > 0.999f, "zero seconds ignores the previous sample");
            var next = ShipSink.At(0f, 5f, 0f, lx, ly, lz, lw, 0f, 1f, true, 0f, 0f, 0f, 1f, 1f);
            Rotate(held, 0f, 1f, 0f, out _, out float upHeld, out _);
            Rotate(next, 0f, 1f, 0f, out _, out float upNext, out _);
            Check(upNext < upHeld - 0.001f, "the roll keeps going for a moment");
            Check(float.IsFinite(next.Qx) && float.IsFinite(next.Qw), "extrapolated attitude stays finite");
        }

        static void RecordedSinkNeedsHalfASecond()
        {
            Check(!ShipSink.HasRecordedSink(-1f, 30f), "no kill marker is not a recorded sink");
            Check(!ShipSink.HasRecordedSink(5f, 5.2f), "one extra sample in the same breath is not a track");
            Check(ShipSink.HasRecordedSink(5f, 5f + ShipSink.MinRecordedSeconds + 0.01f), "half a second of hull samples counts");
        }

        static ShipSink.Pose At(float seconds)
        {
            return ShipSink.At(1f, 8f, 3f, 0f, 0f, 0f, 1f, 0f, seconds, false, 0f, 0f, 0f, 1f, 1f);
        }

        static float Dot(ShipSink.Pose pose, float x, float y, float z, float w)
        {
            return MathF.Abs(pose.Qx * x + pose.Qy * y + pose.Qz * z + pose.Qw * w);
        }

        static float Dot(ShipSink.Pose a, ShipSink.Pose b)
        {
            return Dot(a, b.Qx, b.Qy, b.Qz, b.Qw);
        }

        static void Rotate(ShipSink.Pose pose, float vx, float vy, float vz, out float ox, out float oy, out float oz)
        {
            float qx = pose.Qx, qy = pose.Qy, qz = pose.Qz, qw = pose.Qw;
            float tx = 2f * (qy * vz - qz * vy);
            float ty = 2f * (qz * vx - qx * vz);
            float tz = 2f * (qx * vy - qy * vx);
            ox = vx + qw * tx + (qy * tz - qz * ty);
            oy = vy + qw * ty + (qz * tx - qx * tz);
            oz = vz + qw * tz + (qx * ty - qy * tx);
        }

        static void Check(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
