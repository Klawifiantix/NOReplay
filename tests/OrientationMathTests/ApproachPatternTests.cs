using System;

namespace NOReplay.OrientationTests
{
    static class ApproachPatternTests
    {
        public static int Failed { get; private set; }

        public static void Run()
        {
            ConstantsMatchTheSketch();
            GlideslopeIsTheMeatballAngle();
            MeatballTouchMatchesTheLerp();
            GrooveFollowsTheAngledDeck();
            OverheadStaysOnTheHull();
            HullAxisFinalIsStraightAhead();
            AftAxisIsTurnedTowardTheBow();
            BuildIsDeterministic();
            ShortBufferDoesNotThrow();
        }

        static void ConstantsMatchTheSketch()
        {
            Check(MathF.Abs(ApproachPattern.BreakLeadNm - 1f) < 1e-5f, "break nose is 1 NM ahead");
            Check(ApproachPattern.DownwindAbeamNm < 2f, "downwind is under 2 NM");
            Check(MathF.Abs(ApproachPattern.InitialAsternNm - 2f) < 1e-5f, "initial is 2 NM astern");
            Check(MathF.Abs(ApproachPattern.PatternAltitudeFt - 1500f) < 1e-4f, "pattern altitude is 1500 ft");
            Check(MathF.Abs(ApproachPattern.BolterAheadNm - 1f) < 1e-5f, "bolter runs 1 NM");
            Check(ApproachPattern.InitialStarboardNm > 0f && ApproachPattern.InitialStarboardNm < 1f,
                "initial is to starboard, inside the 1 NM side of the box");
            float interval = ApproachPattern.BreakIntervalM();
            float expect = 300f * 1852f / 3600f * 10f;
            Check(MathF.Abs(interval - expect) < 0.05f, "10 s at 300 kt is " + interval.ToString("0.0") + " m");
            Check(interval < ApproachPattern.NauticalMile, "a later break is still under 1 NM");
        }

        static void GlideslopeIsTheMeatballAngle()
        {
            float tan = MathF.Tan(3.4f * MathF.PI / 180f);
            Check(MathF.Abs(ApproachPattern.GlideslopeRise - 0.06f) < 1e-6f, "rise is the meatball 0.06");
            Check(MathF.Abs(tan - ApproachPattern.GlideslopeRise) < 0.001f,
                "0.06 is within 0.001 of tan(3.4°), got " + tan.ToString("0.00000"));
        }

        static void MeatballTouchMatchesTheLerp()
        {
            Check(ApproachPattern.MeatballTouchAlong(400f) == 0f, "short runway touches at the start");
            Check(ApproachPattern.MeatballTouchAlong(500f) == 0f, "500 m is still the start");
            float mid = ApproachPattern.MeatballTouchAlong(1500f);
            Check(MathF.Abs(mid - 160f) < 0.001f, "1500 m runway blends to 160 m, got " + mid.ToString("0.00"));
            float far = ApproachPattern.MeatballTouchAlong(4000f);
            Check(MathF.Abs(far - 300f) < 0.001f, "long runway clamps at 300 m, got " + far.ToString("0.00"));
            float just = ApproachPattern.MeatballTouchAlong(500.001f);
            Check(just >= 20f && just < 20.1f, "just over 500 m starts at 20 m, got " + just.ToString("0.000"));
        }

