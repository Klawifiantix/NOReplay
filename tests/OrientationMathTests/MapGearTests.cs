using NOReplay;

namespace NOReplay.OrientationTests
{
    static class MapGearTests
    {
        public static int Failed;

        public static void Run()
        {
            var plain = MapClickGate.Decide(true, false, false);
            Expect(plain.RunGameClick && !plain.SelectInPlace, "plain replay click still runs the game click");
            var shift = MapClickGate.Decide(true, true, false);
            Expect(!shift.RunGameClick && shift.SelectInPlace, "shift click selects without the game click");
            var live = MapClickGate.Decide(false, true, false);
            Expect(live.RunGameClick && !live.SelectInPlace, "shift outside a replay is unchanged");
            var dead = MapClickGate.Decide(true, true, true);
            Expect(!dead.RunGameClick && !dead.SelectInPlace, "a suppressed icon is not selected");

            Expect(!GearScale.Absurd(0.2f, 1f, 0.2f), "a normal strut scale stays");
            Expect(GearScale.Absurd(80f, 1f, 0.01f), "an exploded hinge scale is rejected");
            Expect(GearScale.Absurd(0f, 40f, 0f), "a zeroed axis with a large axis is rejected");

            Expect(!DebrisLife.Expired(true, 5f, 1f, 14f), "debris stays inside its replay window");
            Expect(DebrisLife.Expired(true, 20f, 1f, 14f), "debris ends when replay time passes its life");
            Expect(DebrisLife.Expired(true, 0.5f, 1f, 14f), "seeking before the break removes the piece");
            Expect(DebrisLife.Expired(false, 5f, 1f, 14f), "leaving the replay removes the piece");
            if (Failed == 0) System.Console.WriteLine("map/gear ok");
        }

        static void Expect(bool ok, string name)
        {
            if (ok) return;
            Failed++;
            System.Console.Error.WriteLine("FAIL " + name);
        }
    }
}
