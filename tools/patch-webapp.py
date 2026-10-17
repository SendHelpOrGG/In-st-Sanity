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

# ── audio fix: let the loading-screen score's reverb ring out ──────────────────
# The loading screens fade out at ~4.1s (v5 at 4.4s) while their score's last note lands at
# 5.8-6.7s, and the halls behind them (setSpace(0.7 .. 5.0s), plus the 3.4s laugh hall every
# variant feeds) keep sounding for another 1-5s after that. release() used to close the
# AudioContext ~1.1s after the last note, chopping 2.2-3.9s of audible decay - which is heard
# as the sound being cut at the end. release() now holds for the score plus its room and only
# fades the last 0.6s, and a skip (a pointerdown anywhere during the boot screen) releases
# instead of hard-stopping, so moving on visually no longer mutes the score mid-phrase.
AUDIO_FIXES: list[tuple[str, str, str, int]] = [
    (
        "score tail table",
        "  var SCORE_END=[6.20,6.70,6.10,5.80,6.20];",
        "  var SCORE_END=[6.20,6.70,6.10,5.80,6.20];\n"
        "  /* how long each score's room keeps ringing after that last note: v1 default 2.6s hall,\n"
        "     v2 4.2s, v3 1.1s, v4 0.7s, v5 5.0s - and every variant also feeds the 3.4s laugh hall. */\n"
        "  var SCORE_TAIL=[3.4,4.2,3.4,3.4,5.0];",
        1,
    ),
    (
        "finish() passes the tail",
        "    try{ if(window.LS_SOUND) window.LS_SOUND.release(SCORE_END[pick-1]); }catch(_){}",
        "    try{ if(window.LS_SOUND) window.LS_SOUND.release(SCORE_END[pick-1], SCORE_TAIL[pick-1]); }catch(_){}",
        1,
    ),
    (
        "skip() no longer cuts the score",
        "  function skip(){ if(done) return; try{ if(window.LS_SOUND) window.LS_SOUND.stop(); }catch(_){} "
        "t0 = performance.now()-3800; }   // jump straight to the fade",
        "  /* a skip is visual: jump to the fade, but let the score ring out on its own timeline */\n"
        "  function skip(){ if(done) return; try{ if(window.LS_SOUND) window.LS_SOUND.release(SCORE_END[pick-1], "
        "SCORE_TAIL[pick-1]); }catch(_){} t0 = performance.now()-3800; }",
        1,
    ),
    (
        "release() honours the room",
        "    release:function(scoreEnd){\n"
        "      try{\n"
        "        if(!ctx) return;\n"
        "        /* nothing was ever scheduled (blocked context) - close it rather than leak it */\n"
        "        if(!startedAt){ teardown(0.8); return; }\n"
        "        var elapsed = ctx.currentTime - startedAt;\n"
        "        var left = (typeof scoreEnd === 'number' ? scoreEnd : 0) - elapsed;\n"
        "        if(left <= 0.6){ teardown(0.8); return; }\n"
        "        if(releaseTimer) clearTimeout(releaseTimer);\n"
        "        releaseTimer = setTimeout(function(){ releaseTimer=null; teardown(0.8); }, Math.round(left*1000));\n"
        "      }catch(_){ teardown(0.8); }\n"
        "    },",
        "    release:function(scoreEnd, tail){\n"
        "      try{\n"
        "        if(!ctx) return;\n"
        "        /* nothing was ever scheduled (blocked context) - close it rather than leak it */\n"
        "        if(!startedAt){ teardown(0.8); return; }\n"
        "        /* tail = how long this score's room keeps sounding after the last note */\n"
        "        var room = (typeof tail === 'number' && isFinite(tail) && tail > 0) ? tail : 0.8;\n"
        "        var elapsed = ctx.currentTime - startedAt;\n"
        "        var left = (typeof scoreEnd === 'number' ? scoreEnd : 0) - elapsed;\n"
        "        /* hold for what is left of the dry score plus the whole decay, then fade the last\n"
        "           0.6s so closing the context cannot click */\n"
        "        var hold = Math.max(0, left) + room;\n"
        "        if(releaseTimer) clearTimeout(releaseTimer);\n"
        "        releaseTimer = setTimeout(function(){ releaseTimer=null; teardown(0.6); }, Math.round(hold*1000));\n"
        "      }catch(_){ teardown(0.8); }\n"
        "    },",
        1,
    ),
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

    # 4. audio: the loading screens' reverb tail (see AUDIO_FIXES)
    for label, old, new, expected in AUDIO_FIXES:
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
    print(f"        {len(REPLACEMENTS) + len(AUDIO_FIXES)} transformations, all call sites verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
