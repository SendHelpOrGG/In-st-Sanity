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

Nothing else changed: no CSS, no animations, no sound code, no layout. Line count is identical
(6,181), and the transformed script passes `node --check`.

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
