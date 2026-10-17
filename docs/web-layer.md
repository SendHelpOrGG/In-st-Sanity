# The web layer

The ported UI lives in `src/InStSanity/Resources/Raw/wwwroot/index.html` and is served by the
in-app loopback host (see `docs/hybrid-port-plan.md`). This document records where the file came
from, what was changed, and how to reproduce it.

## Provenance

| | |
| --- | --- |
| Source | `index.html` supplied with the porting request |
| Size | 2,853,500 bytes |
| SHA-256 | `369767664003b16b5bc4d3845d81ecabede1751ce856631d76ab58cf743aa69c` |

The file is a single-page app: 110 KB of CSS, a 5,369-line script that builds the entire UI at
runtime, and 2.28 MB of embedded base64 media. `<body>` contains only `<div id="lsBoot">`.

## What was changed, and why

The app was written as a client for a local backend on port 8000. On the desktop that backend is
the app itself (`LoopbackHost`), so the browser-only workarounds had to go. Six transformations,
exactly seven lines:

| # | Change | Reason |
| --- | --- | --- |
| 1 | `SERVER_API_BASE="https://1ddc5d8b8d7a67.lhr.life"` → `""` | A dead dev tunnel; every call to it is a wasted round trip. |
| 2 | Removed the `resolveViaJina` / `resolveViaMicrolink` fallback in `resolveSinglePostVisual` | Posts resolve through `/api/resolve-post` — no third-party scraper. |
| 3 | Removed the same fallback in the page-summary pass | Same. |
| 4 | `wrapProxiedImageUrl()` now returns `/api/image?url=…` | Image proxying is local; previously `wsrv.nl`. It also unwraps URLs that earlier sessions already stored in `wsrv.nl` form, so existing saved state keeps working. |
| 5 | Remaining two `https://wsrv.nl/?url=` literals (detail fallback, built-in browser thumbnails) → `/api/image?url=` | Same. |
| 6 | Dropped the `<link rel="preconnect">` / `dns-prefetch` hints for `wsrv.nl` | That host is no longer contacted. |

Plus the four audio transformations described below (`SCORE_TAIL`, `release()`, `finish()`, `skip()`),
the five JSON-devour cinematics with the one scoped CSS rule they need, and the responsiveness fixes
for a button click and a theme swap (section below). Nothing else changed: no layout, no colours, no
other animation. Every script block in the output passes `node --check`.

## The throw has to own the sticker's transform

