"""
Thin proxy between Unity and the Claude Messages API.

Unity sends the conversation + tool definitions here. This server adds the
API key, forwards the request to Claude, and returns Claude's response as-is.
The agent loop (running tools, sending tool_result back) stays in Unity.
"""

import os

import anthropic
from dotenv import load_dotenv
from fastapi import FastAPI, Header, HTTPException
from pydantic import BaseModel

load_dotenv()

API_KEY = os.environ["ANTHROPIC_API_KEY"]
PROXY_TOKEN = os.environ["PROXY_TOKEN"]  # shared secret so strangers can't use your key
MODEL = os.environ.get("CLAUDE_MODEL", "claude-sonnet-5-5")
MAX_TOKENS = int(os.environ.get("MAX_TOKENS", "1024"))

client = anthropic.Anthropic(api_key=API_KEY)
app = FastAPI(title="VR Lab Claude Proxy")


class ChatRequest(BaseModel):
    messages: list[dict]          # full conversation, Messages API format
    tools: list[dict] = []        # tool definitions: name, description, input_schema
    system: str | None = None     # optional system prompt


@app.get("/health")
def health():
    return {"ok": True, "model": MODEL}


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
