"""
Speech-to-text for the panel's Talk button, run on this computer with an open-source Whisper model
(faster-whisper). Recordings never leave this computer; only the transcribed text goes on to Claude,
the same as a typed question.

The model downloads from Hugging Face the first time (base.en is about 145 MB) and is cached in the
user's .cache\\huggingface folder. Pick another size with WHISPER_MODEL in .env (tiny.en is faster,
small.en more accurate). WHISPER=0 turns voice input off.
"""

import io
import os
import threading
import time
import wave

import numpy as np

# Plain HTTPS downloads from Hugging Face: its newer "Xet" transfer dropped connections on Windows (2026-11-18),
# and Windows can't make the cache's symlinks without Developer Mode, so skip that warning.
os.environ.setdefault("HF_HUB_DISABLE_XET", "1")
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

WHISPER_MODEL = os.environ.get("WHISPER_MODEL", "base.en")
ENABLED = os.environ.get("WHISPER", "1") != "0"
MAX_SECONDS = 20

_model = None
_lock = threading.Lock()


def available() -> tuple[bool, str]:
    """Whether transcription can work here, and why not if it can't."""
    if not ENABLED:
        return False, "Voice input is turned off on the proxy (WHISPER=0 in .env)."
    try:
        import faster_whisper  # noqa: F401
    except ImportError as e:
        return False, (f"faster-whisper isn't installed for the proxy ({e}). In vr-lab-proxy: "
                       r".venv\Scripts\pip install -r requirements.txt (needs Python 3.12 or 3.13).")
    return True, ""


def _get_model():
    """Load the model once; the first call downloads it if needed (can take a minute)."""
    global _model
    with _lock:
        if _model is None:
            from faster_whisper import WhisperModel
            try:
                started = time.time()
                _model = WhisperModel(WHISPER_MODEL, device="cpu", compute_type="int8")
                print(f"Whisper model '{WHISPER_MODEL}' ready in {time.time() - started:.1f} s.")
            except Exception as e:  # download blocked, bad model name, ... The next request tries again.
                raise RuntimeError(f"Couldn't load the Whisper model '{WHISPER_MODEL}': {e}") from e
        return _model


def warm_up_in_background() -> None:
    """Load (and if needed download) the model at startup so the first question isn't slow."""
    ok, _ = available()
    if ok:
        threading.Thread(target=lambda: _safe(_get_model), daemon=True, name="whisper-warmup").start()


def _safe(fn):
    try:
        fn()
    except Exception as e:
        print(e)


def wav_to_audio(data: bytes) -> np.ndarray:
    """16-bit PCM mono WAV -> float32 samples at 16 kHz, as Whisper expects."""
    with wave.open(io.BytesIO(data)) as w:
        if w.getsampwidth() != 2:
            raise ValueError("Expected 16-bit PCM WAV.")
        channels, rate, frames = w.getnchannels(), w.getframerate(), w.getnframes()
        if frames / rate > MAX_SECONDS:
            raise ValueError(f"Recording is longer than {MAX_SECONDS} s.")
        pcm = np.frombuffer(w.readframes(frames), dtype="<i2").astype(np.float32) / 32768.0
    if channels > 1:
        pcm = pcm.reshape(-1, channels).mean(axis=1)
    if rate != 16000:  # simple linear resample; the Unity client already records at 16 kHz
        n = int(len(pcm) * 16000 / rate)
        pcm = np.interp(np.linspace(0, len(pcm) - 1, n), np.arange(len(pcm)), pcm).astype(np.float32)
    return pcm


def transcribe(data: bytes) -> dict:
    audio = wav_to_audio(data)
    if len(audio) < 16000 * 0.3:
        return {"text": "", "seconds": len(audio) / 16000, "note": "too short"}
    started = time.time()
    segments, _info = _get_model().transcribe(audio, language="en", beam_size=1, vad_filter=False)
    text = " ".join(s.text.strip() for s in segments).strip()
    return {"text": text, "seconds": round(len(audio) / 16000, 2), "took_s": round(time.time() - started, 2)}
