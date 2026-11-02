using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// ArrestorGear.Hook and Unhook become ACMI events.
    /// Event=Trap|aircraft|carrier|wire and Event=Untrap|...
    /// </summary>
    internal static class RecordTrap
    {
        static bool _patched;
        static FieldInfo? _attached;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Install()
        {
            if (_patched) return;
            _patched = true;
            var harmony = new Harmony("noreplay.trap");
            var hook = typeof(ArrestorGear).GetMethod("Hook", new[] { typeof(TailHook) });
            var unhook = typeof(ArrestorGear).GetMethod("Unhook", System.Type.EmptyTypes);
            if (hook != null)
                harmony.Patch(hook, postfix: new HarmonyMethod(typeof(RecordTrap), nameof(AfterHook)));
            if (unhook != null)
            {
                harmony.Patch(unhook,
                    prefix: new HarmonyMethod(typeof(RecordTrap), nameof(BeforeUnhook)),
                    postfix: new HarmonyMethod(typeof(RecordTrap), nameof(AfterUnhook)));
            }
            _attached = typeof(ArrestorGear).GetField("attachedHook", Flags);
            Plugin.Log.LogInfo("Trap-Record Hook=" + (hook != null) + " Unhook=" + (unhook != null));
        }

        static void AfterHook(ArrestorGear __instance, bool __result, object[] __args)
        {
            if (!__result || __instance == null || __args == null || __args.Length == 0) return;
            if (__args[0] is not TailHook hook || hook == null) return;
            Write(__instance, hook, false);
        }

        static void BeforeUnhook(ArrestorGear __instance, out string __state)
        {
            __state = "";
            try
            {
                if (!Want() || __instance == null) return;
                if (_attached?.GetValue(__instance) is not TailHook hook || hook == null) return;
                if (!Ids(hook, __instance, out long aircraft, out long carrier, out int wire)) return;
                __state = aircraft.ToString("X", CultureInfo.InvariantCulture)
                    + "|" + carrier.ToString("X", CultureInfo.InvariantCulture)
                    + "|" + wire.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Trap unhook: " + ex.Message);
            }
        }

        static void AfterUnhook(string __state)
        {
            if (string.IsNullOrEmpty(__state)) return;
            var engine = Record.Host != null ? Record.Host.GetComponent<RecordEngine>() : null;
            engine?.WriteGlobalEvent("Event=Untrap|" + __state);
            Record.Log?.LogInfo("Trap release " + __state);
        }

        static void Write(ArrestorGear gear, TailHook hook, bool release)
        {
            try
            {
                if (!Want()) return;
                if (!Ids(hook, gear, out long aircraft, out long carrier, out int wire)) return;
                var engine = Record.Host != null ? Record.Host.GetComponent<RecordEngine>() : null;
                if (engine == null) return;
                string body = "Event=" + (release ? "Untrap" : "Trap")
                    + "|" + aircraft.ToString("X", CultureInfo.InvariantCulture)
                    + "|" + carrier.ToString("X", CultureInfo.InvariantCulture)
                    + "|" + wire.ToString(CultureInfo.InvariantCulture);
                engine.WriteGlobalEvent(body);
                Record.Log?.LogInfo((release ? "Trap release " : "Trap catch ") + wire.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Trap write: " + ex.Message);
            }
        }

        static bool Want()
        {
            return Record.Active && Plugin.CurrentDoc == null;
        }

        static bool Ids(TailHook hook, ArrestorGear gear, out long aircraft, out long carrier, out int wire)
        {
            aircraft = -1;
            carrier = -1;
            wire = gear != null ? gear.wireNumber : 0;
            if (hook == null || gear == null) return false;
            var unit = hook.unitPart != null ? hook.unitPart.parentUnit : null;
            if (unit == null) return false;
            if (unit.GetComponentInParent<ReplayGhost>() != null) return false;
            var ship = gear.GetComponentInParent<Ship>();
            if (ship == null) return false;
            if (ship.GetComponentInParent<ReplayGhost>() != null) return false;
            aircraft = Tacview(unit);
            carrier = Tacview(ship);
            return aircraft >= 0 && carrier >= 0;
        }

        static long Tacview(Unit unit)
        {
            var engine = Record.Host != null ? Record.Host.GetComponent<RecordEngine>() : null;
            if (engine == null || unit == null) return -1;
            long id = unit.persistentID.Id;
            if (engine.tacviewIds.TryGetValue(id, out var known))
                return known;
            if (engine.unitObjects.TryGetValue(id, out var go) && go != null)
            {
                var rec = go.GetComponent<RecordUnit>();
                if (rec != null)
                {
                    engine.tacviewIds[id] = rec.tacviewId;
                    return rec.tacviewId;
                }
            }
            return id + 1;
        }
    }
}
