using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    internal class RecordShip : RecordUnit
    {
        const float DyingSampleSeconds = 0.2f;
        const float DespawnCapSeconds = 180f;

        private Ship ship;
        bool _sinking;
        bool _sinkEnded;
        float _sinkFor;
        float _despawnLimit;
        static bool _delayWarned;

        public virtual void Init(Ship ship)
        {
            base.unit = ship;
            this.ship = ship;
            base.unitId = ship.persistentID.Id;
            base.tacviewId = ship.persistentID.Id + 1;
            base.destroyedEvent = true;
            base.canTarget = true;
            lastState = ship.unitState;

            Faction? faction = base.unit.NetworkHQ?.faction;
            string[] info = { "Default", "Sea+Watercraft", "0", "0" };
            if (Record.UnitInfo["ships"].ContainsKey(ship.definition.unitName))
            {
                info = Record.UnitInfo["ships"][ship.definition.unitName];
            }

            props = new Dictionary<string, string>()
            {
                { "Name", base.unit.definition.unitName },
                { "Coalition", faction?.factionName ?? "Neutral" },
                { "CallSign", $"{ship.definition.unitName} {tacviewId:X}" },
                { "Color", faction == null ? "Green" : (faction.factionName == "Boscali" ? "Blue" : "Red") },
                { "Type", info[1] },
                { "Debug", lastState.ToString() }
            };

            UpdatePose();
            Engine.invokeWriterUpdate(this);
            props.Clear();
            RecordTurrets.Attach(this, ship);

            if (unit != null)
            {
                unit.onDisableUnit += OnUnitDisabled;
                unit.onChangeFaction += OnFactionChanged;
                unit.onRegisterMissile += OnMissileRegistered;
            }

            this.enabled = true;
        }

        public override void Update()
        {
            if (!enabled) return;
            if (_sinking)
            {
                TickSink();
                return;
            }
            if (unit == null || unit.disabled) return;

            timer += Time.deltaTime;
            if (timer < RecordConfig.vehicleUpdateDelta.Value) return;

            UpdatePose();
            if (props.Count > 0)
            {
                Engine.invokeWriterUpdate(this);
                props.Clear();
            }
            timer = 0;
        }

        void TickSink()
        {
            bool gone = ship == null;
            _sinkFor += Time.deltaTime;
            if (gone || _sinkFor >= _despawnLimit)
            {
                EndSink();
                return;
            }
            timer += Time.deltaTime;
            if (timer < DyingSampleSeconds) return;
            timer = 0f;
            UpdatePose();
            if (props.Count > 0)
            {
                Engine.invokeWriterUpdate(this);
                props.Clear();
            }
        }

        private void OnFactionChanged(Unit u)
        {
            if (u != unit || unit.disabled) return;
            string coalition = unit.NetworkHQ?.faction?.factionName ?? "Neutral";
            props = new Dictionary<string, string> { { "Coalition", coalition } };
            string color = coalition switch
            {
                "Boscali" => "Blue",
                "Primeva" => "Red",
                _ => "Green"
            };
            props.Add("Color", color);
            Engine.invokeWriterUpdate(this);
            props.Clear();
        }

        private void OnMissileRegistered(Missile missile)
        {
            if (missile == null) return;
            Record.Log?.LogDebug($"Ship {unitId:X} registered missile {missile.persistentID.Id:X}");
        }

        private void OnUnitDisabled(Unit u)
        {
            if (u != unit || _sinking || base.disabled) return;
            _sinking = true;
            base.disabled = true;
            _sinkFor = 0f;
            _despawnLimit = DespawnLimit();
            // Tacview ignores an unknown property and does not remove the object.
            // Visible=0, which does hide it, is written only when the hull is gone.
            props.Clear();
            if (unit != null) UpdatePose(true);
            props["Sinking"] = "1";
            Engine.invokeWriterUpdate(this);
            props.Clear();
            if (base.destroyedEvent)
                Engine.invokeWriterDestroy(this);
            Record.Log?.LogInfo("Schiff sinkt " + tacviewId.ToString("X")
                + " noch " + _despawnLimit.ToString("0.#", CultureInfo.InvariantCulture) + "s");
            if (_despawnLimit <= 0f || ship == null)
                EndSink();
        }

        float DespawnLimit()
        {
            try
            {
                var field = typeof(Ship).GetField("disabledDespawnDelay",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null && field.GetValue(ship) is float raw && raw >= 0f)
                    return (int)raw;
                if (field == null && !_delayWarned)
                {
                    _delayWarned = true;
                    Record.Log?.LogWarning("Ship.disabledDespawnDelay fehlt. Die Hülle wird bis zum Zerstören mitgeschrieben.");
                }
            }
            catch (Exception ex)
            {
                Record.Log?.LogDebug("Ship despawn delay: " + ex.Message);
            }
            return DespawnCapSeconds;
        }

        void EndSink()
        {
            if (_sinkEnded) return;
            _sinkEnded = true;
            WriteRemoval();
            Unsubscribe();
            enabled = false;
            UnityEngine.Object.Destroy(gameObject);
        }

        void WriteRemoval()
        {
            try
            {
                props.Clear();
                if (unit != null) UpdatePose(true);
                props["Visible"] = "0.0";
                props["Type"] = null!;
                Engine.invokeWriterUpdate(this);
                props.Clear();
            }
            catch (Exception ex)
            {
                Record.Log?.LogDebug("Ship sink end: " + ex.Message);
            }
        }

        private void Unsubscribe()
        {
            if (unit == null) return;
            try
            {
                unit.onDisableUnit -= OnUnitDisabled;
                unit.onChangeFaction -= OnFactionChanged;
                unit.onRegisterMissile -= OnMissileRegistered;
            }
            catch { }
        }

        private void OnDestroy()
        {
            if (_sinking && !_sinkEnded)
            {
                _sinkEnded = true;
                WriteRemoval();
            }
            Unsubscribe();
        }

        public override void LateUpdate() { }
        internal override void UpdateTargets() { }
    }
}