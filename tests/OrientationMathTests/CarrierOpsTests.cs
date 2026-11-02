using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace NOReplay.OrientationTests
{
    static class CarrierOpsTests
    {
        public static void Run()
        {
            RecordedHookBeatsTheRule();
            InferredHookRampsAndSeeks();
            RetractWhenFast();
            BolterIsNotATrap();
            StopOnDeckIsATrap();
            WireCrossPicksTheLine();
            NearestWireWhenNothingCrosses();
            CableReturnsWithReplayTime();
            TrapEventText();
            CarrierTrapAcmi();
        }

        static void RecordedHookBeatsTheRule()
        {
            var samples = new[]
            {
                Sample(0f, 80f, 50f, 200f, false, 0f),
                Sample(2f, 80f, 40f, 180f, true, 0.30f),
                Sample(4f, 80f, 20f, 160f, true, 0.30f)
            };
            var segs = CarrierOps.BuildHook(samples);
            float at1 = CarrierOps.AmountAt(segs, 1f);
            float at3 = CarrierOps.AmountAt(segs, 3f);
            Check(at1 < 0.05f, "recorded hook stays up before the first key, got " + at1.ToString("0.00"));
            Check(MathF.Abs(at3 - 0.30f) < 0.02f, "recorded hook holds 0.30, got " + at3.ToString("0.00"));
        }

        static void InferredHookRampsAndSeeks()
        {
            var samples = Descent(0f, 30f, 80f, 80f);
            var segs = CarrierOps.BuildHook(samples);
            float a0 = CarrierOps.AmountAt(segs, 0f);
            float a1 = CarrierOps.AmountAt(segs, 1f);
            float a2 = CarrierOps.AmountAt(segs, 2f);
            float late = CarrierOps.AmountAt(segs, 5f);
            float again = CarrierOps.AmountAt(segs, 1f);
            Check(a0 < 0.05f, "hook starts stowed, got " + a0.ToString("0.00"));
            Check(MathF.Abs(a1 - 0.5f) < 0.08f, "hook is halfway at 1s, got " + a1.ToString("0.00"));
            Check(a2 > 0.95f, "hook is down at 2s, got " + a2.ToString("0.00"));
            Check(late > 0.95f, "hook stays down while the rule still says deploy");
            Check(MathF.Abs(again - a1) < 0.0001f, "amount at 1s does not depend on a later query");
        }

        static void RetractWhenFast()
        {
            var list = new List<CarrierOps.HookSample>();
            for (float t = 0f; t <= 16f; t += 0.5f)
            {
                float speed = t < 10f ? 80f : 130f;
                float agl = t < 10f ? 50f : 40f;
                float y = 400f - t * 4f;
                list.Add(Sample(t, speed, agl, y, false, 0f));
            }
            var segs = CarrierOps.BuildHook(list.ToArray());
            float down = CarrierOps.AmountAt(segs, 8f);
            float up = CarrierOps.AmountAt(segs, 14f);
            Check(down > 0.95f, "hook down before the speed gate, got " + down.ToString("0.00"));
            Check(up < 0.05f, "hook retracted  after going fast, got " + up.ToString("0.00"));
        }

        static void BolterIsNotATrap()
        {
            var path = new List<CarrierOps.LocalSample>();
            for (int i = 0; i <= 20; i++)
            {
                float t = i * 0.25f;
                path.Add(Local(t, 0f, 12f, -80f + i * 12f, 86f, 1f, 1f));
            }
            var hits = CarrierOps.DetectTraps(path.ToArray(), null);
            Check(hits.Length == 0, "bolter produced " + hits.Length + " traps");
        }

        static void StopOnDeckIsATrap()
        {
            var path = new[]
            {
                Local(0.0f, 0f, 12f, -80f, 72f, 1f, 1f),
                Local(0.5f, 0f, 12f, -55f, 68f, 1f, 0f),
                Local(1.0f, 0f, 12f, -35f, 64f, 1f, 0f),
                Local(1.3f, 0f, 12f, -22f, 50f, 1f, 0f),
                Local(1.8f, 0f, 12f, -8f, 20f, 1f, 0f),
                Local(2.4f, 0f, 12f, 0f, 3f, 1f, 0f),
                Local(3.0f, 0f, 12f, 1f, 0.2f, 1f, 0f)
            };
            var hits = CarrierOps.DetectTraps(path, null);
            Check(hits.Length == 1, "stop on deck traps=" + hits.Length);
            if (hits.Length == 1)
            {
                Check(hits[0].Catch > 0.8f && hits[0].Catch < 1.6f, "catch " + hits[0].Catch.ToString("0.00"));
                Check(hits[0].Release > hits[0].Catch + 0.9f && hits[0].Release < hits[0].Catch + 5.1f,
                    "release " + hits[0].Release.ToString("0.00"));
            }
        }

        static void WireCrossPicksTheLine()
        {
            var path = new[]
            {
                Local(0.0f, 0f, 12f, -80f, 72f, 1f, 1f),
                Local(0.5f, 0f, 12f, -55f, 68f, 1f, 0f),
                Local(1.0f, 0f, 12f, -35f, 64f, 1f, 0f),
                Local(1.3f, 0f, 12f, -22f, 50f, 1f, 0f),
                Local(1.8f, 0f, 12f, -8f, 20f, 1f, 0f),
                Local(2.4f, 0f, 12f, 0f, 3f, 1f, 0f)
            };
            var wires = new[]
            {
                Wire(1, -50f),
                Wire(2, -30f),
                Wire(3, -10f)
            };
            var hits = CarrierOps.DetectTraps(path, wires);
            Check(hits.Length == 1, "wired stop traps=" + hits.Length);
            if (hits.Length == 1)
                Check(hits[0].Wire == 2, "expected wire 2, got " + hits[0].Wire);
        }

        static void NearestWireWhenNothingCrosses()
        {
            var path = new[]
            {
                Local(0.0f, 0f, 12f, -40f, 70f, 1f, 1f),
                Local(0.4f, 0f, 12f, -36f, 60f, 1f, 0f),
                Local(0.8f, 0f, 12f, -33f, 40f, 1f, 0f),
                Local(1.2f, 0f, 12f, -31f, 18f, 1f, 0f),
                Local(1.8f, 0f, 12f, -30f, 3f, 1f, 0f)
            };
            var wires = new[]
            {
                Wire(1, -80f),
                Wire(2, -32f),
                Wire(4, 40f)
            };
            var hits = CarrierOps.DetectTraps(path, wires);
            Check(hits.Length == 1, "nearest traps=" + hits.Length);
            if (hits.Length == 1)
                Check(hits[0].Wire == 2, "nearest wire " + hits[0].Wire);
        }

        static void CableReturnsWithReplayTime()
        {
            Check(MathF.Abs(CarrierOps.ReturnAlong(0f, 20f)) < 0.001f, "no return before release");
            Check(MathF.Abs(CarrierOps.ReturnAlong(1f, 20f) - 4f) < 0.001f, "4 m after one second");
            Check(MathF.Abs(CarrierOps.ReturnAlong(10f, 20f) - 20f) < 0.001f, "return stops at rest");
            float mid = CarrierOps.ReturnAlong(2f, 20f);
            float again = CarrierOps.ReturnAlong(2f, 20f);
            Check(MathF.Abs(mid - again) < 0.0001f, "return distance is a function of elapsed time");
        }

        static void TrapEventText()
        {
            bool ok = CarrierOps.TryParseTrapEvent("Trap|7F|7C|3".AsSpan(), out bool release, out string ac, out string car, out int wire);
            Check(ok && !release && ac == "7F" && car == "7C" && wire == 3, "trap event");
            ok = CarrierOps.TryParseTrapEvent("Untrap|7F|7C|3".AsSpan(), out release, out ac, out car, out wire);
            Check(ok && release && wire == 3, "untrap event");
            ok = CarrierOps.TryParseTrapEvent("Message|7F|Has been destroyed.".AsSpan(), out _, out _, out _, out _);
            Check(!ok, "destruction text is not a trap");
            Check(CarrierOps.LoadoutHasTailhook("0:gun:1|6:TailHook_Multirole1:0"), "ifrit loadout");
            Check(CarrierOps.LoadoutHasTailhook("2:TailHook_Trainer:0"), "trainer loadout");
            Check(!CarrierOps.LoadoutHasTailhook("0:gun:100|1:AAM:2"), "no hook in loadout");
        }

        static void CarrierTrapAcmi()
        {
            string path = FindAcmi();
            if (path.Length == 0)
            {
                Console.WriteLine("skip carrier_trap.acmi");
                return;
            }
            string text = ReadAcmi(path);
            var ac = new List<CarrierOps.HookSample>();
            float time = 0f;
            float ay = 0f;
            bool haveAc = false;
            float tas = 0f, agl = 0f;
            bool haveTas = false, haveAgl = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '/') continue;
                if (line[0] == '#')
                {
                    time = Parse(line.AsSpan(1));
                    continue;
                }
                int comma = line.IndexOf(',');
                if (comma <= 0) continue;
                string id = line.Substring(0, comma);
                if (id != "7F" && id != "7C") continue;
                if (id != "7F") continue;
                var rest = line.AsSpan(comma + 1);
                float ny = 0f;
                bool pose = false;
                float ntas = tas, nagl = agl;
                bool nHaveTas = haveTas, nHaveAgl = haveAgl;
                foreach (var part in rest.ToString().Split(','))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = part.Substring(0, eq);
                    string val = part.Substring(eq + 1);
                    if (key == "T")
                    {
                        var b = val.Split('|');
                        if (b.Length >= 2)
                        {
                            ny = Parse(b[1]);
                            pose = true;
                        }
                    }
                    else if (key == "TAS")
                    {
                        ntas = Parse(val);
                        nHaveTas = true;
                    }
                    else if (key == "AGL")
                    {
                        nagl = Parse(val);
                        nHaveAgl = true;
                    }
                }
                if (id == "7F")
                {
                    if (pose)
                    {
                        ay = ny;
                        haveAc = true;
                    }
                    tas = ntas; agl = nagl; haveTas = nHaveTas; haveAgl = nHaveAgl;
                    if (haveAc)
                    {
                        ac.Add(new CarrierOps.HookSample
                        {
                            Time = time,
                            Speed = haveTas ? tas : 0f,
                            Agl = haveAgl ? agl : 0f,
                            Y = ay,
                            HasAgl = haveAgl,
                            HasTailhook = false
                        });
                    }
                }
            }

            var segs = CarrierOps.BuildHook(ac.ToArray());
            float a112 = CarrierOps.AmountAt(segs, 112f);
            float a140 = CarrierOps.AmountAt(segs, 140f);
            float a210 = CarrierOps.AmountAt(segs, 210f);
            float a226 = CarrierOps.AmountAt(segs, 226f);
            float a233 = CarrierOps.AmountAt(segs, 233f);
            Check(a112 > 0.8f, "hook down on the bolter approach " + a112.ToString("0.00"));
            Check(a140 < 0.25f, "hook up after the bolter " + a140.ToString("0.00"));
            Check(a210 > 0.8f, "hook down on the trap approach " + a210.ToString("0.00"));
            Check(a226 > 0.8f, "hook down at the trap " + a226.ToString("0.00"));
            Check(a233 < 0.25f, "hook up after the stop " + a233.ToString("0.00"));

            var local = BuildLocal(text);
            var hits = CarrierOps.DetectTraps(local.ToArray(), null);
            int traps = 0;
            CarrierOps.TrapHit trap = default;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].Catch < 180f) continue;
                traps++;
                trap = hits[i];
            }
            int early = 0;
            for (int i = 0; i < hits.Length; i++)
                if (hits[i].Catch < 180f) early++;
            Check(early == 0, "bolter counted as a trap");
            Check(traps == 1, "trap count " + traps);
            if (traps == 1)
            {
                Check(trap.Catch > 224.5f && trap.Catch < 226.8f, "catch " + trap.Catch.ToString("0.00"));
                Check(trap.Release > trap.Catch + 0.8f && trap.Release < 229.5f, "release " + trap.Release.ToString("0.00"));
            }

            Console.WriteLine("carrier_trap.acmi");
            for (int i = 1; i < segs.Length; i++)
            {
                string kind = segs[i].Rate > 0f ? "deploy" : "retract";
                Console.WriteLine("hook " + kind + " " + segs[i].Time.ToString("0.00", CultureInfo.InvariantCulture)
                    + " amount " + segs[i].Amount.ToString("0.00", CultureInfo.InvariantCulture));
            }
            Console.WriteLine("amount 112=" + a112.ToString("0.00", CultureInfo.InvariantCulture)
                + " 140=" + a140.ToString("0.00", CultureInfo.InvariantCulture)
                + " 210=" + a210.ToString("0.00", CultureInfo.InvariantCulture)
                + " 226=" + a226.ToString("0.00", CultureInfo.InvariantCulture)
                + " 233=" + a233.ToString("0.00", CultureInfo.InvariantCulture));
            for (int i = 0; i < hits.Length; i++)
            {
                Console.WriteLine("trap catch " + hits[i].Catch.ToString("0.00", CultureInfo.InvariantCulture)
                    + " release " + hits[i].Release.ToString("0.00", CultureInfo.InvariantCulture)
                    + " wire " + hits[i].Wire.ToString(CultureInfo.InvariantCulture)
                    + " at " + hits[i].X.ToString("0.0", CultureInfo.InvariantCulture)
                    + "," + hits[i].Z.ToString("0.0", CultureInfo.InvariantCulture));
            }
        }

        static List<CarrierOps.LocalSample> BuildLocal(string text)
        {
            var ship = new List<Pose>();
            var ac = new List<Pose>();
            var speed = new List<float>();
            var agl = new List<float>();
            var hasAgl = new List<bool>();
            float time = 0f;
            float tas = 0f, alt = 0f;
            bool gotTas = false, gotAgl = false;
            float ax = 0, ay = 0, az = 0, aqx = 0, aqy = 0, aqz = 0, aqw = 1;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line[0] == '#') { time = Parse(line.AsSpan(1)); continue; }
                int comma = line.IndexOf(',');
                if (comma <= 0) continue;
                string id = line.Substring(0, comma);
                if (id != "7F" && id != "7C") continue;
                bool pose = false, quat = false;
                float nx = 0, ny = 0, nz = 0, nqx = 0, nqy = 0, nqz = 0, nqw = 1;
                foreach (var part in line.Substring(comma + 1).Split(','))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = part.Substring(0, eq);
                    string val = part.Substring(eq + 1);
                    if (key == "T")
                    {
                        var b = val.Split('|');
                        if (b.Length >= 3)
                        {
                            nx = Parse(b[0]); ny = Parse(b[1]); nz = Parse(b[2]);
                            pose = true;
                        }
                    }
                    else if (key == "Q")
                    {
                        var b = val.Split('|');
                        if (b.Length >= 4)
                        {
                            nqx = Parse(b[0]); nqy = Parse(b[1]); nqz = Parse(b[2]); nqw = Parse(b[3]);
                            quat = true;
                        }
                    }
                    else if (key == "TAS" && id == "7F") { tas = Parse(val); gotTas = true; }
                    else if (key == "AGL" && id == "7F") { alt = Parse(val); gotAgl = true; }
                }
                if (!pose) continue;
                if (id == "7C")
                {
                    ship.Add(new Pose(time, nx, ny, nz, quat ? nqx : 0f, quat ? nqy : 0f, quat ? nqz : 0f, quat ? nqw : 1f));
                }
                else
                {
                    if (quat) { aqx = nqx; aqy = nqy; aqz = nqz; aqw = nqw; }
                    ax = nx; ay = ny; az = nz;
                    ac.Add(new Pose(time, ax, ay, az, aqx, aqy, aqz, aqw));
                    speed.Add(gotTas ? tas : 0f);
                    agl.Add(gotAgl ? alt : 0f);
                    hasAgl.Add(gotAgl);
                }
            }
            var hookSamples = new CarrierOps.HookSample[ac.Count];
            for (int i = 0; i < ac.Count; i++)
            {
                hookSamples[i] = new CarrierOps.HookSample
                {
                    Time = ac[i].T,
                    Speed = speed[i],
                    Agl = agl[i],
                    Y = ac[i].Y,
                    HasAgl = hasAgl[i]
                };
            }
            var segs = CarrierOps.BuildHook(hookSamples);
            var local = new List<CarrierOps.LocalSample>(ac.Count);
            int sp = 0;
            for (int i = 0; i < ac.Count; i++)
            {
                while (sp + 1 < ship.Count && ship[sp + 1].T <= ac[i].T) sp++;
                if (ship.Count == 0) continue;
                var sh = ship[sp];
                float dx = ac[i].X - sh.X;
                float dy = ac[i].Y - sh.Y;
                float dz = ac[i].Z - sh.Z;
                CarrierOps.InverseRotate(sh.Qx, sh.Qy, sh.Qz, sh.Qw, dx, dy, dz, out float lx, out float ly, out float lz);
                CarrierOps.Rotate(ac[i].Qx, ac[i].Qy, ac[i].Qz, ac[i].Qw, 0f, 0f, 1f, out float fx, out float fy, out float fz);
                CarrierOps.InverseRotate(sh.Qx, sh.Qy, sh.Qz, sh.Qw, fx, fy, fz, out float lfx, out _, out float lfz);
                lx -= lfx * CarrierOps.HookAft;
                lz -= lfz * CarrierOps.HookAft;
                float rel = 0f;
                if (i > 0)
                {
                    float dt = ac[i].T - ac[i - 1].T;
                    if (dt > 0.02f)
                    {
                        float ddx = lx - local[local.Count - 1].X;
                        float ddz = lz - local[local.Count - 1].Z;
                        rel = MathF.Sqrt(ddx * ddx + ddz * ddz) / dt;
                    }
                }
                else rel = speed[i];
                local.Add(new CarrierOps.LocalSample
                {
                    Time = ac[i].T,
                    X = lx,
                    Y = ly,
                    Z = lz,
                    Speed = rel,
                    Hook = CarrierOps.AmountAt(segs, ac[i].T),
                    Agl = agl[i],
                    HasAgl = hasAgl[i]
                });
            }
            return local;
        }

        readonly struct Pose
        {
            public readonly float T, X, Y, Z, Qx, Qy, Qz, Qw;
            public Pose(float t, float x, float y, float z, float qx, float qy, float qz, float qw)
            {
                T = t; X = x; Y = y; Z = z; Qx = qx; Qy = qy; Qz = qz; Qw = qw;
            }
        }

        static CarrierOps.HookSample Sample(float t, float speed, float agl, float y, bool recorded, float hook)
        {
            return new CarrierOps.HookSample
            {
                Time = t,
                Speed = speed,
                Agl = agl,
                Y = y,
                HasAgl = true,
                HasTailhook = recorded,
                Tailhook = hook
            };
        }

        static CarrierOps.HookSample[] Descent(float t0, float seconds, float speed, float agl)
        {
            int n = (int)(seconds / 0.5f) + 1;
            var s = new CarrierOps.HookSample[n];
            for (int i = 0; i < n; i++)
            {
                float t = t0 + i * 0.5f;
                s[i] = Sample(t, speed, agl, 500f - t * 5f, false, 0f);
            }
            return s;
        }

        static CarrierOps.LocalSample Local(float t, float x, float y, float z, float speed, float hook, float agl)
        {
            return new CarrierOps.LocalSample
            {
                Time = t,
                X = x,
                Y = y,
                Z = z,
                Speed = speed,
                Hook = hook,
                Agl = agl,
                HasAgl = true
            };
        }

        static CarrierOps.WireLine Wire(int number, float z)
        {
            return new CarrierOps.WireLine
            {
                Number = number,
                Ax = -20f,
                Ay = 10f,
                Az = z,
                Bx = 20f,
                By = 10f,
                Bz = z
            };
        }

        static string FindAcmi()
        {
            string env = Environment.GetEnvironmentVariable("CARRIER_TRAP") ?? "";
            string[] paths =
            {
                env,
                "/tmp/noreplay-research/carrier_trap.acmi",
                Path.Combine(AppContext.BaseDirectory, "carrier_trap.acmi")
            };
            for (int i = 0; i < paths.Length; i++)
            {
                if (!string.IsNullOrEmpty(paths[i]) && File.Exists(paths[i])) return paths[i];
            }
            return "";
        }

        static string ReadAcmi(string path)
        {
            using var fs = File.OpenRead(path);
            int b0 = fs.ReadByte();
            int b1 = fs.ReadByte();
            fs.Position = 0;
            if (b0 == 0x50 && b1 == 0x4B)
            {
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                using var sr = new StreamReader(zip.Entries[0].Open());
                return sr.ReadToEnd();
            }
            using var text = new StreamReader(fs);
            return text.ReadToEnd();
        }

        static float Parse(ReadOnlySpan<char> raw)
        {
            if (raw.IndexOf(',') < 0)
                return float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            Span<char> buf = stackalloc char[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                buf[i] = raw[i] == ',' ? '.' : raw[i];
            return float.Parse(buf, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static float Parse(string raw) => Parse(raw.AsSpan());

        static int _failed;

        static void Check(bool ok, string message)
        {
            if (ok) return;
            _failed++;
            Console.Error.WriteLine("FAIL " + message);
        }

        public static int Failed => _failed;
    }
}
