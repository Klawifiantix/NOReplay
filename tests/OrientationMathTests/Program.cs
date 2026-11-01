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
            WheelRollsWithTheNose();
            ReverseRollsBackward();
            PivotSplitsTheSides();
            SeekIgnoresATimelineJump();
            MissileLaunchPlaysIgnition();
            MissileFlybySkipsIgnition();
            MissileBurnoutIsWhooshOnly();
            MissileSeekAndReentry();
            MotorLoopOffsetSkipsTheHead();
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

        static void MotorLoopOffsetSkipsTheHead()
        {
            float first = MissileMotorAudio.LoopOffset(4f, 1);
            float second = MissileMotorAudio.LoopOffset(4f, 2);
            Check(first >= MissileMotorAudio.LoopHeadSkip && first < 4f, "offset clears the clip head");
            Check(second >= MissileMotorAudio.LoopHeadSkip && second < 4f, "another salt stays in range");
            Check(MathF.Abs(first - MissileMotorAudio.LoopOffset(4f, 1)) < 0.0001f, "the same missile keeps the same phase");
            Check(MissileMotorAudio.LoopOffset(0.2f, 3) == 0f, "a short one-shot is not treated as a loop");
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
