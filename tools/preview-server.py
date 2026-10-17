#!/usr/bin/env python3
"""Local preview of the app UI, for machines where the Windows build cannot run.

The desktop app serves its UI from the in-process LoopbackHost (see
src/InStSanity/Services/WebHost/LoopbackHost.cs). This script reproduces that
contract with Python's standard library so the web layer can be looked at in any
browser:

    /                 the ported web layer  (Resources/Raw/wwwroot/index.html)
    /mockup.html      the earlier static mock-up of the native shell (preview/index.html)
    /__health         the same diagnostic shape LoopbackHost returns
    /api/*            501 {"ok":false,...} exactly as LoopbackHost answers an
                      unimplemented route, so the UI takes its normal fallback paths

It is a preview tool, not a port of the backend: no media is proxied, no post is
resolved, nothing is written to disk. Range requests are supported because the web
layer plays video/audio.

Usage:
    python tools/preview-server.py [--port 8080] [--host 0.0.0.0]
"""

from __future__ import annotations

import argparse
import json
import mimetypes
import os
import re
import sys
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

REPO = Path(__file__).resolve().parent.parent
WEB_ROOT = REPO / "src" / "InStSanity" / "Resources" / "Raw" / "wwwroot"
MOCKUP = REPO / "preview" / "index.html"

# Spelled out rather than left to the platform mime database: Chromium is strict about
# scripts and stylesheets, and mimetypes differs between Windows and Linux installs.
MIME_TYPES = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".txt": "text/plain; charset=utf-8",
    ".svg": "image/svg+xml",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif": "image/gif",
    ".webp": "image/webp",
    ".mp4": "video/mp4",
    ".webm": "video/webm",
    ".mp3": "audio/mpeg",
    ".woff2": "font/woff2",
}

RANGE_PATTERN = re.compile(r"^bytes=(\d*)-(\d*)$")


def content_type(path: Path) -> str:
    explicit = MIME_TYPES.get(path.suffix.lower())
    if explicit:
        return explicit
    guessed, _ = mimetypes.guess_type(path.name)
    return guessed or "application/octet-stream"


class PreviewHandler(BaseHTTPRequestHandler):
    server_version = "InStSanityPreview/1.0"
    protocol_version = "HTTP/1.1"

    # ── routing ────────────────────────────────────────────────────────────────
    def do_HEAD(self) -> None:  # noqa: N802 - http.server naming
        self.handle_request(send_body=False)

    def do_GET(self) -> None:  # noqa: N802
        self.handle_request(send_body=True)

    def handle_request(self, send_body: bool) -> None:
        route = urlparse(self.path).path

        if route == "/__health":
            self.send_json(
                {
                    "ok": True,
                    "mode": "preview",
                    "webRoot": str(WEB_ROOT),
                    "webRootExists": (WEB_ROOT / "index.html").is_file(),
                    "mockup": MOCKUP.is_file(),
                    "endpoints": ["/api/image", "/api/download-photo"],
                },
                send_body=send_body,
            )
            return

        if route.startswith("/api/"):
            # Mirrors LoopbackHost: unimplemented routes answer 501 in this shape.
            self.send_json(
                {"ok": False, "error": "not_implemented", "route": route},
                status=HTTPStatus.NOT_IMPLEMENTED,
                send_body=send_body,
            )
            return

        if route in ("/", "/index.html"):
            self.send_file(WEB_ROOT / "index.html", send_body=send_body)
            return

        if route in ("/mockup.html", "/mockup"):
            self.send_file(MOCKUP, send_body=send_body)
            return

        # Anything else is looked up next to the web app, with traversal protection.
        candidate = (WEB_ROOT / route.lstrip("/")).resolve()
        try:
            candidate.relative_to(WEB_ROOT.resolve())
        except ValueError:
            self.send_error(HTTPStatus.FORBIDDEN, "Forbidden")
            return

        self.send_file(candidate, send_body=send_body)

    # ── responses ──────────────────────────────────────────────────────────────
    def send_json(self, payload: dict, status: HTTPStatus = HTTPStatus.OK, send_body: bool = True) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if send_body:
            self.wfile.write(body)

    def send_file(self, path: Path, send_body: bool = True) -> None:
        if not path.is_file():
            self.send_json(
                {"ok": False, "error": "not_found", "path": str(path)},
                status=HTTPStatus.NOT_FOUND,
                send_body=send_body,
            )
            return

        try:
            payload = path.read_bytes()
        except OSError as error:
            self.send_json(
                {"ok": False, "error": f"read_failed: {error}"},
                status=HTTPStatus.INTERNAL_SERVER_ERROR,
                send_body=send_body,
            )
            return

        status, body, headers = HTTPStatus.OK, payload, {}
        requested_range = self.headers.get("Range", "")
        parsed = RANGE_PATTERN.match(requested_range.strip())

        if parsed and payload:
            start_text, end_text = parsed.groups()
            total = len(payload)

            if start_text:
                start = int(start_text)
                end = int(end_text) if end_text else total - 1
            else:
                # Suffix range: the last N bytes.
                suffix = int(end_text or 0)
                start = max(0, total - suffix)
                end = total - 1

            if 0 <= start <= end < total:
                status = HTTPStatus.PARTIAL_CONTENT
                body = payload[start : end + 1]
                headers["Content-Range"] = f"bytes {start}-{end}/{total}"

        self.send_response(status)
        self.send_header("Content-Type", content_type(path))
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header(
            "Cache-Control",
            "no-cache" if path.name == "index.html" else "public, max-age=3600",
        )
        for name, value in headers.items():
            self.send_header(name, value)
        self.end_headers()

        if send_body:
            self.wfile.write(body)

    def log_message(self, fmt: str, *args) -> None:
        sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8080)
    parser.add_argument("--host", default="0.0.0.0", help="0.0.0.0 so the preview proxy can reach it")
    options = parser.parse_args()

    if not (WEB_ROOT / "index.html").is_file():
        print(f"! web layer not found at {WEB_ROOT / 'index.html'}", file=sys.stderr)
        return 1

    server = ThreadingHTTPServer((options.host, options.port), PreviewHandler)
    server.daemon_threads = True

    print(f"web layer : {WEB_ROOT / 'index.html'} ({(WEB_ROOT / 'index.html').stat().st_size:,} bytes)")
    print(f"mock-up   : {MOCKUP} ({'present' if MOCKUP.is_file() else 'MISSING'})")
    print(f"listening : http://{options.host}:{options.port}/  (mock-up at /mockup.html)")
    print(f"pid       : {os.getpid()}")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nstopping")
    finally:
        server.server_close()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
