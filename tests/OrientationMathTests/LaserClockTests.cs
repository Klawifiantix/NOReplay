using System.Collections.Generic;
using NOReplay;

namespace NOReplay.OrientationTests
{
    static class LaserClockTests
    {
        public static int Failed;

        public static void Run()
        {
            TwoEmitters();
            AbsentStaysOff();
            ImpactFollowsTheTarget();
            OffCutsTheBeam();
            RemovalCutsTheBeam();
            NoPointDoesNotInventOne();
            AudioEdges();
            PitchFallsAfterTheAttack();
            if (Failed == 0) System.Console.WriteLine("LaserClock ok");
        }

        static void TwoEmitters()
        {
            var list = new List<LaserClock.Event>();
            int n = LaserClock.Parse("turret/a:1|10|2|3|0.5;turret/b:0", 4f, list);
            Expect(n == 2, "two emitters");
            Expect(list[0].Key == "turret/a" && list[0].On && list[0].HasPoint, "first is on at the point");
            Expect(Near(list[0].X, 10f) && Near(list[0].Power, 0.5f), "point and power");
            Expect(list[1].Key == "turret/b" && !list[1].On && !list[1].HasPoint, "second is off");
            Expect(list[0].Time == 4f && list[1].Time == 4f, "both share the line time");
        }

        static void AbsentStaysOff()
        {
            var view = LaserClock.At(null, 10f, -1f);
            Expect(!view.On && !view.HasPoint, "missing channel is off");
            var empty = new List<LaserClock.Event>();
            view = LaserClock.At(empty, 10f, -1f);
            Expect(!view.On, "empty channel is off");
            empty.Add(On(2f, 0f, 0f, 0f));
            view = LaserClock.At(empty, 1f, -1f);
            Expect(!view.On, "before the first sample is off");
        }

        static void ImpactFollowsTheTarget()
        {
            var ev = new List<LaserClock.Event>
            {
                On(1f, 0f, 0f, 0f),
                On(3f, 10f, 0f, 0f)
            };
            var mid = LaserClock.At(ev, 2f, -1f);
            Expect(mid.On && mid.HasPoint && Near(mid.X, 5f), "endpoint lerps while the beam stays on");
            Expect(Near(mid.Age, 1f), "age is time since the burst started");
            var held = LaserClock.At(ev, 4f, -1f);
            Expect(held.On && Near(held.X, 10f), "after the last on sample the impact holds");
        }

        static void OffCutsTheBeam()
        {
            var ev = new List<LaserClock.Event>
            {
                On(1f, 1f, 2f, 3f),
                Off(2f),
                On(5f, 4f, 5f, 6f)
            };
            var gap = LaserClock.At(ev, 3f, -1f);
            Expect(!gap.On && !gap.HasPoint, "an off sample cuts the beam");
            var again = LaserClock.At(ev, 5.5f, -1f);
            Expect(again.On && Near(again.X, 4f) && Near(again.Age, 0.5f), "the next burst starts its own age");
        }

        static void RemovalCutsTheBeam()
        {
            var ev = new List<LaserClock.Event> { On(1f, 1f, 0f, 0f) };
            var live = LaserClock.At(ev, 4f, 6f);
            Expect(live.On, "the beam holds until the shooter is removed");
            var dead = LaserClock.At(ev, 6f, 6f);
            Expect(!dead.On, "removal turns the beam off");
        }

        static void NoPointDoesNotInventOne()
        {
            var list = new List<LaserClock.Event>();
            LaserClock.Parse("mast/laser:1", 1f, list);
            Expect(list.Count == 1 && list[0].On && !list[0].HasPoint, "on without an impact stays without a point");
            var view = LaserClock.At(list, 2f, -1f);
            Expect(view.On && !view.HasPoint, "playback does not invent an endpoint");
        }

        static void AudioEdges()
        {
            var on = new LaserClock.View { On = true, Age = 0.05f };
            var aged = new LaserClock.View { On = true, Age = 0.4f };
            var off = new LaserClock.View();
            var start = LaserClock.Audio(on, true, off, 0.05f, false);
            Expect(start.PlayStart && !start.Loop && !start.PlayEnd, "forward edge plays the start");
            var loop = LaserClock.Audio(aged, true, on, 0.05f, false);
            Expect(!loop.PlayStart && loop.Loop, "a held beam loops and does not retrigger");
            var end = LaserClock.Audio(off, true, aged, 0.05f, false);
            Expect(end.PlayEnd && !end.Loop, "forward release plays the end");
            var paused = LaserClock.Audio(aged, true, on, 0.05f, true);
            Expect(!paused.PlayStart && !paused.PlayEnd && !paused.Loop, "pause is silent");
            var jumped = LaserClock.Audio(aged, true, off, 2f, false);
            Expect(!jumped.PlayStart && jumped.Loop, "a seek into a burst does not replay the start");
            var back = LaserClock.Audio(aged, true, off, -0.05f, false);
            Expect(!back.PlayStart && !back.PlayEnd && back.Loop, "reverse does not play the one-shots");
            var first = LaserClock.Audio(on, false, off, 0f, false);
            Expect(!first.PlayStart && !first.Loop, "the first frame does not invent a click");
        }

        static void PitchFallsAfterTheAttack()
        {
            float early = LaserClock.SustainPitch(true, 1f, 1.5f, 1f, 0.1f);
            float later = LaserClock.SustainPitch(true, 1f, 1.5f, 1f, 0.5f);
            float floor = LaserClock.SustainPitch(true, 1f, 1.5f, 1f, 5f);
            Expect(Near(early, 1.5f), "pitch stays high during the attack");
            Expect(Near(later, 1.2f), "pitch falls once the loop is up");
            Expect(Near(floor, 1f), "pitch does not fall through the sustain pitch");
            Expect(Near(LaserClock.SustainPitch(false, 1f, 2f, 1f, 3f), 1f), "unmodified pitch stays put");
        }

        static LaserClock.Event On(float time, float x, float y, float z)
        {
            return new LaserClock.Event
            {
                Time = time,
                Key = "a",
                On = true,
                HasPoint = true,
                X = x,
                Y = y,
                Z = z,
                Power = 1f
            };
        }

        static LaserClock.Event Off(float time)
        {
            return new LaserClock.Event { Time = time, Key = "a", On = false };
        }

        static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.001f;

        static void Expect(bool ok, string name)
        {
            if (ok) System.Console.WriteLine("ok  " + name);
            else
            {
                Failed++;
                System.Console.WriteLine("FAIL " + name);
            }
        }
    }
}
