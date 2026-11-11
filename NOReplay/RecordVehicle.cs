using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    internal class RecordVehicle : RecordUnit
    {
        private readonly static Dictionary<string, string> TYPES = new()
        {
            { "HLT", "Ground+Heavy+Vehicle" },
            { "StratoLance R9 Launcher", "Ground+Heavy+Vehicle+AntiAircraft" },
            { "T9K41 Boltstrike", "Ground+Heavy+Vehicle+AntiAircraft" },
            { "Spearhead MBT", "Ground+Heavy+Vehicle+Tank" },
            { "Type-12 MBT", "Ground+Heavy+Vehicle+Tank" },
            { "HLT Radar Truck", "Ground+Heavy+Vehicle+Sensor" },
            { "HLT Flatbed", "Ground+Heavy+Vehicle" },
            { "HLT Munitions Truck", "Ground+Heavy+Vehicle" },
            { "HLT Tractor", "Ground+Heavy+Vehicle" },
            { "HLT Fuel Tanker", "Ground+Heavy+Vehicle" },
            { "LCV25 AA", "Ground+Light+Vehicle+AntiAircraft" },
            { "LCV25 AT", "Ground+Light+Vehicle" },
            { "LCV45 Recon Truck", "Ground+Light+Vehicle" },
            { "AFV6 APC", "Ground+Medium+Vehicle" },
            { "AFV6 IFV", "Ground+Medium+Vehicle" },
            { "AFV6 AA", "Ground+Medium+Vehicle+AntiAircraft" },
            { "AFV6 AT", "Ground+Medium+Vehicle" },
            { "AFV8 APC", "Ground+Medium+Vehicle" },
            { "AFV8 IFV", "Ground+Medium+Vehicle" },
            { "AFV8 Mobile Air Defense", "Ground+Medium+Vehicle+AntiAircraft" },
            { "Linebreaker APC", "Ground+Medium+Vehicle" },
            { "Linebreaker IFV", "Ground+Medium+Vehicle" },
            { "Linebreaker SAM", "Ground+Medium+Vehicle+AntiAircraft" },
            { "AeroSentry SPAAG", "Ground+Medium+Vehicle+AntiAircraft" },
            { "FGA-57 Anvil", "Ground+Medium+Vehicle+AntiAircraft" },
            { "Hexhound GMG", "Ground+Medium+Vehicle"},
            { "Hexhound SAM", "Ground+Medium+AntiAircraft+Vehicle" },
            { "M12 Jackknife", "Ground+Medium+Vehicle"},
            { "CRAM Trailer", "Ground+Medium+AntiAircraft+Vehicle"},
            { "Laser CIWS Trailer", "Ground+Medium+AntiAircraft+Vehicle" },
            { "Munitions Container", "Misc+Container" },
            { "Munitions Pallet", "Misc+Container" },
            { "Naval Supply Pallet", "Misc+Container" },
            { "MSV Flatbed", "Ground+Heavy+Vehicle" },
            { "MSV Tractor", "Ground+Heavy+Vehicle" },
            { "MSV Fuel Tanker", "Ground+Heavy+Vehicle" },
            { "MSV Munitions", "Ground+Heavy+Vehicle" },
            { "MSV MRAP", "Ground+Heavy+Vehicle" },
            { "MSV Fire Control", "Ground+Heavy+Vehicle" },
            { "HLT Fire Control", "Ground+Heavy+Vehicle" },
            { "Boltstrike RAM45 Launcher", "Ground+AntiAircraft+Vehicle" },
            { "Radar Container", "Misc+Container" },
            { "MSV Nuclear Ballistic Missile Launcher", "Ground+Heavy+Vehicle" },
            { "MSV Ballistic Missile Launcher", "Ground+Heavy+Vehicle" },
            { "MSV R9 Stratolance Launcher", "Ground+AntiAircraft+Vehicle" },
            { "MSV CRAM", "Ground+Heavy+Vehicle" },
            { "MSV LADS", "Ground+Heavy+Vehicle" },
            { "MSV Radar", "Ground+Heavy+Vehicle" },
            { "Fuel Bladder 1500L", "Misc+Container" },
            { "Fuel Bladder 6000L", "Misc+Container" },
            { "Fuel Bladder 10,000L", "Misc+Container" },
            { "Wreck HLT", "Ground+Heavy+Vehicle" },
            { "Wreck MBT", "Ground+Heavy+Vehicle" }

        };

        private readonly static Dictionary<string, int> RANGE = new()
        {
            { "StratoLance R9 Launcher", 50000 },
            { "T9K41 Boltstrike", 15000 },
            { "LCV24 AA", 5000 },
            { "AFV6 AA", 5000 },
            { "AFV8 Mobile Air Defense", 5000 },
            { "Linebreaker SAM", 5000 },
            { "AeroSentry SPAAG", 4000 },
            { "FGA-57 Anvil", 5500 }
        };

        private Unit? lastTarget;

        public virtual void Init(GroundVehicle vehicle)
        {


            base.unit = vehicle;
            base.unitId = unit.persistentID.Id;
            base.tacviewId = unit.persistentID.Id + 1;
            lastState = unit.unitState;
            Faction? faction = base.unit.NetworkHQ?.faction;

            string[] info = { "Default", "Ground", "0", "0" };
            string unitName = unit.definition.unitName;
            if (TYPES.TryGetValue(unitName, out var mappedType))
            {
                info = new[] { unitName, mappedType, "5000", "5000" };
            }
            else if (Record.UnitInfo["vehicles"].ContainsKey(unitName))
            {
                info = Record.UnitInfo["vehicles"][unitName];
            }
            int range1 = 0;
            int range2 = 0;
            int winningRange = 0;
            int.TryParse(info[2], out range1);
            int.TryParse(info[3], out range2);
            if (range1 > range2) { winningRange = range1; } else { winningRange = range2; }


            if (new[] { "AA", "CIWS (L)", "CRAM", "SAM IR", "SAM R", "SPAAG", "RDR", "HPAD", "HGR-H", "HGR-M", "REV" }.Any(c => unit.definition.code.Contains(c)))
            {
                base.destroyedEvent = true;
            }

            props = new Dictionary<string, string>()
            {
                { "Name", this.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "CallSign", $"{unit.definition.code} {tacviewId:X}"},
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", info[1]},
                { "EngagementRange", winningRange.ToString(CultureInfo.InvariantCulture) },
                { "Debug", lastState.ToString()}
            };
            Engine.invokeWriterUpdate(this);
            props.Clear();
            this.enabled = true;
            base.enabled = true;
            RecordTurrets.Attach(this, vehicle);
            RecordWreck.Register(this, unit.persistentID.Id);
            _unitName = unitName;
        }

        string _unitName = "";

        bool _dying;
        bool _wrecked;
        const float DyingSampleSeconds = 0.2f;

        public override void Update()
        {
            if (!enabled || _wrecked) return;
            if (unit == null)
            {
                if (_dying) OnWreckSpawned();
                return;
            }
            if (_dying || unit.disabled)
            {
                if (!_dying) BeginDying();
                timer += Time.deltaTime;
                if (timer < DyingSampleSeconds) return;
                timer = 0f;
                UpdatePose();
                if (props.Count > 0)
                {
                    Engine.invokeWriterUpdate(this);
                    props.Clear();
                }
                return;
            }
            timer += Time.deltaTime;
            if (timer < RecordConfig.vehicleUpdateDelta.Value)
            {
                return;
            }
            UpdatePose();
            UpdateTargets();
            UpdateState();
            Engine.invokeWriterUpdate(this);
            props.Clear();
            timer = 0;
        }
        internal override void UpdateTargets()
        {
            foreach (WeaponStation station in unit.weaponStations)
            {
                try
                {
                    targets.AddItem<Unit>(station.GetTurret().GetTarget());
                }
                catch
                {
                    //no target
                }

            }

            if (targets.Any())
            {
                if (!lastTargets.Any())
                {
                    lastTargets = targets;
                }
                else
                {
                    if (lastTargets == targets)
                    {
                        return;
                    }
                }
                lastTargets = targets;
                int max = targets.Length;
                if (max > 10)
                {
                    max = 10;
                }
                if (targets.Length > 1)
                {

                    for (int i = 0; i < max; i++)
                    {
                        if (i == 0)
                        {
                            lockedTargetString = "LockedTarget";
                        }
                        else
                        {
                            lockedTargetString = $"LockedTarget{i:X}";
                        }
                        props.Add(lockedTargetString, $"{GetTacviewIdOfUnit(targets[i].persistentID.Id):X}");
                    }
                }
            }
            targets = [];
        }

        public override void LateUpdate()
        {
            // Visible=0 is OnWreckSpawned, when GroundVehicle.SpawnWreckage runs.
        }

        void BeginDying()
        {
            _dying = true;
            props.Clear();
            if (unit != null) UpdatePose(true);
            props["Dying"] = "1";
            // The wreck prefab is at least five seconds later for every vehicle.
            // That wait is not the blast. An ordinary vehicle explodes at death.
            if (!WreckMark.IsCookoff(_unitName))
                props["Boom"] = "1";
            Engine.invokeWriterUpdate(this);
            props.Clear();
            if (destroyedEvent)
                Engine.invokeWriterDestroy(this);
        }

        internal void OnWreckSpawned()
        {
            if (_wrecked) return;
            _wrecked = true;
            try
            {
                props.Clear();
                if (unit != null) UpdatePose(true);
                props["Visible"] = "0.0";
                // Ammunition trucks cook off when the wreck is spawned. The wait
                // is the game's 5s delay plus any extra time the body is still moving.
                if (WreckMark.IsCookoff(_unitName))
                    props["Boom"] = "1";
                props["Type"] = null!;
                Engine.invokeWriterUpdate(this);
                props.Clear();
            }
            catch (Exception ex)
            {
                Record.Log?.LogDebug("Vehicle wreck: " + ex.Message);
            }
            if (unit != null) RecordWreck.Forget(unit.persistentID.Id);
            enabled = false;
            UnityEngine.Object.Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (!_wrecked && _dying)
                OnWreckSpawned();
            else if (unit != null)
                RecordWreck.Forget(unit.persistentID.Id);
        }
    }
}