        static void GrooveFollowsTheAngledDeck()
        {
            var deck = Deck(40f, 15f, -80f, 9f);
            var pts = new ApproachPattern.PatternPoint[ApproachPattern.MaxPoints];
            int n = ApproachPattern.Build(deck, pts);
            Check(n >= 16 && n <= ApproachPattern.MaxPoints, "point count " + n);
            var ball = pts[n - 3];
            var touch = pts[n - 2];
            var bolt = pts[n - 1];
            Check(Near(touch.X, deck.OriginX) && Near(touch.Y, deck.OriginY) && Near(touch.Z, deck.OriginZ),
                "glideslope ends on the wires");
            float hx = ball.X - touch.X;
            float hz = ball.Z - touch.Z;
            float hd = MathF.Sqrt(hx * hx + hz * hz);
            float ballDist = ApproachPattern.BallNm * ApproachPattern.NauticalMile;
            Check(MathF.Abs(hd - ballDist) < 0.25f, "ball is 0.75 NM astern, got " + hd.ToString("0.0"));
            Check(MathF.Abs((ball.Y - touch.Y) / hd - ApproachPattern.GlideslopeRise) < 0.0002f,
                "final slope is the meatball rise");
            float bx = bolt.X - touch.X;
            float bz = bolt.Z - touch.Z;
            float bd = MathF.Sqrt(bx * bx + bz * bz);
            Check(MathF.Abs(bd - ApproachPattern.BolterAheadNm * ApproachPattern.NauticalMile) < 0.25f,
                "bolter is 1 NM, got " + bd.ToString("0.0"));
            Check(MathF.Abs(bolt.Y - touch.Y) < 0.01f, "bolter stays at deck height");
            float cross = hx * bz - hz * bx;
            Check(MathF.Abs(cross) < 1f, "ball, wires and bolter are one straight line");
            ApproachPattern.AxisFromPortAngle(9f, out float ax, out float az);
            float dx = touch.X - ball.X;
            float dz = touch.Z - ball.Z;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            Check(MathF.Abs(dx / len - ax) < 0.002f && MathF.Abs(dz / len - az) < 0.002f,
                "final points along the angled deck, not the hull");
            Check(MathF.Abs(dx) > 100f, "angled final is not the hull centerline");
            Check(bolt.Z > touch.Z, "bolter continues ahead");
        }

        static void OverheadStaysOnTheHull()
        {
            var deck = Deck(40f, 15f, -80f, 9f);
            var pts = new ApproachPattern.PatternPoint[ApproachPattern.MaxPoints];
            int n = ApproachPattern.Build(deck, pts);
            float nm = ApproachPattern.NauticalMile;
            float patternY = deck.OriginY + ApproachPattern.PatternAltitudeFt * ApproachPattern.Foot;
            float initialX = ApproachPattern.InitialStarboardNm * nm;
            float downwindX = -ApproachPattern.DownwindAbeamNm * nm;
            Check(Near(pts[0].X, initialX) && Near(pts[0].Z, -ApproachPattern.InitialAsternNm * nm),
                "initial starts 2 NM astern, independent of the wires");
            Check(MathF.Abs(pts[0].Y - patternY) < 0.05f, "initial is 1500 ft above the deck");
            Check(Near(pts[1].X, initialX) && Near(pts[1].Z, 0f) && MathF.Abs(pts[1].Y - patternY) < 0.05f,
                "break starts abeam the carrier, level");

            float maxLevelZ = float.MinValue;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            int descent = -1;
            for (int i = 0; i < n; i++)
            {
                if (pts[i].X < minX) minX = pts[i].X;
                if (pts[i].X > maxX) maxX = pts[i].X;
                bool level = MathF.Abs(pts[i].Y - patternY) < 0.5f;
                if (level && pts[i].Z > maxLevelZ) maxLevelZ = pts[i].Z;
                if (descent < 0 && pts[i].Y < patternY - 0.5f) descent = i;
            }
            Check(MathF.Abs(maxLevelZ - ApproachPattern.BreakLeadNm * nm) < 0.5f,
                "level break reaches 1 NM ahead, got " + maxLevelZ.ToString("0.0"));
            Check(MathF.Abs(minX - downwindX) < 0.5f, "downwind is 1.75 NM to port, got " + minX.ToString("0.0"));
            Check(MathF.Abs(maxX - initialX) < 0.5f, "nothing flies outside the initial, got " + maxX.ToString("0.0"));
            Check(descent > 2, "the descent starts after the level break");
            Check(pts[descent].X > downwindX + 1f, "the approach turn leaves downwind toward the ship");
            bool xFalls = true;
            for (int i = 2; i < descent; i++)
            {
                if (pts[i].Y < patternY - 0.5f) break;
                if (pts[i].X > pts[i - 1].X + 0.05f) xFalls = false;
            }
            Check(xFalls, "the break is a left turn");
            bool descends = true;
            for (int i = descent; i < n - 1; i++)
            {
                if (pts[i].Y + 0.05f < pts[i + 1].Y) descends = false;
            }
            Check(descends, "final turn and glideslope do not climb");
            float minZ = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (pts[i].Z < minZ) minZ = pts[i].Z;
            Check(minZ > -2.3f * nm, "the line stays near the 2 NM astern edge, got " + (minZ / nm).ToString("0.00") + " NM");
        }

