using BepInEx.Configuration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using System.Globalization;

namespace NOReplay
{
    public static class RecordConfig
    {
        internal const string GeneralSettings = "General Settings";
        internal const string OptionalDataSettings = "Optional Data Settings";
        internal const string VisualSettings = "Visual Settings";
        internal const string DeveloperFeatures = "Developer Features";
        internal const string ReplayHotkeys = "Replay Hotkeys";

        internal const int DefaultUpdateRate = 5;

        internal const float DefaultUnitDiscoveryDelta = 1f;
        internal const float DefaultBulletSimDiscoveryDelta = 0.2f;
        internal const float DefaultAircraftUpdateDelta = 0.2f;
        internal const float DefaultVehicleUpdateDelta = 1f;
        internal const float DefaultMunitionUpdateDelta = 0.2f;
        internal const float DefaultShockwaveUpdateDelta = 0.016f;
        internal const float DefaultShockwaveDiscoveryDelta = 0.5f;
        internal const float DefaultTracerUpdateDelta = 0.2f;
        internal const float DefaultFlareUpdateDelta = 1f;
        internal const float DefaultBuildingUpdateDelta = 1f;

        internal const int DefaultAutoSaveInterval = 60;

        internal const bool DefaultUseMissionTime = true;
        internal const bool DefaultRecordSteamID = true;

        internal const bool DefaultRecordSpeed = true;
        internal const bool DefaultRecordAOA = true;
        internal const bool DefaultRecordAGL = true;
        internal const bool DefaultRecordRadarMode = true;
        internal const bool DefaultRecordLandingGear = true;
        internal const bool DefaultRecordPilotHead = true;
        internal const bool DefaultCompressIDs = false;
        internal const bool DefaultRecordExtraTelemetry = true;

        internal const bool DefaultEnableLogging = false;

        internal const float DefaultTextColorR = 1.0f;
        internal const float DefaultTextColorG = 1.0f;
        internal const float DefaultTextColorB = 1.0f;
        internal const float DefaultTextColorA = 1.0f;

        internal const bool DefaultEnableAutoSaveCountDown = false;
        internal const float DefaultAutoSaveCountDownX = 0.1f;
        internal const float DefaultAutoSaveCountDownY = 0.1f;

        internal const bool DefaultEnableRecordingIndicator = true;
        internal const float DefaultRecordingIndicatorX = 0.2f;
        internal const float DefaultRecordingIndicatorY = 0.2f;
        internal static ConfigEntry<bool> EnableRecordingIndicator;
        internal static ConfigEntry<float> RecordingIndicatorX;
        internal static ConfigEntry<float> RecordingIndicatorY;




        internal const bool DefaultRecordEjectedPilots = false;
        internal const bool DefaultAutoStartRecording = true;

        internal const bool DefaultEnableUnitLogging = false;

        internal const KeyCode DefaultStartStopRecordingKey = KeyCode.F8;
        internal const KeyCode DefaultToggleTimelineKey = KeyCode.F10;
        internal const KeyCode DefaultLookAtLockKey = KeyCode.F;
        internal const KeyCode DefaultToggleTrailsKey = KeyCode.T;
        internal const KeyCode DefaultPlayPauseKey = KeyCode.Space;
        internal const KeyCode DefaultSpeed1Key = KeyCode.Alpha1;
        internal const KeyCode DefaultSpeed2Key = KeyCode.Alpha2;
        internal const KeyCode DefaultSpeed4Key = KeyCode.Alpha4;
        internal const KeyCode DefaultSeekBackKey = KeyCode.LeftArrow;
        internal const KeyCode DefaultSeekForwardKey = KeyCode.RightArrow;
        internal const KeyCode DefaultCasePatternKey = KeyCode.P;

#pragma warning disable CS8618
        private static ConfigEntry<int> _UpdateRate;

        internal static ConfigEntry<float> unitDiscoveryDelta;
        internal static ConfigEntry<float> bulletSimDiscoveryDelta;
        internal static ConfigEntry<float> aircraftUpdateDelta;
        internal static ConfigEntry<float> vehicleUpdateDelta;
        internal static ConfigEntry<float> munitionUpdateDelta;
        internal static ConfigEntry<float> shockwaveUpdateDelta;
        internal static ConfigEntry<float> shockwaveDiscoveryDelta;
        internal static ConfigEntry<float> tracerUpdateDelta;
        internal static ConfigEntry<float> flareUpdateDelta;
        internal static ConfigEntry<float> buildingUpdateDelta;

