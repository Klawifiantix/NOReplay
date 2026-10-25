using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    internal class RecordGunBurst : RecordObject
    {
        static int _next;
        readonly Dictionary<int, Burst> _open = new();
        public static RecordGunBurst? Instance;

        RecordEngine? _engine;
        readonly List<int> _toEnd = new();

        RecordEngine? Recorder()
        {
            if (_engine == null && Record.Host != null)
                _engine = Record.Host.GetComponent<RecordEngine>();
            return _engine;
        }

        public void NotifyFire(Gun gun)
        {
            if (gun == null) return;
            int id = gun.GetInstanceID();
            if (!_open.ContainsKey(id))
            {
                var recorder = Recorder();
                if (recorder == null) return;
                var burst = Begin(gun, recorder);
                if (burst != null)
                {
                    _open[id] = burst;
                    Record.Log?.LogInfo("GunBurst start " + (gun.attachedUnit?.unitName ?? "?"));
                }
            }
        }
        Gun[] _guns = Array.Empty<Gun>();
        float _scan;
        FieldInfo? _ticks;
        FieldInfo? _muzzles;
        FieldInfo? _rate;
        FieldInfo? _muzzleVel;
        FieldInfo? _spread;
        FieldInfo? _info;
        FieldInfo? _lastFired;

        void Awake()
        {
            Instance = this;
            _engine = Record.Host != null ? Record.Host.GetComponent<RecordEngine>() : null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _ticks = typeof(Gun).GetField("ticksSinceTriggerPull", flags);
            _muzzles = typeof(Gun).GetField("muzzles", flags);
            _rate = typeof(Gun).GetField("fireRate", flags);
            _muzzleVel = typeof(Gun).GetField("muzzleVelocity", flags);
            _spread = typeof(Gun).GetField("bulletSpread", flags);
            _info = typeof(Weapon).GetField("info", flags);
            _lastFired = typeof(Gun).GetField("lastFired", flags);
        }

        void Update()
        {
            // Bursts start via NotifyFire. Aim and the end-of-burst poll share a 0.1s clock
            // so lastFired is not boxed every frame.
            if (_open.Count == 0) return;
            var recorder = Recorder();
            if (recorder == null) return;
            float now = Time.time;
            _toEnd.Clear();
            foreach (var pair in _open)
            {
                var burst = pair.Value;
                var gun = burst.Gun;
                if (gun == null)
                {
                    _toEnd.Add(pair.Key);
                    continue;
                }
                if (now - burst.LastAim < 0.1f)
                    continue;
                bool firing = false;
                if (_lastFired?.GetValue(gun) is float fired)
                    firing = Time.timeSinceLevelLoad - fired < 0.5f;
                else if (_ticks?.GetValue(gun) is int ticks)
                    firing = ticks > 0;
                if (firing)
                    WriteAim(burst, gun, recorder);
                else if (now - burst.LastAim > 0.15f)
                    _toEnd.Add(pair.Key);
            }
            for (int i = 0; i < _toEnd.Count; i++)
            {
                int id = _toEnd[i];
                if (_open.TryGetValue(id, out var b))
                {
                    End(b, recorder);
                    _open.Remove(id);
                }
            }
        }

        Burst? Begin(Gun gun, RecordEngine recorder)
        {
            var unit = gun.attachedUnit;
            if (unit == null) return null;
            long parent = unit.persistentID.Id + 1;
            if (recorder.unitObjects.TryGetValue(unit.persistentID.Id, out var go) && go != null)
            {
                var acmi = go.GetComponent<RecordUnit>();
                if (acmi != null) parent = acmi.tacviewId;
            }
            var info = _info?.GetValue(gun);
            float rpm = _rate?.GetValue(gun) is float r ? r : 0f;
            float muzzle = _muzzleVel?.GetValue(gun) is float m ? m : 0f;
            float spread = _spread?.GetValue(gun) is float s ? s : 0f;
            float drag = Field(info, "dragCoef");
            float grav = Field(info, "gravMult");
            if (grav == 0f) grav = 1f;
            var burst = new Burst
            {
                Host = gameObject.AddComponent<RecordObject>(),
                Gun = gun
            };
            burst.Host.unitId = (long)System.Threading.Interlocked.Increment(ref _next) | (1L << 34);
            burst.Host.tacviewId = burst.Host.unitId;
            burst.Host.props = new Dictionary<string, string>
            {
                { "Type", "Misc+GunBurst" },
                { "Parent", parent.ToString("X", CultureInfo.InvariantCulture) },
                { "Name", unit.definition != null ? unit.definition.unitName : unit.name },
                { "Rpm", rpm.ToString("0.##", CultureInfo.InvariantCulture) },
                { "Muzzle", muzzle.ToString("0.##", CultureInfo.InvariantCulture) },
                { "Drag", drag.ToString("0.####", CultureInfo.InvariantCulture) },
                { "Grav", grav.ToString("0.##", CultureInfo.InvariantCulture) },
                { "Spread", spread.ToString("0.###", CultureInfo.InvariantCulture) },
                { "Weapon", GunPath(unit.transform, gun.transform) }
            };
            recorder.invokeWriterUpdate(burst.Host);
            burst.Host.props.Clear();
            WriteAim(burst, gun, recorder);
            return burst;
        }

        void WriteAim(Burst burst, Gun gun, RecordEngine recorder)
        {
            var muzzles = _muzzles?.GetValue(gun) as Transform[];
            Transform? muzzle = muzzles != null && muzzles.Length > 0 ? muzzles[0] : gun.transform;
            if (muzzle == null)
            {
                burst.LastAim = Time.time;
                return;
            }
            Vector3 p = muzzle.position;
            try
            {
                p = new Vector3(muzzle.position.GlobalX(), muzzle.position.GlobalY(), muzzle.position.GlobalZ());
            }
            catch { }
            Vector3 f = muzzle.forward;
            if (f.sqrMagnitude < 0.01f) f = gun.transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            (float lat, float lon) = RecordGeo.CartesianToGeodetic(p.x, p.z);
            burst.Host.props["T"] = string.Join("|",
                lon.ToString(CultureInfo.InvariantCulture),
                lat.ToString(CultureInfo.InvariantCulture),
                p.y.ToString("0.##", CultureInfo.InvariantCulture),
                "0",
                (-pitch).ToString("0.##", CultureInfo.InvariantCulture),
                yaw.ToString("0.##", CultureInfo.InvariantCulture),
                p.x.ToString("0.##", CultureInfo.InvariantCulture),
                p.z.ToString("0.##", CultureInfo.InvariantCulture),
                yaw.ToString("0.##", CultureInfo.InvariantCulture));
            recorder.invokeWriterUpdate(burst.Host);
            burst.Host.props.Clear();
            burst.LastAim = Time.time;
        }

        void End(Burst burst, RecordEngine recorder)
        {
            burst.Host.props["Visible"] = "0";
            burst.Host.props["Type"] = "";
            recorder.invokeWriterUpdate(burst.Host);
            recorder.invokeWriterRemove(burst.Host);
            UnityEngine.Object.Destroy(burst.Host);
        }

        static string GunPath(Transform unit, Transform gun)
        {
            var parts = new List<string>();
            var t = gun;
            while (t != null && t != unit)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static float Field(object? obj, string name)
        {
            if (obj == null) return 0f;
            var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f?.GetValue(obj) is float v ? v : 0f;
        }

        sealed class Burst
        {
            public RecordObject Host = null!;
            public Gun Gun = null!;
            public float LastAim = -1f;
        }
    }
}