The page floats the idle sticker with a CSS animation declared on the `<svg>` element itself
(`.sidebar-sticker-slot svg{animation:impStickerBounce …}`). CSS animations sit *above* normal
author declarations in the cascade, including the `style` attribute, and **pausing** an animation
does not hand the property back — a paused animation still supplies the value. The throw already
froze the sticker with `.throw-action-active svg *{animation-play-state:paused!important}`, so every
`svg.style.transform` the cinematics write (the imp's whole flight: tilt, lunge, scale, recoil) was
being discarded. The jaws and arms moved — they are separate elements — the sticker never did. That
is the "border isn't rotating" report on variant 1: the imp's frame never left its frozen idle pose.

The reference build never hit this because its float is scoped to `.sidebar-sticker-slot.idle svg`
and a throw drops the `idle` class. The port scopes the fix the same way: the throw drops the root
animation (`animation:none!important`) while descendants keep their paused idle pose, so nothing
snaps when the throw starts and the bounce returns when `cleanup()` removes the class.

## The loading-screen sound was being cut short

The five loading-screen variants fade out at ~4.1 s (v5 at 4.4 s) while their score's last note
lands at 5.8–6.7 s, and the halls behind those notes keep sounding well past that: each variant
builds its own room with `setSpace()` (0.7 s to 5.0 s) and every variant also feeds the 3.4 s
"laugh hall". `LS_SOUND.release()` closed the `AudioContext` about 1.1 s after the last note, so
the decay was chopped:

| variant | last note | room rings to | context closed | tail chopped |
| --- | --- | --- | --- | --- |
| v1 | 6.18 s | 9.58 s | 7.29 s | **2.29 s** |
| v2 | 6.66 s | 10.86 s | 7.79 s | **3.07 s** |
| v3 | 6.08 s | 9.48 s | 7.19 s | **2.29 s** |
| v4 | 5.72 s | 9.12 s | 6.89 s | **2.23 s** |
| v5 | 6.16 s | 11.16 s | 7.29 s | **3.87 s** |

That is the sound "cutting in the end" — worst in v2 and v5, whose halls are the longest and
loudest. A second, sharper cut came from `skip()`: any `pointerdown` anywhere during the boot
screen hard-stopped the score mid-phrase (`stop()` fades out in 0.18 s).

The fix adds a per-score tail (`SCORE_TAIL`, matching each variant's room plus the laugh hall) and
makes `release()` hold for the rest of the dry score *plus* the whole decay, fading only the last
0.6 s so closing the context cannot click. A skip is now visual only: it jumps to the fade and lets
the score ring out on its own timeline. Turning the sound off in settings still stops it
immediately, which is what that switch is for.

Measured with the same harness afterwards: the context now closes 0.9 s *after* the room has decayed
(was 2.2–3.9 s before it), for all five variants.

The two scraper functions (`resolveViaJina`, `resolveViaMicrolink`) remain *defined* but are never
called; their URL templates are inert strings. The patch tool asserts that no call site survives.
`sourceKindFromUrl()` still contains the string `wsrv.nl`, because it classifies a URL's
provenance for the source badge — it never performs a request.

## Clicks, theme swaps and the reload freeze

Three UI problems, one cause: work that belongs to the *next* frame ran inside the click handler,
on top of the animation the click had just started.

### "Reload this file" froze the page and ate its own click animation

Pressing the button in *Private settings → Import posts from JSON* runs `useLastUploadedJson()`.
It re-parsed the stored JSON, rebuilt every post, re-wrote the index and then started a cinematic —
all inside the click task. Meanwhile the imp's button-poke (the claw the sticker throws at whatever
button you press) had only just started, so the page froze mid-swing, and the cinematic that followed
cancelled the poke (`json-throw-active` stops `stepBtnClick` on its next frame). Net effect: a hitch
and a click that looked like it did nothing.

Now:

- `playImpButtonClickRandom()` publishes `window.__impBtnFxEndsAt`, and the reload waits for that
  deadline (at most 0.9 s) before touching the library, so the poke plays to completion first.
- Reloading the file that is already loaded no longer re-parses it. `applyImportedJsonText()` keeps
  the text it built the library from; if the same text arrives again the visible half of the import
  is replayed (`showPage`/`render`/`loadCurrentPagePosts`) and the parse, the post rebuilding and the
  index rewrite are skipped. Any different text takes the full path.
- The cinematic starts through `queueImpJsonThrow()` — two `requestAnimationFrame`s later — so its
  first frames no longer land on a main thread that is still laying out the rebuilt grid.

Measured in the jsdom harness (it exaggerates DOM work, so compare paths, not absolute values):
the full import blocks for 86–233 ms depending on the fixture; the replay path keeps the same 20-card
re-render but drops the parse, the entry extraction, every `buildPostFromEntry` call and the index
write, for 44–72 ms. The size-proportional part is what used to be felt as a freeze.

The click itself was checked end to end with a real `pointerdown` on the button: the poke armed a
520 ms deadline, `useLastUploadedJson()` resolved 601 ms after the click (it waited out the poke,
then replayed), and the cinematic's first frame landed at 704 ms — the poke runs to completion
instead of dying on its first frame.

### A theme swap stuttered through its transitions

Every themed control pulls its border, background and shadow from `var(--t-*)`, so a theme flip
changes all of them at once — and dozens of those controls declare `transition: … .16s–.32s`. The
switch therefore repainted the whole page through a third of a second of colour transitions right
after the click.

`setAppTheme()` now puts `theme-swapping` on `<body>` before it applies the new palette; the rule
`body.theme-swapping *{transition:none!important}` lets the swap land in a single frame, and the class
is removed two frames later. Animations are untouched, so the sticker keeps bouncing through it, and
the transitions are exactly as they were before the click.

## Reproducing the transformation

```bash
python tools/patch-webapp.py <pristine-index.html> src/InStSanity/Resources/Raw/wwwroot/index.html
```

The tool refuses to run unless the input matches the SHA-256 above, asserts the expected number of
occurrences for **every** transformation, and re-verifies afterwards that no forbidden host can be
reached. If you edit the web layer by hand, update it — or expect the next run to overwrite the
change.

## What the patch does *not* fix

The removed endpoints (`/api/resolve-post`, `/api/resolve-batch`, `/api/resolve-owners`,
`/api/cached-posts`, `/api/download-selected`, `/api/save-imported-list`, `/api/browser/*`) currently
answer `501 not_implemented`, which the app already handles: it shows its own source badges and
fallback artwork. They are implemented in phases 2 and 3 of the plan.

## Five JSON-devour cinematics

Loading a JSON file plays a short animation of the imp grabbing the file and pitching the posts
into the grid. The app shipped with exactly one of those ("1B", the Clawed Devil-Palms rip); the
reference build `sticker_json_throw_variants_5_animations.html` contains five, and the port now runs
all of them, chosen at random with an equal 1-in-5 chance on every import.

| # | animation | what it does |
| --- | --- | --- |
| 1 | Big-Mouth Devour | palms snatch the file, shove it into the jaws, four chomps with JSON crumbs, a gulp, then the posts are spat out |
| 2 | Clawed Devil-Palms | the original 1B: pincer grab, tear the file in half, alternate left/right rapid-fire pitches |
| 3 | Singularity Snap | a black hole spaghettifies the file, collapses with a pop, the imp smirks, snaps, and the posts sparkle into place |
| 4 | Trident Strike & Post Rain | the trident is cocked, driven through the file, the imp laughs the sidebar's own evil cackle, and the posts rain down into their slots |
| 5 | Slow Smirk & Finger Snap | a massive black hole pulls the file in; the imp recovers into a smirk, snaps side-on, and manifests the posts |

### Both black holes open in the same spot

Two of the cinematics open a black hole: Singularity Snap (#3) and Slow Smirk (#5). They used to
compute it separately — Singularity put it 160 px right of the file badge's start
(`0.54·w + 160 px`, `0.38·h − 10 px`), Slow Smirk a little right of centre on the middle line. Both
now call one helper, `jsonThrowHoleSpot()`, which returns 57% of the viewport width and 50% of the
height, so the hole opens in exactly the same place in both animations (and the file's spiral, the
palms' reach and the collapse rings all follow it).

Checked by forcing each variant in the jsdom harness at a 1024×768 viewport: #3 drew its hole at
`(583.7, 384.0)` and #5 at `(583.68, 384)` — the same point to sub-pixel rounding.

Geometry, timing and choreography are the reference's, unchanged. Only the DOM layer is adapted:
the reference's own sticker ids are mapped onto the app's twelve themed stickers at run time
(the missing halos, laugh mouth and second brow are grafted on and removed afterwards), its sample
grid onto the real `#postGrid` cards, and its effect layer onto `#jsonThrowFxLayer`.

The engine is authored in [`tools/imp-throw-variants.js`](../tools/imp-throw-variants.js) and inlined
into the page by `tools/patch-webapp.py`, so the served web layer stays a single file. Three small
patches in the page route the import through `playImpJsonThrowRandom()`, let the app's existing
`cleanupImpJsonThrow()` tear the engine down, and give the throw the sticker's `transform` (see
"The throw has to own the sticker's transform" above).

### The imp's laugh is the sidebar's laugh

The reference build gave the Trident Strike its own face: it stretched the sticker's single grin
stroke into an open mouth and planted three flat triangles on it. It reads as a cartoon grin, and
it played for the whole animation. The port drops it and borrows the face the sticker already
performs while idling — expression 2 of `initImpSixFacialExpressionsLoop`, *Demonic Villain
Cackle*: the 1450 ms envelope, three uneven "ha" waves, the filled open mouth with six fangs, the
slanted eye wedges with pupils, the head roll and the horn lift.

The cinematics reach that drawing through the same DOM the idle engine uses
(`.imp-default-eyes` / `.imp-expr-eyes`, `.imp-default-mouth` / `.imp-expr-mouth`, both grafted by
`decorateStickerEyeGlow`), and read the same palette (`STICKER_THEME_PALETTES[currentStickerThemeId]`,
a top-level binding in the page's script), so every one of the twelve sticker themes laughs in its
own colours. The laugh starts when the trident lands, not on the wind-up, and the rest face is
restored between "ha" cycles and when the throw ends — `resetImp()` puts the default eyes, mouth,
horn paths and transforms back, and the idle loop resumes unchanged.

The undecorated markup a test harness uses has no expression groups, so the engine grafts a group
inside `.imp-grin` and hides the plain grin path instead; the drawing is identical.

To see a specific variant, append `?jsonThrow=1..5` or set `localStorage['stillSane.jsonThrowVariant']`
to a number; without an override the roll is uniform.
