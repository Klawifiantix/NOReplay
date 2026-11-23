using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    internal class RecordEngine : MonoBehaviour
    {
        private Dictionary<Shockwave, GameObject> waves = [];

        private DateTime startDate;
        private DateTime curTime;
        public static RecordWriter writer;
        private float unitDiscoveryTimer = 0f;
        readonly HashSet<long> _capturedIds = new HashSet<long>();
        float _openUntil;
        float _openNext;
        bool _atStart;

        internal Dictionary<long, GameObject> unitObjects = [];
        internal Dictionary<long, long> tacviewIds = [];

        private Unit[] units = [];
        private bool processUnits = false;
        internal int Retired { get; private set; }

        public void invokeWriterUpdate(RecordObject obj)
        {
            writer.UpdateObject(obj, _atStart ? startDate : curTime);
        }

        /// <summary>
        /// For a few seconds after recording starts, look again for units the
        /// first snapshot missed and write them at the recording start.
        /// </summary>
        internal void ArmOpeningCapture()
        {
            _openUntil = Time.unscaledTime + 5f;
            _openNext = 0f;
        }

        internal void WriteGlobalEvent(string body)
        {
            if (writer == null || string.IsNullOrEmpty(body)) return;
            writer.WriteGlobal(body, curTime);
        }

        public void RegisterUnitFromListener(Unit unit)
        {
            if (unit == null || !unit.persistentID.IsValid) return;
            long id = unit.persistentID.Id;
            if (unitObjects.ContainsKey(id) || !_capturedIds.Add(id)) return;
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
                    Record.Log?.LogInfo("Listener: Missile " + (missile.definition?.unitName ?? "?"));
                    break;
                case PilotDismounted pilot:
                    acmi = new GameObject();
                    acmi.AddComponent<RecordPilot>().Init(pilot);
                    break;
                case Container cargo:
                    acmi = new GameObject();
                    acmi.AddComponent<RecordContainer>().Init(cargo);
                    break;
            }
            if (acmi != null)
                Track(unit, acmi);
        }

        public void CaptureExistingUnits()
        {
            int captured = CaptureUnits(false);
            Plugin.Log.LogInfo("Start-Capture: " + captured + " vorhandene Einheiten");
            SeedShocks();
        }

        void TickOpeningCapture()
        {
            if (!Record.Active || _openUntil <= 0f) return;
            if (Time.unscaledTime > _openUntil)
            {
                _openUntil = 0f;
                return;
            }
            if (Time.unscaledTime < _openNext) return;
            _openNext = Time.unscaledTime + 0.5f;
            _atStart = true;
            int captured = 0;
            try { captured = CaptureUnits(true); }
            finally { _atStart = false; }
            if (captured > 0)
                Plugin.Log.LogInfo("Opening capture +" + captured + " at t=0");
        }

        int CaptureUnits(bool includeInactive)
        {
            var all = includeInactive
                ? Resources.FindObjectsOfTypeAll(typeof(Unit))
                : UnityEngine.Object.FindObjectsOfType(typeof(Unit));
            int captured = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Unit unit) continue;
                try
                {
                    if (TryCapture(unit, includeInactive)) captured++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Capture " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return captured;
        }

        bool TryCapture(Unit unit, bool includeInactive)
        {
            if (unit == null || !unit.persistentID.IsValid) return false;
            var go = unit.gameObject;
            if (go == null || !go.scene.IsValid()) return false;
            if (!includeInactive && !go.activeInHierarchy) return false;
            long id = unit.persistentID.Id;
            if (unitObjects.ContainsKey(id) || !_capturedIds.Add(id)) return false;
            GameObject? acmi = MakeRecorder(unit);
            if (acmi == null) return false;
            Track(unit, acmi);
            return true;
        }

        static GameObject? MakeRecorder(Unit unit)
        {
            switch (unit)
            {
                case Aircraft aircraft:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordAircraft>().Init(aircraft);
                    return acmi;
                }
                case GroundVehicle vehicle:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordVehicle>().Init(vehicle);
                    return acmi;
                }
                case Ship ship:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordShip>().Init(ship);
                    return acmi;
                }
                case Building building:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordBuilding>().Init(building);
                    return acmi;
                }
                case Scenery scenery:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordScenery>().Init(scenery);
                    return acmi;
                }
                case Missile missile:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordMissile>().Init(missile);
                    return acmi;
                }
                case PilotDismounted pilot:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordPilot>().Init(pilot);
                    return acmi;
                }
                case Container cargo:
                {
                    var acmi = new GameObject();
                    acmi.AddComponent<RecordContainer>().Init(cargo);
                    return acmi;
                }
                default:
                    return null;
            }
        }

        void Track(Unit unit, GameObject acmi)
        {
            if (acmi == null) return;
            var rec = acmi.GetComponent<RecordUnit>();
            if (rec != null && rec.disabled)
            {
                RecordBay.Forget(unit);
                Retired++;
                return;
            }
            unitObjects.Add(unit.persistentID.Id, acmi);
            if (unit is Aircraft || unit is Ship || unit is GroundVehicle)
                RecordBay.Watch(unit);
        }

        public void Retire(long id, GameObject recorder)
        {
            if (unitObjects.Remove(id))
                Retired++;
            tacviewIds.Remove(id);
            if (recorder == null) return;
            var turrets = recorder.GetComponent<RecordTurrets>();
            if (turrets != null) turrets.enabled = false;
            recorder.SetActive(false);
        }

        public void WatchShock(Shockwave wave)
        {
            if (wave == null || waves.ContainsKey(wave)) return;
            var host = new GameObject();
            var rec = host.AddComponent<RecordShock>();
            rec.Init(wave);
            rec.enabled = true;
            waves.Add(wave, host);
        }

        void SeedShocks()
        {
            var found = UnityEngine.Object.FindObjectsOfType<Shockwave>();
            for (int i = 0; i < found.Length; i++)
                WatchShock(found[i]);
        }

        public void invokeWriterRemove(RecordObject obj)
        {
            writer.RemoveObject(obj, _atStart ? startDate : curTime);
        }

        public void invokeWriterDestroy(RecordObject obj)
        {
            writer.WriteDestroyedEvent(obj, _atStart ? startDate : curTime);
        }

        public void invokeWriterRepair(RecordObject obj)
        {
            writer.WriteRepairedEvent(obj, _atStart ? startDate : curTime);
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
        float _lockAt = -1f;

        void Update()
        {
            long t0 = RecordPerf.Stamp();
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
                                Track(unit, acmi);
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
                                Track(unit, acmi);
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
                                Track(unit, acmi);
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
                                Track(unit, acmi);
                                isNew = true;
                                break;
                            case PilotDismounted pilot:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordPilot>();
                                acmi.GetComponent<RecordPilot>().Init(pilot);
                                acmi.GetComponent<RecordPilot>().enabled = true;
                                Track(unit, acmi);
                                isNew = true;
                                break;
                            case Container cargo:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordContainer>();
                                acmi.GetComponent<RecordContainer>().Init(cargo);
                                acmi.GetComponent<RecordContainer>().enabled = true;
                                Track(unit, acmi);
                                isNew = true;
                                break;
                            case Building building:
                                acmi = new GameObject();
                                acmi.AddComponent<RecordBuilding>();
                                acmi.GetComponent<RecordBuilding>().Init(building);
                                acmi.GetComponent<RecordBuilding>().enabled = true;
                                Record.Log?.LogDebug($"RECORDED UNIT,{unit.definition.name}," +
                                    $"{unit.definition.unitName}," +
                                    $"{unit.definition.code}");
                                Track(unit, acmi);
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
                                Track(unit, acmi);
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
            RecordPerf.Add("engine", RecordPerf.Stamp() - t0);
        }
        void LateUpdate()
        {
            long t0 = RecordPerf.Stamp();
            unitDiscoveryTimer += Time.deltaTime;
            RecordPerf.Add("engine-late", RecordPerf.Stamp() - t0);
            if (Record.Active)
            {
                long bay = RecordPerf.Stamp();
                try { RecordBay.Sample(); }
                catch { }
                try { TickOpeningCapture(); }
                catch (Exception ex) { Plugin.Log.LogWarning("Opening capture " + ex.Message); }
                RecordPerf.Add("bay", RecordPerf.Stamp() - bay);
            }
            RecordPerf.Frame(unitObjects.Count, Retired);
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
            Retired = 0;
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

    }
}