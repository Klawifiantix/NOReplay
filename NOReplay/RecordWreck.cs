using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// GroundVehicle.WreckAndRemove waits, then SpawnWreckage hides the mesh.
    /// A munitions part can also explode in ApplyDamage, on the hit that crosses
    /// its threshold or on a later fire tick. The postfixes only record those
    /// moments. They do not change the game.
    /// </summary>
    internal static class RecordWreck
    {
        static bool _patched;
        static readonly Dictionary<long, RecordVehicle> _live = new Dictionary<long, RecordVehicle>();
        static readonly Dictionary<long, RecordUnit> _blast = new Dictionary<long, RecordUnit>();
        static FieldInfo? _effects;
        static FieldInfo? _fragments;
        static FieldInfo? _integrity;
        static FieldInfo? _fragmented;

        public static void Install()
        {
            if (_patched) return;
            _patched = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var spawn = typeof(GroundVehicle).GetMethod("SpawnWreckage", flags);
            var damage = typeof(UnitPart).GetMethod("ApplyDamage", flags);
            var broken = typeof(UnitPart).GetMethod("SpawnFragments", flags);
            _effects = typeof(UnitPart).GetField("damageEffects", flags);
            _fragments = typeof(UnitPart).GetField("disintegrationEffects", flags);
            _integrity = typeof(UnitPart).GetField("integrityThreshold", flags);
            _fragmented = typeof(UnitPart).GetField("fragmented", flags);
            var harmony = new Harmony("noreplay.wreck");
            int n = 0;
            if (spawn != null)
            {
                harmony.Patch(spawn, postfix: new HarmonyMethod(typeof(RecordWreck), nameof(AfterSpawn)));
                n++;
            }
            else Plugin.Log?.LogWarning("Wreck-Record: SpawnWreckage fehlt");
            if (damage != null)
            {
                harmony.Patch(damage,
                    prefix: new HarmonyMethod(typeof(RecordWreck), nameof(BeforeDamage)),
                    postfix: new HarmonyMethod(typeof(RecordWreck), nameof(AfterDamage)));
                n++;
            }
            if (broken != null)
            {
                harmony.Patch(broken, postfix: new HarmonyMethod(typeof(RecordWreck), nameof(AfterFragments)));
                n++;
            }
            Plugin.Log?.LogInfo("Wreck-Record: SpawnWreckage");
            Plugin.Log?.LogInfo("Blast-Record hooks=" + n);
        }

        public static void Watch(RecordUnit rec, long unitId)
        {
            if (rec == null) return;
            _blast[unitId] = rec;
        }

        public static void Register(RecordVehicle rec, long unitId)
        {
            if (rec == null) return;
            _live[unitId] = rec;
        }

        public static void Forget(long unitId)
        {
            _live.Remove(unitId);
            _blast.Remove(unitId);
        }

        static void BeforeDamage(UnitPart __instance, float netPierceDamage, float netBlastDamage,
            float netFireDamage, float netImpactDamage, out bool __state)
        {
            __state = false;
            if (!Recording() || __instance == null) return;
            float after = __instance.hitPoints - (netPierceDamage + netBlastDamage + netFireDamage + netImpactDamage);
            __state = WillBlast(__instance, after);
        }

        static void AfterDamage(UnitPart __instance, bool __state)
        {
            if (__state) Note(__instance);
        }

        static void AfterFragments(UnitPart __instance)
        {
            if (!Recording() || __instance == null || !Cookoff(__instance)) return;
            if (_fragments?.GetValue(__instance) is not GameObject[] extra) return;
            for (int i = 0; i < extra.Length; i++)
                if (IsBlast(extra[i])) { Note(__instance); return; }
        }

        static bool Recording()
        {
            return Record.Active && Plugin.CurrentDoc == null;
        }

        static bool Cookoff(UnitPart part)
        {
            var unit = part.GetUnit();
            if (unit == null) return false;
            string name = unit.definition != null ? unit.definition.unitName : unit.unitName;
            return WreckMark.IsCookoff(name);
        }

        static bool WillBlast(UnitPart part, float hpAfter)
        {
            if (!Cookoff(part)) return false;
            if (_effects?.GetValue(part) is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is DamageEffect effect && hpAfter < effect.threshold && IsBlast(effect.prefab))
                        return true;
                }
            }
            bool already = _fragmented?.GetValue(part) is bool done && done;
            float integrity = _integrity?.GetValue(part) is float gate ? gate : float.MinValue;
            if (!already && hpAfter < integrity && _fragments?.GetValue(part) is GameObject[] extra)
            {
                for (int i = 0; i < extra.Length; i++)
                    if (IsBlast(extra[i])) return true;
            }
            return false;
        }

        static bool IsBlast(GameObject prefab)
        {
            if (prefab == null) return false;
            if (prefab.GetComponentInChildren<ExplosionAudio>(true) != null) return true;
            if (prefab.GetComponentInChildren<Shockwave>(true) != null) return true;
            return false;
        }

        static void Note(UnitPart part)
        {
            var unit = part.GetUnit();
            if (unit == null) return;
            try
            {
                if (_blast.TryGetValue(unit.persistentID.Id, out var rec) && rec != null)
                    rec.NoteBlast("part");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug("Blast-Record: " + ex.Message);
            }
        }

        static void AfterSpawn(GroundVehicle __instance)
        {
            if (!Record.Active || Plugin.CurrentDoc != null || __instance == null) return;
            try
            {
                long id = __instance.persistentID.Id;
                if (_live.TryGetValue(id, out var rec) && rec != null)
                    rec.OnWreckSpawned();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Wreck-Record: " + ex.Message);
            }
        }
    }
}
