using System;

namespace NOReplay.OrientationTests
{
    static class TrackCoastTests
    {
        public static int Failed;

        public static void Run()
        {
            HoldsUntilTheGap();
            CoastsTheLastVelocity();
            CapsTheWait();
            CapsASpike();
            ParkedDoesNotDrift();
            SeekIsStable();
            RejectedCoastKeepsTheSample();
        }

        static void HoldsUntilTheGap()
        {
            bool moved = TrackCoast.Try(9.5f, 11f, 8f, 0f, 0f, 0f, 10f, 20f, 0f, 0f,
                out float x, out _, out _, out _, out _, out _);
            Check(!moved && x == 20f, "before the last sample there is nothing to add");
            bool after = TrackCoast.Try(11f, 11f, 8f, 0f, 0f, 0f, 10f, 20f, 0f, 0f,
                out float x2, out _, out _, out _, out _, out _);
            Check(!after && x2 == 20f, "at removal the wreck pose is the sample, not a coast");
        }

        static void CoastsTheLastVelocity()
        {
            // 10 m/s along x. Death is 0.8s after the last point.
            bool ok = TrackCoast.Try(10.4f, 10.8f, 9f, 0f, 0f, 0f, 10f, 10f, 0f, 0f,
                out float x, out float y, out float z, out float vx, out _, out _);
            Check(ok, "a moving vehicle coasts");
            Check(MathF.Abs(x - 14f) < 0.001f, "0.4s at 10 m/s is 4m, got " + x.ToString("0.00"));
            Check(y == 0f && z == 0f, "the other axes stay");
            Check(MathF.Abs(vx - 10f) < 0.001f, "velocity is 10 m/s");
        }

        static void CapsTheWait()
        {
            bool early = TrackCoast.Try(11f, 20f, 9f, 0f, 0f, 0f, 10f, 10f, 0f, 0f,
                out float x1, out _, out _, out _, out _, out _);
            bool late = TrackCoast.Try(14f, 20f, 9f, 0f, 0f, 0f, 10f, 10f, 0f, 0f,
                out float x2, out _, out _, out _, out _, out _);
            Check(early && late, "both queries coast");
            Check(MathF.Abs(x1 - 20f) < 0.001f, "one second is inside the cap");
            float cap = 10f + 10f * TrackCoast.MaxSeconds;
            Check(MathF.Abs(x2 - cap) < 0.001f, "past the cap the pose holds, got " + x2.ToString("0.00"));
            Check(MathF.Abs(x2 - x2) < 0.0001f, "the hold does not depend on query order");
            bool again = TrackCoast.Try(14f, 20f, 9f, 0f, 0f, 0f, 10f, 10f, 0f, 0f,
                out float x3, out _, out _, out _, out _, out _);
            Check(again && MathF.Abs(x3 - x2) < 0.0001f, "seeking back to the same time repeats the pose");
        }

        static void CapsASpike()
        {
            // 200 m in 1s would be a bad sample. Coast at the speed cap, not 200 m/s.
            bool ok = TrackCoast.Try(10.5f, 11f, 9f, 0f, 0f, 0f, 10f, 200f, 0f, 0f,
                out float x, out _, out _, out float vx, out _, out _);
            Check(ok, "a spike still coasts");
            Check(MathF.Abs(vx - TrackCoast.MaxSpeed) < 0.001f, "speed is the cap, got " + vx.ToString("0.0"));
            Check(MathF.Abs(x - (200f + TrackCoast.MaxSpeed * 0.5f)) < 0.05f, "half a second at the cap, not at 200 m/s");
        }

        static void ParkedDoesNotDrift()
        {
            bool ok = TrackCoast.Try(10.5f, 12f, 9f, 5f, 1f, 2f, 10f, 5.1f, 1f, 2f,
                out float x, out _, out _, out _, out _, out _);
            Check(!ok && MathF.Abs(x - 5.1f) < 0.001f, "a vehicle that was sitting still stays");
        }

        static void SeekIsStable()
        {
            bool a = TrackCoast.Try(10.25f, 11f, 9f, 0f, 2f, 0f, 10f, 0f, 6f, 0f,
                out float ax, out float ay, out _, out _, out _, out _);
            bool b = TrackCoast.Try(10.25f, 11f, 9f, 0f, 2f, 0f, 10f, 0f, 6f, 0f,
                out float bx, out float by, out _, out _, out _, out _);
            Check(a && b && ax == bx && ay == by, "two reads of the same time match");
            Check(MathF.Abs(ay - 7f) < 0.001f, "vertical speed coasts too");
            DyingIsNotTheBoom();
        }

        static void DyingIsNotTheBoom()
        {
            Check(WreckMark.MeshShown(6f, 12f), "the wreck mark stays until removal");
            Check(WreckMark.Quiet(6f, 5f), "Dying quiets the vehicle");
            float normal = WreckMark.At(-1f, 5f, 12f, false);
            Check(normal == 5f && WreckMark.Due(5f, normal) && !WreckMark.Due(4.9f, normal),
                "a normal vehicle explodes when it dies");
            float ammo = WreckMark.At(-1f, 5f, 12f, true);
            Check(ammo == 12f && !WreckMark.Due(6f, ammo) && WreckMark.Due(12f, ammo),
                "an ammo unit without a boom mark waits for the wreck");
            Check(WreckMark.At(7f, 5f, 12f, true) == 7f, "a recorded boom is the explosion");
            Check(WreckMark.At(5f, 5f, 12f, true) == 5f, "an ammo unit that blew up on the hit uses that time");
            Check(WreckMark.At(-1f, -1f, 4f, false) == 4f, "an old file with no Dying mark still booms at removal");
            Check(WreckMark.Source(-1f, 5f, false) == "dying" && WreckMark.Source(-1f, 5f, true) == "wreck"
                && WreckMark.Source(7f, 5f, true) == "boom", "the log source names the mark that won");
            Check(WreckMark.IsCookoff("HLT Munitions Truck") && WreckMark.IsCookoff("MSV Munitions")
                && WreckMark.IsCookoff("Munitions Pallet") && WreckMark.IsCookoff("Munitions Container")
                && WreckMark.IsCookoff("Ammo Dump") && WreckMark.IsCookoff("Munitions Bunker"),
                "ammo trucks, pallets, containers, dumps and bunkers cook off");
            Check(!WreckMark.IsCookoff("LCV45 Recon Truck") && !WreckMark.IsCookoff("Linebreaker SAM")
                && !WreckMark.IsCookoff("T9K41 Boltstrike") && !WreckMark.IsCookoff("Naval Supply Pallet")
                && !WreckMark.IsCookoff(null),
                "other units do not cook off");
        }

        static void RejectedCoastKeepsTheSample()
        {
            TrackCoast.Apply(false, 0f, 0f, 0f, 100f, 20f, 30f, out float x, out float y, out float z);
            Check(x == 100f && y == 20f && z == 30f, "a rejected coast keeps the sampled position");
            TrackCoast.Apply(true, 4f, 5f, 6f, 100f, 20f, 30f, out float cx, out float cy, out float cz);
            Check(cx == 4f && cy == 5f && cz == 6f, "an accepted coast replaces the sample");
        }

        static void Check(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
