using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOReplay
{
    internal class RecordVehicle : RecordUnit
    {
        private GroundVehicle vehicle;

        public virtual void Init(GroundVehicle vehicle)
        {
            base.unit = vehicle;
            this.vehicle = vehicle;
            base.unitId = vehicle.persistentID.Id;
            base.tacviewId = vehicle.persistentID.Id + 1;
            base.destroyedEvent = true;
            base.canTarget = true;
            lastState = vehicle.unitState;

            Faction? faction = base.unit.NetworkHQ?.faction;
            string[] info = { "Default", "Ground" };
            if (Record.UnitInfo["vehicles"].ContainsKey(vehicle.definition.unitName))
            {
                info = Record.UnitInfo["vehicles"][vehicle.definition.unitName];
            }

            props = new Dictionary<string, string>()
            {
                { "Name", base.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "CallSign", $"{vehicle.definition.code} {tacviewId:X}" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", info[1] },
                { "Debug", lastState.ToString() }
            };

            UpdatePose();
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];

            // Events
            if (unit != null)
            {
                unit.onDisableUnit += OnUnitDisabled;
                unit.onChangeFaction += OnFactionChanged;
                unit.onRegisterMissile += OnMissileRegistered;
            }

            this.enabled = true;
        }

        public override void Update()
        {
            if (!this.enabled || unit == null || unit.disabled) return;

            timer += Time.deltaTime;
            if (timer < RecordConfig.vehicleUpdateDelta.Value) return;

            // Nur noch Pose
            UpdatePose();
            if (props.Count > 0)
            {
                Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
                props = [];
            }
            timer = 0;
        }

        private void OnFactionChanged(Unit u)
        {
            if (u != unit || unit.disabled) return;
            string coalition = unit.NetworkHQ?.faction?.factionName ?? "Neutral";
            props = new Dictionary<string, string> { { "Coalition", coalition } };
            string color = coalition switch
            {
                "Boscali" => "Blue",
                "Primeva" => "Red",
                _ => "Green"
            };
            props.Add("Color", color);
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];
        }

        private void OnMissileRegistered(Missile missile)
        {
            if (missile == null) return;
            // Missile wird separat über RecordMissile erfasst; hier nur optional loggen
            Record.Log?.LogDebug($"Vehicle {unitId:X} registered missile {missile.persistentID.Id:X}");
        }

        private void OnUnitDisabled(Unit u)
        {
            if (u != unit || base.disabled) return;
            base.disabled = true;
            props = new Dictionary<string, string>
            {
                { "Visible", "0.0" },
                { "Type", null }
            };
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];
            if (base.destroyedEvent)
                Record.Host.GetComponent<RecordEngine>().invokeWriterDestroy(this);

            Unsubscribe();
            this.enabled = false;
            UnityEngine.Object.Destroy(this.gameObject);
        }

        private void Unsubscribe()
        {
            if (unit == null) return;
            try
            {
                unit.onDisableUnit -= OnUnitDisabled;
                unit.onChangeFaction -= OnFactionChanged;
                unit.onRegisterMissile -= OnMissileRegistered;
            }
            catch { }
        }

        private void OnDestroy() => Unsubscribe();

        public override void LateUpdate() { }
        internal override void UpdateTargets() { }
    }
}