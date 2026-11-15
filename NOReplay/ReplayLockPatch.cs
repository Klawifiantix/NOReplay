using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// The map line is MapToolTip.line1. Aircraft and missiles already draw it
    /// from the weapon list and missile.targetID. Ground units do not.
    /// This draws that same line for every replay unit that has a lock, aimed
    /// at the ghost rather than at a faction's last known position.
    /// A missing known position falls back to the unit itself so the game's
    /// own missile and aircraft lines point the right way too.
    /// </summary>
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.TryGetKnownPosition))]
    static class ReplayKnownPosition
    {
        static void Postfix(Unit unit, ref GlobalPosition knownPosition, ref bool __result)
        {
            if (__result || Plugin.CurrentDoc == null || unit == null) return;
            knownPosition = unit.GlobalPosition();
            __result = true;
        }
    }

    [HarmonyPatch(typeof(MapToolTip), "Refresh")]
    static class ReplayMapLockLine
    {
        static FieldInfo? _line;
        static PropertyInfo? _enabled;
        static PropertyInfo? _color;
        static bool _looked;

        static void Postfix(MapToolTip __instance, MapIcon icon)
        {
            if (Plugin.CurrentDoc == null || __instance == null) return;
            if (icon is not UnitMapIcon mark || mark.unit == null) return;
            var ghost = ReplayGhost.ForUnit(mark.unit);
            if (ghost == null) return;
            ghost.ApplyLock();
            var line = Line(__instance);
            if (line == null) return;
            var target = ghost.LockUnit;
            if (target == null)
            {
                SetEnabled(line, false);
                return;
            }
            Draw(line, mark.unit, target);
        }

        static Component? Line(MapToolTip tip)
        {
            if (!_looked)
            {
                _looked = true;
                _line = typeof(MapToolTip).GetField("line1",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            return _line?.GetValue(tip) as Component;
        }

        static void SetEnabled(Component line, bool on)
        {
            _enabled ??= line.GetType().GetProperty("enabled");
            try { _enabled?.SetValue(line, on); } catch { }
        }

        static void Draw(Component line, Unit from, Unit to)
        {
            var map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null) return;
            Vector3 dir = FastMath.Direction(from.GlobalPosition(), to.GlobalPosition());
            float angle = Vector3.SignedAngle(Vector3.forward, new Vector3(dir.x, 0f, dir.z), Vector3.up);
            float pixels = 0f;
            if (DynamicMap.TryGetMapIcon(to, out var targetIcon) && DynamicMap.TryGetMapIcon(from, out var selfIcon)
                && targetIcon != null && selfIcon != null)
                pixels = Vector3.Distance(targetIcon.transform.localPosition, selfIcon.transform.localPosition);
            else
                pixels = FastMath.Distance(from.GlobalPosition(), to.GlobalPosition()) * map.MetersToPixels();
            float scale = map.mapImage.transform.localScale.x;
            var rect = line.transform as RectTransform;
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(1f, pixels * scale);
                rect.localEulerAngles = new Vector3(0f, 0f, 0f - angle);
            }
            _color ??= line.GetType().GetProperty("color");
            var tint = Color.red;
            tint.a = 0.6f;
            try { _color?.SetValue(line, tint); } catch { }
            SetEnabled(line, true);
        }
    }

    [HarmonyPatch(typeof(UnitDebug), "Update")]
    static class ReplayOrbitTarget
    {
        static FieldInfo? _unit;
        static FieldInfo? _text;
        static FieldInfo? _panel;
        static bool _looked;

        static void Postfix(UnitDebug __instance)
        {
            if (Plugin.CurrentDoc == null || __instance == null) return;
            if (!_looked)
            {
                _looked = true;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                _unit = typeof(UnitDebug).GetField("followingUnit", flags);
                _text = typeof(UnitDebug).GetField("target", flags);
                _panel = typeof(UnitDebug).GetField("targetPanel", flags);
            }
            var unit = _unit?.GetValue(__instance) as Unit;
            if (unit == null) return;
            var ghost = ReplayGhost.ForUnit(unit);
            if (ghost == null) return;
            ghost.ApplyLock();
            var target = ghost.LockUnit;
            if (target == null) return;
            var text = _text?.GetValue(__instance) as Component;
            if (text != null)
            {
                var prop = text.GetType().GetProperty("text");
                try { prop?.SetValue(text, target.unitName); } catch { }
            }
            if (_panel?.GetValue(__instance) is GameObject panel)
                panel.SetActive(true);
        }
    }
}
