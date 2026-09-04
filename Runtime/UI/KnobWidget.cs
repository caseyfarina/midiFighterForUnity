using UnityEngine;
using UnityEngine.UIElements;

namespace MidiFighter64
{
    /// <summary>
    /// Base type for any knob visual the status drawer can show. The drawer talks
    /// to knobs only through this contract — value, theme ink, stroke weight — so a
    /// new knob design drops in by subclassing this and returning it from
    /// <c>MidiStatusDrawer.MakeKnob</c>; nothing else in the drawer changes.
    ///
    /// The drawer sizes knobs by flex-grow inside a fixed-size grid (see the knob
    /// fisheye), so a design must fill its cell — the drawer sets flex on the
    /// element; don't hard-code a width/height on top of it. How the design maps
    /// its drawing to that cell is then a *look* choice:
    ///   • draw to the full <c>contentRect</c> (independent x/y radii, the way
    ///     <see cref="KnobDisplay"/> and <see cref="PadCell"/> do) and the knob
    ///     warps with the cell — round at rest, elliptical under the fisheye; or
    ///   • draw to <c>min(width,height)</c> and centre, and it stays round and
    ///     simply re-centres when a cell grows on one axis.
    /// Either is fisheye-safe; pick per design.
    /// </summary>
    public abstract class KnobWidget : VisualElement
    {
        /// <summary>Current value, 0–1.</summary>
        public abstract float Value { get; set; }

        /// <summary>Multiplier on the shared base stroke width, from the drawer's
        /// global stroke weight.</summary>
        public abstract float StrokeScale { get; set; }

        /// <summary>Theme inks: lit ("on"), spent ("off"), and the body outline.
        /// Called from the drawer's palette. A design free to ignore any of the
        /// three still has to accept the call.</summary>
        public abstract void SetInk(Color on, Color off, Color outline);

        /// <summary>When true, the knob must draw a true circle regardless of its
        /// cell's aspect. The drawer sets this on the *focused* knob so the one being
        /// adjusted is never distorted — only its neighbours warp to absorb the
        /// growth. A design that never distorts can leave this as the no-op it is
        /// here; a distorting design overrides it and repaints on change.</summary>
        public virtual bool KeepRound { get; set; }

        /// <summary>The knob grid cell's width ÷ height *at rest*, supplied once by the
        /// drawer. The knob grid is wider than it is tall, so a distorting design
        /// divides its horizontal fill by this to read round at rest and warp only as
        /// the fisheye stretches its cell. Designs that stay round regardless can
        /// ignore it; defaults to 1 (square) so a design that forgets still works.</summary>
        public virtual float NeutralAspect { get; set; } = 1f;
    }
}
