"""
Thin proxy between Unity and the Claude Messages API.

Unity sends the conversation + tool definitions here. This server adds the
API key, forwards the request to Claude, and returns Claude's response as-is.
The agent loop (running tools, sending tool_result back) stays in Unity.
"""

import os
from contextlib import asynccontextmanager

import anthropic
from dotenv import load_dotenv
from fastapi import FastAPI, Header, HTTPException, Request
from pydantic import BaseModel

import speech
from discovery import DISCOVERY_PORT, local_ipv4_addresses, start_announcer

load_dotenv()

API_KEY = os.environ["ANTHROPIC_API_KEY"]
PROXY_TOKEN = os.environ["PROXY_TOKEN"]  # shared secret so strangers can't use your key
MODEL = os.environ.get("CLAUDE_MODEL", "claude-sonnet-5-5")
MAX_TOKENS = int(os.environ.get("MAX_TOKENS", "1024"))
# Must match the --port uvicorn runs on; headsets use it to reach this server.
PORT = int(os.environ.get("PROXY_PORT", "8000"))
ANNOUNCE = os.environ.get("ANNOUNCE", "1") != "0"  # set ANNOUNCE=0 to turn off auto-discovery

client = anthropic.Anthropic(api_key=API_KEY)


@asynccontextmanager
async def lifespan(_app: FastAPI):
    stop = start_announcer(PROXY_TOKEN, PORT) if ANNOUNCE else None
    if stop:
        print(f"Announcing on UDP {DISCOVERY_PORT} so headsets can find this proxy "
              f"(port {PORT}, addresses: {', '.join(local_ipv4_addresses()) or 'none found'}).")
    ok, why = speech.available()
    print(f"Voice input: loading Whisper '{speech.WHISPER_MODEL}' in the background." if ok else f"Voice input off: {why}")
    speech.warm_up_in_background()
    yield
    if stop:
        stop.set()


app = FastAPI(title="VR Lab Claude Proxy", lifespan=lifespan)


class ChatRequest(BaseModel):
    messages: list[dict]          # full conversation, Messages API format
    tools: list[dict] = []        # tool definitions: name, description, input_schema
    system: str | None = None     # optional system prompt


@app.get("/health")
def health():
    voice, _ = speech.available()
    return {"ok": True, "model": MODEL, "voice": voice}


@app.post("/chat")
def chat(req: ChatRequest, x_proxy_token: str = Header(default="")):
    if x_proxy_token != PROXY_TOKEN:
        raise HTTPException(status_code=401, detail="bad proxy token")

    kwargs = {"model": MODEL, "max_tokens": MAX_TOKENS, "messages": req.messages}
    if req.tools:
        kwargs["tools"] = req.tools
    if req.system:
        kwargs["system"] = req.system

    try:
        response = client.messages.create(**kwargs)
    except anthropic.APIStatusError as e:
        # Pass Claude's error back so Unity can show or log it.
        raise HTTPException(status_code=e.status_code, detail=str(e.message))
    except anthropic.APIConnectionError:
        raise HTTPException(status_code=502, detail="could not reach Claude API")

    # Returns {id, content: [...text / tool_use blocks...], stop_reason, usage, ...}
    return response.model_dump()


@app.post("/transcribe")
async def transcribe(request: Request, x_proxy_token: str = Header(default="")):
    """Turn a short 16-bit WAV recording from the panel's Talk button into text, on this computer."""
    if x_proxy_token != PROXY_TOKEN:
        raise HTTPException(status_code=401, detail="bad proxy token")
    ok, why = speech.available()
    if not ok:
        raise HTTPException(status_code=501, detail=why)
    data = await request.body()
    if len(data) > 2_000_000:
        raise HTTPException(status_code=413, detail="Recording too large.")
    try:
        # Whisper is CPU-heavy: run it off the event loop so /chat keeps answering.
        import anyio
        return await anyio.to_thread.run_sync(speech.transcribe, data)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except RuntimeError as e:
        raise HTTPException(status_code=503, detail=str(e))
