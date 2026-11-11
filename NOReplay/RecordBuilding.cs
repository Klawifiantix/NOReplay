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
            string mapName = building.MapUniqueName;
            if (string.IsNullOrEmpty(mapName) && building.BuiltIn)
                mapName = building.UniqueName;
            if ((building.BuiltIn || !string.IsNullOrEmpty(building.MapUniqueName))
                && !string.IsNullOrEmpty(mapName))
                props["Map"] = mapName;

            UpdatePose();
            UpdateState();
            Engine.invokeWriterUpdate(this);
            props.Clear();
            RecordWreck.Watch(this, unit.persistentID.Id);
            if (!unit.disabled)
                RecordTurrets.Attach(this, building);

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

            Engine.invokeWriterUpdate(this);
            props.Clear();
        }

        internal override void NoteBlast(string src)
        {
            if (!MarkBlast() || unit == null) return;
            string label = unit.definition != null ? unit.definition.unitName : unit.unitName;
            props = new Dictionary<string, string>();
            props["Boom"] = "1";
            Engine.invokeWriterUpdate(this);
            props.Clear();
            Record.Log?.LogInfo("Boom-Rec " + label + " src=" + src);
        }

        private void OnUnitDisabled(Unit u)
        {
            if (u != unit || base.disabled) return;

            base.disabled = true;
            props = new Dictionary<string, string>();
            props.Add("Visible", "0.0");
            // A dump or bunker spawns its wreck in UnitDisabled, with no timer.
            string label = unit.definition != null ? unit.definition.unitName : unit.unitName;
            if (WreckMark.IsCookoff(label) && MarkBlast())
            {
                props.Add("Boom", "1");
                Record.Log?.LogInfo("Boom-Rec " + label + " src=disable");
            }
            props.Add("Type", null);

            Engine.invokeWriterUpdate(this);
            props.Clear();

            if (base.destroyedEvent)
            {
                Engine.invokeWriterDestroy(this);
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