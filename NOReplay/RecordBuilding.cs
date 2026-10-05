using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NOReplay
{
    internal class RecordBuilding : RecordUnit
    {
        public Building building;
        private string coalition;
        public virtual void Init(Building building)
        {
            base.unit = building;
            this.building = (Building)base.unit;
            base.unitId = unit.persistentID.Id;
            base.tacviewId = unit.persistentID.Id + 1;
            base.destroyedEvent = true;
            lastState = unit.unitState;
            Faction? faction = this.unit.NetworkHQ?.faction;
            props = new Dictionary<string, string>()
            {
                { "Name", this.unit.definition.unitName },
                { "CallSign", $"{building.definition.code} {tacviewId:X}"},
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type","Ground+Static+Building"},
                { "Debug", lastState.ToString()}
            };
            UpdatePose();
            UpdateState();
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];
            this.enabled = true;
            base.enabled = true;
        }

        public override void Update()
        {
            timer += Time.deltaTime;
            if (timer < RecordConfig.buildingUpdateDelta.Value)
            {
                return;
            }
            if (unit.NetworkHQ?.faction.factionName != coalition && !building.disabled && !base.disabled)
            {
                coalition = unit.NetworkHQ?.faction.factionName ?? "Neutral";
                props.Add("Coalition", unit.NetworkHQ?.faction.factionName ?? "Neutral");

                string color = "Green";
                switch (unit.NetworkHQ?.faction.factionName)
                {
                    case "Boscali":
                        color = "Blue";
                        break;
                    case "Primeva":
                        color = "Red";
                        break;
                    default:
                        color = "Green";
                        break;
                }

                props.Add("Color", color);
                props.Add("Debug", $"{building.unitState.ToString()}");
                Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);

            }
            if (unit.disabled && !base.disabled)
            {
                props = [];
                props.Add("Visible", "0.0");
                props.Add("Type", null);
                Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
                Record.Host.GetComponent<RecordEngine>().invokeWriterDestroy(this);
                props = [];
                base.disabled = true;
            }
            if (!unit.disabled && base.disabled)
            {
                this.Init(this.building);
                props.Add("Visible", "1.0");
                Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
                props = [];
                Record.Host.GetComponent<RecordEngine>().invokeWriterRepair(this);
                base.disabled = false;
            }
            props = [];
            timer = 0;
        }

        public override void LateUpdate()
        {
            
        }

    }
}
