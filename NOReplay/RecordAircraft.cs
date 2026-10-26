using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOReplay
{
    internal class RecordAircraft : RecordUnit
    {
        private bool lastGear = false;
        int _liveryLog;
        private bool lastRadar = false;
        private float lastAGL = float.NaN;
        private float lastTAS = float.NaN;
        private float lastAOA = float.NaN;
        private float lastThrottle = float.NaN;
        private float lastPitch = float.NaN;
        private float lastYaw = float.NaN;
        private float lastRoll = float.NaN;
        private float lastThrust = float.NaN;
        private float avgThrust = float.NaN;

        private Vector3 lastHead = Vector3.zero;

        Aircraft aircraft;
        Aircraft localAircraft = null;

        private string? steamId;
        private string? lastEmittedPilot;
        readonly List<Unit> _targetScratch = new();
        int _lastTargetCount;
        long[] _lastTargetIds = Array.Empty<long>();

        public virtual void Init(Aircraft aircraft)
        {
            GameManager.GetLocalAircraft(out localAircraft);
            if (localAircraft && localAircraft.persistentID == aircraft.persistentID) { base.unit = localAircraft; } else { base.unit = aircraft; }
            this.aircraft = (Aircraft)base.unit;
            base.unitId = aircraft.persistentID.Id;
            base.tacviewId = aircraft.persistentID.Id + 1;
            base.destroyedEvent = true;
            base.canTarget = true;
            lastState = aircraft.unitState;
            Faction? faction = this.unit.NetworkHQ?.faction;
            string[] info = { "Default", "Air" };
            if (Record.UnitInfo["aircraft"].ContainsKey(aircraft.definition.code))
            {
                info = Record.UnitInfo["aircraft"][unit.definition.code];
            }
            props = new Dictionary<string, string>()
            {
                { "Name", this.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Debug", lastState.ToString()},
                { "Type", info[1]},
                { "CallSign", $"{aircraft.definition.code} {tacviewId:X}" }
            };

            if (aircraft.Player != null)
            {
                steamId = aircraft.Player.SteamID.ToString();
                if (RecordConfig.RecordSteamID.Value == true)
                    props["Registration"] = steamId;

                PilotNameCache.Watch(steamId, aircraft.Player);
                ApplyPilotProps(force: true);
            }

            ApplyLoadoutAndLivery();

            Engine.invokeWriterUpdate(this);
            props.Clear();
            this.enabled = true;
            base.enabled = true;
            RecordTurrets.Attach(this, aircraft);
        }

        public override void Update()
        {
            base.destroyedEvent = !aircraft.IsLanded();
            if (!this.enabled || unit.disabled)
                return;

            timer += Time.deltaTime;
            if (timer < RecordConfig.aircraftUpdateDelta.Value)
                return;

            if (aircraft.Player != null)
            {
                steamId ??= aircraft.Player.SteamID.ToString();
                PilotNameCache.Watch(steamId, aircraft.Player);
                ApplyPilotProps(force: false);
            }

            UpdatePose();
            UpdateAircraft();
            UpdateState();
            UpdateTargets();
            Engine.invokeWriterUpdate(this);
            props.Clear();
            timer = 0;
        }

        internal override void UpdateTargets()
        {
            _targetScratch.Clear();
            foreach (var unitTarget in aircraft.weaponManager.GetTargetList())
            {
                if (unitTarget != null)
                    _targetScratch.Add(unitTarget);
            }
            int n = _targetScratch.Count;
            if (n == 0)
                return;
            bool changed = n != _lastTargetCount;
            if (!changed)
            {
                for (int i = 0; i < n; i++)
                {
                    if (_targetScratch[i].persistentID.Id != _lastTargetIds[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (!changed)
                return;
            if (_lastTargetIds.Length < n)
                _lastTargetIds = new long[n];
            for (int i = 0; i < n; i++)
                _lastTargetIds[i] = _targetScratch[i].persistentID.Id;
            _lastTargetCount = n;
            if (n <= 1)
                return;
            int max = n > 10 ? 10 : n;
            for (int i = 0; i < max; i++)
            {
                lockedTargetString = i == 0 ? "LockedTarget" : $"LockedTarget{i:X}";
                props.Add(lockedTargetString, $"{GetTacviewIdOfUnit(_lastTargetIds[i]):X}");
            }
        }

        private float getAvgThrust()
        {
            float avgThrust = float.NaN;
            int count = aircraft.engines.Count;
            for (int i = 0; i < aircraft.engines.Count; i++)
            {
                float thrust = aircraft.engines[i].GetThrust();
                if (thrust != 0f)
                    avgThrust = avgThrust + thrust;
                else
                    count = count - 1;
            }
            if (avgThrust != 0f)
                avgThrust /= count;
            return avgThrust;
        }

        void UpdateAircraft()
        {
            if (aircraft.speed != lastTAS && RecordConfig.RecordSpeed.Value == true)
            {
                props.Add("TAS", aircraft.speed.ToString("0.##", CultureInfo.InvariantCulture));
                props.Add("Mach", (aircraft.speed / 340).ToString("0.###", CultureInfo.InvariantCulture));
                lastTAS = aircraft.speed;
            }

            Vector3 vector3 = aircraft.cockpit.transform.InverseTransformDirection(aircraft.cockpit.rb.velocity);
            float num = MathF.Round(Mathf.Atan2(vector3.y, vector3.z) * -57.29578f, 2);

            if (num != lastAOA && RecordConfig.RecordAOA.Value == true)
            {
                props.Add("AOA", num.ToString("0.##"));
                lastAOA = num;
            }

            if (aircraft.radarAlt != lastAGL && RecordConfig.RecordAGL.Value == true)
            {
                props.Add("AGL", Mathf.Max(0, aircraft.radarAlt).ToString("0.##", CultureInfo.InvariantCulture));
                lastAGL = aircraft.radarAlt;
            }

            if (aircraft.gearDeployed != lastGear && RecordConfig.RecordLandingGear.Value == true)
            {
                props.Add("LandingGear", aircraft.gearDeployed ? "1" : "0");
                lastGear = aircraft.gearDeployed;
            }

            if (aircraft.radar != lastRadar && RecordConfig.RecordRadarMode.Value == true)
            {
                props.Add("RadarMode", aircraft.radar.activated ? "1" : "0");
                lastRadar = aircraft.radar;
            }

            if (localAircraft && localAircraft.persistentID == aircraft.persistentID && CameraStateManager.cameraMode == CameraMode.cockpit && RecordConfig.RecordPilotHead.Value == true)
            {
                Camera camera = CameraStateManager.i.mainCamera;
                Vector3 rot = camera.transform.localEulerAngles;
                Vector3 newRot = new(MathF.Round(rot.x, 2), MathF.Round(rot.y, 2), MathF.Round(rot.z, 2));
                if (newRot != lastHead)
                {
                    if (!Mathf.Approximately(newRot.x, lastHead.x))
                    {
                        float adjusted_pitch = newRot.x > 180.0f ? 360 - newRot.x : -newRot.x;
                        props.Add("PilotHeadPitch", adjusted_pitch.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    if (!Mathf.Approximately(newRot.y, lastHead.y))
                    {
                        props.Add("PilotHeadYaw", newRot.y.ToString("0.##", CultureInfo.InvariantCulture));
                    }
                    lastHead = newRot;
                }
            }

            if (RecordConfig.RecordExtraTelemetry.Value == true)
            {
                if (lastThrottle != aircraft.GetInputs().throttle)
                {
                    props.Add("Throttle", aircraft.GetInputs().throttle.ToString("0.##", CultureInfo.InvariantCulture));
                    lastThrottle = aircraft.GetInputs().throttle;
                }
                if (lastRoll != aircraft.GetInputs().roll)
                {
                    props.Add("RollControlInput", aircraft.GetInputs().roll.ToString("0.##", CultureInfo.InvariantCulture));
                    lastRoll = aircraft.GetInputs().roll;
                }
                if (lastPitch != aircraft.GetInputs().pitch)
                {
                    props.Add("PitchControlInput", aircraft.GetInputs().pitch.ToString("0.##", CultureInfo.InvariantCulture));
                    lastPitch = aircraft.GetInputs().pitch;
                }
                if (lastYaw != aircraft.GetInputs().yaw)
                {
                    props.Add("YawControlInput", aircraft.GetInputs().yaw.ToString("0.##", CultureInfo.InvariantCulture));
                    lastYaw = aircraft.GetInputs().yaw;
                }
                if (lastThrust != getAvgThrust())
                {
                    avgThrust = getAvgThrust();
                    props.Add("EngineRPM", avgThrust.ToString("0.##", CultureInfo.InvariantCulture));
                    lastThrust = avgThrust;
                }
            }
        }

        void ApplyLoadoutAndLivery()
        {
            try
            {
                var loadout = aircraft.loadout;
                if (loadout?.weapons != null && loadout.weapons.Count > 0)
                {
                    var parts = new List<string>();
                    for (int i = 0; i < loadout.weapons.Count; i++)
                    {
                        var mount = loadout.weapons[i];
                        if (mount == null) continue;
                        string key = mount.jsonKey;
                        if (string.IsNullOrEmpty(key)) key = mount.mountName;
                        if (string.IsNullOrEmpty(key)) key = mount.name;
                        if (string.IsNullOrEmpty(key)) continue;
                        key = key.Replace(",", " ").Replace("|", " ").Replace("=", " ");
                        parts.Add(i + ":" + key + ":" + mount.ammo);
                    }
                    if (parts.Count > 0)
                        props["Loadout"] = string.Join("|", parts);
                }
            }
            catch { }

            try
            {
                var key = aircraft.NetworkLiveryKey;
                string value = key.Type.ToString();
                if (key.Type == LiveryKey.KeyType.Builtin)
                    value += ":" + key.Index;
                else if (key.Type == LiveryKey.KeyType.AppData && !string.IsNullOrEmpty(key.AppDataName))
                    value += ":" + key.AppDataName.Replace(",", " ").Replace("|", " ").Replace("=", " ");
                else if (key.Type == LiveryKey.KeyType.Workshop)
                    value += ":" + key.Id;
                props["Livery"] = value;
                if (_liveryLog < 2)
                {
                    _liveryLog++;
                    Record.Log.LogInfo("Livery recorded: " + value);
                }
            }
            catch (Exception ex) { Record.Log.LogWarning("Livery fail: " + ex.Message); }
        }

        private void ApplyPilotProps(bool force)
        {
            string pilot = PilotNameCache.Resolve(steamId);
            if (!force && pilot == lastEmittedPilot)
                return;

            lastEmittedPilot = pilot;
            props["CallSign"] = $"{aircraft.definition.code} ({EscapeAcmi(pilot)})";
        }

        private static string EscapeAcmi(string value) => (value ?? "").Replace(",", "\\,");
    }
}