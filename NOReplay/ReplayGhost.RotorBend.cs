using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct BendBlade
        {
            public Transform Span;
            public Transform[] Segs;
            public int SegCount;
            public int Last;
        }

        struct BendShaft
        {
            public BendBlade[] Blades;
        }

        BendShaft[] _bendShafts = System.Array.Empty<BendShaft>();
        bool _bendCached;
        bool _bendLogged;
        float _bendStateAt = -100f;
        int _bendBlades;
        int _bendSegs;
        static FieldInfo? _bfRotors, _bfSegs, _bfSegTr, _bfLast;
        static bool _bfBound;
        static readonly CultureInfo BendInv = CultureInfo.InvariantCulture;

        void CacheRotorBend()
        {
            _bendCached = true;
            var shaftType = Plugin.FindGameType("RotorShaft");
            var bladeType = Plugin.FindGameType("SwashRotor");
            if (shaftType == null || bladeType == null) return;
            if (!_bfBound)
            {
                _bfBound = true;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _bfRotors = shaftType.GetField("rotors", flags);
                _bfSegs = bladeType.GetField("segments", flags);
                _bfLast = bladeType.GetField("lastAttachedSegmentIndex", flags);
                var segType = bladeType.GetNestedType("RotorSegment", flags);
                _bfSegTr = segType?.GetField("transform", flags);
            }
            var found = GetComponentsInChildren(shaftType, true);
            var shafts = new System.Collections.Generic.List<BendShaft>(found.Length);
            int blades = 0;
            int segs = 0;
            for (int s = 0; s < found.Length; s++)
            {
                if (found[s] is not Component shaft) continue;
                if (_bfRotors?.GetValue(shaft) is not System.Array rotors) continue;
                var list = new System.Collections.Generic.List<BendBlade>(rotors.Length);
                for (int i = 0; i < rotors.Length; i++)
                {
                    if (rotors.GetValue(i) is not Component blade) continue;
                    Transform[] segTr = System.Array.Empty<Transform>();
                    int count = 0;
                    int live = 0;
                    if (_bfSegs?.GetValue(blade) is System.Array parts)
                    {
                        count = parts.Length;
                        segTr = new Transform[count];
                        for (int k = 0; k < parts.Length; k++)
                        {
                            var part = parts.GetValue(k);
                            if (part == null) continue;
                            if (_bfSegTr == null)
                                _bfSegTr = part.GetType().GetField("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (_bfSegTr?.GetValue(part) is Transform tr && tr != null)
                            {
                                segTr[k] = tr;
                                live++;
                            }
                        }
                    }
                    int last = count - 1;
                    try { if (_bfLast?.GetValue(blade) is int n) last = n; } catch { }
                    list.Add(new BendBlade
                    {
                        Span = blade.transform,
                        Segs = segTr,
                        SegCount = count,
                        Last = last
                    });
                    blades++;
                    segs += live;
                }
                if (list.Count == 0) continue;
                shafts.Add(new BendShaft { Blades = list.ToArray() });
            }
            _bendShafts = shafts.ToArray();
            _bendBlades = blades;
            _bendSegs = segs;
            if (!_bendLogged && blades > 0)
            {
                _bendLogged = true;
                Plugin.Log?.LogInfo("Rotor bend drive " + gameObject.name
                    + " rotors=" + _bendShafts.Length
                    + " blades=" + blades
                    + " segs=" + segs);
            }
        }

        void DriveRotorBend()
        {
            if (!_air) return;
            if (!_bendCached) CacheRotorBend();
            if (_bendShafts.Length == 0) return;
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            RotorBend.Frame frame = default;
            bool have = Track != null && Track.BendAt(now, out frame);
            float shownFlap = 0f;
            float shownRatio = 0f;
            bool shown = false;
            for (int s = 0; s < _bendShafts.Length; s++)
            {
                float ratio = 0f;
                float[] flaps = System.Array.Empty<float>();
                if (have && frame.Ratio != null && s < frame.Ratio.Length)
                {
                    ratio = frame.Ratio[s];
                    if (frame.Flap != null && s < frame.Flap.Length && frame.Flap[s] != null)
                        flaps = frame.Flap[s];
                }
                var blades = _bendShafts[s].Blades;
                for (int i = 0; i < blades.Length; i++)
                {
                    var blade = blades[i];
                    if (blade.Span == null) continue;
                    float flap = i < flaps.Length ? flaps[i] : 0f;
                    var euler = blade.Span.localEulerAngles;
                    euler.x = RotorBend.SpanXDeg(flap);
                    blade.Span.localEulerAngles = euler;
                    int last = blade.Last;
                    if (last < 0) continue;
                    bool droop = RotorBend.Droops(flap, ratio);
                    int end = last;
                    if (end >= blade.Segs.Length) end = blade.Segs.Length - 1;
                    for (int k = 0; k <= end; k++)
                    {
                        var tr = blade.Segs[k];
                        if (tr == null) continue;
                        if (!droop)
                        {
                            tr.localEulerAngles = Vector3.zero;
                            continue;
                        }
                        float x = RotorBend.SegmentXDeg(flap, blade.SegCount, k);
                        tr.localEulerAngles = new Vector3(x, 0f, 0f);
                    }
                    if (!shown)
                    {
                        shown = true;
                        shownFlap = RotorBend.SpanXDeg(flap);
                        shownRatio = ratio;
                    }
                }
            }
            float wall = Time.unscaledTime;
            if (wall - _bendStateAt >= 5f)
            {
                _bendStateAt = wall;
                Plugin.Log?.LogInfo("Rotor bend state " + gameObject.name
                    + " flap=" + shownFlap.ToString("0.0", BendInv)
                    + " rpmRatio=" + shownRatio.ToString("0.00", BendInv));
            }
        }
    }
}
