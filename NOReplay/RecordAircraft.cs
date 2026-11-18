using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    internal class RecordAircraft : RecordUnit
    {
        private bool lastGear = false;
        bool _gearSent;

        /// <summary>
        /// The first sample carries the gear the aircraft actually spawned with.
        /// Later samples are written only when that bool changes.
        /// </summary>
        void NoteGear()
        {
            if (RecordConfig.RecordLandingGear == null || !RecordConfig.RecordLandingGear.Value) return;
            if (aircraft == null || aircraft.gearState == LandingGear.GearState.Uninitialized) return;
            bool down = aircraft.gearDeployed;
            if (_gearSent && down == lastGear) return;
            props["LandingGear"] = down ? "1" : "0";
            lastGear = down;
            if (!_gearSent)
                Record.Log?.LogInfo("Gear-Rec " + (aircraft.definition != null ? aircraft.definition.unitName : "aircraft")
                    + " down=" + (down ? "1" : "0"));
            _gearSent = true;
        }
        int _liveryLog;
        private bool lastRadar = false;
        private float lastAGL = float.NaN;
        private float lastTAS = float.NaN;
        private float lastAOA = float.NaN;
        private float lastThrottle = float.NaN;
        string _lastTilt = "";
        bool _tiltLogged;
        private float lastPitch = float.NaN;
        private float lastYaw = float.NaN;
        private float lastRoll = float.NaN;
        private float lastThrust = float.NaN;
        private float avgThrust = float.NaN;
        private float lastRpmRatio = float.NaN;
        private float lastJetRpm = float.NaN;
        private float lastTurbineRpm = float.NaN;
        TailHook? _tailHook;
        FieldInfo? _tailAmount;
        int _tailTries;
        float _lastTailhook = float.NaN;

        private Vector3 lastHead = Vector3.zero;
        RotorShaft[] _bendShafts = Array.Empty<RotorShaft>();
        bool _bendBound;
        bool _bendLogged;
        bool _bendAnnounced;
        float _bendAt = -1f;
        float _bendLogAt = -10f;
        int _bendKeys;
        RotorBend.Frame _bendPrev;
        static FieldInfo? _fBendRatio;
        static FieldInfo? _fBendRotors;
        static FieldInfo? _fBendFlap;

        Aircraft aircraft;
        Aircraft localAircraft = null;

        private string? steamId;
        private string? lastEmittedPilot;
        string? _loadoutSent;
        float _loadoutAge;
        float _loadoutWait;
        bool _loadoutDone;

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
            NoteGear();

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

            EmitRotorBend();
            PublishLoadout();

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
            RepeatPoseIfStill();
            UpdateState();
            UpdateTargets();
            Engine.invokeWriterUpdate(this);
            props.Clear();
            timer = 0;
        }

        internal override void UpdateTargets()
        {
            if (aircraft == null) return;
            NoteLock(aircraft);
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

        void BindRotorBend()
        {
            if (_bendBound && _bendShafts.Length > 0 && _fBendRatio != null && _fBendFlap != null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fBendRatio ??= typeof(RotorShaft).GetField("angularSpeedRatio", flags);
            _fBendRotors ??= typeof(RotorShaft).GetField("rotors", flags);
            _fBendFlap ??= typeof(SwashRotor).GetField("flapAngle", flags);
            var found = new List<RotorShaft>();
            try
            {
                var engines = aircraft.engines;
                for (int i = 0; i < engines.Count; i++)
                    if (engines[i] is RotorShaft shaft && shaft != null) found.Add(shaft);
            }
            catch { }
            try
            {
                var kids = aircraft.GetComponentsInChildren<RotorShaft>(true);
                for (int i = 0; i < kids.Length; i++)
                    if (kids[i] != null && !found.Contains(kids[i])) found.Add(kids[i]);
            }
            catch { }
            if (found.Count == 0)
            {
                if (!_bendLogged)
                {
                    _bendLogged = true;
                    string who = aircraft.definition != null ? aircraft.definition.unitName : aircraft.name;
                    Record.Log?.LogInfo("RotorBend rec " + who + " blades=0"
                        + (_fBendRatio == null || _fBendFlap == null ? " fields=missing" : ""));
                }
                return;
            }
            _bendShafts = found.ToArray();
            _bendBound = true;
            int blades = 0;
            for (int s = 0; s < _bendShafts.Length; s++)
            {
                try
                {
                    if (_fBendRotors?.GetValue(_bendShafts[s]) is SwashRotor[] rotors)
                        blades += rotors.Length;
                }
                catch { }
            }
            if (_bendAnnounced) return;
            _bendAnnounced = true;
            string unit = aircraft.definition != null ? aircraft.definition.unitName : aircraft.name;
            Record.Log?.LogInfo("RotorBend rec " + unit + " blades=" + blades
                + (_fBendRatio == null || _fBendFlap == null ? " fields=missing" : ""));
        }

        /// <summary>
        /// Per shaft, the ratio BendSegments receives, then each blade's flap
        /// in radians. Twenty hertz at most, and only after a 0.02 change.
        /// The shaft is the same RotorShaft that already reports RpmRatio.
        /// </summary>
        void EmitRotorBend()
        {
            if (aircraft == null || !Record.Active) return;
            BindRotorBend();
            float now = Time.time;
            if (_bendShafts.Length == 0 || _fBendRatio == null || _fBendRotors == null || _fBendFlap == null)
            {
                NoteBendSample(now, 0f, 0f);
                return;
            }
            if (_bendAt >= 0f && now - _bendAt < RotorBend.MinInterval) return;
            var frame = new RotorBend.Frame
            {
                Ratio = new float[_bendShafts.Length],
                Flap = new float[_bendShafts.Length][]
            };
            int used = 0;
            float showFlap = 0f;
            float showRatio = 0f;
            for (int s = 0; s < _bendShafts.Length; s++)
            {
                var shaft = _bendShafts[s];
                if (shaft == null) continue;
                float ratio = 0f;
                try { if (_fBendRatio.GetValue(shaft) is float r) ratio = r; } catch { }
                SwashRotor[] blades = Array.Empty<SwashRotor>();
                try { if (_fBendRotors.GetValue(shaft) is SwashRotor[] rotors && rotors != null) blades = rotors; } catch { }
                var flaps = new float[blades.Length];
                int n = 0;
                for (int i = 0; i < blades.Length; i++)
                {
                    if (blades[i] == null) continue;
                    float flap = 0f;
                    try { if (_fBendFlap.GetValue(blades[i]) is float f) flap = f; } catch { }
                    flaps[n++] = flap;
                }
                if (n != flaps.Length)
                {
                    var cut = new float[n];
                    for (int i = 0; i < n; i++) cut[i] = flaps[i];
                    flaps = cut;
                }
                frame.Ratio[used] = ratio;
                frame.Flap[used] = flaps;
                if (s == 0)
                {
                    showRatio = ratio;
                    showFlap = flaps.Length > 0 ? RotorBend.SpanXDeg(flaps[0]) : 0f;
                }
                used++;
            }
            if (used == 0)
            {
                NoteBendSample(now, showFlap, showRatio);
                return;
            }
            if (used != frame.Ratio.Length)
            {
                var ratio = new float[used];
                var flap = new float[used][];
                for (int i = 0; i < used; i++)
                {
                    ratio[i] = frame.Ratio[i];
                    flap[i] = frame.Flap[i];
                }
                frame.Ratio = ratio;
                frame.Flap = flap;
            }
            NoteBendSample(now, showFlap, showRatio);
            if (_bendAt >= 0f && !RotorBend.Changed(_bendPrev, frame))
            {
                _bendAt = now;
                return;
            }
            if (Engine == null || RecordEngine.writer == null) return;
            if (_bendKeys == 0 && BendFrameIdle(frame))
            {
                _bendPrev = frame;
                _bendAt = now;
                return;
            }
            var saved = new Dictionary<string, string>(props);
            bool pose = poseSnap;
            poseSnap = false;
            props.Clear();
            props["RotorBend"] = RotorBend.Write(frame);
            Engine.invokeWriterUpdate(this);
            props.Clear();
            foreach (var kv in saved) props[kv.Key] = kv.Value;
            poseSnap = pose;
            _bendPrev = frame;
            _bendAt = now;
            _bendKeys++;
        }

        static bool BendFrameIdle(RotorBend.Frame frame)
        {
            if (frame.Ratio == null) return true;
            for (int s = 0; s < frame.Ratio.Length; s++)
            {
                if (Math.Abs(frame.Ratio[s]) > 0.001f) return false;
                var flaps = frame.Flap != null && s < frame.Flap.Length ? frame.Flap[s] : null;
                if (flaps == null) continue;
                for (int i = 0; i < flaps.Length; i++)
                    if (Math.Abs(flaps[i]) > 0.001f) return false;
            }
            return true;
        }

        void NoteBendSample(float now, float flapDeg, float ratio, bool force = true)
        {
            if (!force && now - _bendLogAt < 5f) return;
            if (now - _bendLogAt < 5f) return;
            _bendLogAt = now;
            string unit = aircraft.definition != null ? aircraft.definition.unitName : aircraft.name;
            Record.Log?.LogInfo("RotorBend rec state " + unit
                + " flap=" + flapDeg.ToString("0.0", CultureInfo.InvariantCulture)
                + " ratio=" + ratio.ToString("0.00", CultureInfo.InvariantCulture)
                + " keys=" + _bendKeys);
        }

        void EmitPowerRpm()
        {
            try
            {
                var seen = new HashSet<IEngine>();
                float rotorSum = 0f, jetSum = 0f, turbSum = 0f;
                int rotorN = 0, jetN = 0, turbN = 0;
                void Take(IEngine? e)
                {
                    if (e == null || !seen.Add(e)) return;
                    string n = e.GetType().Name;
                    float r = e.GetRPMRatio();
                    if (n == "TurbineEngine") { turbSum += r; turbN++; }
                    else if (n == "Turbojet" || n == "Turbofan") { jetSum += r; jetN++; }
                    else { rotorSum += r; rotorN++; }
                }
                for (int i = 0; i < aircraft.engines.Count; i++)
                    Take(aircraft.engines[i]);
                for (int i = 0; i < aircraft.engineStates.Count; i++)
                    Take(aircraft.engineStates[i]);
                void Emit(string key, ref float last, int n, float sum)
                {
                    if (n <= 0) return;
                    float v = sum / n;
                    if (!float.IsNaN(last) && Mathf.Abs(last - v) < 0.02f) return;
                    props.Add(key, v.ToString("0.00", CultureInfo.InvariantCulture));
                    last = v;
                }
                Emit("RpmRatio", ref lastRpmRatio, rotorN, rotorSum);
                Emit("JetRpm", ref lastJetRpm, jetN, jetSum);
                Emit("TurbineRpm", ref lastTurbineRpm, turbN, turbSum);
            }
            catch { }
        }

        void RecordNacelleTilt()
        {
            TiltWingController? wing = null;
            try { wing = aircraft.GetComponentInChildren<TiltWingController>(true); }
            catch { return; }
            if (wing == null) return;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            if (typeof(TiltWingController).GetField("rotatingJoints", flags)?.GetValue(wing) is not System.Array joints || joints.Length == 0)
            {
                if (!_tiltLogged)
                {
                    _tiltLogged = true;
                    Record.Log?.LogInfo("TiltWing has no rotators on " + aircraft.definition?.unitName);
                }
                return;
            }
            var text = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 0; i < joints.Length && n < 4; i++)
            {
                var item = joints.GetValue(i);
                if (item == null) continue;
                var part = item.GetType().GetField("part", flags)?.GetValue(item) as UnitPart;
                if (part == null) continue;
                float x = part.transform.localEulerAngles.x;
                if (x > 180f) x -= 360f;
                if (n > 0) text.Append('|');
                text.Append(x.ToString("0.##", CultureInfo.InvariantCulture));
                n++;
            }
            if (n == 0) return;
            string now = text.ToString();
            if (now == _lastTilt) return;
            _lastTilt = now;
            props["Tilt"] = now;
            if (!_tiltLogged)
            {
                _tiltLogged = true;
                Record.Log?.LogInfo("Tilt " + aircraft.definition?.unitName + " " + now);
            }
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

            NoteGear();

            RecordNacelleTilt();

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

            // Surfaces read these. They are not part of the extra-telemetry switch,
            // so turning that off does not freeze the sticks and the throttle.
            var inputs = aircraft.GetInputs();
            if (lastThrottle != inputs.throttle)
            {
                props.Add("Throttle", inputs.throttle.ToString("0.##", CultureInfo.InvariantCulture));
                lastThrottle = inputs.throttle;
            }
            if (lastRoll != inputs.roll)
            {
                props.Add("RollControlInput", inputs.roll.ToString("0.##", CultureInfo.InvariantCulture));
                lastRoll = inputs.roll;
            }
            if (lastPitch != inputs.pitch)
            {
                props.Add("PitchControlInput", inputs.pitch.ToString("0.##", CultureInfo.InvariantCulture));
                lastPitch = inputs.pitch;
            }
            if (lastYaw != inputs.yaw)
            {
                props.Add("YawControlInput", inputs.yaw.ToString("0.##", CultureInfo.InvariantCulture));
                lastYaw = inputs.yaw;
            }

            if (RecordConfig.RecordExtraTelemetry.Value == true)
            {
                if (lastThrust != getAvgThrust())
                {
                    avgThrust = getAvgThrust();
                    props.Add("EngineRPM", avgThrust.ToString("0.##", CultureInfo.InvariantCulture));
                    lastThrust = avgThrust;
                }
                EmitPowerRpm();
            }
            EmitTailhook();
        }

        void EmitTailhook()
        {
            if (_tailHook == null)
            {
                if (_tailTries > 40) return;
                _tailTries++;
                if ((_tailTries & 3) != 1) return;
                _tailHook = aircraft.GetComponentInChildren<TailHook>(true);
                if (_tailHook == null) return;
                _tailAmount = typeof(TailHook).GetField("deployedAmount", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (_tailAmount == null || _tailHook == null) return;
            if (_tailAmount.GetValue(_tailHook) is not float amount || float.IsNaN(amount)) return;
            if (!float.IsNaN(_lastTailhook) && Mathf.Abs(_lastTailhook - amount) <= 0.05f) return;
            props.Add("Tailhook", Mathf.Clamp01(amount).ToString("0.00", CultureInfo.InvariantCulture));
            _lastTailhook = amount;
        }

        void ApplyLoadoutAndLivery()
        {
            try
            {
                var line = FormatLoadout();
                if (!string.IsNullOrEmpty(line))
                {
                    props["Loadout"] = line;
                    _loadoutSent = line;
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

        /// <summary>
        /// OnStartServer records before OnStartClient fills a default loadout.
        /// Keep checking until that loadout shows up.
        /// </summary>
        void PublishLoadout()
        {
            if (_loadoutDone || aircraft == null) return;
            _loadoutAge += Time.deltaTime;
            _loadoutWait += Time.deltaTime;
            if (_loadoutWait < 0.25f) return;
            _loadoutWait = 0f;
            if (_loadoutAge > 15f)
                _loadoutDone = true;
            string? line = null;
            try { line = FormatLoadout(); }
            catch { return; }
            if (string.IsNullOrEmpty(line) || line == _loadoutSent) return;
            _loadoutSent = line;
            props["Loadout"] = line;
            if (props.Count != 1 || Engine == null || !Record.Active) return;
            Engine.invokeWriterUpdate(this);
            props.Clear();
        }

        string? FormatLoadout()
        {
            var loadout = aircraft.loadout;
            if (loadout?.weapons == null || loadout.weapons.Count == 0) return null;
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
            if (parts.Count == 0) return null;
            return string.Join("|", parts);
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