using NOReplay;

namespace NOReplay.OrientationTests
{
    static class WreckThrowTests
    {
        public static int Failed;

        public static void Run()
        {
            var a = WreckThrow.Seed(42, 0, 8f);
            var b = WreckThrow.Seed(42, 0, 8f);
            Expect(a.Vx == b.Vx && a.Vy == b.Vy && a.Vz == b.Vz, "the same seed repeats the throw");
            Expect(a.Vy > 0f, "the throw leaves upward");
            var other = WreckThrow.Seed(42, 1, 8f);
            Expect(a.Vx != other.Vx || a.Vz != other.Vz, "two fragments do not share a throw");

            float hit = WreckThrow.ImpactTime(2f, 6f, 0f);
            Expect(hit > 0.2f && hit < 3f, "the piece meets the ground, hit=" + hit.ToString("0.00"));
            WreckThrow.Place(0f, 0f, 2f, 0f, a, 0f, out float x0, out float y0, out _);
            Expect(Near(x0, 0f) && Near(y0, 2f), "age zero is the start pose");
            WreckThrow.Place(hit + 5f, 0f, 2f, 0f, new WreckThrow.Throw { Vx = 1f, Vy = 6f, Vz = 0f }, 0f,
                out float xg, out float yg, out _);
            Expect(Near(yg, 0f), "after impact the piece stays on the ground");
            Expect(xg > 0f && xg < hit + 0.2f, "it does not slide after landing");
            WreckThrow.Place(0.1f, 0f, 2f, 0f, a, 0f, out float xa, out float ya, out float za);
            WreckThrow.Place(0.1f, 0f, 2f, 0f, a, 0f, out float xb, out float yb, out float zb);
            Expect(xa == xb && ya == yb && za == zb, "scrubbing to the same age repeats the place");
            if (Failed == 0) System.Console.WriteLine("wreck throw ok");
            PoseAndFx();
        }

        static void PoseAndFx()
        {
            WreckPose.Fragment(0f, 0f, 0f, 0f, 0f, 0f, 100f, 2f, 50f, false, out float x, out float y, out float z);
            Expect(Near(x, 0f) && Near(y, 0f) && Near(z, 0f), "unparented debris stays where it was spawned");
            WreckPose.Fragment(0f, 0f, 0f, 0f, 0f, 0f, 100f, 2f, 50f, true, out float px, out float py, out float pz);
            Expect(Near(px, 100f) && Near(py, 2f) && Near(pz, 50f), "parented debris moves with the wreck");
            Expect(WreckPose.FlamesStay(true, false), "flames continue when they are not stopped on cull");
            Expect(!WreckPose.FlamesStay(true, true), "cull-stop shuts the flames off");
            Expect(!WreckPose.CollapseDust(0), "no collapse-dust entries means no dust");
            Expect(WreckPose.CollapseDust(2), "collapse dust needs the prefab entries");
        }

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.05f;

        static void Expect(bool ok, string name)
        {
            if (ok) return;
            Failed++;
            System.Console.Error.WriteLine("FAIL " + name);
        }
    }
}
