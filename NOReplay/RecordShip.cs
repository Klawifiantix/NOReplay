using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOReplay
{
    internal class RecordShip : RecordUnit
    {
        private Ship ship;

        public virtual void Init(Ship ship)
        {
            base.unit = ship;
            this.ship = ship;
            base.unitId = ship.persistentID.Id;
            base.tacviewId = ship.persistentID.Id + 1;
            base.destroyedEvent = true;
            base.canTarget = true;
            lastState = ship.unitState;

            Faction? faction = base.unit.NetworkHQ?.faction;
            string[] info = { "Default", "Sea+Watercraft", "0", "0" };
            if (Record.UnitInfo["ships"].ContainsKey(ship.definition.unitName))
            {
                info = Record.UnitInfo["ships"][ship.definition.unitName];
            }

            props = new Dictionary<string, string>()
            {
                { "Name", base.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "CallSign", $"{ship.definition.unitName} {tacviewId:X}" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", info[1] },
                { "Debug", lastState.ToString() }
            };

            UpdatePose();
            Engine.invokeWriterUpdate(this);
            props.Clear();

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

            UpdatePose();
            if (props.Count > 0)
            {
                Engine.invokeWriterUpdate(this);
                props.Clear();
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
            Engine.invokeWriterUpdate(this);
            props.Clear();
        }

        private void OnMissileRegistered(Missile missile)
        {
            if (missile == null) return;
            Record.Log?.LogDebug($"Ship {unitId:X} registered missile {missile.persistentID.Id:X}");
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
            Engine.invokeWriterUpdate(this);
            props.Clear();
            if (base.destroyedEvent)
                Engine.invokeWriterDestroy(this);

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