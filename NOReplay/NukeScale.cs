using System;

namespace NOReplay
{
    /// <summary>
    /// Same blast-radius formula as Shockwave.Start. Yield is kilograms of TNT
    /// on the missile and kilotons on the shockwave (kg * 1e-6).
    /// </summary>
    internal static class NukeScale
    {
        public static float Kilotons(float yieldKg) => yieldKg * 1e-6f;

        public static float ShockPower(float yieldKilotons)
        {
            float kt = yieldKilotons > 0.0001f ? yieldKilotons : 0.0001f;
            return MathF.Pow(kt * 1000000f, 0.3333f);
        }

        public static float BlastRadius(float yieldKilotons) => ShockPower(yieldKilotons) * 13f;

        public static float Propagation(float yieldKilotons, float ageSeconds)
        {
            float age = ageSeconds > 0f ? ageSeconds : 0f;
            return ShockPower(yieldKilotons) * 0.5f + 340f * age;
        }

        /// <summary>Value to store before Update adds <paramref name="step"/>.</summary>
        public static float BeforeStep(float target, float step) => target - step;

        /// <summary>Seconds after burst when the wave passes blastRadius.</summary>
        public static float RadiusAge(float yieldKilotons)
        {
            float power = ShockPower(yieldKilotons);
            return (power * 13f - power * 0.5f) / 340f;
        }

        /// <summary>dustOpacity after Update, fading 0.1 per second once the wave has passed.</summary>
        public static float DustOpacity(float yieldKilotons, float ageSeconds)
        {
            float age = ageSeconds > 0f ? ageSeconds : 0f;
            float passed = age - RadiusAge(yieldKilotons);
            if (passed <= 0f) return 1f;
            float dust = 1f - 0.1f * passed;
            return dust > 0f ? dust : 0f;
        }
    }
}
