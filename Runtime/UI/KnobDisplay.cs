using UnityEngine;
using UnityEngine.UIElements;

namespace MidiFighter64
{
    /// <summary>
    /// Read-only value display shaped like a hardware synth knob: a ring of
    /// uniform tick dots on a 270° arc, a stroked knob body inside it, and a
    /// filled pointer dot on the body that rotates to the current value. Dots
    /// at or below the value take the "on" ink, the rest the "off" ink, so the
    /// value reads at a glance from either the pointer or the ring.
    ///
    /// The shipped <see cref="KnobWidget"/> design. It always fills its cell
    /// vertically and fills width ÷ <see cref="KnobWidget.NeutralAspect"/> horizontally,
    /// so at the knob grid's resting cell shape it reads perfectly round, and warps
    /// into an ellipse only as the fisheye stretches its cell. <see cref="KnobWidget.KeepRound"/>
    /// forces a true circle — the drawer sets it on the focused knob so the knob being
    /// adjusted never distorts while its neighbours warp to absorb the growth.
    /// </summary>
    public class KnobDisplay : KnobWidget
    {
        /// <summary>Base stroke width, before <see cref="StrokeScale"/>. Shared
        /// with <see cref="PadCell"/> so every stroked circle matches.</summary>
        public const float StrokeWidth = 2f;

        // The ring spans 270° with its gap at the bottom. In UI coordinates y
        // grows downward, so 135° is bottom-left, 270° is straight up, and
        // 405° (= 45°) is bottom-right.
        const float ArcStartDeg = 135f;
        const float ArcSweepDeg = 270f;

        const int   DotCount       = 22;      // ~30% fewer than the original 31
        const float DotRadiusFrac  = 0.030f;  // uniform, as a fraction of knob size
        const float RingGapFrac    = 0.055f;  // ring dots → knob body
        const float PointerRadFrac = 0.075f;  // pointer dot radius
        const float PointerPosFrac = 0.62f;   // pointer distance, as a fraction of body radius

        float _strokeScale = 1f;

        /// <summary>Multiplier on <see cref="StrokeWidth"/>, from the drawer's
        /// global stroke weight. Everything else stays proportional to the
        /// element's own size, so only the line thickness changes.</summary>
        public override float StrokeScale
        {
            get => _strokeScale;
            set
            {
                if (Mathf.Approximately(_strokeScale, value)) return;
                _strokeScale = value;
                MarkDirtyRepaint();
            }
        }

        Color _tickOnColor  = Color.white;
        Color _tickOffColor = new Color(0.22f, 0.22f, 0.24f);
        Color _outlineColor = new Color(0.55f, 0.55f, 0.60f);

        /// <summary>Ink for lit dots and the pointer, for spent dots, and for the
        /// knob body outline and ± marks. Set from the drawer's theme palette.</summary>
        public override void SetInk(Color on, Color off, Color outline)
        {
            if (_tickOnColor == on && _tickOffColor == off && _outlineColor == outline) return;
            _tickOnColor  = on;
            _tickOffColor = off;
            _outlineColor = outline;
            MarkDirtyRepaint();
        }