        private static ConfigEntry<string> _OutputPath;
        private static ConfigEntry<int> _AutoSaveInterval;
        private static ConfigEntry<bool> _CompressIDs;
        internal static ConfigEntry<bool> UseMissionTime;
        internal static ConfigEntry<bool> RecordSteamID;
        internal static ConfigEntry<bool> RecordSpeed;
        internal static ConfigEntry<bool> RecordAOA;
        internal static ConfigEntry<bool> RecordAGL;
        internal static ConfigEntry<bool> RecordRadarMode;
        internal static ConfigEntry<bool> RecordLandingGear;
        internal static ConfigEntry<bool> RecordPilotHead;
        internal static ConfigEntry<bool> RecordExtraTelemetry;

        internal static ConfigEntry<float> TextColorR;
        internal static ConfigEntry<float> TextColorG;
        internal static ConfigEntry<float> TextColorB;
        internal static ConfigEntry<float> TextColorA;

        internal static ConfigEntry<bool> EnableAutoSaveCountDown;
        internal static ConfigEntry<float> AutoSaveCountDownX;
        internal static ConfigEntry<float> AutoSaveCountDownY;

        internal static ConfigEntry<bool> EnableUnitLogging;
        internal static ConfigEntry<bool> RecordEjectedPilots;
        internal static ConfigEntry<bool> AutoStartRecording;

        internal static ConfigEntry<bool> EnableLogging;

        internal static ConfigEntry<KeyboardShortcut> StartStopRecordingKey;
        internal static ConfigEntry<KeyboardShortcut> ToggleTimelineKey;
        internal static ConfigEntry<KeyboardShortcut> LookAtLockKey;
        internal static ConfigEntry<KeyboardShortcut> ToggleTrailsKey;
        internal static ConfigEntry<KeyboardShortcut> PlayPauseKey;
        internal static ConfigEntry<KeyboardShortcut> Speed1Key;
        internal static ConfigEntry<KeyboardShortcut> Speed2Key;
        internal static ConfigEntry<KeyboardShortcut> Speed4Key;
        internal static ConfigEntry<KeyboardShortcut> SeekBackKey;
        internal static ConfigEntry<KeyboardShortcut> SeekForwardKey;
        internal static ConfigEntry<KeyboardShortcut> CasePatternKey;

        internal static readonly bool DefaultDestructionEvents = true;
        internal static ConfigEntry<bool> DestructionEvents;

#pragma warning restore

        internal static int UpdateRate
        {
            get
            {
                return _UpdateRate.Value;
            }
        }

        internal static string OutputPath
        {
            get
            {
                return _OutputPath.Value;
            }
        }

        internal static int AutoSaveInterval
        {
            get
            {
                return _AutoSaveInterval.Value;
            }
        }

        internal static bool CompressIDs
        {
            get
            {
                return _CompressIDs.Value;
            }
        }

