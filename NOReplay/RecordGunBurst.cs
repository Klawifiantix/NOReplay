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
            if (gun == null || !Record.Active || Plugin.CurrentDoc != null) return;
            if (gun.GetComponentInParent<ReplayGhost>() != null) return;
            int id = gun.GetInstanceID();
            var recorder = Recorder();
            if (recorder == null) return;
            if (_open.TryGetValue(id, out var existing))
            {
                existing.LastShot = Time.time;
                if (Time.time - existing.LastAim >= 0.08f)
                    WriteAim(existing, gun, recorder);
                if (existing.SingleShot || ClipSpent(gun))
                {
                    End(existing, recorder);
                    _open.Remove(id);
                }
                return;
            }
            var burst = Begin(gun, recorder);
            if (burst != null && burst.Open)
            {
                _open[id] = burst;
                Record.Log?.LogInfo("GunBurst start " + (gun.attachedUnit?.unitName ?? "?"));
            }
        }
        FieldInfo? _muzzles;
        FieldInfo? _rate;
        FieldInfo? _muzzleVel;
        FieldInfo? _spread;
        FieldInfo? _info;
        FieldInfo? _magCap;
        FieldInfo? _reloadTime;
        FieldInfo? _bulletsLoaded;

        void Awake()
        {
            Instance = this;
            _engine = Record.Host != null ? Record.Host.GetComponent<RecordEngine>() : null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _muzzles = typeof(Gun).GetField("muzzles", flags);
            _rate = typeof(Gun).GetField("fireRate", flags);
            _muzzleVel = typeof(Gun).GetField("muzzleVelocity", flags);
            _spread = typeof(Gun).GetField("bulletSpread", flags);
            _info = typeof(Weapon).GetField("info", flags);
            _magCap = typeof(Gun).GetField("magazineCapacity", flags);
            _reloadTime = typeof(Gun).GetField("reloadTime", flags);
            _bulletsLoaded = typeof(Gun).GetField("bulletsLoaded", flags);
        }

        void OnDisable() => FlushAll();

        void OnDestroy() => FlushAll();

        public override void Update()
        {
            if (_open.Count == 0) return;
            var recorder = Recorder();
            if (recorder == null) return;
            float now = Time.time;
            _toEnd.Clear();
            foreach (var pair in _open)
            {
                var burst = pair.Value;
                var gun = burst.Gun;
                if (gun == null || gun.attachedUnit == null || gun.attachedUnit.disabled
                    || now - burst.LastShot > burst.HoldWindow)
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

        void FlushAll()
        {
            if (_open.Count == 0) return;
            var recorder = Recorder();
            var pending = new List<Burst>(_open.Values);
            _open.Clear();
            if (recorder == null) return;
            for (int i = 0; i < pending.Count; i++)
            {
                try { End(pending[i], recorder); }
                catch { }
            }
        }

        public float GetEffectiveRpm(Gun gun, out bool singleShot, out float interval)
        {
            float fireRate = _rate?.GetValue(gun) is float r && r > 0f ? r : 600f;
            int mag = _magCap?.GetValue(gun) is int m ? m : 0;
            float reload = _reloadTime?.GetValue(gun) is float rt ? rt : 0f;
            float fireInterval = 60f / Mathf.Max(fireRate, 1f);
            if (mag == 1 && reload > 0f)
            {
                singleShot = true;
                interval = Mathf.Max(fireInterval, reload);
                return 60f / interval;
            }
            singleShot = fireInterval >= 0.25f;
            interval = fireInterval;
            return fireRate;
        }

        bool ClipSpent(Gun gun)
        {
            return _bulletsLoaded?.GetValue(gun) is int bl && bl <= 0
                && _reloadTime?.GetValue(gun) is float rt && rt > 0.15f;
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
            float rpm = GetEffectiveRpm(gun, out bool singleShot, out float interval);
            float muzzle = _muzzleVel?.GetValue(gun) is float m ? m : 0f;
            float spread = _spread?.GetValue(gun) is float s ? s : 0f;
            float drag = Field(info, "dragCoef");
            float grav = Field(info, "gravMult");
            if (grav == 0f) grav = 1f;
            var burst = new Burst
            {
                Host = gameObject.AddComponent<RecordObject>(),
                Gun = gun,
                LastShot = Time.time,
                HoldWindow = Mathf.Clamp(interval * 1.6f, 0.06f, 0.25f),
                SingleShot = singleShot,
                Open = true
            };
            burst.Host.unitId = (long)System.Threading.Interlocked.Increment(ref _next) | (1L << 35);
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
                { "Weapon", TurretKeys.Sanitize(GunPath(unit.transform, gun.transform)) },
                { "Gun", GunIndex(unit, gun).ToString(CultureInfo.InvariantCulture) }
            };
            recorder.invokeWriterUpdate(burst.Host);
            burst.Host.props.Clear();
            WriteAim(burst, gun, recorder);
            if (singleShot || ClipSpent(gun))
            {
                End(burst, recorder);
                burst.Open = false;
            }
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

        static int GunIndex(Unit unit, Gun gun)
        {
            var guns = TurretKeys.FindGuns(unit);
            for (int i = 0; i < guns.Length; i++)
                if (guns[i] == gun) return i;
            return -1;
        }

        static string GunPath(Transform unit, Transform gun)
        {
            return TurretKeys.Relative(unit, gun);
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
            public float LastShot;
            public float HoldWindow;
            public bool SingleShot;
            public bool Open;
        }
    }
}
