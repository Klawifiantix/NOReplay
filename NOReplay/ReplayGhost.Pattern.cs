using System;
using System.Globalization;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        GameObject? _caseGo;
        LineRenderer? _caseLine;
        bool _caseReady;
        int _caseKind;

        void SyncCasePattern(Transform drive)
        {
            if (!_ship) return;
            if (!Plugin.PatternOn && _caseLine == null) return;
            if (!CasePatternCarrier()) return;
            if (!Plugin.PatternOn || !IsTrackAlive())
            {
                HideCasePattern();
                return;
            }
            if (!EnsureCasePattern()) return;
            if (_caseGo == null || _caseLine == null) return;
            var tr = _caseGo.transform;
            tr.SetPositionAndRotation(drive.position, drive.rotation);
            Vector3 scale = drive.lossyScale;
            if (tr.localScale != scale)
                tr.localScale = scale;
            if (!_caseLine.enabled) _caseLine.enabled = true;
        }

        void HideCasePattern()
        {
            if (_caseLine != null && _caseLine.enabled)
                _caseLine.enabled = false;
        }

        void DestroyCasePattern()
        {
            _caseReady = false;
            _caseLine = null;
            if (_caseGo == null) return;
            UnityEngine.Object.Destroy(_caseGo);
            _caseGo = null;
        }

        bool CasePatternCarrier()
        {
            if (_caseKind != 0) return _caseKind > 0;
            if (_wires.Length > 0 || NameIsHyperion(CarrierLabel())
                || NameIsHyperion(Track?.Type) || NameIsHyperion(name))
            {
                _caseKind = 1;
                return true;
            }
            if (_wiresReady && _ballReady)
                _caseKind = -1;
            return false;
        }

        static bool NameIsHyperion(string? s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.IndexOf("Hyperion", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return s.IndexOf("FleetCarrier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool EnsureCasePattern()
        {
            if (_caseReady) return _caseLine != null;
            if (!_wiresReady || !_ballReady) return false;
            string source;
            ApproachPattern.DeckFrame deck;
            if (!TryMeasureDeck(out deck, out source))
            {
                ApproachPattern.AxisFromPortAngle(ApproachPattern.FallbackDeckAngleDeg, out float ax, out float az);
                deck = new ApproachPattern.DeckFrame { AxisX = ax, AxisZ = az };
                source = "fallback";
            }
            var buf = new ApproachPattern.PatternPoint[ApproachPattern.MaxPoints];
            int n = ApproachPattern.Build(deck, buf);
            _caseReady = true;
            if (n < 2) return false;
            var go = new GameObject("NOReplay_CasePattern");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(null, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = n;
            line.numCornerVertices = 4;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.widthMultiplier = 1f;
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.startWidth = 20f;
            line.endWidth = 20f;
            var col = new Color(0.15f, 0.95f, 0.2f, 1f);
            line.startColor = col;
            line.endColor = col;
            ApplyCaseMaterial(line, col);
            for (int i = 0; i < n; i++)
                line.SetPosition(i, new Vector3(buf[i].X, buf[i].Y, buf[i].Z));
            line.enabled = false;
            _caseGo = go;
            _caseLine = line;
            float lax = deck.AxisX;
            float laz = deck.AxisZ;
            float lmag = Mathf.Sqrt(lax * lax + laz * laz);
            if (lmag > 1e-4f)
            {
                lax /= lmag;
                laz /= lmag;
            }
            if (laz < 0f)
            {
                lax = -lax;
                laz = -laz;
            }
            float deg = Mathf.Atan2(lax, laz) * Mathf.Rad2Deg;
            Plugin.Log.LogInfo("Case pattern " + CarrierLabel()
                + " " + source
                + " pts=" + n.ToString(CultureInfo.InvariantCulture)
                + " deck=" + deg.ToString("0.0", CultureInfo.InvariantCulture));
            return true;
        }

        static void ApplyCaseMaterial(LineRenderer line, Color col)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Hidden/Internal-Colored")
                     ?? Shader.Find("Sprites/Default");
            if (sh != null && sh.name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
                sh = Shader.Find("Sprites/Default") ?? sh;
            if (sh == null)
            {
                Plugin.Log?.LogWarning("Case pattern: kein Shader");
                return;
            }
            var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", col * 2.2f);
            }
            line.material = mat;
        }

        bool TryMeasureDeck(out ApproachPattern.DeckFrame deck, out string source)
        {
            deck = default;
            source = "";
            var drive = Drive;
            bool runway = false;
            float rax = 0f, raz = 1f;
            bool touch = false;
            float tx = 0f, ty = 0f, tz = 0f;

            for (int i = 0; i < _balls.Length; i++)
            {
                var rig = _balls[i];
                if (rig.RunwayStart == null) continue;
                Vector3 s = drive.InverseTransformPoint(rig.RunwayStart.position);
                if (rig.RunwayEnd == null) continue;
                Vector3 e = drive.InverseTransformPoint(rig.RunwayEnd.position);
                float dx = e.x - s.x;
                float dz = e.z - s.z;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 1f) continue;
                rax = dx / len;
                raz = dz / len;
                float along = ApproachPattern.MeatballTouchAlong(len);
                tx = s.x + rax * along;
                ty = s.y;
                tz = s.z + raz * along;
                touch = true;
                runway = true;
                break;
            }

            bool wires = false;
            float wax = 0f, waz = 1f;
            if (_wires.Length > 0)
            {
                float sx = 0f, sy = 0f, sz = 0f;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                int iMin = 0, iMax = 0;
                int count = 0;
                for (int i = 0; i < _wires.Length; i++)
                {
                    var w = _wires[i];
                    float mx = (w.Ax + w.Bx) * 0.5f;
                    float my = (w.Ay + w.By) * 0.5f;
                    float mz = (w.Az + w.Bz) * 0.5f;
                    sx += mx;
                    sy += my;
                    sz += mz;
                    if (mz < minZ) { minZ = mz; iMin = i; }
                    if (mz > maxZ) { maxZ = mz; iMax = i; }
                    count++;
                }
                if (count > 0)
                {
                    tx = sx / count;
                    ty = sy / count;
                    tz = sz / count;
                    touch = true;
                    wires = true;
                    if (count >= 2 && maxZ - minZ > 5f)
                    {
                        var a = _wires[iMin];
                        var b = _wires[iMax];
                        wax = (b.Ax + b.Bx) * 0.5f - (a.Ax + a.Bx) * 0.5f;
                        waz = (b.Az + b.Bz) * 0.5f - (a.Az + a.Bz) * 0.5f;
                    }
                    else
                    {
                        var w = _wires[iMax];
                        float wx = w.Bx - w.Ax;
                        float wz = w.Bz - w.Az;
                        wax = -wz;
                        waz = wx;
                        if (waz < 0f)
                        {
                            wax = -wax;
                            waz = -waz;
                        }
                    }
                    float wlen = Mathf.Sqrt(wax * wax + waz * waz);
                    if (wlen > 0.01f)
                    {
                        wax /= wlen;
                        waz /= wlen;
                    }
                    else
                    {
                        wax = 0f;
                        waz = 1f;
                    }
                }
            }

            if (!touch && !runway)
                return false;

            float ax, az;
            if (runway)
            {
                ax = rax;
                az = raz;
                if (wires && ax * wax + az * waz < 0f)
                {
                    ax = -ax;
                    az = -az;
                }
                source = wires ? "wires+runway" : "runway";
            }
            else
            {
                ax = wax;
                az = waz;
                source = "wires";
            }
            deck = new ApproachPattern.DeckFrame
            {
                OriginX = tx,
                OriginY = ty,
                OriginZ = tz,
                AxisX = ax,
                AxisZ = az
            };
            return true;
        }
    }
}
