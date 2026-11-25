using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
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

            this.enabled = false;
        }

        static bool _lockHook;

        internal static void Install()
        {
            if (_lockHook) return;
            _lockHook = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var methods = typeof(Turret).GetMethods(flags);
            int n = 0;
            var harmony = new Harmony("noreplay.building.lock");
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "SetTarget") continue;
                try
                {
                    harmony.Patch(methods[i], postfix: new HarmonyMethod(typeof(RecordBuilding), nameof(AfterTarget)));
                    n++;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogInfo("Building lock " + methods[i] + " " + ex.Message);
                }
            }
            Plugin.Log?.LogInfo("Building lock hook SetTarget=" + n);
        }

        static void AfterTarget(Turret __instance)
        {
            try
            {
                if (!Record.Active || __instance == null || Plugin.CurrentDoc != null) return;
                var unit = __instance.GetComponentInParent<Building>();
                if (unit == null || Record.Host == null) return;
                var engine = Record.Host.GetComponent<RecordEngine>();
                if (engine == null || !engine.unitObjects.TryGetValue(unit.persistentID.Id, out var go) || go == null)
                    return;
                go.GetComponent<RecordBuilding>()?.CaptureLock();
            }
            catch { }
        }

        internal void CaptureLock()
        {
            if (!Record.Active || unit == null || base.disabled || unit.disabled) return;
            NoteLock(unit);
            if (props.Count == 0) return;
            Engine.invokeWriterUpdate(this);
            props.Clear();
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
            if (unit == null) return;
            string label = unit.definition != null ? unit.definition.unitName : unit.unitName;
            WriteEffect(src, label);
        }

        private void OnUnitDisabled(Unit u)
        {
            if (u != unit || base.disabled) return;

            base.disabled = true;
            props = new Dictionary<string, string>();
            props.Add("Visible", "0.0");
            // UnitDisabled instantiates the building wreck immediately. That is the
            // collapse, not a blast. Fire and explosions were written on the part.
            props.Add("Type", null);

            Engine.invokeWriterUpdate(this);
            props.Clear();

            Unsubscribe();
            this.enabled = false;
            Record.Log?.LogDebug($"STATIC DESTROYED {unitId}");
            Engine.Retire(unitId, gameObject);
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

        public override void LateUpdate()
        {
            // Absichtlich leer – Destroy kommt über Event
        }
    }
}