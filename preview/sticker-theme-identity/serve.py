#!/usr/bin/env python3
"""Tiny static server for the sticker preview (preview tool, not part of the app).

python3 -m http.server is enough to look at the page, but it lets the browser
cache stickers.js aggressively, which is annoying while the art is still being
iterated on. This adds `Cache-Control: no-store` to every response.

    python3 serve.py [--port 4173] [--host 0.0.0.0]
"""

from __future__ import annotations

import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(__file__).resolve().parent


class NoCacheHandler(SimpleHTTPRequestHandler):
    extensions_map = {
        **SimpleHTTPRequestHandler.extensions_map,
        ".js": "text/javascript",
        ".png": "image/png",
        ".svg": "image/svg+xml",
    }

    def end_headers(self) -> None:  # noqa: D102 - stdlib hook
        self.send_header("Cache-Control", "no-store, max-age=0")
        super().end_headers()

    def _send_bundle(self) -> None:
        """Write the single-file build as an attachment (the browser saves it)."""
        data = (ROOT / "sticker-preview.html").read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Disposition", 'attachment; filename="In-st-Sanity-sticker-preview.html"')
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self) -> None:  # noqa: D102 - stdlib hook
        parsed = urlparse(self.path)
        # Three ways to reach the download, so a proxy that drops query strings
        # (or a sandboxed iframe that blocks downloads) still has a plain path:
        #   /download        /download/<anything>
        #   /?download=1     any path with ?download=1
        if parsed.path.rstrip("/").endswith("/download") or parsed.path.startswith("/download/"):
            self._send_bundle()
            return
        if "download" in parse_qs(parsed.query):
            self._send_bundle()
            return
        super().do_GET()

    def log_message(self, fmt: str, *args) -> None:  # keep the log short
        print("%s - %s" % (self.address_string(), fmt % args), flush=True)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=4173)
    ap.add_argument("--host", default="0.0.0.0")
    args = ap.parse_args()

    handler = partial(NoCacheHandler, directory=str(ROOT))
    with ThreadingHTTPServer((args.host, args.port), handler) as httpd:
        print(f"sticker preview → http://{args.host}:{args.port}/  (root: {ROOT})", flush=True)
        httpd.serve_forever()


if __name__ == "__main__":
    main()
