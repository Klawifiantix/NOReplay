using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    internal class RecordObject : MonoBehaviour
    {
        public long unitId;
        public long tacviewId;
        internal float timer = 0f;
        internal float fx, fy, fz, fax, fay, faz;
        internal bool disabled = false;
        internal bool destroyedEvent = false;

        public event Action<string, long[], string>? OnEvent;

        public Dictionary<string, string> props = [];

        public virtual void Init()
        {

        }

        public virtual void Update()
        {

        }

        void Awake()
        {

        }
        void FixedUpdate()
        {

        }
        void Reset()
        {

        }
        void OnDisable()
        {

        }
    }
}
