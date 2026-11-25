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
        readonly HashSet<long> _capturedIds = new HashSet<long>();
        float _openUntil;
        float _openNext;
        bool _atStart;

        internal Dictionary<long, GameObject> unitObjects = [];
        internal Dictionary<long, long> tacviewIds = [];

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
            Plugin.Log.LogInfo("Start-Capture: " + captured + " units already present");
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

        void Awake()
        {
            Record.Log?.LogDebug("USING MONO RECORDER");
            double hours = 0d;
            try
            {
                var mission = MissionManager.CurrentMission;
                if (mission != null) hours = mission.environment.timeOfDay;
            }
            catch { }
            startDate = DateTime.Today + TimeSpan.FromHours(hours);
            Record.Log?.LogDebug("USING MISSION CLOCK");

            curTime = startDate;
            writer = new RecordWriter(startDate);
            if (GetComponent<RecordGunBurst>() == null)
                gameObject.AddComponent<RecordGunBurst>();
            Record.Log?.LogDebug("STARTED MONO RECORDER");
            Record.Manual = false;

        }

        void Update()
        {
            long t0 = RecordPerf.Stamp();
            curTime += TimeSpan.FromSeconds(Time.deltaTime);
            PilotNameCache.Tick();
            RecordPerf.Add("engine", RecordPerf.Stamp() - t0);
        }
        void LateUpdate()
        {
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

    }
}