using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Globalization;

namespace NOReplay
{
    public static class RecordConfig
    {
        internal const string GeneralSettings = "General Settings";
        internal const string VisualSettings = "Visual Settings";
        internal const string ReplayHotkeys = "Replay Hotkeys";

        internal const float AircraftUpdateDelta = 0.2f;
        internal const float VehicleUpdateDelta = 1f;
        internal const float MunitionUpdateDelta = 0.2f;
        internal const float ShockwaveUpdateDelta = 0.016f;
        internal const float TracerUpdateDelta = 0.2f;
        internal const float FlareUpdateDelta = 1f;

        internal const int AutoSaveInterval = 60;

        /// <summary>
        /// Per-event diagnostics stay compiled in. This constant keeps them out of the log.
        /// </summary>
        internal const bool EnableLogging = false;

        internal const float DefaultTextColorR = 1.0f;
        internal const float DefaultTextColorG = 1.0f;
        internal const float DefaultTextColorB = 1.0f;
        internal const float DefaultTextColorA = 1.0f;

        internal const bool DefaultEnableRecordingIndicator = true;
        internal const bool DefaultFade = true;
        internal const float DefaultRecordingIndicatorX = 0.2f;
        internal const float DefaultRecordingIndicatorY = 0.2f;

        internal const bool DefaultAutoStartRecording = false;

        internal const KeyCode DefaultStartStopRecordingKey = KeyCode.F8;
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
        internal static ConfigEntry<bool> EnableRecordingIndicator;
        internal static ConfigEntry<bool> Fade;
        internal static ConfigEntry<float> RecordingIndicatorX;
        internal static ConfigEntry<float> RecordingIndicatorY;

        internal static ConfigEntry<float> TextColorR;
        internal static ConfigEntry<float> TextColorG;
        internal static ConfigEntry<float> TextColorB;
        internal static ConfigEntry<float> TextColorA;

        internal static ConfigEntry<bool> AutoStartRecording;

        internal static ConfigEntry<KeyboardShortcut> StartStopRecordingKey;
        internal static ConfigEntry<KeyboardShortcut> LookAtLockKey;
        internal static ConfigEntry<KeyboardShortcut> ToggleTrailsKey;
        internal static ConfigEntry<KeyboardShortcut> PlayPauseKey;
        internal static ConfigEntry<KeyboardShortcut> Speed1Key;
        internal static ConfigEntry<KeyboardShortcut> Speed2Key;
        internal static ConfigEntry<KeyboardShortcut> Speed4Key;
        internal static ConfigEntry<KeyboardShortcut> SeekBackKey;
        internal static ConfigEntry<KeyboardShortcut> SeekForwardKey;
        internal static ConfigEntry<KeyboardShortcut> CasePatternKey;
#pragma warning restore

        internal static void InitSettings(ConfigFile config)
        {
            Record.Log?.LogDebug("Loading Settings.");

            AutoStartRecording = config.Bind(GeneralSettings, "AutoStartRecording", DefaultAutoStartRecording, "Start recording when a mission loads.");
            Record.Log?.LogDebug($"AutoStartRecording = {AutoStartRecording.Value}");

            EnableRecordingIndicator = config.Bind(VisualSettings, "EnableRecordingIndicator", DefaultEnableRecordingIndicator, "Show the REC label while recording.");
            Record.Log?.LogDebug($"EnableRecordingIndicator = {EnableRecordingIndicator.Value}");

            Fade = config.Bind(VisualSettings, "Fade", DefaultFade, "When on, REC fades out 3 seconds after recording starts, over 1 second. On stop, a red STOP appears in the same place and fades the same way. When off, REC stays for the whole recording and STOP is not shown.");
            Record.Log?.LogDebug($"Fade = {Fade.Value}");

            RecordingIndicatorX = BindUnit(config, "RecordingIndicatorX", DefaultRecordingIndicatorX, "Horizontal position of the REC label, from the left edge. 0 is the left, 1 is the right.");
            RecordingIndicatorY = BindUnit(config, "RecordingIndicatorY", DefaultRecordingIndicatorY, "Vertical position of the REC label, from the top edge. 0 is the top, 1 is the bottom.");

            TextColorR = BindUnit(config, "TextColorR", DefaultTextColorR, "Red channel of the REC label, from 0 to 1. STOP is always red.");
            TextColorG = BindUnit(config, "TextColorG", DefaultTextColorG, "Green channel of the REC label, from 0 to 1. STOP is always red.");
            TextColorB = BindUnit(config, "TextColorB", DefaultTextColorB, "Blue channel of the REC label, from 0 to 1. STOP is always red.");
            TextColorA = BindUnit(config, "TextColorA", DefaultTextColorA, "Opacity of the REC label, from 0 to 1. The fade multiplies this. STOP uses the same opacity.");

            StartStopRecordingKey = BindHotkey(
                config,
                "Hotkeys",
                "StartStopRecordingKey",
                DefaultStartStopRecordingKey,
                "Start or stop recording in a live match. Default: F8. During playback the key is ignored.");

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

            WarnHotkeyCollisions();
        }

        static ConfigEntry<float> BindUnit(ConfigFile config, string key, float fallback, string description)
        {
            var entry = config.Bind(VisualSettings, key, fallback, description);
            if (entry.Value < 0f || entry.Value > 1f)
            {
                Record.Log?.LogWarning(key + " must be within 0.0 - 1.0. Using " + fallback.ToString("0.0", CultureInfo.InvariantCulture) + ".");
                entry.Value = fallback;
            }
            Record.Log?.LogDebug(key + " = " + entry.Value.ToString("0.00", CultureInfo.InvariantCulture));
            return entry;
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
                        Plugin.Log.LogWarning("Hotkey clash: " + keys[i].Name + " (" + a + ") is bound to " + a.MainKey + ". WASD still moves the free camera.");
                }
                for (int j = i + 1; j < keys.Length; j++)
                {
                    if (!TryShortcut(keys[j].Entry, out var b)) continue;
                    if (a.MainKey != b.MainKey) continue;
                    string id = keys[i].Name + "|" + keys[j].Name + "|" + a.MainKey;
                    if (!_warnedHotkeys.Add(id)) continue;
                    Plugin.Log.LogWarning("Hotkey clash: " + keys[i].Name + " (" + a + ") and " + keys[j].Name + " (" + b + ") both use " + a.MainKey + ".");
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
