using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Records UnitPart / AeroPart breaks and rotor segment loss.
    /// A prefix on Detach ignores parts that are already detached, so debris
    /// breaking up on the ground is not stored again. SpawnFragments after
    /// Detach sees IsDetached and is skipped; an in-place disintegration is Kind=F.
    /// </summary>
    internal static class RecordParts
    {
        static bool _patched;
        static int _logs;
        static readonly HashSet<BreakKey> _seen = new HashSet<BreakKey>();

        readonly struct BreakKey : IEquatable<BreakKey>
        {
            public readonly long Unit;
            public readonly int Part;
            public readonly char Kind;
            public readonly int Seg;
            public readonly int Rotor;

            public BreakKey(long unit, int part, char kind, int seg, int rotor)
            {
                Unit = unit;
                Part = part;
                Kind = kind;
                Seg = seg;
                Rotor = rotor;
            }

            public bool Equals(BreakKey other)
            {
                return Unit == other.Unit && Part == other.Part && Kind == other.Kind
                    && Seg == other.Seg && Rotor == other.Rotor;
            }

            public override bool Equals(object obj) => obj is BreakKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Unit.GetHashCode();
                    hash = (hash * 397) ^ Part;
                    hash = (hash * 397) ^ Kind;
                    hash = (hash * 397) ^ Seg;
                    hash = (hash * 397) ^ Rotor;
                    return hash;
                }
            }
        }
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo? _rotorAircraft;
        static FieldInfo? _rotorLast;
        static FieldInfo? _rotorIndex;

        public static void Install()
        {
            if (_patched) return;
            _patched = true;
            var harmony = new Harmony("noreplay.parts");
            var detach = typeof(UnitPart).GetMethod("Detach", new[] { typeof(Vector3), typeof(Vector3) });
            var fragments = typeof(UnitPart).GetMethod("SpawnFragments", Type.EmptyTypes);
            if (detach != null)
                harmony.Patch(detach, prefix: new HarmonyMethod(typeof(RecordParts), nameof(OnDetach)));
            if (fragments != null)
                harmony.Patch(fragments, prefix: new HarmonyMethod(typeof(RecordParts), nameof(OnFragments)));
            var breaks = typeof(SwashRotor).GetMethod("BreakSegments", Flags);
            if (breaks != null)
            {
                harmony.Patch(breaks,
                    prefix: new HarmonyMethod(typeof(RecordParts), nameof(OnRotorBefore)),
                    postfix: new HarmonyMethod(typeof(RecordParts), nameof(OnRotorAfter)));
            }
            Plugin.Log.LogInfo("Part-Record: Detach=" + (detach != null)
                + " Fragments=" + (fragments != null)
                + " Rotor=" + (breaks != null));
        }

        public static void Reset()
        {
            _seen.Clear();
            _logs = 0;
        }

        static void OnDetach(UnitPart __instance, object[] __args)
        {
            try
            {
                if (!Want(__instance)) return;
                if (__instance.IsDetached()) return;
                Vector3 vel = Vector3.zero;
                if (__args != null && __args.Length > 0 && __args[0] is Vector3 given)
                    vel = given;
                if (vel.sqrMagnitude < 0.25f && __instance.rb != null)
                    vel = __instance.rb.velocity;
                Write(__instance.parentUnit, __instance, "D", __instance.id, -1, -1, "", vel);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Part Detach: " + ex.Message);
            }
        }

        static void OnFragments(UnitPart __instance)
        {
            try
            {
                if (!Want(__instance)) return;
                if (__instance.IsDetached()) return;
                Write(__instance.parentUnit, __instance, "F", __instance.id, -1, -1, "", Vector3.zero);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Part Fragments: " + ex.Message);
            }
        }

        static void OnRotorBefore(SwashRotor __instance, out int __state)
        {
            __state = LastSegment(__instance);
        }

        static void OnRotorAfter(SwashRotor __instance, int __state)
        {
            try
            {
                if (__instance == null || !Record.Active || Plugin.CurrentDoc != null) return;
                if (__instance.GetComponentInParent<ReplayGhost>() != null) return;
                int now = LastSegment(__instance);
                if (now >= __state) return;
                var unit = RotorUnit(__instance);
                if (unit == null) return;
                int rotor = RotorOrdinal(unit, __instance);
                byte part = RotorDamageIndex(__instance);
                string path = TurretKeys.Sanitize(TurretKeys.Relative(unit.transform, __instance.transform));
                Vector3 vel = Vector3.zero;
                if (unit.rb != null) vel = unit.rb.velocity;
                for (int seg = __state; seg > now; seg--)
                    Write(unit, null, "R", part, seg, rotor, path, vel);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Rotor break: " + ex.Message);
            }
        }

        static bool Want(UnitPart part)
        {
            if (part == null || !Record.Active || Plugin.CurrentDoc != null) return false;
            if (part.parentUnit == null) return false;
            if (part.GetComponentInParent<ReplayGhost>() != null) return false;
            return true;
        }

        static void Write(Unit unit, UnitPart? part, string kind, int partId, int seg, int rotor, string path, Vector3 vel)
        {
            var engine = Record.Host != null ? Record.Host.GetComponent<RecordEngine>() : null;
            if (engine == null || RecordEngine.writer == null || kind.Length == 0) return;
            var key = new BreakKey(unit.persistentID.Id, partId, kind[0], seg, rotor);
            if (!_seen.Add(key)) return;

            long parent = unit.persistentID.Id + 1;
            if (engine.unitObjects.TryGetValue(unit.persistentID.Id, out var owner) && owner != null)
            {
                var rec = owner.GetComponent<RecordUnit>();
                if (rec != null) parent = rec.tacviewId;
            }
            if (part != null && path.Length == 0)
                path = TurretKeys.Sanitize(TurretKeys.Relative(unit.transform, part.transform));

            var host = new GameObject("NOReplay_PartBreak");
            host.hideFlags = HideFlags.HideAndDontSave;
            var obj = host.AddComponent<RecordObject>();
            obj.unitId = System.Threading.Interlocked.Increment(ref _seq) | (1L << 36);
            obj.tacviewId = obj.unitId;
            obj.props = new Dictionary<string, string>
            {
                { "Type", "Misc+PartBreak" },
                { "Parent", parent.ToString("X", CultureInfo.InvariantCulture) },
                { "Name", Clean(unit.definition != null ? unit.definition.unitName : unit.name) },
                { "Part", partId.ToString(CultureInfo.InvariantCulture) },
                { "Kind", kind },
                { "Vx", vel.x.ToString("0.##", CultureInfo.InvariantCulture) },
                { "Vy", vel.y.ToString("0.##", CultureInfo.InvariantCulture) },
                { "Vz", vel.z.ToString("0.##", CultureInfo.InvariantCulture) }
            };
            if (path.Length > 0) obj.props["Path"] = path;
            if (seg >= 0) obj.props["Seg"] = seg.ToString(CultureInfo.InvariantCulture);
            if (rotor >= 0) obj.props["Rotor"] = rotor.ToString(CultureInfo.InvariantCulture);
            try { engine.invokeWriterUpdate(obj); }
            catch (Exception ex) { Plugin.Log?.LogWarning("Part write: " + ex.Message); }
            obj.props.Clear();
            UnityEngine.Object.Destroy(host);
            if (_logs < 12)
            {
                _logs++;
                Record.Log?.LogInfo("Teil " + kind + " id=" + partId
                    + (seg >= 0 ? " seg=" + seg : "")
                    + " " + (unit.definition != null ? unit.definition.unitName : unit.name)
                    + " " + path);
            }
        }

        static int _seq;

        static string Clean(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace(',', ' ').Replace('=', ' ').Replace('|', ' ');
        }

        static int LastSegment(SwashRotor rotor)
        {
            _rotorLast ??= typeof(SwashRotor).GetField("lastAttachedSegmentIndex", Flags);
            try
            {
                if (_rotorLast?.GetValue(rotor) is int n) return n;
            }
            catch { }
            return -1;
        }

        static byte RotorDamageIndex(SwashRotor rotor)
        {
            _rotorIndex ??= typeof(SwashRotor).GetField("damageableIndex", Flags);
            try
            {
                if (_rotorIndex?.GetValue(rotor) is byte b) return b;
            }
            catch { }
            return 0;
        }

        static Unit? RotorUnit(SwashRotor rotor)
        {
            _rotorAircraft ??= typeof(SwashRotor).GetField("aircraft", Flags);
            try
            {
                if (_rotorAircraft?.GetValue(rotor) is Unit u && u != null) return u;
            }
            catch { }
            return rotor.GetComponentInParent<Unit>();
        }

        static int RotorOrdinal(Unit unit, SwashRotor rotor)
        {
            var all = unit.GetComponentsInChildren<SwashRotor>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] == rotor) return i;
            return -1;
        }
    }
}
