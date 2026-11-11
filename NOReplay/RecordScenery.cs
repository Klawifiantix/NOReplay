using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NOReplay
{
    internal class RecordScenery : RecordUnit
    {
        public Scenery scenery;
        private bool subscribed;

        public virtual void Init(Scenery scenery)
        {
            base.unit = scenery;
            this.scenery = (Scenery)base.unit;
            base.unitId = unit.persistentID.Id;
            base.tacviewId = unit.persistentID.Id + 1;
            base.destroyedEvent = true;
            lastState = unit.unitState;

            props = new Dictionary<string, string>()
            {
                { "Name", this.unit.definition.unitName },
                { "CallSign", $"{scenery.definition.code} {tacviewId:X}"},
                { "Coalition", "Neutral" },
                { "Color", "Green" },
                { "Type","Ground+Static+Building"},
                { "Debug", lastState.ToString()}
            };

            UpdatePose();
            UpdateState();
            Engine.invokeWriterUpdate(this);
            props.Clear();
            RecordWreck.Watch(this, unit.persistentID.Id);

            // Events statt Polling
            if (!subscribed && unit != null)
            {
                unit.onDisableUnit += OnUnitDisabled;
                subscribed = true;
            }

            // Bereits zerstört?
            if (unit.disabled)
            {
                OnUnitDisabled(unit);
                return;
            }

            // Kein weiteres Update/LateUpdate mehr
            this.enabled = false;
            base.enabled = false;
        }

        internal override void NoteBlast(string src)
        {
            if (!MarkBlast() || unit == null) return;
            string label = unit.definition != null ? unit.definition.unitName : unit.unitName;
            props = new Dictionary<string, string>();
            props["Boom"] = "1";
            Engine.invokeWriterUpdate(this);
            props.Clear();
            Record.Log?.LogInfo("Boom-Rec " + label + " src=" + src);
        }

        private void OnUnitDisabled(Unit u)
        {
            if (u != unit || base.disabled) return;

            base.disabled = true;
            props = new Dictionary<string, string>();
            props.Add("Visible", "0.0");
            string label = unit.definition != null ? unit.definition.unitName : unit.unitName;
            if (WreckMark.IsCookoff(label) && MarkBlast())
            {
                props.Add("Boom", "1");
                Record.Log?.LogInfo("Boom-Rec " + label + " src=disable");
            }
            props.Add("Type", null);

            Engine.invokeWriterUpdate(this);
            props.Clear();

            if (base.destroyedEvent)
            {
                Engine.invokeWriterDestroy(this);
            }

            Unsubscribe();
            this.enabled = false;
            Record.Log?.LogDebug($"SCENERY DESTROYED {unitId}");
            UnityEngine.Object.Destroy(this.gameObject);
        }

        private void Unsubscribe()
        {
            if (!subscribed || unit == null) return;
            try
            {
                unit.onDisableUnit -= OnUnitDisabled;
            }
            catch { }
            subscribed = false;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public override void Update()
        {
            // Absichtlich leer
        }

        public override void LateUpdate()
        {
            // Absichtlich leer – Destroy kommt über Event
        }
    }
}