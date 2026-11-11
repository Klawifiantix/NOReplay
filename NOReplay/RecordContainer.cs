using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// A pallet or bladder is a Container, not a GroundVehicle, so the vehicle
    /// recorder never saw it. The body is the spawned unit. The parachute is
    /// recorded separately from the Parachute component.
    /// </summary>
    internal class RecordContainer : RecordUnit
    {
        public void Init(Container box)
        {
            unit = box;
            unitId = unit.persistentID.Id;
            tacviewId = unit.persistentID.Id + 1;
            lastState = unit.unitState;
            Faction? faction = unit.NetworkHQ?.faction;
            string name = unit.definition != null ? unit.definition.unitName : unit.unitName;
            if (string.IsNullOrEmpty(name)) name = "Container";
            props = new Dictionary<string, string>()
            {
                { "Name", name },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", "Misc+Container" },
                { "Debug", lastState.ToString() }
            };
            Engine.invokeWriterUpdate(this);
            props.Clear();
            enabled = true;
            Record.Log?.LogInfo("Cargo rec " + name + " id=" + unitId.ToString(CultureInfo.InvariantCulture));
            _name = name;
        }

        string _name = "";

        protected override void OnDyingProps()
        {
            // A pallet or container hides as soon as it is disabled. There is no cook-off timer.
            if (!props.ContainsKey("Boom") && WreckMark.IsCookoff(_name))
                props["Boom"] = "1";
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
            timer = 0f;
        }
    }
}
