using UnityEngine;
using UnityEngine.UIElements;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Read-only value display shaped like a synth knob: N tick dots on a
    /// 270° arc whose radii ramp from small (min end) to large (max end),
    /// with an inner value arc that grows to the current value.
    /// </summary>
    public class KnobDisplay : VisualElement
    {
        public const float StrokeWidth = 2f;
        const int TickCount = 12;
        const float ArcStartDeg = 135f;
        const float ArcSweepDeg = 270f;

        // Each tick's radius ramps from small (min end) to large (max end).
        // Color is white for ticks at/below the current value, dark grey above.
        const float TickMinFrac = 0.03f;   // radius as fraction of knob size
        const float TickMaxFrac = 0.08f;

        // Value arc, drawn just inside the tick ring, grows from ArcStartDeg to
        // (ArcStartDeg + value * ArcSweepDeg).
        const float ValueArcInsetFrac = 0.04f;  // gap between ticks and arc
        const float ValueArcWidth     = 1.5f;

        static readonly Color TickOnColor  = Color.white;
        static readonly Color TickOffColor = new Color(0.22f, 0.22f, 0.24f);
        static readonly Color ArcColor     = Color.white;

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

            var p = ctx.painter2D;
            float size    = Mathf.Min(rect.width, rect.height);
            var center    = new Vector2(rect.center.x, rect.center.y);
            float rMinDot = size * TickMinFrac;
            float rMaxDot = size * TickMaxFrac;
            float rTickRing = size * 0.5f - rMaxDot - 1f;

            // Tick dots along the outer arc
            for (int i = 0; i < TickCount; i++)
            {
                float t = i / (float)(TickCount - 1);
                float angRad = (ArcStartDeg + t * ArcSweepDeg) * Mathf.Deg2Rad;
                var pos = new Vector2(
                    center.x + Mathf.Cos(angRad) * rTickRing,
                    center.y + Mathf.Sin(angRad) * rTickRing);

                float dotRadius = Mathf.Lerp(rMinDot, rMaxDot, t);
                Color c = t <= _value + 0.0001f ? TickOnColor : TickOffColor;

                p.fillColor = c;
                p.BeginPath();
                p.Arc(pos, dotRadius, 0f, 360f);
                p.Fill();
            }

            // Value arc — grows from start angle to current value, drawn just
            // inside the tick ring.
            if (_value > 0.0001f)
            {
                float rArc = rTickRing - rMaxDot - size * ValueArcInsetFrac;
                if (rArc > ValueArcWidth)
                {
                    float endDeg = ArcStartDeg + _value * ArcSweepDeg;
                    p.strokeColor = ArcColor;
                    p.lineWidth = ValueArcWidth;
                    p.BeginPath();
                    p.Arc(center, rArc, ArcStartDeg, endDeg);
                    p.Stroke();
                }
            }
        }
    }
}
