using System;
using NOReplay;

namespace NOReplay.OrientationTests
{
    static class RotorBendTests
    {
        public static int Failed;

        public static void Run()
        {
            Expect(Near(RotorBend.SpanXDeg(-0.1f), 5.729578f), "span hinge is minus flap in degrees");
            Expect(!RotorBend.Droops(-0.1f, 0.2f), "ratio 0.2 does not droop");
            Expect(RotorBend.Droops(-0.1f, 0.19f), "a slow negative flap droops");
            Expect(!RotorBend.Droops(0.1f, 0.05f), "a positive flap does not droop");
            Expect(Near(RotorBend.SegmentXDeg(-0.1f, 4, 0), -2.864789f), "the hinge segment takes half the bend");
            Expect(Near(RotorBend.SegmentXDeg(-0.1f, 4, 1), 1.909859f), "the outer segments share the rest");

            var frame = new RotorBend.Frame
            {
                Ratio = new[] { 0.15f, 0.9f },
                Flap = new[] { new[] { -0.1f, 0.02f }, new[] { 0.01f } }
            };
            Expect(RotorBend.Read(RotorBend.Write(frame).AsSpan(), out var back), "a bend line parses");
            Expect(back.Ratio != null && back.Ratio.Length == 2 && Near(back.Ratio[0], 0.15f), "shaft ratio survives the line");
            Expect(back.Flap != null && back.Flap[0].Length == 2 && Near(back.Flap[0][0], -0.1f), "per-blade flap survives the line");
            var mid = RotorBend.Lerp(frame, new RotorBend.Frame
            {
                Ratio = new[] { 0.25f, 0.9f },
                Flap = new[] { new[] { -0.2f, 0.02f }, new[] { 0.01f } }
            }, 0.5f);
            Expect(Near(mid.Ratio[0], 0.2f) && Near(mid.Flap[0][0], -0.15f), "keys interpolate");
            if (Failed == 0) Console.WriteLine("rotor bend ok");
        }

        static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

        static void Expect(bool ok, string name)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + name);
        }
    }
}
