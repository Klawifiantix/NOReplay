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

        /// <summary>
        /// Turbojet and Turbofan nozzle swivel. Pitch and roll feed the
        /// pitch axis, yaw feeds the yaw axis, both slew at 70 deg/s.
        /// Above the airspeed limit the nozzles snap shut. Below 3 m
        /// radar altitude the yaw term is dropped.
        /// </summary>
        public static void Tvc(ref float pitch, ref float yaw,
            float stickPitch, float stickRoll, float stickYaw,
            float gainX, float gainZ, float vecX, float vecY, float vecZ,
            float airspeed, float maxAirspeed, float radarAlt,
            float dt, bool snap)
        {
            if (airspeed > maxAirspeed)
            {
                pitch = 0f;
                yaw = 0f;
                return;
            }
            float targetPitch = 0f - Clamp1(stickPitch * gainX) * vecX;
            targetPitch -= Clamp1(stickRoll * gainZ) * vecZ;
            float targetYaw = radarAlt < 3f ? 0f : Clamp1(0f - stickYaw) * vecY;
            Slew(ref pitch, targetPitch, 70f, dt, snap);
            Slew(ref yaw, targetYaw, 70f, dt, snap);
        }

        /// <summary>
        /// Airbrake.Update. Throttle exactly zero opens the flap at openSpeed.
        /// Any other throttle closes it at the same speed. Snap writes 0 or 1.
        /// </summary>
        public static void Airbrake(ref float open, bool deploy, float speed, float dt, bool snap)
        {
            float target = deploy ? 1f : 0f;
            if (snap || dt <= 0f)
            {
                open = target;
                return;
            }
            if (speed <= 0f) return;
            float step = speed * dt;
            if (deploy) open += step;
            else open -= step;
            if (open > 1f) open = 1f;
            else if (open < 0f) open = 0f;
        }

        public static float TvcPitchEuler(float pitch)
        {
            if (pitch > 20f) return 20f;
            if (pitch < -20f) return -20f;
            return pitch;
        }

        static float Clamp1(float v)
        {
            if (v > 1f) return 1f;
            if (v < -1f) return -1f;
            return v;
        }

        static void Slew(ref float current, float target, float speed, float dt, bool snap)
        {
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
