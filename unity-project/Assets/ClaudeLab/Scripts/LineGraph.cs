using UnityEngine;
using UnityEngine.UI;

namespace ClaudeLab
{
    /// <summary>
    /// Draws the lines of the current start_measurement recording inside its RectTransform:
    /// axes, a zero line when values go negative, and one line per series.
    /// The x axis runs from 0 to the recording's duration; the y axis fits the readings (always including 0).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class LineGraph : MaskableGraphic
    {
        public StartMeasurementTool source;
        public float lineWidth = 3f;
        public float axisWidth = 1.5f;
        public Color axisColor = new Color(0.55f, 0.60f, 0.68f);

        /// <summary>The y range drawn last, for the axis labels.</summary>
        public float YMin { get; private set; }
        public float YMax { get; private set; } = 1f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;

            ComputeRange(out float yMin, out float yMax, out float xMax);
            YMin = yMin;
            YMax = yMax;

            Vector2 ToLocal(Vector2 p) => new Vector2(
                r.xMin + p.x / xMax * r.width,
                r.yMin + (p.y - yMin) / (yMax - yMin) * r.height);

            // Axes: left edge, and the bottom (or the zero line when readings go below zero).
            Line(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), axisWidth, axisColor);
            float zeroY = yMin < 0f ? ToLocal(new Vector2(0f, 0f)).y : r.yMin;
            Line(vh, new Vector2(r.xMin, zeroY), new Vector2(r.xMax, zeroY), axisWidth, axisColor);

            if (source == null) return;
            foreach (var s in source.AllSeries)
                for (int i = 1; i < s.points.Count; i++)
                    Line(vh, ToLocal(s.points[i - 1]), ToLocal(s.points[i]), lineWidth, s.color);
        }

        /// <summary>The axis range the graph uses for the current readings (also used for the axis labels).</summary>
        public void ComputeRange(out float yMin, out float yMax, out float xMax)
        {
            yMin = 0f;
            yMax = 0f;
            xMax = source != null && source.Duration > 0f ? source.Duration : 1f;
            if (source != null)
                foreach (var s in source.AllSeries)
                    foreach (var p in s.points)
                    {
                        if (p.y < yMin) yMin = p.y;
                        if (p.y > yMax) yMax = p.y;
                        if (p.x > xMax) xMax = p.x;
                    }
            if (yMax - yMin < 1e-3f) yMax = yMin + 1f; // flat or empty: give the axis some height
            float pad = (yMax - yMin) * 0.05f;
            if (yMax > 0f) yMax += pad;
            if (yMin < 0f) yMin -= pad;
        }

        // A straight segment as a thin quad.
        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            int i = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = color;
            v.position = a - n; vh.AddVert(v);
            v.position = a + n; vh.AddVert(v);
            v.position = b + n; vh.AddVert(v);
            v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
