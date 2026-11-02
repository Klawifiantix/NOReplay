using System;

namespace NOReplay
{
    /// <summary>
    /// Folding-fin pose as a function of replay time.
    /// Missile.FoldingFin.UnfoldFin steps deployedAmount by fixedDeltaTime * deploySpeed
    /// and writes localEulerAngles from Vector3.Lerp(foldAngle, deployAngle, amount).
    /// UnfoldFins does that once per rendered frame (UniTask.Yield), so the live
    /// open is frame-rate dependent. Playback integrates one step per second of
    /// deploySpeed, which is what those steps sum to when the game renders once
    /// per physics tick. No Unity types, so a seek can be checked without the game.
    /// </summary>
    public static class FinDeploy
    {
        public enum Kind
        {
            /// <summary>Seeker does not call DeployFins. Leave the prefab pose.</summary>
            None = 0,
            /// <summary>DeployFins runs from the seeker's Initialize, at launch.</summary>
            Immediate = 1,
            /// <summary>DeployFins runs once timeSinceSpawn passes finDelay.</summary>
            FinDelay = 2,
            /// <summary>DeployFins runs once timeSinceSpawn passes guidanceDelay.</summary>
            Guidance = 3,
        }

        /// <summary>
        /// Which clock the seeker uses. Unknown seekers with folding fins open from launch.
        /// OpticalSeekerHighDrag opens drag petals from altitude and does not use this.
        /// </summary>
        public static Kind KindFor(string? seekerTypeName)
        {
            switch (seekerTypeName)
            {
                case "OpticalSeeker":
                case "OpticalSeekerBomb":
                case "OpticalSeekerCruiseMissile":
                case "ARMSeeker":
                    return Kind.FinDelay;
                case "ARHSeeker":
                case "IRSeeker":
                    return Kind.Guidance;
                case "OpticalSeekerHighDrag":
                    return Kind.None;
                case "SARHSeeker":
                case "BallisticMissileGuidance":
                case "LaserSeeker":
                case "InertialSeekerShell":
                case "OpticalSeekerShell":
                    return Kind.Immediate;
                default:
                    return Kind.Immediate;
            }
        }

        /// <summary>
        /// Serialized defaults from the seeker scripts. The prefab value wins when it can be read.
        /// </summary>
        public static float FallbackDelay(string? seekerTypeName)
        {
            switch (seekerTypeName)
            {
                case "OpticalSeekerCruiseMissile":
                    return 1f;
                case "ARMSeeker":
                    return 0.5f;
                case "OpticalSeeker":
                case "OpticalSeekerBomb":
                    return 0.2f;
                case "ARHSeeker":
                    return 1f;
                case "IRSeeker":
                    return 0.25f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 0 is folded, 1 is open. The game starts the open on the first tick where
        /// timeSinceSpawn is greater than the delay, so equality stays folded.
        /// A deploy speed of 0 never moves. Speed is the fraction opened per second.
        /// </summary>
        public static float Amount(float ageSeconds, float delaySeconds, float deploySpeed)
        {
            if (!(ageSeconds > delaySeconds)) return 0f;
            if (!(deploySpeed > 0f)) return 0f;
            float open = (ageSeconds - delaySeconds) * deploySpeed;
            if (open >= 1f) return 1f;
            if (open <= 0f) return 0f;
            return open;
        }

        /// <summary>Component lerp, the same blend Missile.FoldingFin writes into localEulerAngles.</summary>
        public static void LerpEuler(
            float ax, float ay, float az,
            float bx, float by, float bz,
            float amount,
            out float x, out float y, out float z)
        {
            float t = amount;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            x = ax + (bx - ax) * t;
            y = ay + (by - ay) * t;
            z = az + (bz - az) * t;
        }
    }
}
