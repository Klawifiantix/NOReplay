using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace NOReplay
{
    internal class RecordUnit : RecordObject
    {
        private Vector3 lastPos = new(float.NaN, float.NaN, float.NaN);
        float _poseAt = -1f;
        bool _holdArm;
        float _holdQx, _holdQy, _holdQz, _holdQw;
        static int _holdLogs;
        private Vector3 lastRot = new(float.NaN, float.NaN, float.NaN);
        internal Unit unit;
        internal Unit.UnitState lastState;
        internal bool purge = false;
        internal bool canTarget = false;

        internal Unit[] lastTargets = [];
        internal Unit[] targets = [];
        internal string lockedTargetString = "LockedTarget";

        public virtual void Init(Unit unit)
        {
            /*
            this.unit = unit;
            base.unitId = unit.persistentID.Id;
            base.tacviewId = unit.persistentID.Id + 1;
            lastState = unit.unitState;
            Faction? faction = this.unit.NetworkHQ?.faction;
            props = new Dictionary<string, string>()
            {
                { "Name", this.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Debug", lastState.ToString()}
            };
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];
            this.enabled = true;
            base.enabled = true;
            */
        }
        public override void Update()
        {

        }
        /// <summary>Called once, on the disable sample, after Visible=0 is queued.</summary>
        protected virtual void OnDyingProps()
        {
        }

        float _blastAt = -999f;
        float _fireAt = -999f;

        /// <summary>False when this hit was already written. A later blast is a new line.</summary>
        protected bool MarkBlast() => MarkTime(ref _blastAt);

        protected bool MarkFire() => MarkTime(ref _fireAt);

        bool MarkTime(ref float last)
        {
            if (UnityEngine.Time.time - last < BlastSchedule.SameHitSeconds) return false;
            last = UnityEngine.Time.time;
            return true;
        }

        internal virtual void NoteBlast(string src)
        {
        }

        /// <summary>
        /// One line when a part effect spawns. Fire stays on the part.
        /// Anything else is the explosion. Disable and the wreck are not blasts.
        /// </summary>
        protected void WriteEffect(string src, string label)
        {
            bool fire = src.StartsWith("fire", StringComparison.Ordinal);
            if (fire)
            {
                if (!MarkFire()) return;
            }
            else if (!MarkBlast()) return;
            props.Clear();
            if (unit != null) UpdatePose(true);
            props[fire ? "Fire" : "Boom"] = "1";
            Engine.invokeWriterUpdate(this);
            props.Clear();
            Plugin.Log?.LogInfo((fire ? "Fire-Rec " : "Boom-Rec ") + label + " " + src);
        }

        public virtual void LateUpdate()
        {
            if (!Record.Active) return;
            try
            {
                if (unit == null || !unit.disabled) return;
                {
                    bool first = !base.disabled;
                    base.disabled = true;
                    if (first && unit != null)
                    {
                        props.Clear();
                        UpdatePose(true);
                        var turrets = GetComponent<RecordTurrets>();
                        if (turrets != null) turrets.enabled = false;
                        RecordBay.Forget(unit);
                    }
                    if (!props.ContainsKey("Visible"))
                        props.Add("Visible", "0.0");
                    if (first)
                        OnDyingProps();
                    if (!props.ContainsKey("Type"))
                        props.Add("Type", null!);
                    Engine.invokeWriterUpdate(this);
                    props.Clear();
                    if (base.destroyedEvent && !purge)
                    {
                        Engine.invokeWriterDestroy(this);
                    }
                    if (purge)
                    {
                        Engine.invokeWriterRemove(this);
                        this.enabled = false;
                        Record.Log?.LogDebug($"DISABLING UNIT {unitId.ToString(CultureInfo.InvariantCulture)}");
                        Engine.Retire(unitId, gameObject);
                        GameObject.Destroy(gameObject);
                    }
                    //this is to stagger the Destroyed Event and the Remove call to 2 different time steps, as having them both on the same time step will confuse Tacview
                    purge = true;
                }
            }
            catch
            {
                GameObject.Destroy(this);
            }
        }

        void SnapPose(float x, float y, float z, Quaternion q)
        {
            // Numbers only. The file thread formats T and Q.
            poseSnap = true;
            poseX = x;
            poseY = y;
            poseZ = z;
            poseQx = q.x;
            poseQy = q.y;
            poseQz = q.z;
            poseQw = q.w;
        }

        internal bool MotionDue => poseSnap || props.Count > 0;

        internal void CommitMotion()
        {
            if (!MotionDue || Engine == null) return;
            Engine.invokeWriterUpdate(this);
            props.Clear();
        }

        internal void UpdatePose(bool force = false)
        {
            if (unit == null) return;
            float fx = MathF.Round(unit.transform.position.GlobalX(), 2);
            float fy = MathF.Round(unit.transform.position.GlobalY(), 2);
            float fz = MathF.Round(unit.transform.position.GlobalZ(), 2);

            float fax = MathF.Round(unit.transform.eulerAngles.x, 2);
            float fay = MathF.Round(unit.transform.eulerAngles.y, 2);
            float faz = MathF.Round(unit.transform.eulerAngles.z, 2);

            Vector3 newPos = new(fx, fy, fz);
            Vector3 newRot = new(fax, fay, faz);
            bool moved = newPos != lastPos || newRot != lastRot;
            if (!force && !moved) return;

            float now = Time.time;
            // Repeat the pose we already stored, at this timestamp, before
            // the first sample of a move. The replay then holds until here.
            if (!force && !_holdArm && _poseAt >= 0f && PoseHold.Pause(now - _poseAt))
            {
                _holdArm = true;
                SnapPose(lastPos.x, lastPos.y, lastPos.z, new Quaternion(_holdQx, _holdQy, _holdQz, _holdQw));
                if (_holdLogs < 8)
                {
                    _holdLogs++;
                    string name = unit.definition != null ? unit.definition.unitName : unit.name;
                    Record.Log?.LogInfo("Pose hold " + name
                        + " gap=" + (now - _poseAt).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                }
                return;
            }
            _holdArm = false;
            var q = unit.transform.rotation;
            SnapPose(fx, fy, fz, q);
            lastPos = newPos;
            lastRot = newRot;
            _holdQx = q.x;
            _holdQy = q.y;
            _holdQz = q.z;
            _holdQw = q.w;
            _poseAt = now;
        }

        internal void RepeatPoseIfStill()
        {
            if (poseSnap || float.IsNaN(lastPos.x)) return;
            if (!props.ContainsKey("RpmRatio") && !props.ContainsKey("JetRpm")
                && !props.ContainsKey("TurbineRpm") && !props.ContainsKey("Throttle"))
                return;
            SnapPose(lastPos.x, lastPos.y, lastPos.z, unit.transform.rotation);
        }

        internal void UpdateState()
        {
            if (unit.unitState != lastState)
            {
                lastState = unit.unitState;
                props.Add("Debug", lastState.ToString());
            }
        }

        internal virtual void UpdateTargets()
        {

        }


        long _lockSeen = -1;

        /// <summary>
        /// One line when the locked target changes. 0 clears it.
        /// The value is the same hex id the recorder already uses for the unit.
        /// </summary>
        internal void NoteLock(Unit host)
        {
            uint id = 0;
            if (host is Missile missile)
                id = missile.targetID.IsValid ? missile.targetID.Id : 0;
            else
            {
                var target = FirstLock(host);
                if (target != null && target.persistentID.IsValid)
                    id = target.persistentID.Id;
            }
            if (_lockSeen == id) return;
            if (_lockSeen < 0 && id == 0)
            {
                _lockSeen = 0;
                return;
            }
            _lockSeen = id;
            string hex = id == 0 ? "0" : (id + 1).ToString("X", CultureInfo.InvariantCulture);
            props["LockedTarget"] = hex;
            string who = host != null && !string.IsNullOrEmpty(host.unitName) ? host.unitName : name;
            Plugin.Log?.LogInfo("Lock-Rec " + who + " -> " + hex);
        }

        internal static Unit? FirstLock(Unit host)
        {
            if (host == null) return null;
            if (host is Aircraft aircraft && aircraft.weaponManager != null)
            {
                var list = aircraft.weaponManager.GetTargetList();
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != null) return list[i];
                }
            }
            try
            {
                var stations = host.weaponStations;
                if (stations == null) return null;
                for (int i = 0; i < stations.Count; i++)
                {
                    var station = stations[i];
                    if (station == null) continue;
                    var turret = station.GetTurret();
                    var target = turret != null ? turret.GetTarget() : null;
                    if (target != null) return target;
                }
            }
            catch { }
            return null;
        }

        internal long GetTacviewIdOfUnit(long unitId)
        {
            try
            {
                var eng = Engine;
                if (eng.tacviewIds.TryGetValue(unitId, out var known))
                    return known;
                if (!eng.unitObjects.TryGetValue(unitId, out var go) || go == null)
                    return -1;
                var rec = go.GetComponent<RecordUnit>();
                if (rec == null)
                    return -1;
                eng.tacviewIds[unitId] = rec.tacviewId;
                return rec.tacviewId;
            }
            catch
            {
                Record.Log?.LogDebug($"GetTacviewIdofUnit failed to look up UNIT ID {unitId}");
                return -1;
            }
        }
    }
}