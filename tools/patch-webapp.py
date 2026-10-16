#!/usr/bin/env python3
"""Produce the desktop build of the In[st]Sanity web layer from the pristine upload.

The web app was written as a client for a local backend (see
docs/hybrid-port-plan.md section 1.1). On the desktop it is served by the in-app
loopback host, so everything that reached out to third-party proxies must go away.

Verified against the pristine file:
    sha256 369767664003b16b5bc4d3845d81ecabede1751ce856631d76ab58cf743aa69c
    size   2853500 bytes

Usage:
    python tools/patch-webapp.py <pristine-index.html> <wwwroot/index.html>
"""

from __future__ import annotations

import hashlib
import pathlib
import sys

EXPECTED_SHA256 = "369767664003b16b5bc4d3845d81ecabede1751ce856631d76ab58cf743aa69c"

# ── replacements ────────────────────────────────────────────────────────────────
# Each entry: (label, old, new, expected_occurrences)

REPLACEMENTS: list[tuple[str, str, str, int]] = [
    (
        "dead dev tunnel",
        'const SERVER_API_BASE="https://1ddc5d8b8d7a67.lhr.life";',
        'const SERVER_API_BASE="";',
        1,
    ),
    (
        "scraper fallback (single post)",
        "try{if(await resolveViaJina(post,false)&&post.fullResolved&&!needsFullCarouselResolution(post))return}"
        "catch(_){}try{if(await resolveViaMicrolink(post,false))return}catch(_){}",
        "/* porting: third-party scraper fallbacks removed - posts resolve through the local /api/* host */",
        1,
    ),
    (
        "scraper fallback (page summary)",
        "let ok=false;try{ok=await resolveViaJina(post,true)}catch(_){}"
        "if(!ok){try{ok=await resolveViaMicrolink(post,true)}catch(_){}}",
        "const ok=false;/* porting: third-party scraper fallback removed */",
        1,
    ),
    # Image URLs are proxied by the local host instead of wsrv.nl. The function is a
    # single line, so it is replaced line-wise (see apply_line_replacements).
    (
        "image proxy function",
        "function wrapProxiedImageUrl(rawUrl,isThumb=false){",
        "function wrapProxiedImageUrl(rawUrl,isThumb=false){"
        "if(!rawUrl)return '';"
        "if(rawUrl.startsWith('data:'))return rawUrl;"
        "if(rawUrl.startsWith('https://wsrv.nl/')){"          # unwrap previously proxied URLs
        "const m=rawUrl.match(/[?&]url=([^&]+)/);"
        "rawUrl=m?decodeURIComponent(m[1]):rawUrl}"
        "return `/api/image?url=${encodeURIComponent(rawUrl)}`}",
        1,
    ),
    (
        "remaining image proxy URLs",
        "https://wsrv.nl/?url=",
        "/api/image?url=",
        2,  # detail view fallback + built-in browser thumbnails
    ),
    (
        "preconnect hints for the removed proxy",
        '<link rel="preconnect" href="https://wsrv.nl" crossorigin>'
        '<link rel="dns-prefetch" href="https://wsrv.nl">',
        "",
        1,
    ),
]

# Strings that must no longer be *reached*. The two scraper helpers stay defined but
# uncalled (their URL templates are inert string literals), which the call-site checks
# below prove.
FORBIDDEN_CALL_PATTERNS = [
    ("await resolveViaJina(", 0),
    ("await resolveViaMicrolink(", 0),
    ("lhr.life", 0),
]

ALLOWED_RESIDUE = [
    # (1) sourceKindFromUrl() classifies URL provenance; it never performs a request.
    # (2) the unwrap check in the rewritten wrapProxiedImageUrl(), which recognises an
    #     already-proxied URL so previously saved state keeps working offline.
    ("wsrv.nl", 2),
    # inert templates inside the uncalled resolveViaMicrolink()/resolveViaJina() bodies.
    ("api.microlink.io", 2),
    ("r.jina.ai", 2),
]


def apply_line_replacements(text: str) -> tuple[str, list[str]]:
    """Replace the whole line that declares wrapProxiedImageUrl."""
    notes: list[str] = []
    lines = text.split("\n")
    hits = 0
    for i, line in enumerate(lines):
        if "function wrapProxiedImageUrl(" in line:
            new_def = REPLACEMENTS[3][2]
            if not line.startswith("function wrapProxiedImageUrl("):
                raise SystemExit("! wrapProxiedImageUrl is not at the start of its line - patch manually")
            lines[i] = new_def
            hits += 1
    if hits != 1:
        raise SystemExit(f"! expected exactly 1 wrapProxiedImageUrl definition, found {hits}")
    notes.append(f"  wrapProxiedImageUrl rewritten ({hits} line)")
    return "\n".join(lines), notes


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    source, destination = (pathlib.Path(p) for p in sys.argv[1:3])
    text = source.read_text(encoding="utf-8", errors="surrogateescape")

    digest = hashlib.sha256(text.encode("utf-8", errors="surrogateescape")).hexdigest()
    if digest != EXPECTED_SHA256:
        print(f"! input sha256 {digest}")
        print(f"  expected      {EXPECTED_SHA256}")
        print("  This is not the pristine upload; refusing to patch. Re-check the file or update the constant.")
        return 1

    print(f"input : {source}  ({len(text)} chars, sha256 ok)")

    # 1. literal replacements that are independent of the image-proxy rewrite
    for label, old, new, expected in (REPLACEMENTS[0], REPLACEMENTS[1], REPLACEMENTS[2]):
        found = text.count(old)
        if found != expected:
            print(f"! {label}: expected {expected} occurrence(s), found {found}")
            return 1
        text = text.replace(old, new)
        print(f"  ok  {label} ({found})")

    # 2. rewrite the image-proxy helper (line based) so that the remaining
    #    wsrv.nl literals are only the two call-site fallbacks
    text, notes = apply_line_replacements(text)
    for note in notes:
        print(note)

    # 3. sweep the remaining proxy literals + head hints
    for label, old, new, expected in (REPLACEMENTS[4], REPLACEMENTS[5]):
        found = text.count(old)
        if found != expected:
            print(f"! {label}: expected {expected} occurrence(s), found {found}")
            return 1
        text = text.replace(old, new)
        print(f"  ok  {label} ({found})")

    for pattern, expected in FORBIDDEN_CALL_PATTERNS:
        found = text.count(pattern)
        if found != expected:
            print(f"! '{pattern}' still occurs {found} time(s), expected {expected}")
            return 1
        print(f"  ok  no '{pattern}'")

    for pattern, expected in ALLOWED_RESIDUE:
        found = text.count(pattern)
        if found != expected:
            print(f"! '{pattern}' occurs {found} time(s), expected {expected} (classification string only)")
            return 1
        print(f"  ok  '{pattern}' residue limited to {found} (no request performed)")

    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(text, encoding="utf-8", errors="surrogateescape")

    out_digest = hashlib.sha256(text.encode("utf-8", errors="surrogateescape")).hexdigest()
    print(f"output: {destination}  ({len(text)} chars)")
    print(f"        sha256 {out_digest}")
    print(f"        {len(REPLACEMENTS)} transformations, all call sites verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
