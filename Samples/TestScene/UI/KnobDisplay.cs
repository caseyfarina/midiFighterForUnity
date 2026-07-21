using UnityEngine;
using UnityEngine.UIElements;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Read-only value display shaped like a hardware synth knob: a ring of
    /// uniform tick dots on a 270° arc, a stroked knob body inside it, and a
    /// filled pointer dot on the body that rotates to the current value. Dots
    /// at or below the value take the "on" ink, the rest the "off" ink, so the
    /// value reads at a glance from either the pointer or the ring.
    /// </summary>
    public class KnobDisplay : VisualElement
    {
        /// <summary>Base stroke width, before <see cref="StrokeScale"/>. Shared
        /// with <see cref="PadCell"/> so every stroked circle matches.</summary>
        public const float StrokeWidth = 2f;

        // The ring spans 270° with its gap at the bottom. In UI coordinates y
        // grows downward, so 135° is bottom-left, 270° is straight up, and
        // 405° (= 45°) is bottom-right.
        const float ArcStartDeg = 135f;
        const float ArcSweepDeg = 270f;

        const int   DotCount       = 31;
        const float DotRadiusFrac  = 0.030f;  // uniform, as a fraction of knob size
        const float RingGapFrac    = 0.055f;  // ring dots → knob body
        const float PointerRadFrac = 0.075f;  // pointer dot radius
        const float PointerPosFrac = 0.62f;   // pointer distance, as a fraction of body radius

        float _strokeScale = 1f;

        /// <summary>Multiplier on <see cref="StrokeWidth"/>, from the drawer's
        /// global stroke weight. Everything else stays proportional to the
        /// element's own size, so only the line thickness changes.</summary>
        public float StrokeScale
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
        public void SetInk(Color on, Color off, Color outline)
        {
            if (_tickOnColor == on && _tickOffColor == off && _outlineColor == outline) return;
            _tickOnColor  = on;
            _tickOffColor = off;
            _outlineColor = outline;
            MarkDirtyRepaint();
        }

        float _value;
        public float Value
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
            float size    = Mathf.Min(rect.width, rect.height);
            var center    = new Vector2(rect.center.x, rect.center.y);
            float stroke  = StrokeWidth * _strokeScale;
            float rDot    = size * DotRadiusFrac;
            float rRing   = size * 0.5f - rDot - 1f;
            float rBody   = rRing - rDot - size * RingGapFrac;
            if (rBody <= stroke) return;   // too small to draw meaningfully

            // Ring of uniform dots, lit up to the current value.
            for (int i = 0; i < DotCount; i++)
            {
                float t = i / (float)(DotCount - 1);
                var pos = PointOnArc(center, rRing, ArcStartDeg + t * ArcSweepDeg);

                p.fillColor = t <= _value + 0.0001f ? _tickOnColor : _tickOffColor;
                p.BeginPath();
                p.Arc(pos, rDot, 0f, 360f);
                p.Fill();
            }

            // Knob body.
            p.strokeColor = _outlineColor;
            p.lineWidth   = stroke;
            p.BeginPath();
            p.Arc(center, rBody, 0f, 360f);
            p.Stroke();

            // Pointer dot — same angle mapping as the ring, so it always lines
            // up with the last lit dot.
            var pointer = PointOnArc(center, rBody * PointerPosFrac,
                                     ArcStartDeg + _value * ArcSweepDeg);
            p.fillColor = _tickOnColor;
            p.BeginPath();
            p.Arc(pointer, size * PointerRadFrac, 0f, 360f);
            p.Fill();
        }

        static Vector2 PointOnArc(Vector2 center, float radius, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(center.x + Mathf.Cos(rad) * radius,
                               center.y + Mathf.Sin(rad) * radius);
        }
    }
}
