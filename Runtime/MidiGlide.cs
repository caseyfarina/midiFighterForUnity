using UnityEngine;

namespace MidiFighter64
{
    /// <summary>
    /// One continuous control's glide toward its last MIDI value: a critically damped
    /// follow (<see cref="Mathf.SmoothDamp(float, float, ref float, float, float, float)"/>),
    /// so a 7-bit knob moves smoothly instead of in 1/127 steps, and lands without
    /// overshoot. The first value a control sends is taken as-is, so nothing glides up
    /// from zero at startup. A struct with no allocations; the router keeps one per control.
    /// </summary>
    public struct MidiGlide
    {
        /// <summary>Within this of the target, and slower than this per second, the glide
        /// lands on the target and stops.</summary>
        public const float SettleDistance = 1e-4f;
        public const float SettleSpeed    = 1e-3f;

        public float Value;
        public float Target;
        public float Velocity;
        public bool  Seen;
        public bool  Moving;

        /// <summary>Takes a new MIDI value. Returns true when it should be reported now
        /// (the first value, or any value with gliding off); otherwise
        /// <see cref="Step"/> carries it there over the next frames.</summary>
        public bool SetTarget(float target, float seconds)
        {
            Target = target;
            if (!Seen || seconds <= 0f)
            {
                Seen = true;
                Value = target;
                Velocity = 0f;
                Moving = false;
                return true;
            }
            Moving = true;
            return false;
        }

        /// <summary>Advances the glide by <paramref name="dt"/> seconds. Returns true when
        /// <see cref="Value"/> changed and should be reported.</summary>
        public bool Step(float seconds, float dt)
        {
            if (!Moving) return false;
            // Gliding switched off mid-glide: land at once.
            if (seconds <= 0f) { Land(); return true; }
            if (dt <= 0f) return false;
            Value = Mathf.SmoothDamp(Value, Target, ref Velocity, seconds, Mathf.Infinity, dt);
            if (Mathf.Abs(Target - Value) < SettleDistance && Mathf.Abs(Velocity) < SettleSpeed) Land();
            return true;
        }

        /// <summary>Forgets the control, so its next value is taken as-is.</summary>
        public void Reset() => this = default;

        void Land()
        {
            Value = Target;
            Velocity = 0f;
            Moving = false;
        }
    }
}
