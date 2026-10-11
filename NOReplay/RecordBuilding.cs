using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOReplay
{
    internal class RecordBuilding : RecordUnit
    {
        public Building building;
        private string coalition;
        private bool subscribed;

        public virtual void Init(Building building)
        {
            base.unit = building;
            this.building = (Building)base.unit;
            base.unitId = unit.persistentID.Id;
            base.tacviewId = unit.persistentID.Id + 1;
            base.destroyedEvent = true;
            lastState = unit.unitState;

            Faction? faction = this.unit.NetworkHQ?.faction;
            coalition = faction?.factionName ?? "Neutral";

            props = new Dictionary<string, string>()
            {
                { "Name", this.unit.definition.unitName },
                { "CallSign", $"{building.definition.code} {tacviewId:X}"},
                { "Coalition", coalition },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type","Ground+Static+Building"},
                { "Debug", lastState.ToString()}
            };

            UpdatePose();
            UpdateState();
            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];

            // Events statt Polling
            if (!subscribed && unit != null)
            {
                unit.onDisableUnit += OnUnitDisabled;
                unit.onChangeFaction += OnFactionChanged;
                subscribed = true;
            }

            // Bereits zerstört?
            if (unit.disabled)
            {
                OnUnitDisabled(unit);
                return;
            }

            // Kein weiteres Update/LateUpdate mehr
            this.enabled = false;
            base.enabled = false;
        }

        private void OnFactionChanged(Unit u)
        {
            if (u != unit || unit.disabled || base.disabled) return;

            string newCoalition = unit.NetworkHQ?.faction?.factionName ?? "Neutral";
            if (newCoalition == coalition) return;

            coalition = newCoalition;
            props = new Dictionary<string, string>();
            props.Add("Coalition", coalition);

            string color = "Green";
            switch (coalition)
            {
                case "Boscali": color = "Blue"; break;
                case "Primeva": color = "Red"; break;
            }
            props.Add("Color", color);
            props.Add("Debug", building.unitState.ToString());

            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];
        }

        private void OnUnitDisabled(Unit u)
        {
            if (u != unit || base.disabled) return;

            base.disabled = true;
            props = new Dictionary<string, string>();
            props.Add("Visible", "0.0");
            props.Add("Type", null);

            Record.Host.GetComponent<RecordEngine>().invokeWriterUpdate(this);
            props = [];

            if (base.destroyedEvent)
            {
                Record.Host.GetComponent<RecordEngine>().invokeWriterDestroy(this);
            }

            Unsubscribe();
            this.enabled = false;
            Record.Log?.LogDebug($"STATIC DESTROYED {unitId}");
            UnityEngine.Object.Destroy(this.gameObject);
        }

        private void Unsubscribe()
        {
            if (!subscribed || unit == null) return;
            try
            {
                unit.onDisableUnit -= OnUnitDisabled;
                unit.onChangeFaction -= OnFactionChanged;
            }
            catch { }
            subscribed = false;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public override void Update()
        {
            // Absichtlich leer – kein Polling mehr
        }

        public override void LateUpdate()
        {
            // Absichtlich leer – Destroy kommt über Event
        }
    }
}