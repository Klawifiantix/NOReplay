using System;

namespace NOReplay
{
    /// <summary>
    /// Case I overhead for the Hyperion, in carrier-local meters.
    /// +Z is the bow, +X is starboard, +Y is up — the same axes as the
    /// wire positions stored on the carrier ghost.
    /// Initial, the level break and the downwind are parallel to the hull.
    /// The ball, the glideslope and the bolter follow the angled-deck axis
    /// passed in with the deck frame, and the slope is the meatball's.
    /// The polyline does not depend on replay time.
    /// </summary>
    public static class ApproachPattern
    {
        public const float NauticalMile = 1852f;
        public const float Foot = 0.3048f;

        /// <summary>Initial and downwind, above the landing deck. Owner sketch: 1500 ft.</summary>
        public const float PatternAltitudeFt = 1500f;

        /// <summary>Initial starts this far astern of the carrier origin. Owner sketch: about 2 NM.</summary>
        public const float InitialAsternNm = 2f;

        /// <summary>
        /// Initial is this far to starboard of the hull centerline, so the left
        /// break crosses the ship. The sketch's box extends about 1 NM to that side;
        /// the ideal line sits inside it.
        /// </summary>
        public const float InitialStarboardNm = 0.25f;

        /// <summary>
        /// Downwind, to port of the hull centerline. Owner sketch: under 2 NM,
        /// and the box is about 2 NM abeam.
        /// </summary>
        public const float DownwindAbeamNm = 1.75f;

        /// <summary>
        /// Nose of the level break, ahead of the carrier origin.
        /// Half of starboard plus abeam, which is 1 NM — the sketch's "1 NM ahead".
        /// </summary>
        public const float BreakLeadNm = (InitialStarboardNm + DownwindAbeamNm) * 0.5f;

        /// <summary>Break starts abeam the carrier origin (the ghost pivot stands in for the bow).</summary>
        public const float BreakAbeamM = 0f;

        /// <summary>Nominal initial speed. Used only to size the break interval below.</summary>
        public const float InitialSpeedKt = 300f;

        /// <summary>
        /// Case I interval from the sketch ("optional later break of about 10 s").
        /// The drawn line is the break abeam the carrier, not a delayed one.
        /// At <see cref="InitialSpeedKt"/> this is about 0.83 NM.
        /// </summary>
        public const float BreakIntervalSec = 10f;

        /// <summary>Start of the groove. The final turn rolls out here, then the glideslope is straight.</summary>
        public const float BallNm = 0.75f;

        /// <summary>
        /// Meatball glideslope, rise over run. 0.06 is about 3.4°.
        /// ReplayGhost.Meatball uses this same factor.
        /// </summary>
        public const float GlideslopeRise = 0.06f;

        /// <summary>Bolter or waveoff, straight ahead along the angled deck from the wires.</summary>
        public const float BolterAheadNm = 1f;

        /// <summary>
        /// Port angle used only when the prefab has neither a landing runway nor wires.
        /// A Nimitz-style angled deck is about 9° to port of the hull.
        /// </summary>
        public const float FallbackDeckAngleDeg = 9f;

        /// <summary>Samples on each 180° turn. 24 puts a vertex on the nose of the break.</summary>
        public const int TurnSegments = 24;

        public const int MaxPoints = 96;

        // Meatball touch offset along the landing runway. Matches the Lerp in DriveMeatball.
        public const float MeatballRunwayLong = 500f;
        public const float MeatballTouchNear = 20f;
        public const float MeatballTouchFar = 300f;
        public const float MeatballTouchBlend = 0.0005f;

        public struct DeckFrame
        {
            public float OriginX, OriginY, OriginZ;
            public float AxisX, AxisZ;
        }

        public struct PatternPoint
        {
            public float X, Y, Z;
        }

        /// <summary>Approach direction for a deck angled <paramref name="degreesPort"/> to port of the bow.</summary>
        public static void AxisFromPortAngle(float degreesPort, out float axisX, out float axisZ)
        {
            float r = degreesPort * (MathF.PI / 180f);
            axisX = -MathF.Sin(r);
            axisZ = MathF.Cos(r);
        }

