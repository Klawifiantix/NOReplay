using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    internal class RecordEngine : MonoBehaviour
    {
        private Dictionary<Shockwave, GameObject> waves = [];
        readonly List<Shockwave> deadWaves = new();
        Shockwave[] shockwaves = [];

        private DateTime startDate;
        private DateTime curTime;
        public static RecordWriter writer;
        private float unitDiscoveryTimer = 0f;
        private float shockwaveDiscoveryTimer = 0f;

        internal Dictionary<long, GameObject> unitObjects = [];
        internal Dictionary<long, long> tacviewIds = [];

        private Unit[] units = [];
        private bool processUnits = false;
        private bool processShockWaves = false;

        public void invokeWriterUpdate(RecordObject obj)
        {
            writer.UpdateObject(obj, curTime);
        }

        public void RegisterUnitFromListener(Unit unit)
        {
            if (unit == null || unitObjects.ContainsKey(unit.persistentID.Id)) return;
            GameObject? acmi = null;
            switch (unit)
            {
                case Aircraft aircraft:
                    acmi = new GameObject();
                    acmi.AddComponent<RecordAircraft>().Init(aircraft);
                    break;
                case GroundVehicle vehicle:
                    acmi = new GameObject();
                    acmi.AddComponent<RecordVehicle>().Init(vehicle);
                    break;
                case Ship ship:
                    acmi = new GameObject();
                    acmi.AddComponent<RecordShip>().Init(ship);
                    break;
                case Building building:
                    acmi = new GameObject();
                    acmi.AddComponent<RecordBuilding>().Init(building);
                    break;
                case Missile missile:
                    acmi = new GameObject();
                    acmi.AddComponent<RecordMissile>().Init(missile);
                    Record.Log?.LogInfo("Listener: Missile " + (missile.definition?.unitName ?? "?"));
                    break;
            }
            if (acmi != null)
                unitObjects.Add(unit.persistentID.Id, acmi);
        }

        public void CaptureExistingUnits()
        {
            var all = UnityEngine.Object.FindObjectsOfType<Unit>();
            int captured = 0;
            foreach (var unit in all)
            {
                if (unit == null || unitObjects.ContainsKey(unit.persistentID.Id)) continue;
                GameObject? acmi = null;
                switch (unit)
                {
                    case Aircraft aircraft:
                        acmi = new GameObject();
                        acmi.AddComponent<RecordAircraft>().Init(aircraft);
                        break;
                    case GroundVehicle vehicle:
                        acmi = new GameObject();
                        acmi.AddComponent<RecordVehicle>().Init(vehicle);
                        break;
                    case Ship ship:
                        acmi = new GameObject();
                        acmi.AddComponent<RecordShip>().Init(ship);
                        break;
                    case Building building:
                        acmi = new GameObject();
                        acmi.AddComponent<RecordBuilding>().Init(building);
                        break;
                    case Scenery scenery:
                        acmi = new GameObject();
                        acmi.AddComponent<RecordScenery>().Init(scenery);
                        break;
                    case Missile missile:
                        acmi = new GameObject();
                        acmi.AddComponent<RecordMissile>().Init(missile);
                        break;
                }
                if (acmi != null)
                {
                    unitObjects.Add(unit.persistentID.Id, acmi);
                    captured++;
                }
            }
            Plugin.Log.LogInfo("Start-Capture: " + captured + " vorhandene Einheiten");
        }

        public void invokeWriterRemove(RecordObject obj)
        {
            writer.RemoveObject(obj, curTime);
        }

        public void invokeWriterDestroy(RecordObject obj)
        {
            writer.WriteDestroyedEvent(obj, curTime);
        }

        public void invokeWriterRepair(RecordObject obj)
        {
            writer.WriteRepairedEvent(obj, curTime);
        }

        void Awake()
        {
            Record.Log?.LogDebug("USING MONO RECORDER");
            if (RecordConfig.UseMissionTime?.Value == true)
            {
                startDate = DateTime.Today + TimeSpan.FromHours(MissionManager.CurrentMission.environment.timeOfDay);
                Record.Log?.LogDebug("USING MISSION CLOCK");
            }
            else
            {
                startDate = DateTime.Now;
                Record.Log?.LogDebug("USING SERVER CLOCK");
            }

            curTime = startDate;
            writer = new RecordWriter(startDate);
            if (GetComponent<RecordGunBurst>() == null)
                gameObject.AddComponent<RecordGunBurst>();
            Record.Log?.LogDebug("STARTED MONO RECORDER");
            Record.Manual = false;

        }
        void Update()
        {
            curTime += TimeSpan.FromSeconds(Time.deltaTime);
            PilotNameCache.Tick();
            if (processUnits)
            {
                foreach (var unit in units)
                {
                    if (!unit.networked || (unit.disabled && unit.GetType() != typeof(Missile)))
                    {
                        continue;
                    }
                    bool isNew = false;
                    if (!unitObjects.TryGetValue(unit.persistentID.Id, out GameObject acmi))
                    {

                        switch (unit)
                        {
                            case Aircraft aircraft:

                                // Flares handled via SpawnLocal hook
                                acmi = new GameObject();
                                acmi.AddComponent<RecordAircraft>();
                                acmi.GetComponent<RecordAircraft>().Init(aircraft);
                                acmi.GetComponent<RecordAircraft>().enabled = true;
                                Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                    $"{unit.definition.unitName}," +
                                    $"{unit.definition.code}");
                                unitObjects.Add(unit.persistentID.Id, acmi);
                                isNew = true;
                                break;
                            case Missile missile:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordMissile>();
                                acmi.GetComponent<RecordMissile>().Init(missile);
                                acmi.GetComponent<RecordMissile>().enabled = true;
                                Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                    $"{unit.definition.unitName}," +
                                    $"{unit.definition.code}");
                                unitObjects.Add(unit.persistentID.Id, acmi);
                                isNew = true;
                                break;
                            case GroundVehicle vehicle:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordVehicle>();
                                acmi.GetComponent<RecordVehicle>().Init(vehicle);
                                acmi.GetComponent<RecordVehicle>().enabled = true;
                                Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                    $"{unit.definition.unitName}," +
                                    $"{unit.definition.code}");
                                unitObjects.Add(unit.persistentID.Id, acmi);
                                isNew = true;
                                break;
                            case Ship ship:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordShip>();
                                acmi.GetComponent<RecordShip>().Init(ship);
                                acmi.GetComponent<RecordShip>().enabled = true;
                                Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                    $"{unit.definition.unitName}," +
                                    $"{unit.definition.code}");
                                unitObjects.Add(unit.persistentID.Id, acmi);
                                isNew = true;
                                break;
                            case PilotDismounted pilot:
                                if (RecordConfig.RecordEjectedPilots.Value == true)
                                {
                                    acmi = new GameObject();
                                    acmi.AddComponent<RecordPilot>();
                                    acmi.GetComponent<RecordPilot>().Init(pilot);
                                    acmi.GetComponent<RecordPilot>().enabled = true;
                                    Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                        $"{unit.definition.unitName}," +
                                        $"{unit.definition.code}");
                                    unitObjects.Add(unit.persistentID.Id, acmi);
                                    isNew = true;
                                }
                                break;
                            case Building building:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordBuilding>();
                                acmi.GetComponent<RecordBuilding>().Init(building);
                                acmi.GetComponent<RecordBuilding>().enabled = true;
                                Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                    $"{unit.definition.unitName}," +
                                    $"{unit.definition.code}");
                                unitObjects.Add(unit.persistentID.Id, acmi);
                                isNew = true;
                                break;
                            case Scenery scenery:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordScenery>();
                                acmi.GetComponent<RecordScenery>().Init(scenery);
                                acmi.GetComponent<RecordScenery>().enabled = true;
                                Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                    $"{unit.definition.unitName}," +
                                    $"{unit.definition.code}");
                                unitObjects.Add(unit.persistentID.Id, acmi);
                                isNew = true;
                                break;
                            default:
                                break;
                        }
                        //acmi = new GameObject();
                    }
                }
                processUnits = false;
            }
            if (processShockWaves)
            {
                foreach (Shockwave wave in shockwaves)
                {
                    if (wave == null || waves.ContainsKey(wave))
                        continue;
                    GameObject shockwave = new GameObject();
                    shockwave.AddComponent<RecordShock>();
                    shockwave.GetComponent<RecordShock>().Init(wave);
                    shockwave.GetComponent<RecordShock>().enabled = true;
                    waves.Add(wave, shockwave);
                }
                processShockWaves = false;
            }
        }
        void LateUpdate()
        {
            unitDiscoveryTimer += Time.deltaTime;
            shockwaveDiscoveryTimer += Time.deltaTime;
            if (shockwaveDiscoveryTimer >= RecordConfig.shockwaveDiscoveryDelta.Value)
            {
                processShockWaves = ShockWaveDiscovery();
            }
            // Unit discovery is now event-driven via RecordListener (UnitRegistry.RegisterUnit).
            // Periodic FindObjectsByType scan disabled.
            // if (unitDiscoveryTimer >= RecordConfig.unitDiscoveryDelta.Value)
            // {
            //     processUnits = UnitDiscovery();
            // }

        }
        void OnDisable()
        {
            writer?.Close();
            Record.Log?.LogDebug("DISABLED MONO RECORDER");
        }
        void OnDestroy()
        {
            unitObjects = [];
            tacviewIds = [];
            waves = [];
            writer?.Close();
            Record.Log?.LogDebug("DESTROYED MONO RECORDER");
        }

        private bool UnitDiscovery()
        {
            foreach (int key in unitObjects.Keys)
            {
                if (null == unitObjects[key])
                {
                    // Record.Log?.LogDebug($"REMOVING OBJECT {key.ToString(CultureInfo.InvariantCulture)}");
                    unitObjects.Remove(key);
                }
            }
            //Record.Log?.LogDebug($"Frame TICK at {curTime.ToString(CultureInfo.InvariantCulture)}");
            units = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            //Record.Log?.LogDebug($"DISCOVERED {units.Length.ToString(CultureInfo.InvariantCulture)} UNITS!");
            unitDiscoveryTimer = 0f;
            return true;

        }

        public bool ShockWaveDiscovery()
        {
            deadWaves.Clear();
            foreach (var kv in waves)
            {
                if (kv.Value == null)
                    deadWaves.Add(kv.Key);
            }
            for (int i = 0; i < deadWaves.Count; i++)
                waves.Remove(deadWaves[i]);
            shockwaves = UnityEngine.Object.FindObjectsByType<Shockwave>(FindObjectsSortMode.None);
            shockwaveDiscoveryTimer = 0f;
            return true;
        }
    }
}