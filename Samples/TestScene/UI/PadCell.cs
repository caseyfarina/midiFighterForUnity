using UnityEngine;
using UnityEngine.UIElements;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Universal button/toggle indicator: empty stroked circle (same line weight
    /// as <see cref="KnobDisplay"/>), with an inner filled circle when active.
    ///   Toggle mode → inner circle at 2/3 radius when Active is true.
    ///   Button mode → inner circle at 1/5 radius while Active is true.
    /// </summary>
    public class PadCell : VisualElement
    {
        public enum Mode { Toggle, Button }

        static readonly Color StrokeColor = new Color(0.55f, 0.55f, 0.60f);

        Mode _mode = Mode.Toggle;
        bool _active;
        Color _fillColor = Color.white;

        public Mode CellMode
        {
            get => _mode;
            set { if (_mode != value) { _mode = value; MarkDirtyRepaint(); } }
        }

        public bool Active
        {
            get => _active;
            set { if (_active != value) { _active = value; MarkDirtyRepaint(); } }
        }

        public Color FillColor
        {
            get => _fillColor;
            set { if (_fillColor != value) { _fillColor = value; MarkDirtyRepaint(); } }
        }

        public PadCell()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        // Bezier approximation of a quarter circle: control point offset ratio.
        const float Kappa = 0.5522847498f;

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;

            var p = ctx.painter2D;
            float stroke = KnobDisplay.StrokeWidth;
            var center = new Vector2(rect.center.x, rect.center.y);
            float rx = rect.width  * 0.5f - stroke;
            float ry = rect.height * 0.5f - stroke;
            if (rx <= 0 || ry <= 0) return;

            // Outer stroked ellipse — stretches with the cell.
            p.strokeColor = StrokeColor;
            p.lineWidth = stroke;
            p.BeginPath();
            AddEllipsePath(p, center, rx, ry);
            p.Stroke();

            if (!_active) return;

            // Inner filled ellipse when active.
            float factor = _mode == Mode.Toggle ? (2f / 3f) : (1f / 5f);
            p.fillColor = _fillColor;
            p.BeginPath();
            AddEllipsePath(p, center, rx * factor, ry * factor);
            p.Fill();
        }

        // Draws an axis-aligned ellipse using 4 cubic beziers.
        static void AddEllipsePath(Painter2D p, Vector2 c, float rx, float ry)
        {
            float ox = rx * Kappa;
            float oy = ry * Kappa;
            var right  = new Vector2(c.x + rx, c.y);
            var top    = new Vector2(c.x,      c.y - ry);
            var left   = new Vector2(c.x - rx, c.y);
            var bottom = new Vector2(c.x,      c.y + ry);

            p.MoveTo(right);
            p.BezierCurveTo(new Vector2(right.x, right.y - oy), new Vector2(top.x + ox, top.y),    top);
            p.BezierCurveTo(new Vector2(top.x - ox, top.y),     new Vector2(left.x, left.y - oy),  left);
            p.BezierCurveTo(new Vector2(left.x, left.y + oy),   new Vector2(bottom.x - ox, bottom.y), bottom);
            p.BezierCurveTo(new Vector2(bottom.x + ox, bottom.y), new Vector2(right.x, right.y + oy), right);
            p.ClosePath();
        }
    }
}
