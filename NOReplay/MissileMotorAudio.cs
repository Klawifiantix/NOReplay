using System;

namespace NOReplay
{
    /// <summary>
    /// Which missile sounds belong at a replay time.
    /// Missile.Motor.Thrust plays startupSource only while delayTimer is still running,
    /// then Activate() starts audioSources from the beginning of each clip.
    /// Burnout stops the looping sources. Missile.flightSound is the whoosh and
    /// stays up after the motor is out. No Unity types, so the timing can be tested
    /// without the game assemblies.
    /// </summary>
    public static class MissileMotorAudio
    {
        /// <summary>Seconds after the ignition event during which the launch sound may still start.</summary>
        public const float IgnitionWindow = 1.25f;

        /// <summary>Seconds skipped at the head of a looping motor clip when joining a burn already underway.</summary>
        public const float LoopHeadSkip = 0.45f;

        /// <summary>
        /// A non-looping motor clip at least this long is the burn itself, not the ignition one-shot.
        /// Shorter one-shots are the launch transient and are not restarted mid-flight.
        /// </summary>
        public const float SustainedOneShot = 2.5f;

        public struct Stage
        {
            public float Delay;
            public float Burn;

            public Stage(float delay, float burn)
            {
                Delay = delay > 0f ? delay : 0f;
                Burn = burn > 0f ? burn : 0f;
            }
        }

        public enum MotorStart
        {
            Silent = 0,
            FromHead = 1,
            Offset = 2,
        }

        public struct Cue
        {
            public bool Ignition;
            public MotorStart Motor;
            public bool Flight;
            public bool SnapFlight;
            public int Stage;
            public float SinceActivate;
        }

        /// <summary>
        /// A spent motor (no fuel, or no burn) adds no time. The game skips it in MotorThrust.
        /// </summary>
        public static Stage FromFuel(float delay, float burn, float fuel)
        {
            if (!(fuel > 0f) || !(burn > 0f)) return new Stage(0f, 0f);
            return new Stage(delay, burn);
        }

        /// <summary>
        /// ignitionStage / headStage: the stage index whose one-shot or clip head was already
        /// started. Passing that same index suppresses a second start (range re-entry).
        /// -1 means nothing has been started yet.
        /// </summary>
        public static Cue At(float age, Stage[]? stages, int ignitionStage, int headStage)
        {
            if (age < 0f || float.IsNaN(age)) age = 0f;
            var cue = new Cue
            {
                Stage = -1,
                Flight = true,
                SnapFlight = age > IgnitionWindow,
                Motor = MotorStart.Silent,
            };
            if (!TryLocate(age, stages, out int index, out float start, out float activate, out _))
                return cue;

            float delay = stages![index].Delay;
            bool inDelay = age < activate;
            cue.Stage = index;
            cue.SinceActivate = inDelay ? 0f : age - activate;
            // startupSource plays only while this motor's delayTimer is counting down,
            // and only if the camera arrives inside the short window that opens at that delay.
            bool startupWindow = delay > 0f && age <= start + IgnitionWindow;
            cue.Ignition = startupWindow && ignitionStage != index;
            if (inDelay || !(stages[index].Burn > 0f))
                return cue;

            bool head = age <= activate + IgnitionWindow && headStage != index;
            cue.Motor = head ? MotorStart.FromHead : MotorStart.Offset;
            return cue;
        }

        /// <summary>
        /// A timeline jump. Landing inside an ignition window may start that sound.
        /// Landing in the middle of a burn must not.
        /// </summary>
        public static void OnSeek(float age, Stage[]? stages, ref int ignitionStage, ref int headStage)
        {
            var fresh = At(age, stages, -1, -1);
            if (fresh.Ignition)
                ignitionStage = -1;
            else if (fresh.Stage >= 0)
                ignitionStage = fresh.Stage;
            else
                ignitionStage = int.MaxValue;

            // Silent means the burn has not lit yet. Do not mark the clip head as already
            // heard, or the motor would skip its start a moment later.
            if (fresh.Stage < 0 || fresh.Motor == MotorStart.Offset)
                headStage = fresh.Stage < 0 ? int.MaxValue : fresh.Stage;
            else
                headStage = -1;
        }

        /// <summary>
        /// A start time past the ignition transient at the head of a looping clip.
        /// Same salt, same time, so a missile does not hunt a new phase every frame.
        /// </summary>
        public static float LoopOffset(float clipLength, int salt)
        {
            if (!(clipLength > LoopHeadSkip + 0.05f) || float.IsNaN(clipLength)) return 0f;
            float span = clipLength - LoopHeadSkip - 0.02f;
            uint x = (uint)salt * 747796405u + 2891336453u;
            x = ((x >> (int)(x & 15)) ^ x) * 0x45d9f3bu;
            float u = (x & 0xFFFFFFu) / 16777216f;
            return LoopHeadSkip + u * span;
        }

        /// <summary>
        /// Where a long non-looping motor clip would be if it had been playing since ignition.
        /// Negative means the clip would already have finished.
        /// </summary>
        public static float OneShotTime(float clipLength, float sinceActivate)
        {
            if (!(clipLength > 0.05f) || float.IsNaN(clipLength)) return -1f;
            if (sinceActivate < 0f || float.IsNaN(sinceActivate)) sinceActivate = 0f;
            if (sinceActivate >= clipLength - 0.02f) return -1f;
            return sinceActivate;
        }

        public static bool TryLocate(float age, Stage[]? stages, out int index, out float stageStart, out float activateAt, out float burnoutAt)
        {
            index = -1;
            stageStart = 0f;
            activateAt = 0f;
            burnoutAt = 0f;
            if (stages == null || stages.Length == 0) return false;
            if (age < 0f || float.IsNaN(age)) age = 0f;
            float t = 0f;
            for (int i = 0; i < stages.Length; i++)
            {
                float delay = stages[i].Delay;
                float burn = stages[i].Burn;
                if (delay <= 0f && burn <= 0f) continue;
                float activate = t + delay;
                float end = activate + burn;
                if (age < end)
                {
                    index = i;
                    stageStart = t;
                    activateAt = activate;
                    burnoutAt = end;
                    return true;
                }
                t = end;
            }
            return false;
        }
    }
}
