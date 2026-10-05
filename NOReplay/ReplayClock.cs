using UnityEngine;

namespace NOReplay
{
    public sealed class ReplayClock
    {
        public float Time;
        public float Duration;
        public float Speed = 1f;
        public bool Paused = true;

        public void Reset(float duration)
        {
            Duration = duration > 0f ? duration : 0f;
            Time = 0f;
            Speed = 1f;
            Paused = true;
        }

        public bool WentBack;
        bool _reverseCleared;

        public void Seek(float t)
        {
            float max = Duration > 0f ? Duration : 0f;
            if (t < 0f) t = 0f;
            if (t > max) t = max;
            if (t < Time - 0.02f) WentBack = true;
            Time = t;
        }

        public void Tick(float dt)
        {
            if (Paused || Duration <= 0f) return;
            if (Speed < 0f)
            {
                if (!_reverseCleared) { WentBack = true; _reverseCleared = true; }
            }
            else _reverseCleared = false;
            Time += dt * Speed;
            if (Time < 0f) Time = 0f;
            if (Time > Duration) Time = Duration;
        }
    }
}