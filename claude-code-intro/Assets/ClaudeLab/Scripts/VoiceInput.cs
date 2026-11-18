using System;
using System.Collections;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace ClaudeLab
{
    /// <summary>
    /// A "Hold to talk" button on the panel. While held, it records from the headset's (or computer's) microphone;
    /// on release it sends the recording to the proxy's /transcribe, which turns it into text on that computer
    /// with Whisper, and the text goes to Claude like a typed question. Recordings stay on the proxy computer.
    /// ClaudeLabPanel adds this at Play time when Show Voice Input is ticked.
    /// </summary>
    [RequireComponent(typeof(ClaudeLabPanel))]
    public class VoiceInput : MonoBehaviour
    {
        public float buttonHeight = 64f;
        public float fontSize = 24f;
        public Color idleColor = new Color(0.45f, 0.30f, 0.65f);
        public Color listeningColor = new Color(0.80f, 0.25f, 0.25f);
        [Tooltip("Longest recording, in seconds.")]
        [Range(3, 20)] public int maxSeconds = 12;

        const int SampleRate = 16000;
        const string IdleLabel = "Hold to talk";

        ClaudeLabPanel _panel;
        Button _button;
        Image _image;
        TextMeshProUGUI _label;
        string _device;
        AudioClip _clip;
        bool _recording;
        bool _sending;

        void Awake()
        {
            _panel = GetComponent<ClaudeLabPanel>();
            Build();
        }

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        void OnEnable()
        {
            if (_panel.agent != null) _panel.agent.onBusyChanged.AddListener(OnBusy);
        }

        void OnDisable()
        {
            if (_panel.agent != null) _panel.agent.onBusyChanged.RemoveListener(OnBusy);
            if (_recording) Microphone.End(_device);
            _recording = false;
        }

        void OnBusy(bool busy) => Refresh();

        void Refresh()
        {
            bool busy = _panel.agent != null && _panel.agent.IsBusy;
            _button.interactable = _recording || (!busy && !_sending);
            _image.color = _recording ? listeningColor : idleColor;
            if (!_recording && !_sending) _label.text = IdleLabel;
        }

        // ---- recording ----

        void BeginRecording()
        {
            if (_recording || _sending || (_panel.agent != null && _panel.agent.IsBusy)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                _panel.ShowStatus("Allow microphone access, then hold the button again.");
                return;
            }
#endif
            if (Microphone.devices.Length == 0)
            {
                _panel.ShowStatus("No microphone found.");
                return;
            }
            _device = Microphone.devices[0];
            _clip = Microphone.Start(_device, false, maxSeconds, SampleRate);
            _recording = true;
            _label.text = "Listening… let go to send";
            Refresh();
        }

        void EndRecording()
        {
            if (!_recording) return;
            int samples = Microphone.GetPosition(_device);
            Microphone.End(_device);
            _recording = false;

            if (_clip == null || samples < SampleRate * 0.3f)
            {
                _panel.ShowStatus("That was too short. Hold the button while you talk.");
                Refresh();
                return;
            }
            var data = new float[samples * _clip.channels];
            _clip.GetData(data, 0);
            StartCoroutine(Send(ToWav(data, _clip.channels, _clip.frequency)));
        }

        IEnumerator Send(byte[] wav)
        {
            var agent = _panel.agent;
            var settings = agent != null ? agent.Settings : null;
            if (agent == null || settings == null || string.IsNullOrEmpty(agent.ProxyUrl))
            {
                _panel.ShowStatus("No proxy settings, so the recording can't be sent.");
                yield break;
            }

            _sending = true;
            _label.text = "Turning speech into text…";
            Refresh();

            string url = agent.ProxyUrl.TrimEnd('/') + "/transcribe";
            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(wav);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "audio/wav");
            req.SetRequestHeader("X-Proxy-Token", settings.proxyToken);
            req.timeout = 60; // the first request may wait for the model to finish loading
            yield return req.SendWebRequest();

            _sending = false;
            Refresh();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string detail = "";
                try { detail = (string)JObject.Parse(req.downloadHandler.text)["detail"] ?? ""; } catch (Exception) { }
                _panel.ShowStatus(req.responseCode == 501
                    ? $"Voice input isn't set up on the proxy. {detail}"
                    : $"Couldn't turn speech into text ({req.responseCode} {req.error}). {detail}");
                yield break;
            }

            string text = "";
            try { text = ((string)JObject.Parse(req.downloadHandler.text)["text"] ?? "").Trim(); }
            catch (Exception) { }
            if (text.Length == 0)
            {
                _panel.ShowStatus("Didn't catch that. Try again a little closer to the mic.");
                yield break;
            }
            _panel.AskText(text);
        }

        static byte[] ToWav(float[] samples, int channels, int rate)
        {
            using var ms = new MemoryStream(44 + samples.Length * 2);
            using var w = new BinaryWriter(ms);
            int dataBytes = samples.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);                    // fmt chunk size
            w.Write((short)1);              // PCM
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);   // byte rate
            w.Write((short)(channels * 2)); // block align
            w.Write((short)16);             // bits per sample
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            foreach (float s in samples)
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), short.MinValue, short.MaxValue));
            w.Flush();
            return ms.ToArray();
        }

        // ---- building ----

        void Build()
        {
            _button = ClaudeLabPanel.CreateButton(transform, IdleLabel, idleColor, Color.white, buttonHeight, fontSize);
            _button.name = "Hold To Talk";
            _image = _button.GetComponent<Image>();
            _label = _button.GetComponentInChildren<TextMeshProUGUI>();
            // Right under the reply box, like the other input rows.
            Transform replyBox = _panel.replyText != null ? _panel.replyText.transform.parent : null;
            if (replyBox != null && replyBox.parent == transform)
                _button.transform.SetSiblingIndex(replyBox.GetSiblingIndex() + 1);

            // Press and hold, not click: record while the trigger, mouse button or pinch is held down.
            var hold = _button.gameObject.AddComponent<HoldHandler>();
            hold.onDown = BeginRecording;
            hold.onUp = EndRecording;
        }

        class HoldHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public Action onDown, onUp;
            public void OnPointerDown(PointerEventData e) => onDown?.Invoke();
            public void OnPointerUp(PointerEventData e) => onUp?.Invoke();
        }
    }
}
