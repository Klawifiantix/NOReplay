using System;

namespace NOReplay
{
    /// <summary>
    /// The control-surface job in the game. A non-flap axis slews toward
    /// stick times range at servoSpeed. A flap follows the gear, not the stick.
    /// A split surface opens when the throttle is closed, plus a yaw term.
    /// Snap writes the target at once, which is what a seek or a pause needs.
    /// </summary>
    public static class SurfServo
    {
        public static void Axis(ref float current, float input, float range, float speed, float dt, bool snap)
        {
            float limit = Math.Abs(range);
            float target = input * range;
            if (target > limit) target = limit;
            else if (target < -limit) target = -limit;
            if (snap || dt <= 0f)
            {
                current = target;
                return;
            }
            float step = speed * dt;
            float delta = target - current;
            if (delta > step) delta = step;
            else if (delta < -step) delta = -step;
            current += delta;
            if (current > limit) current = limit;
            else if (current < -limit) current = -limit;
        }

        public static void Flap(ref float current, float range, bool gearDown, float speed, float dt, bool snap)
        {
            float limit = Math.Abs(range);
            float target = gearDown ? -limit : 0f;
            if (snap || dt <= 0f)
            {
                current = target;
                return;
            }
            float step = speed * dt;
            if (gearDown) current -= step;
            else current += step;
            if (current > 0f) current = 0f;
            else if (current < -limit) current = -limit;
        }

        public static void Split(ref float current, float throttle, bool hasThrottle, float yaw, float maxSplit, float yawSplit, float speed, float dt, bool snap)
        {
            if (maxSplit <= 0f)
            {
                current = 0f;
                return;
            }
            float target = hasThrottle && throttle == 0f ? maxSplit : 0f;
            target += yawSplit * yaw * maxSplit;
            if (target < 0f) target = 0f;
            else if (target > maxSplit) target = maxSplit;
            if (snap || dt <= 0f)
            {
                current = target;
                return;
            }
            float step = speed * dt;
            float delta = target - current;
            if (delta > step) delta = step;
            else if (delta < -step) delta = -step;
            current += delta;
        }
    }
}
