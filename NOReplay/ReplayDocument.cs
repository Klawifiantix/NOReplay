using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    public class ReplayDocument
    {
        public string Title = "";
        public string MapId = "";
        public float TimeOfDay = -1f;
        public float Conditions = -1f;
        public float CloudHeight = -1f;
        public float WindSpeed = -1f;
        public float WindTurbulence = -1f;
        public float MoonPhase = -1f;
        public string DataRecorder = "";
        public float Duration;
        public List<ReplayObject> Objects = new List<ReplayObject>();
        public List<CarrierTrap> Traps = new List<CarrierTrap>();
        public List<FeedLog.Item> Feeds = new List<FeedLog.Item>();

        public void IndexChildren()
        {
            var byId = new Dictionary<string, ReplayObject>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (o == null) continue;
                o.OrdnanceLaunches = null;
                o.GunBursts = null;
                o.PartBreaks = null;
                if (string.IsNullOrEmpty(o.Id) || byId.ContainsKey(o.Id)) continue;
                byId[o.Id] = o;
            }
            var launches = new Dictionary<string, List<ReplayObject>>(StringComparer.OrdinalIgnoreCase);
            var bursts = new Dictionary<string, List<ReplayObject>>(StringComparer.OrdinalIgnoreCase);
            var parts = new Dictionary<string, List<ReplayObject>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (o == null || string.IsNullOrEmpty(o.ParentId)) continue;
                if (!byId.ContainsKey(o.ParentId)) continue;
                if (o.EventTime() < 0f && !IsPartBreak(o.Type) && !IsGunBurst(o.Type)) continue;
                if (IsPartBreak(o.Type)) AddChild(parts, o.ParentId, o);
                else if (IsGunBurst(o.Type)) AddChild(bursts, o.ParentId, o);
                else if (IsOrdnanceLaunch(o.Type)) AddChild(launches, o.ParentId, o);
            }
            Stamp(byId, launches, 0);
            Stamp(byId, bursts, 1);
            Stamp(byId, parts, 2);
        }

        static void AddChild(Dictionary<string, List<ReplayObject>> map, string parentId, ReplayObject child)
        {
            if (!map.TryGetValue(parentId, out var list))
            {
                list = new List<ReplayObject>();
                map[parentId] = list;
            }
            list.Add(child);
        }

        static void Stamp(Dictionary<string, ReplayObject> byId, Dictionary<string, List<ReplayObject>> map, int kind)
        {
            foreach (var pair in map)
            {
                if (!byId.TryGetValue(pair.Key, out var parent)) continue;
                var list = pair.Value;
                list.Sort(CompareEvent);
                var arr = list.ToArray();
                if (kind == 0) parent.OrdnanceLaunches = arr;
                else if (kind == 1) parent.GunBursts = arr;
                else parent.PartBreaks = arr;
            }
        }

        static int CompareEvent(ReplayObject a, ReplayObject b)
        {
            int c = a.EventTime().CompareTo(b.EventTime());
            if (c != 0) return c;
            return string.CompareOrdinal(a.Id, b.Id);
        }

        public static bool IsGunBurst(string? type)
        {
            return (type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsPartBreak(string? type)
        {
            return (type ?? "").IndexOf("PartBreak", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsOrdnanceLaunch(string? type)
        {
            string t = type ?? "";
            if (t.Length == 0) return false;
            if (IsGunBurst(t) || IsPartBreak(t)) return false;
            if (t.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Decoy", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Rocket", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Torpedo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Weapon+", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static int CountAtOrBefore(ReplayObject[] items, float time)
        {
            int lo = 0, hi = items.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (items[mid].EventTime() <= time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }

    public class ReplayObject
    {
        public string Id = "";
        public string? Name;
        public string? Type;
        public string? Coalition;
        public string? Pilot;
        public string? CallSign;
        public string? Registration;
        public string? Loadout;
        public string? Livery;
        /// <summary>MapUniqueName of a built-in map building. Empty on mission-placed buildings and on older files.</summary>
        public string? Map;
        public string? ParentId;
        public string? Weapon;
        public int GunIndex = -1;
        /// <summary>Missile blast yield in kilograms of TNT. Negative when the file has no Yield property.</summary>
        public float Yield = -1f;
        public float Rpm;
        public float Muzzle;
        public float Drag;
        public float Grav = 1f;
        public float Spread;
        public bool HasTurrets { get; private set; }
        public float SpawnAt = -1f;
        public float RemovedAt = -1f;
        /// <summary>Replay time of Sinking=1. -1 when the file has no kill marker. The hull may keep moving until <see cref="RemovedAt"/>.</summary>
        public float KilledAt = -1f;
        /// <summary>Replay time of Dying=1. The unit is disabled here. The wreck may be later.</summary>
        public float DyingAt = -1f;
        /// <summary>Replay time of Boom=1. Negative when the file has no blast mark.</summary>
        public float BoomAt = -1f;
        public bool HasPart;
        public byte PartId;
        public string? PartKind;
        public string? PartPath;
        public int PartSeg = -1;
        public int PartRotor = -1;
        public float PartVx, PartVy, PartVz;
        public ReplayObject[]? OrdnanceLaunches;
        public ReplayObject[]? GunBursts;
        public ReplayObject[]? PartBreaks;
        public float NudgeX;
        public float NudgeZ;
        public List<ReplaySample> Samples = new List<ReplaySample>();
        int _sampleCursor = -1;

        readonly struct SampleTimes : SampleIndex.ISampleTimes
        {
            public readonly List<ReplaySample> Samples;
            public SampleTimes(List<ReplaySample> samples) { Samples = samples; }
            public int Count => Samples.Count;
            public float TimeAt(int index) => Samples[index].Time;
        }

        public bool TrySample(float t, out ReplaySample s)
        {
            s = default;
            int n = Samples.Count;
            if (n == 0) return false;
            if (n == 1)
            {
                s = Samples[0];
                _sampleCursor = 0;
                return true;
            }
            if (t <= Samples[0].Time)
            {
                s = Samples[0];
                _sampleCursor = 0;
                return true;
            }
            if (t >= Samples[n - 1].Time)
            {
                s = Samples[n - 1];
                _sampleCursor = n - 2;
                return true;
            }
            int lo = SampleIndex.Bracket(new SampleTimes(Samples), t, ref _sampleCursor);
            int hi = lo + 1;
            var a = Samples[lo];
            var b = Samples[hi];
            float span = b.Time - a.Time;
            float u = span > 0.0001f ? (t - a.Time) / span : 0f;
            OrientationOf(a, out float ax, out float ay, out float az, out float aw);
            OrientationOf(b, out float bx, out float by, out float bz, out float bw);
            OrientationMath.Slerp(ax, ay, az, aw, bx, by, bz, bw, u,
                out float qx, out float qy, out float qz, out float qw);
            s = new ReplaySample
            {
                Time = t,
                X = Mathf.Lerp(a.X, b.X, u),
                Y = Mathf.Lerp(a.Y, b.Y, u),
                Z = Mathf.Lerp(a.Z, b.Z, u),
                Pitch = Mathf.LerpAngle(a.Pitch, b.Pitch, u),
                Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, u),
                Roll = Mathf.LerpAngle(a.Roll, b.Roll, u),
                HasGear = a.HasGear || b.HasGear,
                Gear = a.HasGear ? a.Gear : b.Gear,
                HasTas = a.HasTas || b.HasTas,
                Tas = a.HasTas ? a.Tas : b.Tas,
                HasThrottle = a.HasThrottle || b.HasThrottle,
                Throttle = a.HasThrottle ? a.Throttle : b.Throttle,
                HasRpm = a.HasRpm || b.HasRpm,
                Rpm = a.HasRpm && b.HasRpm ? Mathf.Lerp(a.Rpm, b.Rpm, u) : (a.HasRpm ? a.Rpm : b.Rpm),
                HasJetRpm = a.HasJetRpm || b.HasJetRpm,
                JetRpm = a.HasJetRpm && b.HasJetRpm ? Mathf.Lerp(a.JetRpm, b.JetRpm, u) : (a.HasJetRpm ? a.JetRpm : b.JetRpm),
                HasTurbineRpm = a.HasTurbineRpm || b.HasTurbineRpm,
                TurbineRpm = a.HasTurbineRpm && b.HasTurbineRpm ? Mathf.Lerp(a.TurbineRpm, b.TurbineRpm, u) : (a.HasTurbineRpm ? a.TurbineRpm : b.TurbineRpm),
                HasAgl = a.HasAgl || b.HasAgl,
                Agl = a.HasAgl && b.HasAgl ? Mathf.Lerp(a.Agl, b.Agl, u) : (a.HasAgl ? a.Agl : b.Agl),
                HasTailhook = a.HasTailhook || b.HasTailhook,
                Tailhook = a.HasTailhook && b.HasTailhook ? Mathf.Lerp(a.Tailhook, b.Tailhook, u) : (a.HasTailhook ? a.Tailhook : b.Tailhook),
                HasTilt = a.HasTilt || b.HasTilt,
                TiltCount = a.HasTilt && b.HasTilt ? (a.TiltCount < b.TiltCount ? a.TiltCount : b.TiltCount) : (a.HasTilt ? a.TiltCount : b.TiltCount),
                Tilt0 = BlendTilt(a, b, 0, u),
                Tilt1 = BlendTilt(a, b, 1, u),
                Tilt2 = BlendTilt(a, b, 2, u),
                Tilt3 = BlendTilt(a, b, 3, u),
                HasQuat = true,
                Qx = qx,
                Qy = qy,
                Qz = qz,
                Qw = qw
            };
            return true;
        }

        static float BlendTilt(ReplaySample a, ReplaySample b, int index, float u)
        {
            bool ha = a.HasTilt && index < a.TiltCount;
            bool hb = b.HasTilt && index < b.TiltCount;
            if (ha && hb) return TiltText.LerpAngle(a.TiltAt(index), b.TiltAt(index), u);
            if (ha) return a.TiltAt(index);
            if (hb) return b.TiltAt(index);
            return 0f;
        }

        static void OrientationOf(ReplaySample s, out float x, out float y, out float z, out float w)
        {
            if (s.HasQuat)
            {
                x = s.Qx;
                y = s.Qy;
                z = s.Qz;
                w = s.Qw;
                return;
            }
            OrientationMath.FromPlaybackEuler(s.Pitch, s.Yaw, s.Roll, out x, out y, out z, out w);
        }

        Dictionary<string, TurretChannel>? _turrets;

        public void AddTurretKey(float time, string key, float traverse, float elevation)
        {
            if (string.IsNullOrEmpty(key)) return;
            _turrets ??= new Dictionary<string, TurretChannel>(System.StringComparer.Ordinal);
            if (!_turrets.TryGetValue(key, out var ch))
            {
                ch = new TurretChannel();
                _turrets[key] = ch;
            }
            int n = ch.Times.Count;
            if (n > 0 && Mathf.Abs(ch.Times[n - 1] - time) < 0.0001f)
            {
                ch.Traverse[n - 1] = traverse;
                ch.Elevation[n - 1] = elevation;
            }
            else
            {
                ch.Times.Add(time);
                ch.Traverse.Add(traverse);
                ch.Elevation.Add(elevation);
            }
            if (ch.Times.Count > 1
                || Mathf.Abs(traverse) > TurretStillDegrees
                || Mathf.Abs(elevation) > TurretStillDegrees)
                ch.Moves = true;
            HasTurrets = true;
            if (ch.Moves) HasMovingTurrets = true;
        }

        /// <summary>
        /// True when some turret channel moved, or holds a non-zero pose.
        /// One 0.00|0.00 spawn sample does not count.
        /// </summary>
        public bool HasMovingTurrets { get; private set; }

        const float TurretStillDegrees = 0.05f;

        /// <summary>
        /// Exact key, then a path suffix either way, then the only channel
        /// when the unit recorded a single turret.
        /// </summary>
        TurretChannel? FindTurretChannel(string key)
        {
            if (_turrets == null || string.IsNullOrEmpty(key)) return null;
            if (_turrets.TryGetValue(key, out var exact)) return exact;
            foreach (var pair in _turrets)
            {
                string k = pair.Key;
                if (string.IsNullOrEmpty(k)) continue;
                if (EndsWithSegment(k, key) || EndsWithSegment(key, k))
                    return pair.Value;
            }
            if (_turrets.Count == 1)
            {
                foreach (var pair in _turrets)
                    return pair.Value;
            }
            return null;
        }

        static bool EndsWithSegment(string path, string key)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(key)) return false;
            int need = key.Length + 1;
            if (path.Length < need || path[path.Length - need] != '/') return false;
            return string.Compare(path, path.Length - key.Length, key, 0, key.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// A channel owns the visual pose when it has several samples or any
        /// angle away from zero. One static (0, 0) baseline does not.
        /// </summary>
        public bool TurretChannelMoves(string key)
        {
            var ch = FindTurretChannel(key);
            return ch != null && ch.Moves;
        }

        public bool TryTurret(string key, float time, out float traverse, out float elevation)
        {
            traverse = 0f;
            elevation = 0f;
            var ch = FindTurretChannel(key);
            if (ch == null) return false;
            int n = ch.Times.Count;
            if (n == 0) return false;
            if (n == 1 || time <= ch.Times[0])
            {
                traverse = ch.Traverse[0];
                elevation = ch.Elevation[0];
                ch.Cursor = 0;
                return true;
            }
            if (time >= ch.Times[n - 1])
            {
                traverse = ch.Traverse[n - 1];
                elevation = ch.Elevation[n - 1];
                ch.Cursor = n - 2;
                return true;
            }
            int lo = SampleIndex.Bracket(ch.Times, time, ref ch.Cursor);
            int hi = lo + 1;
            float span = ch.Times[hi] - ch.Times[lo];
            float u = span > 0.0001f ? (time - ch.Times[lo]) / span : 0f;
            traverse = Mathf.LerpAngle(ch.Traverse[lo], ch.Traverse[hi], u);
            elevation = Mathf.LerpAngle(ch.Elevation[lo], ch.Elevation[hi], u);
            return true;
        }

        sealed class TurretChannel
        {
            public readonly List<float> Times = new List<float>();
            public readonly List<float> Traverse = new List<float>();
            public readonly List<float> Elevation = new List<float>();
            public int Cursor = -1;
            public bool Moves;
        }

        Dictionary<string, List<LaserClock.Event>>? _laserBeams;

        public void AddLaserLine(float time, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var scratch = new List<LaserClock.Event>();
            if (LaserClock.Parse(text, time, scratch) == 0) return;
            _laserBeams ??= new Dictionary<string, List<LaserClock.Event>>(StringComparer.Ordinal);
            for (int i = 0; i < scratch.Count; i++)
            {
                var ev = scratch[i];
                if (string.IsNullOrEmpty(ev.Key)) continue;
                if (!_laserBeams.TryGetValue(ev.Key, out var list))
                {
                    list = new List<LaserClock.Event>();
                    _laserBeams[ev.Key] = list;
                }
                list.Add(ev);
            }
        }

        public int LaserCount(string key)
        {
            var list = FindLaser(key);
            return list != null ? list.Count : 0;
        }

        public bool TryLaser(string key, float time, out LaserClock.View view)
        {
            view = default;
            var list = FindLaser(key);
            if (list == null) return false;
            view = LaserClock.At(list, time, RemovedAt);
            return true;
        }

        List<LaserClock.Event>? FindLaser(string key)
        {
            if (_laserBeams == null || string.IsNullOrEmpty(key)) return null;
            if (_laserBeams.TryGetValue(key, out var exact)) return exact;
            foreach (var pair in _laserBeams)
            {
                string k = pair.Key;
                if (string.IsNullOrEmpty(k)) continue;
                if (EndsWithSegment(k, key) || EndsWithSegment(key, k))
                    return pair.Value;
            }
            if (_laserBeams.Count == 1)
            {
                foreach (var pair in _laserBeams)
                    return pair.Value;
            }
            return null;
        }

        Dictionary<string, List<ChuteClock.Event>>? _chutes;

        public bool HasChute => _chutes != null && _chutes.Count > 0;

        public void AddChuteLine(float time, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var scratch = new List<ChuteClock.Event>();
            if (ChuteClock.Parse(text, time, scratch) == 0) return;
            _chutes ??= new Dictionary<string, List<ChuteClock.Event>>(StringComparer.Ordinal);
            for (int i = 0; i < scratch.Count; i++)
            {
                var ev = scratch[i];
                if (string.IsNullOrEmpty(ev.Key)) continue;
                if (!_chutes.TryGetValue(ev.Key, out var list))
                {
                    list = new List<ChuteClock.Event>();
                    _chutes[ev.Key] = list;
                }
                list.Add(ev);
            }
        }

        public int ChuteCount(string key)
        {
            var list = FindChute(key);
            return list != null ? list.Count : 0;
        }

        public float ChuteDeploy(string key)
        {
            var list = FindChute(key);
            if (list == null) return -1f;
            for (int i = 0; i < list.Count; i++)
                if (list[i].On && list[i].Kind == ChuteClock.Kind.Canopy) return list[i].Time;
            return -1f;
        }

        public bool TryChute(string key, float time, out ChuteClock.View view)
        {
            view = default;
            var list = FindChute(key);
            if (list == null) return false;
            view = ChuteClock.At(list, time, RemovedAt);
            return true;
        }

        public bool ChuteCrossed(string key, float prev, float now, float dt, bool paused)
        {
            return ChuteClock.Crossed(FindChute(key), prev, now, dt, paused);
        }

        List<ChuteClock.Event>? FindChute(string key)
        {
            if (_chutes == null || string.IsNullOrEmpty(key)) return null;
            if (_chutes.TryGetValue(key, out var exact)) return exact;
            if (_chutes.Count == 1 && (key.Length > 0 && key[0] == 'c'))
            {
                foreach (var pair in _chutes)
                {
                    if (pair.Key.Length > 0 && pair.Key[0] == 'c')
                        return pair.Value;
                }
            }
            return null;
        }

        Dictionary<string, List<DoorClock.Event>>? _doors;
        Dictionary<string, List<ChuteClock.Event>>? _glass;

        public bool HasDoors => _doors != null && _doors.Count > 0;
        public bool HasGlass => _glass != null && _glass.Count > 0;

        public void AddDoorLine(float time, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var scratch = new List<DoorClock.Event>();
            if (DoorClock.Parse(text, time, scratch) == 0) return;
            _doors ??= new Dictionary<string, List<DoorClock.Event>>(StringComparer.Ordinal);
            for (int i = 0; i < scratch.Count; i++)
            {
                var ev = scratch[i];
                if (string.IsNullOrEmpty(ev.Key)) continue;
                if (!_doors.TryGetValue(ev.Key, out var list))
                {
                    list = new List<DoorClock.Event>();
                    _doors[ev.Key] = list;
                }
                list.Add(ev);
            }
        }

        public void AddGlassLine(float time, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var scratch = new List<ChuteClock.Event>();
            if (ChuteClock.Parse(text, time, scratch) == 0) return;
            _glass ??= new Dictionary<string, List<ChuteClock.Event>>(StringComparer.Ordinal);
            for (int i = 0; i < scratch.Count; i++)
            {
                var ev = scratch[i];
                if (string.IsNullOrEmpty(ev.Key)) continue;
                if (!_glass.TryGetValue(ev.Key, out var list))
                {
                    list = new List<ChuteClock.Event>();
                    _glass[ev.Key] = list;
                }
                list.Add(ev);
            }
        }

        public int DoorCount()
        {
            if (_doors == null) return 0;
            int n = 0;
            foreach (var pair in _doors) n += pair.Value.Count;
            return n;
        }

        public int GlassCount()
        {
            if (_glass == null) return 0;
            int n = 0;
            foreach (var pair in _glass) n += pair.Value.Count;
            return n;
        }

        public bool TryDoor(string key, float time, out DoorClock.View view)
        {
            view = default;
            if (_doors == null || !_doors.TryGetValue(key, out var list)) return false;
            view = DoorClock.At(list, time, RemovedAt);
            return view.Present;
        }

        public float GlassStart(string key)
        {
            if (_glass == null || !_glass.TryGetValue(key, out var list) || list.Count == 0) return -1f;
            return list[0].Time;
        }

        public bool TryGlass(string key, float time, out ChuteClock.View view)
        {
            view = default;
            if (_glass == null || !_glass.TryGetValue(key, out var list)) return false;
            view = ChuteClock.At(list, time, RemovedAt);
            return true;
        }

        public bool GlassCrossed(string key, float prev, float now, float dt, bool paused)
        {
            if (_glass == null || !_glass.TryGetValue(key, out var list)) return false;
            return ChuteClock.Crossed(list, prev, now, dt, paused);
        }

        public IEnumerable<string> DoorKeys()
        {
            if (_doors == null) yield break;
            foreach (var pair in _doors) yield return pair.Key;
        }

        public IEnumerable<string> GlassKeys()
        {
            if (_glass == null) yield break;
            foreach (var pair in _glass) yield return pair.Key;
        }

        public float EventTime()
        {
            if (SpawnAt >= 0f) return SpawnAt;
            if (Samples.Count > 0) return Samples[0].Time;
            return -1f;
        }

        public float BurstEnd()
        {
            if (RemovedAt >= 0f) return RemovedAt;
            if (Samples.Count > 0) return Samples[Samples.Count - 1].Time;
            float start = EventTime();
            return start >= 0f ? start : 0f;
        }

        public bool IsParkedStatic()
        {
            int n = Samples.Count;
            if (n == 0) return false;
            float maxTas = 0f;
            float x0 = Samples[0].X, z0 = Samples[0].Z;
            float maxD = 0f;
            int step = n > 40 ? n / 40 : 1;
            for (int i = 0; i < n; i += step)
            {
                var s = Samples[i];
                if (s.HasTas && s.Tas > maxTas) maxTas = s.Tas;
                float dx = s.X - x0, dz = s.Z - z0;
                float d = dx * dx + dz * dz;
                if (d > maxD) maxD = d;
            }
            return maxTas < 8f && maxD < 400f;
        }
    }

    public struct ReplaySample
    {
        public float Time;
        public float X, Y, Z;
        public float Pitch, Yaw, Roll;
        public bool HasQuat;
        public float Qx, Qy, Qz, Qw;
        public bool HasGear;
        public float Gear;
        public bool HasTas;
        public float Tas;
        public bool HasThrottle;
        public float Throttle;
        public bool HasRpm;
        public float Rpm;
        public bool HasJetRpm;
        public float JetRpm;
        public bool HasTurbineRpm;
        public float TurbineRpm;
        public bool HasAgl;
        public float Agl;
        public bool HasTailhook;
        public float Tailhook;
        public bool HasTilt;
        public int TiltCount;
        public float Tilt0, Tilt1, Tilt2, Tilt3;

        public float TiltAt(int index)
        {
            return index == 0 ? Tilt0 : index == 1 ? Tilt1 : index == 2 ? Tilt2 : Tilt3;
        }

        public ReplaySample Clone() => this;
    }

    public struct CarrierTrap
    {
        public string AircraftId;
        public string CarrierId;
        public int Wire;
        public float Catch;
        public float Release;
        public bool Recorded;
    }
}