        /// <summary>How far a 10 s later break travels at initial speed. Not part of the polyline.</summary>
        public static float BreakIntervalM()
        {
            return InitialSpeedKt * NauticalMile / 3600f * BreakIntervalSec;
        }

        /// <summary>
        /// Touch point's distance along the landing runway from its start.
        /// Short runways use the start. Longer ones blend from 20 m to 300 m, clamped.
        /// </summary>
        public static float MeatballTouchAlong(float runwayLength)
        {
            if (runwayLength <= MeatballRunwayLong) return 0f;
            float t = (runwayLength - MeatballRunwayLong) * MeatballTouchBlend;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return MeatballTouchNear + (MeatballTouchFar - MeatballTouchNear) * t;
        }

        /// <summary>
        /// Writes the ideal line into <paramref name="dest"/> and returns how many points were stored.
        /// The last point is the bolter end, the one before it is the wires, and the one before that is the ball.
        /// </summary>
        public static int Build(in DeckFrame deck, PatternPoint[]? dest)
        {
            if (dest == null || dest.Length == 0) return 0;
            float ax = deck.AxisX;
            float az = deck.AxisZ;
            float mag = MathF.Sqrt(ax * ax + az * az);
            if (mag < 1e-4f)
            {
                ax = 0f;
                az = 1f;
            }
            else
            {
                ax /= mag;
                az /= mag;
            }
            if (az < 0f)
            {
                ax = -ax;
                az = -az;
            }

            float nm = NauticalMile;
            float patternY = deck.OriginY + PatternAltitudeFt * Foot;
            float initialX = InitialStarboardNm * nm;
            float downwindX = -DownwindAbeamNm * nm;
            float radius = (initialX - downwindX) * 0.5f;
            float cx = initialX - radius;
            float cz = BreakAbeamM;

            int n = 0;
            void Add(float x, float y, float z)
            {
                if (n > 0)
                {
                    float dx = x - dest[n - 1].X;
                    float dy = y - dest[n - 1].Y;
                    float dz = z - dest[n - 1].Z;
                    if (dx * dx + dy * dy + dz * dz < 0.01f) return;
                }
                if (n >= dest.Length) return;
                dest[n].X = x;
                dest[n].Y = y;
                dest[n].Z = z;
                n++;
            }

            Add(initialX, patternY, -InitialAsternNm * nm);
            int seg = TurnSegments;
            for (int i = 0; i <= seg; i++)
            {
                float yaw = -MathF.PI * (i / (float)seg);
                Add(cx + radius * MathF.Cos(yaw), patternY, cz - radius * MathF.Sin(yaw));
            }

            float ballDist = BallNm * nm;
            float ballX = deck.OriginX - ax * ballDist;
            float ballZ = deck.OriginZ - az * ballDist;
            float ballY = deck.OriginY + GlideslopeRise * ballDist;

            float denom = 1f + az;
            bool arc = false;
            float r2 = 0f;
            float c2x = 0f;
            float c2z = 0f;
            float yawTo = 0f;
            if (denom > 0.25f)
            {
                r2 = (ballX - downwindX) / denom;
                if (r2 >= 40f)
                {
                    arc = true;
                    c2x = ballX - r2 * az;
                    c2z = ballZ + r2 * ax;
                    yawTo = MathF.Atan2(ax, az);
                }
            }
            if (arc)
            {
                Add(c2x - r2, patternY, c2z);
                float yawFrom = MathF.PI;
                if (yawTo > yawFrom) yawTo -= 2f * MathF.PI;
                for (int i = 1; i <= seg; i++)
                {
                    float u = i / (float)seg;
                    float yaw = yawFrom + (yawTo - yawFrom) * u;
                    float y = patternY + (ballY - patternY) * u;
                    Add(c2x + r2 * MathF.Cos(yaw), y, c2z - r2 * MathF.Sin(yaw));
                }
            }
            else
            {
                Add(downwindX, patternY, ballZ);
                Add(ballX, ballY, ballZ);
            }

            Add(deck.OriginX, deck.OriginY, deck.OriginZ);
            float ahead = BolterAheadNm * nm;
            Add(deck.OriginX + ax * ahead, deck.OriginY, deck.OriginZ + az * ahead);
            return n;
        }
    }
}
