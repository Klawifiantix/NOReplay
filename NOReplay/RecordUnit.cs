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
        public virtual void LateUpdate()
        {
            try
            {
                if (unit.disabled)
                {
                    base.disabled = true;
                    props.Add("Visible", "0.0");
                    props.Add("Type", null);
                    Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
                    props = [];
                    if (base.destroyedEvent && !purge)
                    {
                        Record.Host.GetComponent<RecordEngine>().invokeWriterDestroy(this);
                    }
                    if (purge)
                    {
                        Record.Host.GetComponent<RecordEngine>().invokeWriterRemove(this);
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

        internal void UpdatePose()
        {
            float fx = MathF.Round(unit.transform.position.GlobalX(), 2);
            float fy = MathF.Round(unit.transform.position.GlobalY(), 2);
            float fz = MathF.Round(unit.transform.position.GlobalZ(), 2);

            float fax = MathF.Round(unit.transform.eulerAngles.x, 2);
            float fay = MathF.Round(unit.transform.eulerAngles.y, 2);
            float faz = MathF.Round(unit.transform.eulerAngles.z, 2);

            Vector3 newPos = new(fx, fy, fz);
            Vector3 newRot = new(fax, fay, faz);

            if (newPos != lastPos || newRot != lastRot)
            {
                props.Add("T", UpdatePosition(newPos));

                // Raw transform quaternion
                var q = unit.transform.rotation;
                props.Add("Q", $"{q.x.ToString(CultureInfo.InvariantCulture)}|{q.y.ToString(CultureInfo.InvariantCulture)}|{q.z.ToString(CultureInfo.InvariantCulture)}|{q.w.ToString(CultureInfo.InvariantCulture)}");

                lastPos = newPos;
                lastRot = newRot;
            }
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
            long id = -1;
            try
            {
                id = Record.Host.GetComponent<RecordEngine>().unitObjects[unitId].GetComponent<RecordUnit>().tacviewId;
            }
            catch
            {
                Record.Log?.LogDebug($"GetTacviewIdofUnit failed to look up UNIT ID {unitId}");
                id = -1;
            }
            return id;
        }
    }
}