        internal static void InitSettings(ConfigFile config)
        {
            Record.Log?.LogDebug("Loading Settings.");

            _UpdateRate = config.Bind(GeneralSettings, "UpdateRate", DefaultUpdateRate, "DEPRECATED. SEE THE OTHER UPDATE RATE SETTINGS");
            Record.Log?.LogDebug($"UpdateRate = {_UpdateRate.Value}");
            if (!Enumerable.Range(0, 1001).Contains(_UpdateRate.Value))
            {
                Record.Log?.LogWarning($"UpdateRate out of range! Setting default value {DefaultUpdateRate}!");
                _UpdateRate.Value = DefaultUpdateRate;
            }
            //NEW UPDATE RATE SETTINGS
            unitDiscoveryDelta = config.Bind(GeneralSettings, "Unit Discovery Rate", DefaultUnitDiscoveryDelta, $"Time interval in seconds to discover Units. Default = {DefaultUnitDiscoveryDelta.ToString(CultureInfo.InvariantCulture)}");
            if (unitDiscoveryDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid unitDiscoveryDelta! Setting default value {DefaultUnitDiscoveryDelta}!");
                Record.Log?.LogDebug($"unitDiscoveryDelta = {unitDiscoveryDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            bulletSimDiscoveryDelta = config.Bind(GeneralSettings, "BulletSim Discovery Rate", DefaultBulletSimDiscoveryDelta, $"Time interval in seconds to discover objects that fire bullets. Default = {DefaultBulletSimDiscoveryDelta.ToString(CultureInfo.InvariantCulture)}");
            if (bulletSimDiscoveryDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid bulletSimDiscoveryDelta! Setting default value {DefaultBulletSimDiscoveryDelta}!");
                Record.Log?.LogDebug($"bulletSimDiscoveryDelta = {bulletSimDiscoveryDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            shockwaveDiscoveryDelta = config.Bind(GeneralSettings, "Shockwave Discovery Rate", DefaultShockwaveDiscoveryDelta, $"Time interval in seconds to discover explosion shockwaves. Default = {DefaultShockwaveDiscoveryDelta.ToString(CultureInfo.InvariantCulture)}");
            if (shockwaveDiscoveryDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid shockwaveDiscoveryDelta! Setting default value {DefaultShockwaveDiscoveryDelta}!");
                Record.Log?.LogDebug($"shockwaveDiscoveryDelta = {shockwaveDiscoveryDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            aircraftUpdateDelta = config.Bind(GeneralSettings, "Aircraft Update Rate", DefaultAircraftUpdateDelta, $"Time interval in seconds to update Aircraft. Default = {DefaultAircraftUpdateDelta.ToString(CultureInfo.InvariantCulture)}");
            if (aircraftUpdateDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid aircraftUpdateDelta! Setting default value {DefaultAircraftUpdateDelta}!");
                Record.Log?.LogDebug($"aircraftUpdateDelta = {aircraftUpdateDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            vehicleUpdateDelta = config.Bind(GeneralSettings, "Vehicle Update Rate", DefaultVehicleUpdateDelta, $"Time interval in seconds to update Vehicles and Ships. Default = {DefaultVehicleUpdateDelta.ToString(CultureInfo.InvariantCulture)}");
            if (vehicleUpdateDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid vehicleUpdateDelta! Setting default value {DefaultVehicleUpdateDelta}!");
                Record.Log?.LogDebug($"vehicletUpdateDelta = {vehicleUpdateDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            munitionUpdateDelta = config.Bind(GeneralSettings, "Munition Update Rate", DefaultMunitionUpdateDelta, $"Time interval in seconds to update Bombs, Missiles and Rockets. Default = {DefaultMunitionUpdateDelta.ToString(CultureInfo.InvariantCulture)}");
            if (munitionUpdateDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid munitionUpdateDelta! Setting default value {DefaultMunitionUpdateDelta}!");
                Record.Log?.LogDebug($"munitionUpdateDelta = {munitionUpdateDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            shockwaveUpdateDelta = config.Bind(GeneralSettings, "Shockwave Update Rate", DefaultShockwaveUpdateDelta, $"Time interval in seconds to update Shockwave Propagation. Default = {DefaultShockwaveUpdateDelta.ToString(CultureInfo.InvariantCulture)}");
            if (shockwaveUpdateDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid shockwaveUpdateDelta! Setting default value {DefaultShockwaveUpdateDelta}!");
                Record.Log?.LogDebug($"aircraftUpdateDelta = {shockwaveUpdateDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            tracerUpdateDelta = config.Bind(GeneralSettings, "Tracer Update Rate", DefaultTracerUpdateDelta, $"Time interval in seconds to Projectile Tracers. Default = {DefaultTracerUpdateDelta.ToString(CultureInfo.InvariantCulture)}");
            if (tracerUpdateDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid tracerUpdateDelta! Setting default value {DefaultTracerUpdateDelta}!");
                Record.Log?.LogDebug($"tracerUpdateDelta = {tracerUpdateDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            flareUpdateDelta = config.Bind(GeneralSettings, "Flare Update Rate", DefaultFlareUpdateDelta, $"Time interval in seconds to update Flares. Default = {DefaultFlareUpdateDelta.ToString(CultureInfo.InvariantCulture)}");
            if (flareUpdateDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid flareUpdateDelta! Setting default value {DefaultFlareUpdateDelta}!");
                Record.Log?.LogDebug($"flareUpdateDelta = {flareUpdateDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            buildingUpdateDelta = config.Bind(GeneralSettings, "Building Update Rate", DefaultBuildingUpdateDelta, $"Time interval in seconds to update Buildings. Default = {DefaultBuildingUpdateDelta.ToString(CultureInfo.InvariantCulture)}");
            if (buildingUpdateDelta.Value <= 0f)
            {
                Record.Log?.LogWarning($"Invalid buildingUpdateDelta! Setting default value {DefaultBuildingUpdateDelta}!");
                Record.Log?.LogDebug($"buildingUpdateDelta = {buildingUpdateDelta.Value.ToString(CultureInfo.InvariantCulture)}");
            }


            string DefaultOutputPath = Application.persistentDataPath + "/Replays/";
            _OutputPath = config.Bind(GeneralSettings, "OutputPath", DefaultOutputPath, "The location where Tacview files will be saved. Must be a valid folder path.");
            Record.Log?.LogDebug($"OutputPath = {_OutputPath.Value}");

            (bool isFolder, bool success) = RecordGeo.IsFileOrFolder(_OutputPath.Value);
            if (!isFolder || !success)
            {
                Record.Log?.LogWarning($"Invalid OutputPath! Setting default value {DefaultOutputPath}!");
                _OutputPath.Value = DefaultOutputPath;
            }

            _AutoSaveInterval = config.Bind(GeneralSettings, "AutoSaveInterval", DefaultAutoSaveInterval, "Time interval for automatically updating the Tacview file. Min value: 60");
            Record.Log?.LogDebug($"AutoSaveInterval = {_AutoSaveInterval.Value}");

            if (_AutoSaveInterval.Value < 60)
            {
                Record.Log?.LogWarning($"Invalid AutoSaveInterval! Setting default value {DefaultAutoSaveInterval}!");
                _AutoSaveInterval.Value = DefaultAutoSaveInterval;
            }

            UseMissionTime = config.Bind(OptionalDataSettings, "UseMissionTime", DefaultUseMissionTime, "Use Mission (true) or Server Time (false) for the clock in the recording.");
            Record.Log?.LogDebug($"UseMissionTime = {UseMissionTime.Value}");

            RecordSteamID = config.Bind(OptionalDataSettings, "RecordSteamID", DefaultRecordSteamID, "Record Steam ID in the Registration label of player aircraft objects.");
            Record.Log?.LogDebug($"RecordSteamID = {RecordSteamID.Value}");

            RecordSpeed = config.Bind(OptionalDataSettings, "RecordSpeed", DefaultRecordSpeed, "Toggle recording True Airspeed and Mach number. Default: true");
            Record.Log?.LogDebug($"RecordSpeed = {RecordSpeed?.Value}");

            RecordAOA = config.Bind(OptionalDataSettings, "RecordAOA", DefaultRecordAOA, "Toggle recording Angle of Attack. Default: true");
            Record.Log?.LogDebug($"RecordAOA = {RecordAOA?.Value}");

            RecordAGL = config.Bind(OptionalDataSettings, "RecordAGL", DefaultRecordAGL, "Toggle recording height above ground level. Default: true");
            Record.Log?.LogDebug($"RecordAGL = {RecordAGL?.Value}");

            RecordRadarMode = config.Bind(OptionalDataSettings, "RecordRadarMode", DefaultRecordRadarMode, "Toggle recording radar mode changes. Default: true");
            Record.Log?.LogDebug($"RecordRadarMode = {RecordRadarMode?.Value}");

            RecordLandingGear = config.Bind(OptionalDataSettings, "RecordLandingGear", DefaultRecordLandingGear, "Toggle recording landing gear changes. Default: true");
            Record.Log?.LogDebug($"RecordLandingGear = {RecordLandingGear?.Value}");

            RecordPilotHead = config.Bind(OptionalDataSettings, "RecordPilotHead", DefaultRecordPilotHead, "Toggle recording pilot head movement. Default: true");
            Record.Log?.LogDebug($"RecordPilotHead = {RecordPilotHead?.Value}");

            RecordExtraTelemetry = config.Bind(OptionalDataSettings, "RecordExtraTelemetry", DefaultRecordExtraTelemetry, "Toggle recording Extra Telemetry. Default: true");
            Record.Log?.LogDebug($"RecordExtraTelemetry = {RecordExtraTelemetry?.Value}");

            _CompressIDs = config.Bind(OptionalDataSettings, "CompressIDs", DefaultCompressIDs, "Compress IDs to reduce filesize with less determinism.");
            Record.Log?.LogDebug($"CompressIDs = {_CompressIDs.Value}");

            RecordEjectedPilots = config.Bind(GeneralSettings, "RecordEjectedPilots", DefaultRecordEjectedPilots, "Logged at recording start. Ejected pilots are recorded either way, because playback needs the track.");
            Record.Log?.LogDebug($"RecordEjectedPilots = {RecordEjectedPilots.Value}");

            DestructionEvents = config.Bind(GeneralSettings, "DestructionEvents", DefaultDestructionEvents, "Toggle Recording Destruction Events for Buildings and Ships.");
            Record.Log?.LogDebug($"DestructionEvents = {DestructionEvents.Value}");

            AutoStartRecording = config.Bind(GeneralSettings, "AutoStartRecording", DefaultAutoStartRecording, "Toggle Automatically starting to record on mission load.");
            Record.Log?.LogDebug($"AutoStartRecording = {AutoStartRecording.Value}");

            EnableAutoSaveCountDown = config.Bind(VisualSettings, "EnableAutoSaveCountDown", DefaultEnableAutoSaveCountDown, "Toggle AutoSave Countdown Timer.");
            Record.Log?.LogDebug($"EnableAutoSaveCountDown = {EnableAutoSaveCountDown.Value}");

            EnableRecordingIndicator = config.Bind(VisualSettings, "EnableRecordingIndicator", DefaultEnableRecordingIndicator, "Toggle Recording Indicator.");
            Record.Log?.LogDebug($"EnableRecordingIndicator = {EnableRecordingIndicator.Value}");

            AutoSaveCountDownX = config.Bind(VisualSettings, "AutoSaveCountDownX", DefaultAutoSaveCountDownX, "X coordinate of Auto Save Countdown Timer on GUI. Scales with Resolution. Value range: 0.0 - 1.0");
            if (AutoSaveCountDownX.Value < 0f || AutoSaveCountDownX.Value > 1.0f)
            {
                Record.Log?.LogWarning($"AutoSaveCountDownX must be within 0.0 - 1.0 range! Setting default value {DefaultAutoSaveCountDownX}!");
            }
            Record.Log?.LogDebug($"AutoSaveCountDownX = {AutoSaveCountDownX.Value}");

            RecordingIndicatorY = config.Bind(VisualSettings, "RecordingIndicatorY", DefaultRecordingIndicatorY, "Y coordinate of Auto Save Countdown Timer on GUI. Scales with Resolution. Value range: 0.0 - 1.0");
            if (RecordingIndicatorY.Value < 0f || RecordingIndicatorY.Value > 1.0f)
            {
                Record.Log?.LogWarning($"RecordingIndicatorX must be within 0.0 - 1.0 range! Setting default value {DefaultRecordingIndicatorX}!");
            }
            Record.Log?.LogDebug($"RecordingIndicatorX = {RecordingIndicatorY.Value}");

            RecordingIndicatorX = config.Bind(VisualSettings, "RecordingIndicatorX", DefaultRecordingIndicatorX, "X coordinate of Auto Save Countdown Timer on GUI. Scales with Resolution. Value range: 0.0 - 1.0");
            if (RecordingIndicatorX.Value < 0f || RecordingIndicatorX.Value > 1.0f)
            {
                Record.Log?.LogWarning($"RecordingIndicatorX must be within 0.0 - 1.0 range! Setting default value {DefaultRecordingIndicatorX}!");
            }
            Record.Log?.LogDebug($"RecordingIndicatorX = {RecordingIndicatorX.Value}");

            AutoSaveCountDownY = config.Bind(VisualSettings, "AutoSaveCountDownY", DefaultAutoSaveCountDownY, "Y coordinate of Auto Save Countdown Timer on GUI. Scales with Resolution. Value range: 0.0 - 1.0");
            if (AutoSaveCountDownY.Value < 0f || AutoSaveCountDownY.Value > 1.0f)
            {
                Record.Log?.LogWarning($"AutoSaveCountDownX must be within 0.0 - 1.0 range! Setting default value {DefaultAutoSaveCountDownX}!");
            }
            Record.Log?.LogDebug($"AutoSaveCountDownX = {AutoSaveCountDownY.Value}");

            TextColorR = config.Bind(VisualSettings, "TextColorR", DefaultTextColorR, "Red color value for GUI Text. Value range: 0.0 - 1.0");
            if (TextColorR.Value < 0f || TextColorR.Value > 1.0f)
            {
                Record.Log?.LogWarning($"TextColorR must be within 0.0 - 1.0 range! Setting default value {DefaultTextColorR}!");
            }
            Record.Log?.LogDebug($"TextColorR = {TextColorR.Value}");

            TextColorG = config.Bind(VisualSettings, "TextColorG", DefaultTextColorG, "Green color value for GUI Text. Value range: 0.0 - 1.0");
            if (TextColorG.Value < 0f || TextColorG.Value > 1.0f)
            {
                Record.Log?.LogWarning($"TextColorG must be within 0.0 - 1.0 range! Setting default value {DefaultTextColorG}!");
            }
            Record.Log?.LogDebug($"TextColorG = {TextColorG.Value}");

            TextColorB = config.Bind(VisualSettings, "TextColorB", DefaultTextColorB, "Blue color value for GUI Text. Value range: 0.0 - 1.0");
            if (TextColorB.Value < 0f || TextColorB.Value > 1.0f)
            {
                Record.Log?.LogWarning($"TextColorB must be within 0.0 - 1.0 range! Setting default value {DefaultTextColorB}!");
            }
            Record.Log?.LogDebug($"TextColorB = {TextColorB.Value}");

            TextColorA = config.Bind(VisualSettings, "TextColorA", DefaultTextColorA, "Transparency value for GUI Text. Value range: 0.0 - 1.0");
            if (TextColorA.Value < 0f || TextColorA.Value > 1.0f)
            {
                Record.Log?.LogWarning($"TextColorA must be within 0.0 - 1.0 range! Setting default value {DefaultTextColorA}!");
            }
            Record.Log?.LogDebug($"TextColorA = {TextColorA.Value}");

            EnableUnitLogging = config.Bind(DeveloperFeatures, "EnableUnknownUnitLogging", DefaultEnableUnitLogging, "Toggle logging Unknown Units that are unknown to ACMI Recorder. Default: false");
            Record.Log?.LogDebug($"EnableUnknownUnitLogging = {EnableUnitLogging.Value}");

            StartStopRecordingKey = BindHotkey(
                config,
                "Hotkeys",
                "StartStopRecordingKey",
                DefaultStartStopRecordingKey,
                "Start or stop recording in a live match. Default: F8. During playback the key is ignored.");

            ToggleTimelineKey = BindHotkey(config, ReplayHotkeys, "Toggle Timeline", DefaultToggleTimelineKey,
                "Hide or show the replay timeline. Default: F10.");
            LookAtLockKey = BindHotkey(config, ReplayHotkeys, "Look-at Lock", DefaultLookAtLockKey,
                "Toggle look-at lock on the selected unit. Default: F. Ignored while a text field is focused. The game binding for this key is muted during replay.");
            ToggleTrailsKey = BindHotkey(config, ReplayHotkeys, "Toggle Trails", DefaultToggleTrailsKey,
                "Toggle flight-path trails. Default: T. Ignored while a text field is focused. The game binding for this key is muted during replay.");
            PlayPauseKey = BindHotkey(config, ReplayHotkeys, "Play/Pause", DefaultPlayPauseKey,
                "Play or pause the replay. Default: Space.");
            Speed1Key = BindHotkey(config, ReplayHotkeys, "Speed 1x", DefaultSpeed1Key,
                "Set playback speed to 1x. Default: 1.");
            Speed2Key = BindHotkey(config, ReplayHotkeys, "Speed 2x", DefaultSpeed2Key,
                "Set playback speed to 2x. Default: 2.");
            Speed4Key = BindHotkey(config, ReplayHotkeys, "Speed 4x", DefaultSpeed4Key,
                "Set playback speed to 4x. Default: 4.");
            SeekBackKey = BindHotkey(config, ReplayHotkeys, "Seek Backward", DefaultSeekBackKey,
                "Jump the replay back 5 seconds. Default: Left Arrow.");
            SeekForwardKey = BindHotkey(config, ReplayHotkeys, "Seek Forward", DefaultSeekForwardKey,
                "Jump the replay forward 5 seconds. Default: Right Arrow.");
            CasePatternKey = BindHotkey(config, ReplayHotkeys, "Case Pattern", DefaultCasePatternKey,
                "Toggle the green Case I approach pattern on each Hyperion. Default: P. Off when the game starts. Ignored while a text field is focused. The game binding for this key is muted during replay.");

            EnableLogging = config.Bind(DeveloperFeatures, "EnableLogging", DefaultEnableLogging, "Toggle Logging. Default: false");
            Record.Log?.LogDebug($"EnableLogging = {EnableLogging.Value}");

            WarnHotkeyCollisions();
        }

        static readonly HashSet<string> _warnedHotkeys = new HashSet<string>();

        static ConfigEntry<KeyboardShortcut> BindHotkey(ConfigFile config, string section, string key, KeyCode code, string description)
        {
            var entry = config.Bind(section, key, new KeyboardShortcut(code), description);
            entry.SettingChanged += OnHotkeyChanged;
            Record.Log?.LogDebug(key + " = " + entry.Value);
            return entry;
        }

        static void OnHotkeyChanged(object sender, EventArgs e)
        {
            WarnHotkeyCollisions();
            Plugin.RefreshReplayKeyMute();
        }

        internal static void WarnHotkeyCollisions()
        {
            var keys = new[]
            {
                new HotkeySlot("Start/stop recording", StartStopRecordingKey),
                new HotkeySlot("Toggle timeline", ToggleTimelineKey),
                new HotkeySlot("Look-at lock", LookAtLockKey),
                new HotkeySlot("Toggle trails", ToggleTrailsKey),
                new HotkeySlot("Play/pause", PlayPauseKey),
                new HotkeySlot("Speed 1x", Speed1Key),
                new HotkeySlot("Speed 2x", Speed2Key),
                new HotkeySlot("Speed 4x", Speed4Key),
                new HotkeySlot("Seek backward", SeekBackKey),
                new HotkeySlot("Seek forward", SeekForwardKey),
                new HotkeySlot("Case pattern", CasePatternKey),
            };
            for (int i = 0; i < keys.Length; i++)
            {
                if (!TryShortcut(keys[i].Entry, out var a)) continue;
                if (a.MainKey == KeyCode.W || a.MainKey == KeyCode.A
                    || a.MainKey == KeyCode.S || a.MainKey == KeyCode.D)
                {
                    string flyId = keys[i].Name + "|WASD|" + a.MainKey;
                    if (_warnedHotkeys.Add(flyId))
                        Plugin.Log.LogWarning("Hotkey-Konflikt: " + keys[i].Name + " (" + a + ") liegt auf " + a.MainKey + ". WASD bewegt die freie Kamera weiter.");
                }
                for (int j = i + 1; j < keys.Length; j++)
                {
                    if (!TryShortcut(keys[j].Entry, out var b)) continue;
                    if (a.MainKey != b.MainKey) continue;
                    string id = keys[i].Name + "|" + keys[j].Name + "|" + a.MainKey;
                    if (!_warnedHotkeys.Add(id)) continue;
                    Plugin.Log.LogWarning("Hotkey-Konflikt: " + keys[i].Name + " (" + a + ") und " + keys[j].Name + " (" + b + ") nutzen " + a.MainKey + ".");
                }
            }
        }

        static bool TryShortcut(ConfigEntry<KeyboardShortcut>? entry, out KeyboardShortcut key)
        {
            key = default;
            if (entry == null) return false;
            try
            {
                key = entry.Value;
                return key.MainKey != KeyCode.None;
            }
            catch
            {
                return false;
            }
        }

        struct HotkeySlot
        {
            internal readonly string Name;
            internal readonly ConfigEntry<KeyboardShortcut>? Entry;
            internal HotkeySlot(string name, ConfigEntry<KeyboardShortcut>? entry)
            {
                Name = name;
                Entry = entry;
            }
        }
    }
}