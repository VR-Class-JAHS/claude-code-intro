using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ClaudeLab
{
    /// <summary>
    /// A text box and Send button under the panel's reply, for typing any request (Enter also sends).
    /// ClaudeLabPanel adds this at Play time when Show Typed Input is ticked; by default only in the Editor and
    /// desktop builds, since typing in a headset isn't practical.
    /// While the box has focus, the Input System keyboard is paused so typed letters don't also move the
    /// simulated head or press simulated controller buttons (T is the trigger, G the grip, WASD moves).
    /// </summary>
    [RequireComponent(typeof(ClaudeLabPanel))]
    public class TypedPromptInput : MonoBehaviour
    {
        public float rowHeight = 56f;
        public float fontSize = 22f;
        public Color fieldColor = new Color(0.93f, 0.94f, 0.96f);
        public Color fieldTextColor = new Color(0.10f, 0.12f, 0.16f);
        public Color sendColor = new Color(0.20f, 0.36f, 0.62f);

        ClaudeLabPanel _panel;
        TMP_InputField _field;
        Button _send;
        bool _keyboardPaused;

        void Awake()
        {
            _panel = GetComponent<ClaudeLabPanel>();
            BuildRow();
        }

        void OnEnable()
        {
            if (_panel != null && _panel.agent != null) _panel.agent.onBusyChanged.AddListener(OnBusy);
        }

        void OnDisable()
        {
            if (_panel != null && _panel.agent != null) _panel.agent.onBusyChanged.RemoveListener(OnBusy);
            ResumeSimulators();
        }

        void Send()
        {
            string text = _field.text.Trim();
            if (text.Length == 0) return;
            if (_panel.agent != null && _panel.agent.IsBusy) return; // keep the text; Send re-enables when Claude finishes
            _panel.AskText(text);
            _field.text = "";
            _field.ActivateInputField(); // ready for the next question
        }

        void OnBusy(bool busy)
        {
            if (_send != null) _send.interactable = !busy;
        }

        // ---- keeping typed letters away from the XR simulator ----
        // The simulator reads the keyboard through the Input System (T = trigger, G = grip, WASD = move).
        // Turning the simulator itself off would reset the simulated head and controllers, so instead the
        // Input System's keyboard is switched off while the text box has focus. The text box still gets
        // every key: it reads them from Unity's UI event queue, not from the Input System.

        void PauseSimulators()
        {
            if (_keyboardPaused || Keyboard.current == null) return;
            InputSystem.DisableDevice(Keyboard.current);
            _keyboardPaused = true;
        }

        void ResumeSimulators()
        {
            if (!_keyboardPaused) return;
            _keyboardPaused = false;
            if (Keyboard.current != null) InputSystem.EnableDevice(Keyboard.current);
            else
                foreach (var device in InputSystem.devices)
                    if (device is Keyboard && !device.enabled) InputSystem.EnableDevice(device);
        }

        // ---- building the row ----

        void BuildRow()
        {
            var row = new GameObject("Typed Input", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.layer = gameObject.layer;
            row.transform.SetParent(transform, false);
            // Right under the reply box, so the question sits next to the answer.
            Transform replyBox = _panel.replyText != null ? _panel.replyText.transform.parent : null;
            if (replyBox != null && replyBox.parent == transform)
                row.transform.SetSiblingIndex(replyBox.GetSiblingIndex() + 1);
            row.GetComponent<LayoutElement>().preferredHeight = rowHeight;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            _field = BuildField(row.transform);
            _field.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _send = ClaudeLabPanel.CreateButton(row.transform, "Send", sendColor, Color.white, rowHeight, fontSize);
            var sendLayout = _send.GetComponent<LayoutElement>();
            sendLayout.preferredWidth = 110f;
            sendLayout.flexibleWidth = 0f;
            _send.onClick.AddListener(Send);
        }

        TMP_InputField BuildField(Transform parent)
        {
            var go = new GameObject("Text Box", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = fieldColor;

            // Text area with a mask, as TMP_InputField expects: viewport > placeholder + text.
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.layer = go.layer;
            area.transform.SetParent(go.transform, false);
            var areaRt = (RectTransform)area.transform;
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(14, 6);
            areaRt.offsetMax = new Vector2(-14, -6);

            var placeholder = AddText(area.transform, "Placeholder", "Type a question, then press Enter…",
                new Color(fieldTextColor.r, fieldTextColor.g, fieldTextColor.b, 0.5f));
            placeholder.fontStyle = FontStyles.Italic;
            var text = AddText(area.transform, "Text", "", fieldTextColor);

            var field = go.GetComponent<TMP_InputField>();
            field.targetGraphic = go.GetComponent<Image>(); // not set automatically when added from code
            field.textViewport = areaRt;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 300;
            field.onSubmit.AddListener(_ => Send());
            field.onSelect.AddListener(_ => PauseSimulators());
            field.onDeselect.AddListener(_ => ResumeSimulators());
            return field;
        }

        TextMeshProUGUI AddText(Transform parent, string name, string value, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = value;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
