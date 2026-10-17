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

# ── the five JSON-devour cinematics ───────────────────────────────────────────
# Variant 1B (Clawed Devil-Palms) has always shipped in the page; the reference
# build adds four more, and all five are picked with an equal 1-in-5 chance when
# a JSON file is loaded. The engine is checked in as a separate authored file
# (tools/imp-throw-variants.js) and inlined here, so the served web layer stays
# a single document.
JSON_THROW_FIXES: list[tuple[str, str, str, int]] = [
    (
        "import routes through the five-way picker",
        "playImpJsonGrabAndThrow1B(name,posts.length);",
        "playImpJsonThrowRandom(name,posts.length);",
        1,
    ),
    (
        "the app's cleanup also tears the engine down",
        "  document.querySelectorAll('#postGrid .card').forEach(c=>c.classList.remove('card-throw-landed'));\n}",
        "  document.querySelectorAll('#postGrid .card').forEach(c=>c.classList.remove('card-throw-landed'));\n"
        "  try{ if(window.LS_JSON_THROW) window.LS_JSON_THROW.cleanup(); }catch(_){}\n}",
        1,
    ),
    (
        # The page floats the idle sticker with a CSS animation on the <svg>
        # itself.  A *paused* animation still wins the cascade over a normal
        # declaration, including the style attribute, so every cinematic's
        # svg.style.transform (translate + rotate + scale, the imp's whole
        # flight) was being swallowed: the jaws and arms moved, the sticker
        # never did.  The reference build scopes its float to
        # `.sidebar-sticker-slot.idle svg` and drops that class on throw, so it
        # never had the collision.  Dropping the animation on the root only is
        # the equivalent - descendants keep their paused idle pose (aura pulse,
        # decor pop) so nothing snaps at the start of the throw.
        "the throw owns the sticker's transform",
        ".sidebar-sticker-slot.throw-action-active svg,"
        ".sidebar-sticker-slot.throw-action-active svg *{animation-play-state:paused!important}",
        ".sidebar-sticker-slot.throw-action-active svg{animation:none!important}\n"
        ".sidebar-sticker-slot.throw-action-active svg *{animation-play-state:paused!important}",
        1,
    ),
]