        static void HullAxisFinalIsStraightAhead()
        {
            var deck = new ApproachPattern.DeckFrame
            {
                OriginX = 10f,
                OriginY = 4f,
                OriginZ = 20f,
                AxisX = 0f,
                AxisZ = 1f
            };
            var pts = new ApproachPattern.PatternPoint[ApproachPattern.MaxPoints];
            int n = ApproachPattern.Build(deck, pts);
            var ball = pts[n - 3];
            var touch = pts[n - 2];
            Check(MathF.Abs(ball.X - touch.X) < 0.2f, "hull-aligned groove has no lateral offset");
            Check(ball.Z < touch.Z, "the ball is astern of the wires");
            Check(pts[n - 1].Z > touch.Z, "bolter goes forward");
        }

        static void AftAxisIsTurnedTowardTheBow()
        {
            var deck = new ApproachPattern.DeckFrame
            {
                OriginX = 0f,
                OriginY = 0f,
                OriginZ = 0f,
                AxisX = 0f,
                AxisZ = -1f
            };
            var pts = new ApproachPattern.PatternPoint[ApproachPattern.MaxPoints];
            int n = ApproachPattern.Build(deck, pts);
            Check(pts[n - 1].Z > pts[n - 2].Z, "an aft axis is flipped so the bolter leaves toward the bow");
        }

        static void BuildIsDeterministic()
        {
            var deck = Deck(-12f, 8f, 30f, 11f);
            var a = new ApproachPattern.PatternPoint[ApproachPattern.MaxPoints];
            var b = new ApproachPattern.PatternPoint[ApproachPattern.MaxPoints];
            int na = ApproachPattern.Build(deck, a);
            int nb = ApproachPattern.Build(deck, b);
            Check(na == nb && na > 0, "two builds share a count");
            bool same = na == nb;
            for (int i = 0; same && i < na; i++)
            {
                if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z) same = false;
            }
            Check(same, "the polyline does not depend on anything but the deck");
        }

        static void ShortBufferDoesNotThrow()
        {
            var deck = Deck(0f, 0f, 0f, 9f);
            var tiny = new ApproachPattern.PatternPoint[4];
            int n = ApproachPattern.Build(deck, tiny);
            Check(n == tiny.Length, "a short buffer stores what fits, got " + n);
            Check(ApproachPattern.Build(deck, null) == 0, "null buffer");
            ApproachPattern.AxisFromPortAngle(ApproachPattern.FallbackDeckAngleDeg, out float ax, out float az);
            Check(ax < 0f && az > 0.9f, "fallback axis is a few degrees to port");
        }

        static ApproachPattern.DeckFrame Deck(float ox, float oy, float oz, float portDeg)
        {
            ApproachPattern.AxisFromPortAngle(portDeg, out float ax, out float az);
            return new ApproachPattern.DeckFrame
            {
                OriginX = ox,
                OriginY = oy,
                OriginZ = oz,
                AxisX = ax,
                AxisZ = az
            };
        }

        static bool Near(float a, float b)
        {
            return MathF.Abs(a - b) < 0.05f;
        }

        static void Check(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
