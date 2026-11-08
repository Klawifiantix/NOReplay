using System;

namespace NOReplay.OrientationTests
{
    static class TireTiltTests
    {
        public static int Failed;

        public static void Run()
        {
            AirborneIsSilent();
            TouchdownSmokes();
            EstablishedRollDoesNotSmoke();
            DriftSkids();
            PauseEmitsNothing();
            SeekRepeats();
            TiltParse();
            WheelCatchesUp();
            LevelDeckDoesNotSqueal();
            DescentIsATouchdown();
        }

        static void WheelCatchesUp()
        {
            Check(MathF.Abs(TireMath.SpinSlip(60f, 0f) - 60f) < 0.01f, "a resting wheel slips at the full ground speed");
            Check(MathF.Abs(TireMath.SpinSlip(60f, 0.4f) - 20f) < 0.01f, "0.4s of spin leaves the 20 m/s smoke line");
            Check(MathF.Abs(TireMath.SpinSlip(60f, 0.5f) - 10f) < 0.01f, "half a second has almost caught 60 m/s");
            Check(TireMath.TouchdownSlip(60f, 0f), "the first contact still squeals");
            Check(!TireMath.TouchdownSlip(60f, 0.5f), "after the wheel catches there is no squeal");
            var roll = TireMath.At(true, true, 60f, 0f, false, true, 0.02f);
            Check(roll.Rolling && !roll.Smoke && !roll.Skid, "established rolling is not a squeal");
        }

        static void LevelDeckDoesNotSqueal()
        {
            float[] times = { 10f, 10.4f, 10.8f };
            float[] ys = { 20f, 20.05f, 19.95f };
            float age = TireMath.SecondsSinceDescent(11f, 20f, times, ys, 3);
            Check(age > 0.8f, "a level deck is not a fresh descent, age " + age.ToString("0.00"));
            Check(!TireMath.TouchdownSlip(55f, age), "rolling along the deck does not squeal");
        }

        static void DescentIsATouchdown()
        {
            float[] times = { 9.6f, 9.8f };
            float[] ys = { 28f, 24f };
            float age = TireMath.SecondsSinceDescent(10f, 20f, times, ys, 2);
            Check(age < 0.45f, "the last descending sample is the contact age, age " + age.ToString("0.00"));
            Check(TireMath.TouchdownSlip(70f, age), "that arrival still has wheel slip");
        }

        static void AirborneIsSilent()
        {
            var r = TireMath.At(false, true, 80f, 0f, true, true, 0.02f);
            Check(!r.Rolling && !r.Smoke && !r.Skid && r.SmokeCount == 0, "no contact, no tyre effects");
        }

        static void TouchdownSmokes()
        {
            Check(TireMath.RecentAir(1f, true, 12f, false) == false, "missing earlier altitude is not a touchdown");
            Check(TireMath.RecentAir(1f, true, 12f, true), "high then low is a touchdown");
            var r = TireMath.At(true, true, 60f, 0f, true, true, 0.02f);
            Check(r.Smoke && r.Skid && r.Rolling && r.SmokeCount > 0, "fast touchdown smokes and screeches");
            Check(r.NoisePitch > 0.5f, "rolling pitch follows speed");
        }

        static void EstablishedRollDoesNotSmoke()
        {
            var r = TireMath.At(true, true, 40f, 0f, false, true, 0.02f);
            Check(r.Rolling && !r.Smoke && !r.Skid && r.SmokeCount == 0, "spun-up tyres roll without smoke");
            var dirt = TireMath.At(true, false, 10f, 0f, false, true, 0.02f);
            Check(dirt.Dust && !dirt.Smoke, "dirt throws dust, not the tarmac smoke");
        }

        static void DriftSkids()
        {
            var r = TireMath.At(true, true, 25f, 12f, false, true, 0.02f);
            Check(r.Skid && r.Smoke && r.SmokeCount > 0, "lateral drift screeches and smokes");
        }

        static void PauseEmitsNothing()
        {
            var r = TireMath.At(true, true, 70f, 20f, true, false, 0.02f);
            Check(!r.Smoke && r.SmokeCount == 0 && !r.Rolling, "pause and a stopped clock emit nothing");
        }

        static void SeekRepeats()
        {
            var a = TireMath.At(true, true, 55f, 1f, true, true, 0.02f);
            var b = TireMath.At(true, true, 55f, 1f, true, true, 0.02f);
            Check(a.SmokeCount == b.SmokeCount && a.SkidVolume == b.SkidVolume, "the same inputs do not accumulate");
        }

        static void TiltParse()
        {
            Check(TiltText.Read("12.5|-3|0|40", out var a) && a.Count == 4, "four nacelles");
            Check(MathF.Abs(a.Angle[0] - 12.5f) < 0.001f && MathF.Abs(a.Angle[1] + 3f) < 0.001f, "signed angles");
            Check(TiltText.Read("", out var empty) == false && empty.Count == 0, "blank tilt is absent");
            var mid = TiltText.Lerp(a, TiltText.ReadOrEmpty("20|3|0|40"), 0.5f);
            Check(mid.Count == 4 && MathF.Abs(mid.Angle[0] - 16.25f) < 0.02f, "nacelles blend on the short arc");
            Check(MathF.Abs(mid.Angle[1] - 0f) < 0.05f, "signed blend crosses zero");
        }

        static void Check(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
