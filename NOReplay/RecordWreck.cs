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
            // ShipPart.ApplyDamage does not call the base method, so the
            // UnitPart patch never sees a ship compartment.
            var shipDamage = typeof(ShipPart).GetMethod("ApplyDamage", flags);
            if (shipDamage != null && shipDamage.DeclaringType == typeof(ShipPart))
            {
                harmony.Patch(shipDamage,
                    prefix: new HarmonyMethod(typeof(RecordWreck), nameof(BeforeDamage)),
                    postfix: new HarmonyMethod(typeof(RecordWreck), nameof(AfterDamage)));
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
            float netFireDamage, float netImpactDamage, out int __state)
        {
            __state = 0;
            if (!Recording() || __instance == null) return;
            float after = __instance.hitPoints - (netPierceDamage + netBlastDamage + netFireDamage + netImpactDamage);
            __state = EffectsOf(__instance, after, includeFragments: true);
        }

        static void AfterDamage(UnitPart __instance, int __state)
        {
            Note(__instance, __state);
        }

        static void AfterFragments(UnitPart __instance)
        {
            if (!Recording() || __instance == null || !Watched(__instance)) return;
            if (_fragments?.GetValue(__instance) is not GameObject[] extra) return;
            int flags = 0;
            for (int i = 0; i < extra.Length; i++)
                flags |= Kind(extra[i]);
            Note(__instance, flags);
        }

        // The game itself splits the prefab in ApplyDamage and SpawnFragments.
        // A root DamageParticles stays on the part: that is the fire.
        // Anything else is parented to the world: that is the explosion.
        // ExplosionAudio and Shockwave are not required. The wreck prefab is neither.
        const int FireBit = 1;
        const int BoomBit = 2;

        static int EffectsOf(UnitPart part, float hpAfter, bool includeFragments)
        {
            if (!Watched(part)) return 0;
            int flags = 0;
            if (_effects?.GetValue(part) is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is DamageEffect effect && hpAfter < effect.threshold)
                        flags |= Kind(effect.prefab);
                }
            }
            bool already = _fragmented?.GetValue(part) is bool done && done;
            float integrity = _integrity?.GetValue(part) is float gate ? gate : float.MinValue;
            if (includeFragments && !already && hpAfter < integrity && _fragments?.GetValue(part) is GameObject[] extra)
            {
                for (int i = 0; i < extra.Length; i++)
                    flags |= Kind(extra[i]);
            }
            return flags;
        }

        static int Kind(GameObject prefab)
        {
            if (prefab == null) return 0;
            // TryGetComponent on the instantiated root is the game's test.
            if (prefab.GetComponent<DamageParticles>() != null) return FireBit;
            return BoomBit;
        }

        static void Note(UnitPart part, int flags)
        {
            if (flags == 0 || part == null) return;
            var unit = part.GetUnit();
            if (unit == null) return;
            try
            {
                if (!_blast.TryGetValue(unit.persistentID.Id, out var rec) || rec == null) return;
                if ((flags & FireBit) != 0) rec.NoteBlast("fire part=" + part.id.ToString());
                if ((flags & BoomBit) != 0) rec.NoteBlast("boom part=" + part.id.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Blast-Record: " + ex.Message);
            }
        }

        static bool Recording()
        {
            return Record.Active && Plugin.CurrentDoc == null;
        }

        static bool Watched(UnitPart part)
        {
            var unit = part.GetUnit();
            if (unit == null) return false;
            return _blast.ContainsKey(unit.persistentID.Id);
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
