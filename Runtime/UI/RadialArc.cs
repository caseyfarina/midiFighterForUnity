using UnityEngine;
using UnityEngine.UIElements;

namespace MidiFighter64
{
    /// <summary>
    /// Read-only value display shaped as an arc: a faint full-sweep track with a
    /// brighter fill growing along it from the start edge. One primitive covers
    /// every continuous mixer control in the radial layouts — knob rows, channel
    /// faders, and the master ring — which differ only in radius, sweep and stroke.
    ///
    /// Each instance spans the whole radial square and draws a single arc around
    /// its centre, so the elements overlap harmlessly: they are transparent apart
    /// from their own stroke and all ignore picking.
    /// </summary>
    public class RadialArc : VisualElement
    {
        /// <summary>Arc radius in pixels, measured from the element's centre.</summary>
        public float Radius { get; set; }

        /// <summary>Start angle in UI degrees — y grows downward, so 270° is
        /// straight up and increasing angles run clockwise. Matches
        /// <see cref="KnobDisplay"/>'s convention so the two read consistently.</summary>
        public float StartDeg { get; set; }

        /// <summary>Angular length of the track. 360 draws a closed ring.</summary>
        public float SweepDeg { get; set; } = 360f;

        /// <summary>Stroke width before <see cref="StrokeScale"/>, in pixels. Set per
        /// band so the ring stack reads by thickness as well as radius.</summary>
        public float BaseStroke { get; set; } = 2f;

        float _strokeScale = 1f;

        /// <summary>Multiplier on <see cref="BaseStroke"/>, from the drawer's global
        /// stroke weight. Every stroked drawing in the drawer goes through this or
        /// it silently ignores the slider.</summary>
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

        Color _fillColor  = Color.white;
        Color _trackColor = new Color(0.14f, 0.14f, 0.16f);

        /// <summary>Ink for the filled portion and for the unfilled track. Set from
        /// the drawer's theme palette — never a literal at a build site, or a theme
        /// change will skip it.</summary>
        public void SetInk(Color fill, Color track)
        {
            if (_fillColor == fill && _trackColor == track) return;
            _fillColor  = fill;
            _trackColor = track;
            MarkDirtyRepaint();
        }

        float _value;

        /// <summary>Current value, 0–1. Drives how far the fill grows along the track.</summary>
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

        /// <summary>Clamped assignment, for the layout-agnostic event handlers to
        /// call without caring whether a radial layout is even built.</summary>
        public void SetValue(float v) => Value = v;

        public RadialArc()
        {
            pickingMode = PickingMode.Ignore;
            // Fills its parent so every arc shares one coordinate space: the centre
            // of the radial square. Radii are then plain pixel distances from it.
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0 || rect.height <= 0 || Radius <= 0f) return;

            var p       = ctx.painter2D;
            var center  = new Vector2(rect.center.x, rect.center.y);
            float stroke = BaseStroke * _strokeScale;
            if (stroke <= 0f) return;

            p.lineWidth = stroke;
            p.lineCap   = LineCap.Round;

            // Track first, so the fill always paints over it.
            p.strokeColor = _trackColor;
            p.BeginPath();
            p.Arc(center, Radius, StartDeg, StartDeg + SweepDeg);
            p.Stroke();

            // A zero-length fill would still paint a dot through the round cap,
            // which reads as a stuck value rather than an empty one.
            float filled = SweepDeg * _value;
            if (filled <= 0.01f) return;

            p.strokeColor = _fillColor;
            p.BeginPath();
            p.Arc(center, Radius, StartDeg, StartDeg + filled);
            p.Stroke();
        }
    }
}
