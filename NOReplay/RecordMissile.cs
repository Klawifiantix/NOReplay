using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    internal class RecordMissile : RecordUnit
    {
        private readonly Dictionary<string, string> TYPES = new()
        {
            {"MSL", "Weapon+Missile"},
            {"BOMB", "Weapon+Bomb"}
        };

        bool _shell;

        static readonly FieldInfo? warheadField =
            typeof(Missile).GetField("warhead", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly Dictionary<Type, FieldInfo?> detonatedByType = new();

        private float lastAGL = float.NaN;
        private float lastTAS = float.NaN;
        private float lastAOA = float.NaN;
        private uint lastTarget = 0;

        internal bool Detonated
        {
            get; private set;
        }
        Missile missile;
        GameObject shockwave;

        public event Action<RecordMissile>? OnDetonate;

        public virtual void Init(Missile missile)
        {
            base.unit = missile;
            this.missile = (Missile)base.unit;
            base.unitId = missile.persistentID.Id;
            base.tacviewId = missile.persistentID.Id + 1;
            base.canTarget = true;
            string[] info = { "Default", "Weapon+Missile" };
            if (Record.UnitInfo["missiles"].ContainsKey(missile.definition.code))
            {
                info = Record.UnitInfo["missiles"][missile.definition.code];
            }
            string code = missile.definition?.code ?? "";
            _shell = IsPhysicalShell(missile);
            if (_shell)
                info[1] = "Weapon+Shell";
            else if (TYPES.TryGetValue(code, out var mapped))
                info[1] = mapped;
            else if ((missile.definition?.unitName ?? "").IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0
                     || code.IndexOf("BOMB", StringComparison.OrdinalIgnoreCase) >= 0)
                info[1] = "Weapon+Bomb";
            else
                info[1] = "Weapon+Missile";
            if (_shell)
            {
                Record.Log?.LogInfo("Kanone-Shell " + (missile.definition?.unitName ?? "?")
                    + " code=" + code
                    + " seeker=" + (missile.GetComponent<MissileSeeker>()?.GetType().Name ?? "-"));
            }
            if (unit.unitName.EndsWith("kt)"))
            {
                base.destroyedEvent = true;
            }

            lastState = missile.unitState;
            Faction? faction = base.unit.NetworkHQ?.faction;
            props = new Dictionary<string, string>()
            {
                { "Name", base.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", info[1] },
                { "CallSign", $"{missile.definition.unitName} {tacviewId:X}" },
                { "Debug", lastState.ToString()}
            };
            var recorder = Engine;
            if (missile.owner != null && recorder.unitObjects.TryGetValue(missile.owner.persistentID.Id, out var ownerGo) && ownerGo != null)
            {
                var ownerRec = ownerGo.GetComponent<RecordUnit>();
                if (ownerRec != null)
                    props["Parent"] = ownerRec.tacviewId.ToString("X", CultureInfo.InvariantCulture);
            }
            UpdatePose();
            recorder.invokeWriterUpdate(this);
            props.Clear();
            this.enabled = true;
            base.enabled = true;
        }
        bool _deathWritten;

        void WriteDeath()
        {
            if (_deathWritten || !this.enabled) return;
            _deathWritten = true;
            try
            {
                if (unit != null)
                {
                    UpdatePose();
                    UpdateState();
                }
                props["Visible"] = "0.0";
                props["Type"] = null;
                Engine.invokeWriterUpdate(this);
                props.Clear();
                Engine.invokeWriterRemove(this);
                Record.Log?.LogInfo("Missile tot " + (missile != null ? missile.definition.unitName : "?")
                    + " id=" + tacviewId.ToString("X"));
            }
            catch { }
            this.enabled = false;
        }

        void OnDisable()
        {
            WriteDeath();
        }

        void OnDestroy()
        {
            WriteDeath();
        }

        public override void Update()
        {
            if (_deathWritten) return;
            if (unit != null && unit.disabled)
            {
                WriteDeath();
                return;
            }
            if (!this.enabled)
            {
                return;
            }
            if (IsWarheadDetonated())
            {
                WriteDeath();
                return;
            }
            timer += Time.deltaTime;
            float step = _shell ? 0.05f : RecordConfig.munitionUpdateDelta.Value;
            if (timer < step)
            {
                return;
            }
            UpdatePose();
            UpdateState();
            UpdateMissile();
            Engine.invokeWriterUpdate(this);
            props.Clear();
            timer = 0;
        }

        static bool IsPhysicalShell(Missile missile)
        {
            if (missile == null) return false;
            string code = missile.definition?.code ?? "";
            if (code.Equals("SHL", StringComparison.OrdinalIgnoreCase)) return true;
            var seeker = missile.GetComponent<MissileSeeker>();
            if (seeker == null) return false;
            string seekerName = seeker.GetType().Name;
            return seekerName.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool IsWarheadDetonated()
        {
            try
            {
                var wh = warheadField?.GetValue(missile);
                if (wh != null)
                {
                    var wt = wh.GetType();
                    if (!detonatedByType.TryGetValue(wt, out var detField))
                    {
                        detField = wt.GetField("detonated", BindingFlags.NonPublic | BindingFlags.Instance);
                        detonatedByType[wt] = detField;
                    }
                    if (detField?.GetValue(wh) is bool d && d)
                        return true;
                }
            }
            catch { }
            return false;
        }

        void UpdateMissile()
        {
            if (IsWarheadDetonated())
            {
                WriteDeath();
                return;
            }

            if (unit.speed != lastTAS && RecordConfig.RecordSpeed.Value == true)
            {
                props.Add("TAS", unit.speed.ToString("0.##", CultureInfo.InvariantCulture));
                props.Add("Mach", (unit.speed / 340).ToString("0.##", CultureInfo.InvariantCulture));
                lastTAS = unit.speed;
            }

            Vector3 vector3 = unit.transform.InverseTransformDirection(unit.rb.velocity);
            float num = Mathf.Atan2(vector3.y, vector3.z) * -57.29578f;

            if (num != lastAOA && RecordConfig.RecordAOA.Value == true)
            {
                props.Add("AOA", num.ToString("0.##", CultureInfo.InvariantCulture));
                lastAOA = num;
            }

            if (unit.radarAlt != lastAGL && RecordConfig.RecordAGL.Value == true)
            {
                props.Add("AGL", unit.radarAlt.ToString("0.##", CultureInfo.InvariantCulture));
                lastAGL = unit.radarAlt;
            }

            if (missile.targetID.Id != lastTarget)
            {
                if (missile.targetID.Id != -1)
                {
                    props.Add("LockedTarget", $"{GetTacviewIdOfUnit(missile.targetID.Id):X}");

                    if (lastTarget == -1)
                        props.Add("LockedTargetMode", "1");
                }
                else
                    props.Add("LockedTargetMode", "0");


                lastTarget = missile.targetID.Id;
            }
        }
    }
}