        float _value;
        public override float Value
        {
            get => _value;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, _value)) return;
                _value = v;
                MarkDirtyRepaint();
            }
        }

        public override bool KeepRound
        {
            get => base.KeepRound;
            set
            {
                if (base.KeepRound == value) return;
                base.KeepRound = value;
                MarkDirtyRepaint();
            }
        }

        public override float NeutralAspect
        {
            get => base.NeutralAspect;
            set
            {
                if (Mathf.Approximately(base.NeutralAspect, value)) return;
                base.NeutralAspect = value;
                MarkDirtyRepaint();
            }
        }

        public KnobDisplay()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;

            var p         = ctx.painter2D;
            var center    = new Vector2(rect.center.x, rect.center.y);
            float stroke  = StrokeWidth * _strokeScale;

            // Map the knob into its cell along two independent axes. It always fills
            // the cell's height; horizontally it fills width / NeutralAspect, so a cell
            // at its resting aspect comes out round and a cell the fisheye has stretched
            // comes out elliptical — continuously, so it warps smoothly with the flex
            // animation and never pops. KeepRound (the focused knob) forces a true
            // circle sized to the shorter axis, so the knob being adjusted grows but
            // never distorts; only its neighbours warp.
            float sizeX, sizeY;
            if (KeepRound)
            {
                sizeX = sizeY = Mathf.Min(rect.width, rect.height);
            }
            else
            {
                sizeY = rect.height;
                sizeX = rect.width / Mathf.Max(0.01f, NeutralAspect);
            }

            // Thickness-like quantities stay uniform (round dots, even gaps, round
            // pointer): base them on the shorter axis so they never balloon on a
            // stretched knob.
            float unit   = Mathf.Min(sizeX, sizeY);
            float rDot   = unit * DotRadiusFrac;
            float rRingX = sizeX * 0.5f - rDot - 1f;
            float rRingY = sizeY * 0.5f - rDot - 1f;
            float rBodyX = rRingX - rDot - unit * RingGapFrac;
            float rBodyY = rRingY - rDot - unit * RingGapFrac;
            if (Mathf.Min(rBodyX, rBodyY) <= stroke) return;   // too small to draw meaningfully

            // Ring of uniform dots, lit up to the current value. The dots stay round
            // but sit on an elliptical ring, so the ring stretches with the cell.
            for (int i = 0; i < DotCount; i++)
            {
                float t = i / (float)(DotCount - 1);
                var pos = PointOnEllipse(center, rRingX, rRingY, ArcStartDeg + t * ArcSweepDeg);

                p.fillColor = t <= _value + 0.0001f ? _tickOnColor : _tickOffColor;
                p.BeginPath();
                p.Arc(pos, rDot, 0f, 360f);
                p.Fill();
            }

            // Knob body — a stroked ellipse (a true circle when round), stretching
            // with the cell like PadCell's.
            p.strokeColor = _outlineColor;
            p.lineWidth   = stroke;
            p.BeginPath();
            AddEllipsePath(p, center, rBodyX, rBodyY);
            p.Stroke();

            // Pointer dot — same angle mapping as the ring, so it always lines
            // up with the last lit dot.
            var pointer = PointOnEllipse(center, rBodyX * PointerPosFrac, rBodyY * PointerPosFrac,
                                         ArcStartDeg + _value * ArcSweepDeg);
            p.fillColor = _tickOnColor;
            p.BeginPath();
            p.Arc(pointer, unit * PointerRadFrac, 0f, 360f);
            p.Fill();
        }

        // Bezier approximation of a quarter circle: control point offset ratio.
        // (Same constant PadCell uses to draw its ellipse.)
        const float Kappa = 0.5522847498f;

        static Vector2 PointOnEllipse(Vector2 center, float radX, float radY, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(center.x + Mathf.Cos(rad) * radX,
                               center.y + Mathf.Sin(rad) * radY);
        }

        // Axis-aligned ellipse via 4 cubic beziers — Painter2D's Arc only draws true
        // circles, so a stretched body needs this. Mirrors PadCell.AddEllipsePath.
        static void AddEllipsePath(Painter2D p, Vector2 c, float rx, float ry)
        {
            float ox = rx * Kappa;
            float oy = ry * Kappa;
            var right  = new Vector2(c.x + rx, c.y);
            var top    = new Vector2(c.x,      c.y - ry);
            var left   = new Vector2(c.x - rx, c.y);
            var bottom = new Vector2(c.x,      c.y + ry);

            p.MoveTo(right);
            p.BezierCurveTo(new Vector2(right.x, right.y - oy),   new Vector2(top.x + ox, top.y),       top);
            p.BezierCurveTo(new Vector2(top.x - ox, top.y),       new Vector2(left.x, left.y - oy),     left);
            p.BezierCurveTo(new Vector2(left.x, left.y + oy),     new Vector2(bottom.x - ox, bottom.y), bottom);
            p.BezierCurveTo(new Vector2(bottom.x + ox, bottom.y), new Vector2(right.x, right.y + oy),   right);
            p.ClosePath();
        }
    }
}
