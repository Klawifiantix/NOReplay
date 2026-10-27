using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        AmmoStation[]? _stations;
        int _ammoStamp = -1;
        int _ammoEpoch = -1;
        float _ammoWaitFrom = -1f;
        int[] _frameLaunches = Array.Empty<int>();
        int[] _frameRounds = Array.Empty<int>();
        bool _ammoLogged;
        int _ammoMissLog;

        static FieldInfo? _firedField;
        static bool _firedLooked;
        static FieldInfo? _gunLoaded;
        static bool _gunLoadedLooked;
        static FieldInfo? _launcherReloading;
        static bool _launcherReloadingLooked;
        readonly Dictionary<int, int> _weaponFull = new Dictionary<int, int>();

        sealed class AmmoStation
        {
            public WeaponStation Station = null!;
            public string[] Names = Array.Empty<string>();
            public bool TakesLaunches;
            public int Capacity = 64;
            public MountedMissile[] Missiles = Array.Empty<MountedMissile>();
            public Renderer[][] MissileRends = Array.Empty<Renderer[]>();
            public MissileLauncher[] Launchers = Array.Empty<MissileLauncher>();
            public int[] LauncherFull = Array.Empty<int>();
            public Gun[] Guns = Array.Empty<Gun>();
            public int[] GunFull = Array.Empty<int>();
            public ReplayObject[][] Bursts = Array.Empty<ReplayObject[]>();
            public float[] LaunchTimes = Array.Empty<float>();
            public int LastLaunches = int.MinValue;
            public int LastRounds = int.MinValue;
        }

        void TickAmmo()
        {
            if (_ordnance || Track == null) return;
            if (!EnsureAmmo()) return;
            var stations = _stations;
            if (stations == null || stations.Length == 0) return;
            float time = Plugin.Clock.Time;
            int epoch = _structEpoch;
            int n = stations.Length;
            if (_frameLaunches.Length != n)
            {
                _frameLaunches = new int[n];
                _frameRounds = new int[n];
            }
            bool rebuild = epoch != _ammoEpoch;
            for (int i = 0; i < n; i++)
            {
                int launches = CountLaunches(stations[i], time);
                int rounds = CountRounds(stations[i], time);
                _frameLaunches[i] = launches;
                _frameRounds[i] = rounds;
                if (launches != stations[i].LastLaunches)
                    rebuild = true;
            }
            if (rebuild)
            {
                WakeInactive();
                _ammoHide.Clear();
                _ammoInactive.Clear();
                _ammoInactiveIds.Clear();
                for (int i = 0; i < n; i++)
                    PaintMissiles(stations[i], _frameLaunches[i]);
                _ammoEpoch = epoch;
            }
            for (int i = 0; i < n; i++)
            {
                var st = stations[i];
                if (_frameLaunches[i] == st.LastLaunches && _frameRounds[i] == st.LastRounds)
                    continue;
                PaintCounts(st, _frameLaunches[i], time);
                st.LastLaunches = _frameLaunches[i];
                st.LastRounds = _frameRounds[i];
            }
        }

        bool EnsureAmmo()
        {
            var unit = StructureUnit();
            if (unit == null || unit.weaponStations == null) return false;
            int stamp = AmmoStamp(unit);
            if (_stations != null && stamp == _ammoStamp) return true;
            int weapons = WeaponCount(unit);
            if (weapons == 0)
            {
                if (_ammoWaitFrom < 0f) _ammoWaitFrom = Time.unscaledTime;
                if (Time.unscaledTime - _ammoWaitFrom < 8f)
                    return false;
            }
            BindAmmo(unit);
            _ammoStamp = stamp;
            return _stations != null;
        }

        static int WeaponCount(Unit unit)
        {
            var list = unit.weaponStations;
            if (list == null) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var ws = list[i];
                if (ws?.Weapons == null) continue;
                n += ws.Weapons.Count;
            }
            return n;
        }

        static int AmmoStamp(Unit unit)
        {
            var list = unit.weaponStations;
            if (list == null || list.Count == 0) return 0;
            int stamp = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var ws = list[i];
                int c = ws?.Weapons != null ? ws.Weapons.Count : 0;
                stamp = stamp * 31 + c + 1;
            }
            return stamp;
        }

        void BindAmmo(Unit unit)
        {
            var list = unit.weaponStations;
            if (list == null || list.Count == 0)
            {
                _stations = Array.Empty<AmmoStation>();
                return;
            }
            var guns = TurretKeys.FindGuns(unit);
            var paths = new string[guns.Length];
            for (int i = 0; i < guns.Length; i++)
            {
                var gun = guns[i];
                paths[i] = gun == null
                    ? ""
                    : TurretKeys.Sanitize(TurretKeys.Relative(unit.transform, gun.transform));
            }
            var stations = new AmmoStation[list.Count];
            int used = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var ws = list[i];
                if (ws == null) continue;
                try { ws.AccountAmmo(); } catch { }
                var slot = new AmmoStation { Station = ws };
                FillWeapons(slot);
                slot.Names = Candidates(ws);
                slot.TakesLaunches = TakesLaunches(ws, slot);
                int cap = ws.FullAmmo;
                if (cap <= 0) cap = slot.Missiles.Length + slot.LauncherFull.Length;
                if (cap <= 0) cap = ws.Weapons != null ? ws.Weapons.Count : 0;
                if (cap <= 0) cap = 64;
                slot.Capacity = cap;
                AttachBursts(slot, guns, paths, Track?.GunBursts);
                stations[used++] = slot;
            }
            if (used != stations.Length)
            {
                var trimmed = new AmmoStation[used];
                for (int i = 0; i < used; i++) trimmed[i] = stations[i];
                stations = trimmed;
            }
            AssignLaunches(stations, Track?.OrdnanceLaunches);
            for (int i = 0; i < stations.Length; i++)
            {
                stations[i].LastLaunches = int.MinValue;
                stations[i].LastRounds = int.MinValue;
            }
            _stations = stations;
            _ammoEpoch = -1;
            if (!_ammoLogged)
            {
                _ammoLogged = true;
                int launches = Track?.OrdnanceLaunches != null ? Track.OrdnanceLaunches.Length : 0;
                int bursts = Track?.GunBursts != null ? Track.GunBursts.Length : 0;
                Plugin.Log.LogInfo("Munition " + (Track?.Name ?? name)
                    + " stations=" + stations.Length + " starts=" + launches + " kanonen=" + bursts);
            }
        }

        void FillWeapons(AmmoStation slot)
        {
            var weapons = slot.Station.Weapons;
            if (weapons == null) return;
            var missiles = new List<MountedMissile>();
            var launchers = new List<MissileLauncher>();
            var launcherFull = new List<int>();
            var guns = new List<Gun>();
            var gunFull = new List<int>();
            for (int i = 0; i < weapons.Count; i++)
            {
                var w = weapons[i];
                if (w == null) continue;
                if (w is MountedMissile mm) missiles.Add(mm);
                else if (w is MissileLauncher launcher)
                {
                    launchers.Add(launcher);
                    launcherFull.Add(RememberFull(launcher, launcher.GetFullAmmo(), launcher.ammo));
                }
                else if (w is Gun gun)
                {
                    guns.Add(gun);
                    gunFull.Add(RememberFull(gun, gun.GetFullAmmo(), gun.ammo));
                }
            }
            slot.Missiles = missiles.ToArray();
            slot.MissileRends = new Renderer[slot.Missiles.Length][];
            for (int i = 0; i < slot.Missiles.Length; i++)
            {
                var mm = slot.Missiles[i];
                slot.MissileRends[i] = mm != null
                    ? mm.GetComponentsInChildren<Renderer>(true)
                    : Array.Empty<Renderer>();
            }
            slot.Launchers = launchers.ToArray();
            slot.LauncherFull = launcherFull.ToArray();
            slot.Guns = guns.ToArray();
            slot.GunFull = gunFull.ToArray();
            slot.Bursts = new ReplayObject[slot.Guns.Length][];
            for (int i = 0; i < slot.Bursts.Length; i++)
                slot.Bursts[i] = Array.Empty<ReplayObject>();
        }

        static bool TakesLaunches(WeaponStation station, AmmoStation slot)
        {
            if (slot.Missiles.Length == 0 && slot.Launchers.Length == 0) return false;
            var info = station.WeaponInfo;
            if (info != null && info.gun && !info.missile && !info.bomb) return false;
            if (info != null && (info.troops || info.sling) && !info.missile && !info.bomb && slot.Missiles.Length == 0)
                return false;
            return true;
        }

        void AttachBursts(AmmoStation slot, Gun[] all, string[] paths, ReplayObject[]? bursts)
        {
            if (bursts == null || slot.Guns.Length == 0) return;
            var buckets = new List<ReplayObject>[slot.Guns.Length];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<ReplayObject>();
            for (int b = 0; b < bursts.Length; b++)
            {
                var burst = bursts[b];
                if (burst == null) continue;
                int local = MatchGun(burst, slot.Guns, all, paths);
                if (local < 0) continue;
                buckets[local].Add(burst);
            }
            for (int i = 0; i < buckets.Length; i++)
                slot.Bursts[i] = buckets[i].ToArray();
        }

        static int MatchGun(ReplayObject burst, Gun[] stationGuns, Gun[] all, string[] paths)
        {
            if (!string.IsNullOrEmpty(burst.Weapon))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null || !PathEquals(paths[i], burst.Weapon)) continue;
                    int local = IndexOfGun(stationGuns, all[i]);
                    if (local >= 0) return local;
                }
                string leaf = burst.Weapon;
                int slash = leaf.LastIndexOf('/');
                if (slash >= 0 && slash < leaf.Length - 1)
                    leaf = leaf.Substring(slash + 1);
                for (int i = 0; i < stationGuns.Length; i++)
                {
                    var gun = stationGuns[i];
                    if (gun != null && gun.name.Equals(leaf, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            if (burst.GunIndex >= 0 && burst.GunIndex < all.Length && all[burst.GunIndex] != null)
                return IndexOfGun(stationGuns, all[burst.GunIndex]);
            return -1;
        }

        static int IndexOfGun(Gun[] guns, Gun gun)
        {
            for (int i = 0; i < guns.Length; i++)
                if (guns[i] == gun) return i;
            return -1;
        }

        void AssignLaunches(AmmoStation[] stations, ReplayObject[]? launches)
        {
            if (launches == null || launches.Length == 0) return;
            var buckets = new List<float>[stations.Length];
            var assigned = new int[stations.Length];
            for (int i = 0; i < stations.Length; i++)
                buckets[i] = new List<float>();
            int missed = 0;
            for (int i = 0; i < launches.Length; i++)
            {
                var launch = launches[i];
                if (launch == null) continue;
                int best = -1;
                int bestScore = 0;
                for (int s = 0; s < stations.Length; s++)
                {
                    if (!stations[s].TakesLaunches) continue;
                    int score = ScoreLaunch(launch, stations[s].Names);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = s;
                    }
                }
                if (best < 0 || bestScore < 60)
                {
                    missed++;
                    continue;
                }
                if (assigned[best] >= stations[best].Capacity)
                {
                    int alt = -1;
                    int altScore = 0;
                    for (int s = 0; s < stations.Length; s++)
                    {
                        if (!stations[s].TakesLaunches || assigned[s] >= stations[s].Capacity) continue;
                        int score = ScoreLaunch(launch, stations[s].Names);
                        if (score >= 60 && score > altScore)
                        {
                            altScore = score;
                            alt = s;
                        }
                    }
                    if (alt >= 0) best = alt;
                }
                assigned[best]++;
                buckets[best].Add(launch.EventTime());
            }
            for (int i = 0; i < stations.Length; i++)
                stations[i].LaunchTimes = buckets[i].ToArray();
            if (missed > 0 && _ammoMissLog < 3)
            {
                _ammoMissLog++;
                Plugin.Log.LogInfo("Starts ohne Station " + missed + " " + (Track?.Name ?? name));
            }
        }

        static int ScoreLaunch(ReplayObject launch, string[] names)
        {
            string have = launch.Name ?? "";
            string call = launch.CallSign ?? "";
            int best = 0;
            for (int i = 0; i < names.Length; i++)
            {
                string c = names[i];
                if (c.Length == 0) continue;
                if (have.Equals(c, StringComparison.OrdinalIgnoreCase)) return 100;
                if (call.Equals(c, StringComparison.OrdinalIgnoreCase))
                    best = Math.Max(best, 95);
                else if (call.StartsWith(c, StringComparison.OrdinalIgnoreCase)
                    && (call.Length == c.Length || call[c.Length] == ' '))
                    best = Math.Max(best, 90);
                else if (c.Length >= 4 && have.Length >= 4
                    && (have.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0
                        || c.IndexOf(have, StringComparison.OrdinalIgnoreCase) >= 0))
                    best = Math.Max(best, 60);
            }
            return best;
        }

        static string[] Candidates(WeaponStation station)
        {
            var list = new List<string>();
            var info = station.WeaponInfo;
            if (info == null && station.Weapons != null)
            {
                for (int i = 0; i < station.Weapons.Count; i++)
                {
                    if (station.Weapons[i] != null && station.Weapons[i].info != null)
                    {
                        info = station.Weapons[i].info;
                        break;
                    }
                }
            }
            if (info == null) return Array.Empty<string>();
            try
            {
                AddName(list, info.weaponName);
                AddName(list, info.shortName);
                var prefab = info.weaponPrefab;
                if (prefab != null)
                {
                    AddName(list, prefab.name);
                    var missile = prefab.GetComponent<Missile>();
                    if (missile != null)
                    {
                        if (missile.definition != null)
                        {
                            AddName(list, missile.definition.unitName);
                            AddName(list, missile.definition.code);
                        }
                        var nested = missile.GetWeaponInfo();
                        if (nested != null)
                        {
                            AddName(list, nested.weaponName);
                            AddName(list, nested.shortName);
                        }
                    }
                }
            }
            catch { }
            return list.ToArray();
        }

        static void AddName(List<string> list, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            string n = value.Replace("(Clone)", "").Trim();
            if (n.Length == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(n, StringComparison.OrdinalIgnoreCase)) return;
            }
            list.Add(n);
        }

        static int CountLaunches(AmmoStation st, float time)
        {
            var times = st.LaunchTimes;
            int lo = 0, hi = times.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] <= time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        int CountRounds(AmmoStation st, float time)
        {
            int sum = 0;
            for (int i = 0; i < st.Guns.Length; i++)
                sum += GunRounds(st, i, time);
            return sum;
        }

        int GunRounds(AmmoStation st, int gun, float time)
        {
            if (gun < 0 || gun >= st.Bursts.Length) return 0;
            var bursts = st.Bursts[gun];
            int full = gun < st.GunFull.Length ? st.GunFull[gun] : 0;
            int n = 0;
            for (int i = 0; i < bursts.Length; i++)
            {
                n += RoundsOf(bursts[i], time);
                if (full > 0 && n >= full) return full;
            }
            return n;
        }

        int RoundsOf(ReplayObject burst, float time)
        {
            if (burst == null || burst.Samples == null || burst.Samples.Count == 0) return 0;
            float start = burst.SpawnAt >= 0f ? burst.SpawnAt : burst.Samples[0].Time;
            if (time < start) return 0;
            float lastSample = burst.Samples[burst.Samples.Count - 1].Time;
            float end = lastSample;
            float rpm = ResolveEffectiveGunRpm(burst, out bool singleShot);
            float step = Mathf.Clamp(60f / Mathf.Max(rpm, 1f), 0.008f, 30f);
            if (singleShot && (end - start) < Mathf.Max(step * 0.9f, 0.7f))
                end = start;
            float until = Mathf.Min(time, end);
            if (until + 0.0001f < start) return 0;
            int n = Mathf.FloorToInt((until - start) / step + 0.0001f) + 1;
            return n < 1 ? 1 : n;
        }

        void PaintMissiles(AmmoStation st, int launches)
        {
            int fired = launches;
            if (fired < 0) fired = 0;
            if (fired > st.Missiles.Length) fired = st.Missiles.Length;
            for (int i = 0; i < st.Missiles.Length; i++)
            {
                var mm = st.Missiles[i];
                if (mm == null) continue;
                bool off = i < fired;
                SetFired(mm, off);
                var rends = st.MissileRends[i];
                if (off)
                {
                    if (rends != null)
                    {
                        for (int r = 0; r < rends.Length; r++)
                        {
                            var rend = rends[r];
                            if (rend == null) continue;
                            _ammoHide.Add(rend.GetInstanceID());
                            ApplyReplayRenderer(rend);
                        }
                    }
                    MarkInactive(mm.gameObject);
                }
                else if (!TransformDetached(mm.transform))
                {
                    if (!mm.gameObject.activeSelf)
                        mm.gameObject.SetActive(true);
                    if (rends != null)
                    {
                        for (int r = 0; r < rends.Length; r++)
                            ApplyReplayRenderer(rends[r]);
                    }
                }
                else if (rends != null)
                {
                    for (int r = 0; r < rends.Length; r++)
                        ApplyReplayRenderer(rends[r]);
                }
            }
        }

        void PaintCounts(AmmoStation st, int launches, float time)
        {
            if (st.Missiles.Length == 0)
            {
                int remain = launches;
                for (int i = 0; i < st.Launchers.Length; i++)
                {
                    var launcher = st.Launchers[i];
                    if (launcher == null) continue;
                    int launcherFull = i < st.LauncherFull.Length ? st.LauncherFull[i] : 0;
                    int spend = Mathf.Clamp(remain, 0, launcherFull);
                    launcher.ammo = launcherFull - spend;
                    SetReloading(launcher, 0);
                    remain -= spend;
                }
            }
            for (int i = 0; i < st.Guns.Length; i++)
            {
                var gun = st.Guns[i];
                if (gun == null) continue;
                int gunFull = i < st.GunFull.Length ? st.GunFull[i] : 0;
                int spent = GunRounds(st, i, time);
                int left = Mathf.Clamp(gunFull - spent, 0, Mathf.Max(gunFull, 0));
                gun.ammo = left;
                SetGunLoaded(gun, left);
            }
            try { st.Station.AccountAmmo(); } catch { }
            int full = st.Station.FullAmmo;
            if (full <= 0) full = st.Capacity;
            int spentVisual = launches;
            if (st.Missiles.Length > 0)
                spentVisual = Mathf.Min(launches, st.Missiles.Length);
            else if (st.Launchers.Length > 0)
                spentVisual = Mathf.Min(launches, full);
            int gunSpent = 0;
            for (int i = 0; i < st.Guns.Length; i++)
                gunSpent += GunRounds(st, i, time);
            try
            {
                if (st.Missiles.Length > 0 || st.Launchers.Length > 0 || st.Guns.Length > 0)
                {
                    if (st.Station.FullAmmo <= 0) st.Station.FullAmmo = full;
                    st.Station.Ammo = Mathf.Clamp(full - spentVisual - gunSpent, 0, Mathf.Max(full, 0));
                }
                st.Station.Updated();
            }
            catch { }
        }

        void MarkInactive(GameObject go)
        {
            if (go == null) return;
            int id = go.GetInstanceID();
            if (_ammoInactiveIds.Add(id))
                _ammoInactive.Add(go);
            if (go.activeSelf)
                go.SetActive(false);
        }

        void WakeInactive()
        {
            for (int i = 0; i < _ammoInactive.Count; i++)
            {
                var go = _ammoInactive[i];
                if (go != null && !go.activeSelf && !TransformDetached(go.transform))
                    go.SetActive(true);
            }
        }

        static void SetFired(MountedMissile mm, bool fired)
        {
            if (!_firedLooked)
            {
                _firedLooked = true;
                _firedField = typeof(MountedMissile).GetField("fired",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (_firedField == null)
                    Plugin.Log.LogWarning("MountedMissile.fired fehlt");
            }
            try { _firedField?.SetValue(mm, fired); } catch { }
            mm.ammo = fired ? 0 : 1;
        }

        int RememberFull(Component weapon, int reported, int currentAmmo)
        {
            int id = weapon.GetInstanceID();
            if (_weaponFull.TryGetValue(id, out int cached) && cached > 0)
                return cached;
            int full = reported > 0 ? reported : currentAmmo;
            if (full < 0) full = 0;
            if (full > 0) _weaponFull[id] = full;
            return full;
        }

        static void SetGunLoaded(Gun gun, int rounds)
        {
            if (!_gunLoadedLooked)
            {
                _gunLoadedLooked = true;
                _gunLoaded = typeof(Gun).GetField("bulletsLoaded",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (_gunLoaded == null)
                    Plugin.Log.LogWarning("Gun.bulletsLoaded fehlt");
            }
            try { _gunLoaded?.SetValue(gun, rounds); } catch { }
        }

        static void SetReloading(MissileLauncher launcher, int rounds)
        {
            if (!_launcherReloadingLooked)
            {
                _launcherReloadingLooked = true;
                _launcherReloading = typeof(MissileLauncher).GetField("reloadingAmmo",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            try { _launcherReloading?.SetValue(launcher, rounds); } catch { }
        }
    }
}
