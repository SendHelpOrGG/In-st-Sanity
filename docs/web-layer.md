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

Plus the four audio transformations described below (`SCORE_TAIL`, `release()`, `finish()`, `skip()`)
and the five JSON-devour cinematics (section below).

Four further transformations fix the loading-screen audio (section below). Nothing else changed:
no CSS, no animations, no layout. The transformed script passes `node --check`.

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
| 4 | Trident Strike & Post Rain | the trident is cocked, driven through the file, and the posts rain down into their slots |
| 5 | Slow Smirk & Finger Snap | a massive black hole pulls the file in; the imp recovers into a smirk, snaps side-on, and manifests the posts |

Geometry, timing and choreography are the reference's, unchanged. Only the DOM layer is adapted:
the reference's own sticker ids are mapped onto the app's twelve themed stickers at run time
(the missing halos, laugh mouth and second brow are grafted on and removed afterwards), its sample
grid onto the real `#postGrid` cards, and its effect layer onto `#jsonThrowFxLayer`.

The engine is authored in [`tools/imp-throw-variants.js`](../tools/imp-throw-variants.js) and inlined
into the page by `tools/patch-webapp.py`, so the served web layer stays a single file. Two small
patches in the page route the import through `playImpJsonThrowRandom()` and let the app's existing
`cleanupImpJsonThrow()` tear the engine down.

To see a specific variant, append `?jsonThrow=1..5` or set `localStorage['stillSane.jsonThrowVariant']`
to a number; without an override the roll is uniform.
