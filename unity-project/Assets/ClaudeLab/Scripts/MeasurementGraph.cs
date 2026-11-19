using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ClaudeLab
{
    /// <summary>
    /// The graph area on the panel: a title and status line, the LineGraph, axis labels and a colour key.
    /// ClaudeLabPanel adds this at Play time when Show Graph is ticked. It follows the scene's
    /// StartMeasurementTool and redraws as readings arrive.
    /// </summary>
    [RequireComponent(typeof(ClaudeLabPanel))]
    public class MeasurementGraph : MonoBehaviour
    {
        public float height = 230f;
        public Color backgroundColor = new Color(0.15f, 0.18f, 0.24f, 1f);
        public Color labelColor = new Color(0.72f, 0.76f, 0.82f);

        StartMeasurementTool _source;
        LineGraph _graph;
        TextMeshProUGUI _title, _status, _yMax, _yMin, _xMax, _key;

        void Awake()
        {
            _source = FindFirstObjectByType<StartMeasurementTool>();
            Build();
        }

        void OnEnable()
        {
            if (_source != null) _source.onChanged.AddListener(Refresh);
        }

        void OnDisable()
        {
            if (_source != null) _source.onChanged.RemoveListener(Refresh);
        }

        void Start() => Refresh();

        void Refresh()
        {
            if (_graph == null) return;
            _graph.SetVerticesDirty();
            _graph.ComputeRange(out float yMin, out float yMax, out _);

            if (_source == null)
            {
                _title.text = "Graph";
                _status.text = "No measurement tool in the scene. Run Claude Lab → Add Lab Assistant to Scene.";
                return;
            }

            switch (_source.Status)
            {
                case StartMeasurementTool.State.Idle:
                    _title.text = "Graph";
                    _status.text = "Ask Claude to graph something.";
                    break;
                case StartMeasurementTool.State.WaitingForRelease:
                    _title.text = _source.Title;
                    _status.text = "Waiting for Release…";
                    break;
                case StartMeasurementTool.State.Recording:
                    _title.text = _source.Title;
                    _status.text = $"Recording {_source.Elapsed:0.0} / {_source.Duration:0.#} s";
                    break;
                default:
                    _title.text = _source.Title;
                    _status.text = $"Done: {_source.Elapsed:0.0} s. Ask Claude what the graph shows.";
                    break;
            }

            bool hasData = _source.Status != StartMeasurementTool.State.Idle;
            _yMax.text = hasData ? Format(yMax) : "";
            _yMin.text = hasData ? Format(yMin) : "";
            _xMax.text = hasData ? $"{_source.Duration:0.#} s" : "";
            _key.text = string.Join("   ", _source.AllSeries.Select(s =>
                $"<color=#{ColorUtility.ToHtmlStringRGB(s.color)}>— {s.label}</color>"));
        }

        static string Format(float v) => Mathf.Abs(v) >= 100f ? v.ToString("0") : Mathf.Abs(v) >= 10f ? v.ToString("0.#") : v.ToString("0.##");

        // ---- building ----

        void Build()
        {
            var panel = GetComponent<ClaudeLabPanel>();
            var box = new GameObject("Graph", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            box.layer = gameObject.layer;
            box.transform.SetParent(transform, false);
            Transform replyBox = panel.replyText != null ? panel.replyText.transform.parent : null;
            if (replyBox != null && replyBox.parent == transform)
                box.transform.SetSiblingIndex(replyBox.GetSiblingIndex() + 1);
            var bg = box.GetComponent<Image>();
            bg.color = backgroundColor;
            bg.raycastTarget = false;
            box.GetComponent<LayoutElement>().preferredHeight = height;

            _title = Label(box.transform, "Title", 20f, Color.white, TextAlignmentOptions.TopLeft,
                new Vector2(0, 1), new Vector2(0.55f, 1), new Vector2(14, -34), new Vector2(0, -8));
            _title.fontStyle = FontStyles.Bold;
            _status = Label(box.transform, "Status", 16f, labelColor, TextAlignmentOptions.TopRight,
                new Vector2(0.55f, 1), new Vector2(1, 1), new Vector2(6, -34), new Vector2(-14, -10));
            _status.enableAutoSizing = true; // the idle hint is long
            _status.fontSizeMin = 11f;
            _status.fontSizeMax = 16f;
            _status.textWrappingMode = TextWrappingModes.Normal;

            // Plot area, leaving room for the y labels on the left and the key at the bottom.
            var plot = new GameObject("Plot", typeof(RectTransform), typeof(CanvasRenderer), typeof(LineGraph));
            plot.layer = box.layer;
            plot.transform.SetParent(box.transform, false);
            var prt = (RectTransform)plot.transform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(64, 46);
            prt.offsetMax = new Vector2(-18, -44);
            _graph = plot.GetComponent<LineGraph>();
            _graph.source = _source;
            _graph.raycastTarget = false;

            _yMax = Label(box.transform, "Y Max", 15f, labelColor, TextAlignmentOptions.TopRight,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(4, -64), new Vector2(58, -40));
            _yMin = Label(box.transform, "Y Min", 15f, labelColor, TextAlignmentOptions.BottomRight,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(4, 40), new Vector2(58, 64));
            _xMax = Label(box.transform, "X Max", 15f, labelColor, TextAlignmentOptions.TopRight,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(-120, 22), new Vector2(-18, 44));
            var x0 = Label(box.transform, "X Zero", 15f, labelColor, TextAlignmentOptions.TopLeft,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(60, 22), new Vector2(120, 44));
            x0.text = "0";
            _key = Label(box.transform, "Key", 16f, Color.white, TextAlignmentOptions.BottomLeft,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 4), new Vector2(-130, 24));
            _key.richText = true;
        }

        static TextMeshProUGUI Label(Transform parent, string name, float size, Color color, TextAlignmentOptions align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.text = "";
            return tmp;
        }
    }
}
