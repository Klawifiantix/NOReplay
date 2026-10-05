using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace NOReplay
{ 
    internal class RecordPilot : RecordUnit
    {
        public PilotDismounted pilot;

        public virtual void Init(PilotDismounted pilot)
        {
            base.unit = pilot;
            this.pilot = (PilotDismounted)base.unit;
            base.unitId = unit.persistentID.Id;
            base.tacviewId = unit.persistentID.Id + 1;
            lastState = unit.unitState;
            Faction? faction = this.unit.NetworkHQ?.faction;
            props = new Dictionary<string, string>()
            {
                { "Name", this.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", "Ground+Light+Human+Air+Parachutist" },
                { "Debug", lastState.ToString()}
            };
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];
            this.enabled = true;
            base.enabled = true;
        }

        public override void Update()
        {
            if (!this.enabled || unit.disabled)
            {
                return;
            }
            timer += Time.deltaTime;
            if (timer < RecordConfig.vehicleUpdateDelta.Value)
            {
                return;
            }
            if (pilot.radarAlt <= 1)
            {
                props.Add("Visible", "0.0");
                props.Add("Type", null);
                Record.Log?.LogDebug($"PARACHUTE LANDED {unitId.ToString(CultureInfo.InvariantCulture)}");
                Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
                Record.Host.GetComponent<RecordEngine>().invokeWriterRemove(this);
                GameObject.Destroy(this);
            }
            UpdatePose();
            UpdateState();
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];
            timer = 0;
        }
    }
}
