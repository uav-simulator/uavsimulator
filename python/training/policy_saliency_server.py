"""HTTP sidecar server for live saliency: backend MJPEG → saliency PNG.

Runs alongside the .NET backend. Frontend embeds <img src="/saliency?..."> with
a timestamp parameter to refresh at ~2 Hz. Each request fetches the latest
camera snapshot from the backend, runs Grad-style saliency on the bound
model, and returns a side-by-side PNG (orig | heatmap-overlay).

Usage:
    python python/training/policy_saliency_server.py \\
        --port 5288 \\
        --model python/training/artifacts/cardboard-corridor-ppo-v9-rev16/1.0.0/cardboard-corridor-ppo-v9-rev16_sb3.zip \\
        --backend http://127.0.0.1:5287

CORS is enabled so the frontend (any origin) can <img src> the result.
"""
from __future__ import annotations

import argparse
import io
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import numpy as np
import requests
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "python"))

from training.policy_saliency import (  # noqa: E402
    annotate_decision,
    compute_saliency,
    load_sb3_model,
    overlay_heatmap,
)


_MODEL = None
_BACKEND = "http://127.0.0.1:5287"
_IMG_SIZE = 84


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass  # silence default logging

    def _cors(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")

    def do_OPTIONS(self):
        self.send_response(204)
        self._cors()
        self.end_headers()

    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/health":
            self.send_response(200)
            self._cors()
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"ok": true}')
            return
        if url.path != "/saliency":
            self.send_response(404)
            self._cors()
            self.end_headers()
            return

        params = parse_qs(url.query)
        client_id = (params.get("clientId") or ["web"])[0]
        runtime_mode = (params.get("runtimeMode") or ["real-robot"])[0]
        ultra_cm = float((params.get("ultrasonic_cm") or ["50"])[0])
        ultra_norm = float(np.clip(ultra_cm / 100.0 / 5.0, 0.0, 1.0))

        try:
            r = requests.get(
                f"{_BACKEND}/api/camera/snapshot?clientId={client_id}&runtimeMode={runtime_mode}",
                timeout=5,
            )
            r.raise_for_status()
            pil = (
                Image.open(io.BytesIO(r.content))
                .convert("RGB")
                .resize((_IMG_SIZE, _IMG_SIZE), Image.BILINEAR)
            )
            img = np.asarray(pil, dtype=np.uint8)

            chosen, probs, sal = compute_saliency(_MODEL, img, ultra_norm)
            overlay = overlay_heatmap(img, sal)
            side = np.concatenate([img, overlay], axis=1)
            annotated = annotate_decision(side, probs, chosen, ultra_norm)
            buf = io.BytesIO()
            Image.fromarray(annotated).save(buf, format="PNG")
            png = buf.getvalue()

            self.send_response(200)
            self._cors()
            self.send_header("Content-Type", "image/png")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(png)))
            self.end_headers()
            self.wfile.write(png)
        except Exception as e:
            msg = str(e).encode("utf-8")
            self.send_response(500)
            self._cors()
            self.send_header("Content-Type", "text/plain")
            self.end_headers()
            self.wfile.write(msg)


def main() -> int:
    global _MODEL, _BACKEND, _IMG_SIZE
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=5288)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--model", required=True)
    ap.add_argument("--backend", default="http://127.0.0.1:5287")
    args = ap.parse_args()

    print(f"Loading model {args.model}...", flush=True)
    _MODEL = load_sb3_model(Path(args.model).expanduser().resolve())
    img_shape = _MODEL.observation_space.spaces["image"].shape
    _IMG_SIZE = max(img_shape)
    _BACKEND = args.backend.rstrip("/")
    print(f"  image size: {_IMG_SIZE}x{_IMG_SIZE}", flush=True)
    print(f"  backend:    {_BACKEND}", flush=True)
    print(f"Listening on http://{args.host}:{args.port}/saliency", flush=True)

    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.serve_forever()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
