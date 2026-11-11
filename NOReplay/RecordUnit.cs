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

        /// <summary>False when this hit was already written. A later blast is a new line.</summary>
        protected bool MarkBlast()
        {
            if (UnityEngine.Time.time - _blastAt < BlastSchedule.SameHitSeconds) return false;
            _blastAt = UnityEngine.Time.time;
            return true;
        }

        internal virtual void NoteBlast(string src)
        {
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
                        GameObject.Destroy(this);
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

        private string UpdatePosition(Vector3 newPos)
        {
            // Raw Unity global position – no geo conversion
            return $"{newPos.x.ToString(CultureInfo.InvariantCulture)}|{newPos.y.ToString(CultureInfo.InvariantCulture)}|{newPos.z.ToString(CultureInfo.InvariantCulture)}";
        }

        internal void UpdatePose(bool force = false)
        {
            float fx = MathF.Round(unit.transform.position.GlobalX(), 2);
            float fy = MathF.Round(unit.transform.position.GlobalY(), 2);
            float fz = MathF.Round(unit.transform.position.GlobalZ(), 2);

            float fax = MathF.Round(unit.transform.eulerAngles.x, 2);
            float fay = MathF.Round(unit.transform.eulerAngles.y, 2);
            float faz = MathF.Round(unit.transform.eulerAngles.z, 2);

            Vector3 newPos = new(fx, fy, fz);
            Vector3 newRot = new(fax, fay, faz);

            if (force || newPos != lastPos || newRot != lastRot)
            {
                if (!props.ContainsKey("T"))
                    props.Add("T", UpdatePosition(newPos));

                // Raw transform quaternion
                if (!props.ContainsKey("Q"))
                {
                    var q = unit.transform.rotation;
                    props.Add("Q", $"{q.x.ToString(CultureInfo.InvariantCulture)}|{q.y.ToString(CultureInfo.InvariantCulture)}|{q.z.ToString(CultureInfo.InvariantCulture)}|{q.w.ToString(CultureInfo.InvariantCulture)}");
                }
                lastPos = newPos;
                lastRot = newRot;
            }
        }

        internal void RepeatPoseIfStill()
        {
            if (props.ContainsKey("T") || float.IsNaN(lastPos.x)) return;
            if (!props.ContainsKey("RpmRatio") && !props.ContainsKey("JetRpm")
                && !props.ContainsKey("TurbineRpm") && !props.ContainsKey("Throttle"))
                return;
            props.Add("T", UpdatePosition(lastPos));
            var q = unit.transform.rotation;
            props.Add("Q", q.x.ToString(CultureInfo.InvariantCulture) + "|"
                + q.y.ToString(CultureInfo.InvariantCulture) + "|"
                + q.z.ToString(CultureInfo.InvariantCulture) + "|"
                + q.w.ToString(CultureInfo.InvariantCulture));
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