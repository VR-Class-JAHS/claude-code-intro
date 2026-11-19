using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ClaudeLab
{
    /// <summary>
    /// An "Exit lab" button beside "New conversation" at the bottom of the panel. It asks for a second press
    /// within a few seconds, so a stray click doesn't close the app, then quits: back to the Quest's home,
    /// or out of Play mode in the Editor.
    /// ClaudeLabPanel adds this at Play time when Show Exit Button is ticked.
    /// </summary>
    [RequireComponent(typeof(ClaudeLabPanel))]
    public class ExitButton : MonoBehaviour
    {
        public Color color = new Color(0.55f, 0.22f, 0.22f);
        public Color confirmColor = new Color(0.85f, 0.25f, 0.25f);
        [Tooltip("Seconds the second press has to come within.")]
        public float confirmSeconds = 3f;

        const string Label = "Exit lab";

        Button _button;
        Image _image;
        TextMeshProUGUI _label;
        Coroutine _confirming;

        void Awake()
        {
            var panel = GetComponent<ClaudeLabPanel>();
            var newConversation = panel.newConversationButton;
            float height = 48f;
            if (newConversation != null && newConversation.TryGetComponent<LayoutElement>(out var le) && le.preferredHeight > 0f)
                height = le.preferredHeight;

            _button = ClaudeLabPanel.CreateButton(transform, Label, color, Color.white, height, 22f);
            _button.name = "Exit Lab";
            _image = _button.GetComponent<Image>();
            _label = _button.GetComponentInChildren<TextMeshProUGUI>();
            _button.onClick.AddListener(OnPressed);

            // Share a row with "New conversation" if it's there; otherwise the button sits on its own row.
            if (newConversation != null && newConversation.transform.parent == transform)
            {
                var row = new GameObject("Bottom Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.layer = gameObject.layer;
                row.transform.SetParent(transform, false);
                row.transform.SetSiblingIndex(newConversation.transform.GetSiblingIndex());
                row.GetComponent<LayoutElement>().preferredHeight = height;
                var h = row.GetComponent<HorizontalLayoutGroup>();
                h.spacing = 10f;
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandWidth = true;
                h.childForceExpandHeight = true;
                newConversation.transform.SetParent(row.transform, false);
                _button.transform.SetParent(row.transform, false);
                newConversation.GetComponent<LayoutElement>().flexibleWidth = 2f; // the more common button gets more room
                _button.GetComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        void OnPressed()
        {
            if (_confirming == null)
            {
                _confirming = StartCoroutine(Confirm());
                return;
            }
            Quit();
        }

        IEnumerator Confirm()
        {
            _label.text = "Press again to exit";
            _image.color = confirmColor;
            yield return new WaitForSeconds(confirmSeconds);
            _label.text = Label;
            _image.color = color;
            _confirming = null;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
