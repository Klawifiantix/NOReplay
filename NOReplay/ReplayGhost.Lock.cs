using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        static readonly Dictionary<string, ReplayGhost> _lockById =
            new Dictionary<string, ReplayGhost>(System.StringComparer.OrdinalIgnoreCase);
        static int _lockByCount = -1;
        static int _lockScanFrame = -1;
        static FieldInfo? _missileTarget;
        static FieldInfo? _turretTarget;
        static bool _lockFields;

        string _lockHex = "";
        bool _lockHeld;
        Unit? _lockUnit;

        internal Unit? LockUnit => _lockUnit;

        internal static ReplayGhost? ForUnit(Unit unit)
        {
            if (unit == null) return null;
            var direct = unit.GetComponent<ReplayGhost>() ?? unit.GetComponentInParent<ReplayGhost>();
            if (direct != null) return direct;
            var list = Plugin.GhostComps;
            for (int i = 0; i < list.Count; i++)
            {
                var ghost = list[i];
                if (ghost == null) continue;
                var hosted = ghost.GetComponent<Unit>() ?? ghost.GetComponentInChildren<Unit>(true);
                if (hosted == unit) return ghost;
            }
            return null;
        }

        internal void ApplyLock()
        {
            if (Track == null || Plugin.Clock == null) return;
            if (Track.Locks.Count == 0 && !_lockHeld) return;
            string want = Track.LockAt(Plugin.Clock.Time);
            if (_lockHeld && want == _lockHex && (want.Length == 0 || _lockUnit != null)) return;
            Unit? target = null;
            if (want.Length > 0)
            {
                var other = FindLocked(want);
                if (other != null)
                    target = other.GetComponent<Unit>() ?? other.GetComponentInChildren<Unit>(true);
                if (target == null) return;
            }
            _lockHex = want;
            _lockHeld = true;
            _lockUnit = target;
            PushLock(target);
            Plugin.Log?.LogInfo("Lock " + (string.IsNullOrEmpty(Track.Name) ? name : Track.Name)
                + " -> " + (target != null ? target.unitName : "none"));
        }

        void PushLock(Unit? target)
        {
            EnsureLockFields();
            var missile = GetComponent<Missile>() ?? GetComponentInChildren<Missile>(true);
            if (missile != null && _missileTarget != null)
            {
                PersistentID id = target != null ? target.persistentID : default;
                try { _missileTarget.SetValue(missile, id); } catch { }
            }
            var aircraft = GetComponent<Aircraft>() ?? GetComponentInChildren<Aircraft>(true);
            if (aircraft != null && aircraft.weaponManager != null)
            {
                if (target == null)
                    aircraft.weaponManager.SetTargetList(System.ReadOnlySpan<PersistentID>.Empty);
                else
                {
                    System.Span<PersistentID> one = stackalloc PersistentID[1];
                    one[0] = target.persistentID;
                    aircraft.weaponManager.SetTargetList(one);
                }
            }
            var turret = GetComponentInChildren<Turret>(true);
            if (turret != null && _turretTarget != null)
            {
                try { _turretTarget.SetValue(turret, target); } catch { }
            }
        }

        static void EnsureLockFields()
        {
            if (_lockFields) return;
            _lockFields = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _missileTarget = typeof(Missile).GetField("_targetID", flags);
            _turretTarget = typeof(Turret).GetField("target", flags);
        }

        static ReplayGhost? FindLocked(string hex)
        {
            int n = Plugin.GhostComps.Count;
            if (_lockByCount != n)
                RebuildLockIndex();
            if (_lockById.TryGetValue(hex, out var hit) && hit != null) return hit;
            if (_lockScanFrame == Time.frameCount) return null;
            _lockScanFrame = Time.frameCount;
            RebuildLockIndex();
            _lockById.TryGetValue(hex, out hit);
            return hit;
        }

        static void RebuildLockIndex()
        {
            _lockById.Clear();
            var list = Plugin.GhostComps;
            for (int i = 0; i < list.Count; i++)
            {
                var ghost = list[i];
                if (ghost == null || ghost.Track == null || string.IsNullOrEmpty(ghost.Track.Id)) continue;
                _lockById[ghost.Track.Id] = ghost;
            }
            _lockByCount = list.Count;
        }
    }
}
