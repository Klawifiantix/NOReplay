using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

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
            MissileLaunchPlaysIgnition();
            MissileFlybySkipsIgnition();
            MissileBurnoutIsWhooshOnly();
            MissileSeekAndReentry();
            SeekBackBeforeLaunchThenPlay();
            SeekForwardIntoTheBurn();
            MotorLoopOffsetSkipsTheHead();
            SampleCursorMatchesBinarySearch();
            SampleCursorSurvivesASeek();
            AcmiPropsMatchSplit();
            AcmiPoseMatchesTheOldReader();
            FloatFieldDoesNotNeedSetValue();
            CruiseWingsWaitThenOpen();
            GlideWingsUseTheShortDelay();
            SeekFoldsTheWingsAgain();
            FinKindsMatchTheSeekers();
            CarrierOpsTests.Run();
            _failed += CarrierOpsTests.Failed;
            ApproachPatternTests.Run();
            _failed += ApproachPatternTests.Failed;
            ShipSinkTests.Run();
            _failed += ShipSinkTests.Failed;
            TrackCoastTests.Run();
            MapGearTests.Run();
            _failed += MapGearTests.Failed;
            WreckThrowTests.Run();
            _failed += WreckThrowTests.Failed;
            _failed += TrackCoastTests.Failed;
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

        static MissileMotorAudio.Stage[] Motor(float delay, float burn)
        {
            return new[] { new MissileMotorAudio.Stage(delay, burn) };
        }

        static void MissileLaunchPlaysIgnition()
        {
            var stages = Motor(0.4f, 6f);
            var atLaunch = MissileMotorAudio.At(0.1f, stages, -1, -1);
            Check(atLaunch.Ignition && atLaunch.Motor == MissileMotorAudio.MotorStart.Silent, "delay plays startup, not the motor yet");
            Check(atLaunch.Flight && !atLaunch.SnapFlight, "whoosh ramps in from the launch");

            var justLit = MissileMotorAudio.At(0.5f, stages, -1, -1);
            Check(justLit.Ignition, "startup still allowed just after the delay");
            Check(justLit.Motor == MissileMotorAudio.MotorStart.FromHead, "motor clip starts at the head when the burn begins");

            var dry = MissileMotorAudio.At(0f, Motor(0f, 5f), -1, -1);
            Check(!dry.Ignition, "no delay means the game never plays startupSource");
            Check(dry.Motor == MissileMotorAudio.MotorStart.FromHead, "delay 0 still starts audioSources at the head");
        }

        static void MissileFlybySkipsIgnition()
        {
            var stages = Motor(0.4f, 8f);
            var past = MissileMotorAudio.At(3f, stages, -1, -1);
            Check(!past.Ignition, "a flyby past the window does not start startupSource");
            Check(past.Motor == MissileMotorAudio.MotorStart.Offset, "the motor loop joins past the ignition head");
            Check(past.Flight && past.SnapFlight, "the whoosh is already at speed");
            Check(past.SinceActivate > 2f, "offset is measured from motor light");

            var again = MissileMotorAudio.At(0.5f, stages, 0, 0);
            Check(!again.Ignition, "leaving and re-entering does not replay startup");
            Check(again.Motor == MissileMotorAudio.MotorStart.Offset, "re-entry does not restart the clip head");
        }

        static void MissileBurnoutIsWhooshOnly()
        {
            var stages = new[]
            {
                new MissileMotorAudio.Stage(0.2f, 3f),
                MissileMotorAudio.FromFuel(0.3f, 4f, 1f),
                MissileMotorAudio.FromFuel(1f, 5f, 0f),
            };
            // Stage 0 ends at 3.2. Stage 1 delay 0.3 then burn 4, ends at 7.5. Spent stage adds nothing.
            var delay2 = MissileMotorAudio.At(3.3f, stages, -1, -1);
            Check(delay2.Stage == 1 && delay2.Ignition && delay2.Motor == MissileMotorAudio.MotorStart.Silent,
                "second stage startup only while its own delay is open");
            var lit2 = MissileMotorAudio.At(3.6f, stages, -1, -1);
            Check(lit2.Stage == 1 && lit2.Motor == MissileMotorAudio.MotorStart.FromHead, "second stage lights from the head");
            var coast = MissileMotorAudio.At(6f, stages, 1, 1);
            Check(coast.Stage == 1 && !coast.Ignition && coast.Motor == MissileMotorAudio.MotorStart.Offset, "second stage mid-burn is the loop");
            var done = MissileMotorAudio.At(8f, stages, -1, -1);
            Check(done.Stage < 0 && done.Motor == MissileMotorAudio.MotorStart.Silent && !done.Ignition && done.Flight,
                "after burnout only the whoosh remains");
            var none = MissileMotorAudio.At(4f, Array.Empty<MissileMotorAudio.Stage>(), -1, -1);
            Check(none.Flight && none.Motor == MissileMotorAudio.MotorStart.Silent && !none.Ignition, "a bomb has no motor");
        }

        static void MissileSeekAndReentry()
        {
            var stages = Motor(0.4f, 6f);
            int ignition = 0;
            int head = 0;
            MissileMotorAudio.OnSeek(0.05f, stages, ref ignition, ref head);
            Check(ignition < 0 && head < 0, "seeking onto the launch arms ignition again");
            MissileMotorAudio.OnSeek(4f, stages, ref ignition, ref head);
            Check(ignition == 0 && head == 0, "seeking into the flight suppresses ignition");
            var cue = MissileMotorAudio.At(4f, stages, ignition, head);
            Check(!cue.Ignition && cue.Motor == MissileMotorAudio.MotorStart.Offset, "the seek lands on the sustain");
            float shot = MissileMotorAudio.OneShotTime(5f, 6f);
            Check(shot < 0f, "a finished one-shot stays finished");
            Check(MathF.Abs(MissileMotorAudio.OneShotTime(5f, 1.2f) - 1.2f) < 0.001f, "a long clip continues from the burn clock");
        }

        static void SeekBackBeforeLaunchThenPlay()
        {
            var stages = Motor(0.4f, 6f);
            const float launch = 10f;
            const float removed = 25f;
            int ignition = 0;
            int head = 0;
            MissileMotorAudio.ResetGates(ref ignition, ref head);
            var before = MissileMotorAudio.AfterSeek(launch - 2f, launch, removed, stages);
            Check(!before.Ignition && before.Motor == MissileMotorAudio.MotorStart.Silent && !before.Flight,
                "before launch is silence, gates cleared");
            var launchCue = MissileMotorAudio.At(0.1f, stages, ignition, head);
            Check(launchCue.Ignition && launchCue.Flight, "the same missile ignites again when the clock reaches the launch");
            Check(launchCue.Motor == MissileMotorAudio.MotorStart.Silent, "the motor waits out its delay");
        }

        static void SeekForwardIntoTheBurn()
        {
            var stages = Motor(0.4f, 6f);
            const float launch = 10f;
            const float removed = 25f;
            int ignition = 0;
            int head = 0;
            MissileMotorAudio.ResetGates(ref ignition, ref head);
            var mid = MissileMotorAudio.AfterSeek(launch + 4f, launch, removed, stages);
            Check(!mid.Ignition && mid.Motor == MissileMotorAudio.MotorStart.Offset && mid.Flight,
                "a forward seek into the burn is the sustain, not the ignition");
            var coast = MissileMotorAudio.AfterSeek(launch + 8f, launch, removed, stages);
            Check(!coast.Ignition && coast.Motor == MissileMotorAudio.MotorStart.Silent && coast.Flight,
                "a seek after burnout is the whoosh only");
            var dead = MissileMotorAudio.AfterSeek(removed + 1f, launch, removed, stages);
            Check(!dead.Ignition && dead.Motor == MissileMotorAudio.MotorStart.Silent && !dead.Flight,
                "a seek after detonation is silence");
        }

        static void MotorLoopOffsetSkipsTheHead()
        {
            float first = MissileMotorAudio.LoopOffset(4f, 1);
            float second = MissileMotorAudio.LoopOffset(4f, 2);
            Check(first >= MissileMotorAudio.LoopHeadSkip && first < 4f, "offset clears the clip head");
            Check(second >= MissileMotorAudio.LoopHeadSkip && second < 4f, "another salt stays in range");
            Check(MathF.Abs(first - MissileMotorAudio.LoopOffset(4f, 1)) < 0.0001f, "the same missile keeps the same phase");
            Check(MissileMotorAudio.LoopOffset(0.2f, 3) == 0f, "a short one-shot is not treated as a loop");
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

        struct Series : SampleIndex.ISampleTimes
        {
            public float[] Times;
            public int Count => Times.Length;
            public float TimeAt(int index) => Times[index];
        }

        static int NaiveBracket(float[] times, float t)
        {
            int lo = 0;
            int hi = times.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (times[mid] <= t) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        static void SampleCursorMatchesBinarySearch()
        {
            var times = new float[400];
            for (int i = 0; i < times.Length; i++)
                times[i] = i * 0.1f;
            times[20] = times[19];
            times[21] = times[19];
            var series = new Series { Times = times };
            int cursor = -1;
            var rng = new Random(7);
            for (int n = 0; n < 2000; n++)
            {
                float t = (float)(rng.NextDouble() * 39.0 + 0.01);
                int got = SampleIndex.Bracket(series, t, ref cursor);
                int want = NaiveBracket(times, t);
                if (got != want)
                {
                    Check(false, "bracket " + t.ToString("0.000") + " got " + got + " want " + want);
                    return;
                }
            }
            var list = new List<float>(times);
            int listCursor = -1;
            float probe = 12.34f;
            Check(SampleIndex.Bracket(list, probe, ref listCursor) == NaiveBracket(times, probe), "list bracket");
            Check(true, "sample cursor");
        }

        static void SampleCursorSurvivesASeek()
        {
            var times = new float[50];
            for (int i = 0; i < times.Length; i++) times[i] = i;
            var series = new Series { Times = times };
            int cursor = -1;
            SampleIndex.Bracket(series, 40f, ref cursor);
            int back = SampleIndex.Bracket(series, 3.2f, ref cursor);
            Check(back == NaiveBracket(times, 3.2f), "seek back " + back);
            int fwd = SampleIndex.Bracket(series, 3.4f, ref cursor);
            Check(fwd == 3, "step forward " + fwd);
        }

        static List<string> ScanProps(string text)
        {
            var found = new List<string>();
            var props = new AcmiScan.Props(text);
            while (props.MoveNext())
                found.Add(props.Key.ToString() + "=" + props.Value.ToString());
            return found;
        }

        static List<string> SplitProps(string text)
        {
            var found = new List<string>();
            foreach (var part in text.Split(','))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                found.Add(part.Substring(0, eq) + "=" + part.Substring(eq + 1));
            }
            return found;
        }

        static void AcmiPropsMatchSplit()
        {
            string[] lines =
            {
                "T=1|2|3,Name=Cricket,Throttle=0,8",
                "Type=Air+FixedWing,Coalition=Boscali,,Pilot=Ada",
                "=skip,Visible=0,Q=0|0|0|1",
                "",
                "A=1,",
                "Gear=1,TAS=120,5,JetRpm=0.5"
            };
            for (int i = 0; i < lines.Length; i++)
            {
                var a = ScanProps(lines[i]);
                var b = SplitProps(lines[i]);
                bool same = a.Count == b.Count;
                for (int k = 0; same && k < a.Count; k++)
                    if (a[k] != b[k]) same = false;
                Check(same, "props " + i + " " + string.Join("|", a));
            }
            Check(AcmiScan.TryFloat("0,8".AsSpan(), out float folded) && MathF.Abs(folded - 0.8f) < 0.0001f, "comma float");
            Check(!AcmiScan.TryFloat("0,8".AsSpan(), out _, false), "strict comma float");
        }

        static bool LegacyPose(string val, out AcmiScan.Pose pose)
        {
            pose = default;
            var bits = val.Split('|');
            if (bits.Length < 3) return false;
            bool any = false;
            bool Try(int i, out float v)
            {
                v = 0f;
                if (i >= bits.Length || string.IsNullOrEmpty(bits[i])) return false;
                return float.TryParse(bits[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            }
            if (bits.Length == 3)
            {
                if (Try(0, out var rx)) { pose.X = rx; pose.HasX = true; any = true; }
                if (Try(1, out var ry)) { pose.Y = ry; pose.HasY = true; any = true; }
                if (Try(2, out var rz)) { pose.Z = rz; pose.HasZ = true; any = true; }
                pose.Any = any;
                return any;
            }
            if (Try(2, out var alt)) { pose.Y = alt; pose.HasY = true; any = true; }
            if (bits.Length == 5)
            {
                if (Try(3, out var x5)) { pose.X = x5; pose.HasX = true; any = true; }
                if (Try(4, out var z5)) { pose.Z = z5; pose.HasZ = true; any = true; }
                pose.Any = any;
                return any;
            }
            if (bits.Length > 3 && Try(3, out var roll)) { pose.Roll = roll; pose.HasRoll = true; any = true; }
            if (bits.Length > 4 && Try(4, out var pitch)) { pose.Pitch = pitch; pose.HasPitch = true; any = true; }
            if (bits.Length > 5 && Try(5, out var yaw)) { pose.Yaw = yaw; pose.HasYaw = true; any = true; }
            if (bits.Length > 6 && Try(6, out var x)) { pose.X = x; pose.HasX = true; any = true; }
            if (bits.Length > 7 && Try(7, out var z)) { pose.Z = z; pose.HasZ = true; any = true; }
            pose.Any = any;
            return any;
        }

        static void AcmiPoseMatchesTheOldReader()
        {
            string[] samples =
            {
                "1|2|3",
                "1,5|2|3,25",
                "| |100|0|5|90|10|20",
                "a|b|c|d|e",
                "1|2",
                "",
                "0|1|2|3|4|5|6|7",
                "1|2|3|",
                "|||",
            };
            for (int i = 0; i < samples.Length; i++)
            {
                bool oldOk = LegacyPose(samples[i], out var oldPose);
                bool newOk = AcmiScan.ParsePose(samples[i], out var neu);
                bool same = oldOk == newOk && oldOk == neu.Any
                    && oldPose.HasX == neu.HasX && oldPose.HasY == neu.HasY && oldPose.HasZ == neu.HasZ
                    && oldPose.HasRoll == neu.HasRoll && oldPose.HasPitch == neu.HasPitch && oldPose.HasYaw == neu.HasYaw
                    && oldPose.X == neu.X && oldPose.Y == neu.Y && oldPose.Z == neu.Z
                    && oldPose.Roll == neu.Roll && oldPose.Pitch == neu.Pitch && oldPose.Yaw == neu.Yaw;
                Check(same, "pose " + samples[i]);
            }
            Check(AcmiScan.ParseQuat("0|1|0|0".AsSpan(), out float x, out float y, out float z, out float w)
                && x == 0f && y == 1f && z == 0f && w == 0f, "quat");
            Check(!AcmiScan.ParseQuat("0|1|0".AsSpan(), out _, out _, out _, out _), "short quat");
        }

        sealed class SpeedBox
        {
            public float Speed;
            public bool On;
        }

        static void CruiseWingsWaitThenOpen()
        {
            // OpticalSeekerCruiseMissile default finDelay is 1s. deploySpeed 1 opens over the next second.
            Check(FinDeploy.Amount(1f, 1f, 1f) == 0f, "still folded on the delay tick");
            Check(MathF.Abs(FinDeploy.Amount(1.5f, 1f, 1f) - 0.5f) < 0.0001f, "cruise wing half open");
            Check(FinDeploy.Amount(2f, 1f, 1f) == 1f, "cruise wing open after one second");
            Check(FinDeploy.Amount(30f, 1f, 1f) == 1f, "cruise wing stays open");
            Check(FinDeploy.Amount(0.5f, 1f, 0f) == 0f, "zero deploy speed stays folded");
            FinDeploy.LerpEuler(0f, 0f, 90f, 0f, 0f, 0f, 0.5f, out float x, out float y, out float z);
            Check(MathF.Abs(z - 45f) < 0.0001f && x == 0f && y == 0f, "euler blend is component lerp");
        }

        static void GlideWingsUseTheShortDelay()
        {
            float delay = FinDeploy.FallbackDelay("OpticalSeekerBomb");
            Check(MathF.Abs(delay - 0.2f) < 0.0001f, "glide bomb default delay");
            Check(FinDeploy.Amount(0.2f, delay, 1f) == 0f, "glide wing folded at the delay");
            Check(MathF.Abs(FinDeploy.Amount(0.7f, delay, 1f) - 0.5f) < 0.0001f, "glide wing half open");
            Check(FinDeploy.Amount(-1f, delay, 1f) == 0f, "before launch is folded");
            Check(MathF.Abs(FinDeploy.Amount(1.25f, 1f, 2f) - 0.5f) < 0.0001f, "faster fin is half open sooner");
        }

        static void SeekFoldsTheWingsAgain()
        {
            float delay = FinDeploy.FallbackDelay("OpticalSeekerCruiseMissile");
            Check(FinDeploy.Amount(8f, delay, 1f) == 1f, "seek forward is open");
            Check(FinDeploy.Amount(0.2f, delay, 1f) == 0f, "seek back before the delay is folded");
            Check(FinDeploy.Amount(0f, 0f, 1f) == 0f, "launch instant of an immediate fin stays folded");
            Check(MathF.Abs(FinDeploy.Amount(0.25f, 0f, 1f) - 0.25f) < 0.0001f, "immediate fin has been opening since launch");
        }

        static void FinKindsMatchTheSeekers()
        {
            Check(FinDeploy.KindFor("OpticalSeekerCruiseMissile") == FinDeploy.Kind.FinDelay, "cruise uses finDelay");
            Check(FinDeploy.KindFor("OpticalSeekerBomb") == FinDeploy.Kind.FinDelay, "glide bomb uses finDelay");
            Check(FinDeploy.KindFor("OpticalSeeker") == FinDeploy.Kind.FinDelay, "optical missile uses finDelay");
            Check(FinDeploy.KindFor("ARMSeeker") == FinDeploy.Kind.FinDelay, "ARM uses finDelay");
            Check(FinDeploy.KindFor("ARHSeeker") == FinDeploy.Kind.Guidance, "ARH opens with guidance");
            Check(FinDeploy.KindFor("IRSeeker") == FinDeploy.Kind.Guidance, "IR opens with guidance");
            Check(FinDeploy.KindFor("SARHSeeker") == FinDeploy.Kind.Immediate, "SARH opens at launch");
            Check(FinDeploy.KindFor("BallisticMissileGuidance") == FinDeploy.Kind.Immediate, "ballistic opens at launch");
            Check(FinDeploy.KindFor("OpticalSeekerHighDrag") == FinDeploy.Kind.None, "high drag is not a folding fin");
            Check(MathF.Abs(FinDeploy.FallbackDelay("OpticalSeekerCruiseMissile") - 1f) < 0.0001f, "cruise default delay");
            Check(MathF.Abs(FinDeploy.FallbackDelay("IRSeeker") - 0.25f) < 0.0001f, "IR default guidance delay");
            Check(MathF.Abs(FinDeploy.FallbackDelay("ARHSeeker") - 1f) < 0.0001f, "ARH default guidance delay");
            Check(MathF.Abs(FinDeploy.FallbackDelay("ARMSeeker") - 0.5f) < 0.0001f, "ARM default delay");
        }

        static void FloatFieldDoesNotNeedSetValue()
        {
            var box = new SpeedBox();
            var speed = typeof(SpeedBox).GetField(nameof(SpeedBox.Speed), BindingFlags.Instance | BindingFlags.Public);
            var on = typeof(SpeedBox).GetField(nameof(SpeedBox.On), BindingFlags.Instance | BindingFlags.Public);
            FieldFast.SetFloat(speed!, box, 42.5f);
            FieldFast.SetBool(on!, box, true);
            Check(box.Speed == 42.5f && FieldFast.GetFloat(speed!, box) == 42.5f, "float field");
            Check(box.On, "bool field");
            for (int i = 0; i < 1000; i++)
                FieldFast.SetFloat(speed!, box, i);
            Check(box.Speed == 999f, "float writes stick");
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
