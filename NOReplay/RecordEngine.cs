using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NuclearOption.Networking;
using UnityEngine;

namespace NOReplay
{
    internal class RecordEngine : MonoBehaviour
    {
        private static readonly FieldInfo bulletSim = typeof(Gun).GetField("bulletSim", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo bullets = typeof(BulletSim).GetField("bullets", BindingFlags.NonPublic | BindingFlags.Instance);

        private Dictionary<Shockwave, GameObject> waves = [];
        Shockwave[] shockwaves = [];

        private DateTime startDate;
        private DateTime curTime;
        public static RecordWriter writer;
        private float unitDiscoveryTimer = 0f;
        private float bulletSimDiscoveryTimer = 0f;
        private float shockwaveDiscoveryTimer = 0f;

        internal Dictionary<long, GameObject> unitObjects = [];


        private Dictionary<BulletSim.Bullet, GameObject> tracers = [];

        private Unit[] units = [];
        private BulletSim[] bulletSims = [];
        private bool processUnits = false;
        private bool processBulletSims = false;
        private bool processShockWaves = false;

        public void invokeWriterUpdate(RecordObject obj)
        {
            writer.UpdateObject(obj, curTime);
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
            Record.Log?.LogDebug("STARTED MONO RECORDER");
            Record.Manual = false;

        }
        void Update()
        {
            curTime += TimeSpan.FromSeconds(Time.deltaTime);
            if (!units.Any())
            {
                return;
            }
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

                                aircraft.onAddIRSource += (IRSource source) =>
                                {
                                    if (source.flare)
                                    {
                                        GameObject flare = new GameObject();
                                        flare.AddComponent<RecordFlare>();
                                        flare.GetComponent<RecordFlare>().Init(source);
                                        flare.GetComponent<RecordFlare>().enabled = true;
                                    }
                                };
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
            if (processBulletSims)
            {
                processBulletSims = false;
            }
            if (GetComponent<RecordGunBurst>() == null)
                gameObject.AddComponent<RecordGunBurst>();

            if (processShockWaves)
            {
                foreach (Shockwave wave in shockwaves)
                {
                    if (waves.ContainsKey(wave))
                    {
                        continue;
                    }
                    GameObject shockwave = new GameObject();
                    shockwave.AddComponent<RecordShock>();
                    shockwave.GetComponent<RecordShock>().Init(wave);
                    shockwave.GetComponent<RecordShock>().enabled = true;
                    waves.Add(wave, shockwave);
                }
            }
        }
        void LateUpdate()
        {
            unitDiscoveryTimer += Time.deltaTime;
            bulletSimDiscoveryTimer += Time.deltaTime;
            shockwaveDiscoveryTimer += Time.deltaTime;
            if (shockwaveDiscoveryTimer >= RecordConfig.shockwaveDiscoveryDelta.Value)
            {
                processShockWaves = ShockWaveDiscovery();
            }
            if (unitDiscoveryTimer >= RecordConfig.unitDiscoveryDelta.Value)
            {
                processUnits = UnitDiscovery();
            }
            if (bulletSimDiscoveryTimer >= RecordConfig.bulletSimDiscoveryDelta.Value)
            {
                processBulletSims = BulletSimDiscovery();
            }

        }
        void OnDisable()
        {
            writer?.Close();
            Record.Log?.LogDebug("DISABLED MONO RECORDER");
        }
        void OnDestroy()
        {
            unitObjects = [];
            waves = [];
            tracers = [];
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

        internal bool BulletSimDiscovery()
        {
            try
            {
                foreach (BulletSim.Bullet key in tracers.Keys)
                {
                    if (null == tracers[key])
                    {
                        //Record.Log?.LogDebug($"REMOVING TRACER");
                        try
                        {
                            tracers.Remove(key);
                        }
                        catch
                        {
                            //wtf
                        }

                    }
                }
            }
            catch
            {
                //wtf2
            }

            bulletSims = UnityEngine.Object.FindObjectsByType<BulletSim>(FindObjectsSortMode.None);
            //Record.Log?.LogDebug($"DISCOVERED {bulletSims.Length.ToString(CultureInfo.InvariantCulture)} BULLETSIMS!");
            bulletSimDiscoveryTimer = 0f;
            return true;
        }

        public bool ShockWaveDiscovery()
        {
            foreach (Shockwave key in waves.Keys)
            {
                if (null == waves[key])
                {
                    //Record.Log?.LogDebug($"REMOVING TRACER");
                    waves.Remove(key);
                }
            }
            shockwaves = UnityEngine.Object.FindObjectsByType<Shockwave>(FindObjectsSortMode.None);
            //Record.Log?.LogDebug($"DISCOVERED {shockwaves.Length.ToString(CultureInfo.InvariantCulture)} SHOCKWAVES!");
            shockwaveDiscoveryTimer = 0f;
            return true;
        }
    }
}