# ── click feedback, theme swap and the reload freeze ─────────────────────────
# Three UI problems that share a cause: work that belongs to the *next* frame
# was being run inside the click handler, on top of the animation the click
# had just started.
#
#  * "Reload this file" re-parsed the JSON it had already parsed, rebuilt every
#    post and re-wrote the index, then started a cinematic - all in the click
#    task, while the imp's button-poke was still winding up. The page froze for
#    that beat and the cinematic killed the poke after a frame, so the click
#    looked like it did nothing.  Now: identical text takes a cheap replay
#    path, the poke is allowed to finish, and the cinematic starts two frames
#    later, once the rebuilt grid has been laid out.
#  * A theme flip changes every var(--t-*) at once, and dozens of controls
#    transition border/background/box-shadow over .16-.32s, so the switch
#    repainted the page through a third of a second of transitions.  Now the
#    swap runs with transitions off for two frames.
UI_FIXES: list[tuple[str, str, str, int]] = [
    (
        "the button poke publishes when it ends",
        "  const totalDur=chosenVariant===1?480:(chosenVariant===2?520:640);\n"
        "  let hitTriggered=false;\n"
        "  const startTime=performance.now();",
        "  const totalDur=chosenVariant===1?480:(chosenVariant===2?520:640);\n"
        "  let hitTriggered=false;\n"
        "  const startTime=performance.now();\n"
        "  /* how long this poke owns the sticker - useLastUploadedJson() waits it out so the\n"
        "     reload cannot freeze the page mid-swing and the cinematic cannot cut it off */\n"
        "  window.__impBtnFxEndsAt=startTime+totalDur;",
        1,
    ),
    (
        "cancel clears that deadline",
        "function cancelImpButtonClickFx(){\n  impBtnClickAnimId++;",
        "function cancelImpButtonClickFx(){\n  impBtnClickAnimId++;\n  window.__impBtnFxEndsAt=0;",
        1,
    ),
    (
        "the reload waits for the click's own animation",
        "async function useLastUploadedJson(){let rec=lastJsonMeta;if(!rec)rec=await readLastUploadedJson();"
        "if(!rec||!rec.text){toast('No previously uploaded JSON found on this device');refreshLastJsonUi();return}"
        "lastJsonMeta=rec;try{",
        "function waitForImpButtonClickFx(){\n"
        "  const left=(Number(window.__impBtnFxEndsAt)||0)-performance.now();\n"
        "  if(!(left>16))return Promise.resolve();\n"
        "  return new Promise(resolve=>setTimeout(resolve,Math.min(900,left)));\n"
        "}\n"
        "async function useLastUploadedJson(){\n"
        "  let rec=lastJsonMeta;if(!rec)rec=await readLastUploadedJson();\n"
        "  if(!rec||!rec.text){toast('No previously uploaded JSON found on this device');refreshLastJsonUi();return}\n"
        "  lastJsonMeta=rec;\n"
        "  /* This used to run inside the click task: the parse and the whole library rebuild ran\n"
        "     while the imp was still winding up his poke, froze the page for that beat, and the\n"
        "     cinematic then cut the poke off mid-swing. Let the click feedback land first. */\n"
        "  await waitForImpButtonClickFx();\n"
        "  try{",
        1,
    ),
    (
        "the cinematic starts after the grid has painted",
        "loadCurrentPagePosts();playImpJsonThrowRandom(name,posts.length);toast(",
        "loadCurrentPagePosts();queueImpJsonThrow(name,posts.length);toast(",
        1,
    ),
    (
        "reloading the loaded file replays instead of re-parsing",
        "async function applyImportedJsonText(name,text){"
        "const modalSum=document.getElementById('uploadJsonModalSummary');",
        "let lastImportedJsonSource='';\n"
        "/* one frame for layout, one for the first paint, then roll the cinematic */\n"
        "function queueImpJsonThrow(name,count){\n"
        "  requestAnimationFrame(()=>requestAnimationFrame(()=>playImpJsonThrowRandom(name,count)));\n"
        "}\n"
        "async function applyImportedJsonText(name,text){\n"
        "  const modalSum=document.getElementById('uploadJsonModalSummary');\n"
        "  /* \"Reload this file\" hands back the exact text the library was built from.\n"
        "     Re-parsing it, rebuilding every post and re-writing the index produced an\n"
        "     identical library and froze the page right after the click, so replay the\n"
        "     visible half of the import. Any different text takes the full path below. */\n"
        "  if(posts.length&&activeImportFileName===name&&text&&text===lastImportedJsonSource){\n"
        "    closeUploadJsonModal();\n"
        "    coverErrorAttempted.clear();\n"
        "    currentPost=-1;currentPage=1;filter='all';\n"
        "    document.querySelectorAll('.filter').forEach((btn,idx)=>btn.classList.toggle('active',idx===0));\n"
        "    showPage('library');render();loadCurrentPagePosts();\n"
        "    queueImpJsonThrow(name,posts.length);\n"
        "    toast(`Reloaded ${posts.length} post${posts.length===1?'':'s'} from ${name}`);\n"
        "    refreshLastJsonUi();\n"
        "    return posts.length;\n"
        "  }",
        1,
    ),
    (
        "remember the text the library was built from",
        "posts=entries.map((entry,idx)=>buildPostFromEntry(entry,idx,false));",
        "posts=entries.map((entry,idx)=>buildPostFromEntry(entry,idx,false));lastImportedJsonSource=String(text||'');",
        1,
    ),
    (
        "a theme swap runs without transitions",
        "function setAppTheme(themeId,quiet=false){\n"
        "  const id=APP_THEME_NAMES[themeId]?themeId:'default';\n"
        "  if(id==='default'){",
        "function setAppTheme(themeId,quiet=false){\n"
        "  const id=APP_THEME_NAMES[themeId]?themeId:'default';\n"
        "  /* A flip swaps every var(--t-*) at once, and dozens of controls animate their\n"
        "     border/background/box-shadow over .16-.32s: the switch used to repaint the page\n"
        "     through a third of a second of transitions right after the click. Freeze them\n"
        "     for the swap itself - they come back untouched two frames later. */\n"
        "  document.body.classList.add('theme-swapping');\n"
        "  if(id==='default'){",
        1,
    ),
    (
        "release the freeze after the swap",
        "  if(!quiet){\n"
        "    playUiClick();\n"
        "    toast(`Theme applied: ${APP_THEME_NAMES[id]}`);\n"
        "  }\n"
        "}",
        "  if(!quiet){\n"
        "    playUiClick();\n"
        "    toast(`Theme applied: ${APP_THEME_NAMES[id]}`);\n"
        "  }\n"
        "  const doneSwapping=()=>document.body.classList.remove('theme-swapping');\n"
        "  requestAnimationFrame(()=>requestAnimationFrame(doneSwapping));\n"
        "}",
        1,
    ),
    (
        "the theme swap stylesheet",
        "body.json-throw-active #postGrid .card.card-throw-landed{\n"
        "  opacity:1!important;\n"
        "  transform:translate3d(0,0,0) scale(1)!important;\n"
        "}",
        "body.json-throw-active #postGrid .card.card-throw-landed{\n"
        "  opacity:1!important;\n"
        "  transform:translate3d(0,0,0) scale(1)!important;\n"
        "}\n"
        "/* theme swap: let the new palette land in one frame instead of animating\n"
        "   every border, background and shadow the page owns */\n"
        "body.theme-swapping,body.theme-swapping *,body.theme-swapping *::before,body.theme-swapping *::after{transition:none!important}",
        1,
    ),
]

JSON_THROW_JS = pathlib.Path(__file__).with_name("imp-throw-variants.js")

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

    # 5. the five JSON-devour cinematics
    for label, old, new, expected in JSON_THROW_FIXES:
        found = text.count(old)
        if found != expected:
            print(f"! {label}: expected {expected} occurrence(s), found {found}")
            return 1
        text = text.replace(old, new)
        print(f"  ok  {label} ({found})")

    # 5b. click feedback, the theme swap and the reload freeze
    for label, old, new, expected in UI_FIXES:
        found = text.count(old)
        if found != expected:
            print(f"! {label}: expected {expected} occurrence(s), found {found}")
            return 1
        text = text.replace(old, new)
        print(f"  ok  {label} ({found})")

    module = JSON_THROW_JS.read_text(encoding="utf-8")
    if "window.LS_JSON_THROW" not in module:
        print(f"! {JSON_THROW_JS.name}: does not look like the animation module")
        return 1
    tail = "</body></html>"
    if text.count(tail) != 1:
        print(f"! expected one '{tail}', found {text.count(tail)}")
        return 1
    text = text.replace(tail, "<script>\n" + module.rstrip("\n") + "\n</script>\n" + tail, 1)
    print(f"  ok  five JSON-devour cinematics inlined ({len(module)} chars)")

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
    print(f"        {len(REPLACEMENTS) + len(AUDIO_FIXES) + len(JSON_THROW_FIXES) + len(UI_FIXES) + 1} transformations, all call sites verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
