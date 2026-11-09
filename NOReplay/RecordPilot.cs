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
            string pilotName = unit.definition != null && !string.IsNullOrEmpty(unit.definition.unitName)
                ? unit.definition.unitName
                : (string.IsNullOrEmpty(unit.unitName) ? "Pilot" : unit.unitName);
            props = new Dictionary<string, string>()
            {
                { "Name", pilotName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", "Ground+Light+Human+Air+Parachutist" },
                { "Debug", lastState.ToString()}
            };
            Engine.invokeWriterUpdate(this);
            props.Clear();
            this.enabled = true;
            base.enabled = true;
            Record.Log?.LogInfo("Pilot rec " + pilotName
                + " id=" + unitId.ToString(CultureInfo.InvariantCulture)
                + " config=" + (RecordConfig.RecordEjectedPilots.Value ? "1" : "0"));
        }

        public override void Update()
        {
            if (!enabled || unit == null || unit.disabled) return;
            timer += Time.deltaTime;
            if (timer < RecordConfig.vehicleUpdateDelta.Value) return;
            UpdatePose();
            UpdateState();
            if (props.Count > 0)
            {
                Engine.invokeWriterUpdate(this);
                props.Clear();
            }
            timer = 0;
        }
    }
}
