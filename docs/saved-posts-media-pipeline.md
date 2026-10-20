# Saved Posts media pipeline — full breakdown

How images and videos get from Instagram into the Saved Posts tab, every method involved,
why some posts never show, and a concrete plan for fixing those and the page-turn/scroll lag.

All paths refer to the current tree (`src/InStSanity` + `Resources/Raw/wwwroot/index.html`).

---

## 1. The pipeline at a glance

```
saved_posts.json (import)
   └─ label_values → post URLs (shortcode only — NO media)
        │
        ▼
  ┌─────────────────────────────┐
  │ RESOLVE (C#)                │  InstagramResolver / SaveFreeClient
  │ who posted it, cover,       │  ← /api/resolve-post, /api/resolve-batch,
  │ slides, mp4 URLs            │     /api/resolve-owners, /api/cached-posts
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │ DELIVER (C#)                │  MediaProxyEndpoint + MediaCache
  │ fetch bytes from the CDN,   │  ← /api/image  (inline)
  │ cache to disk, sniff MIME   │     /api/download-photo (attachment)
  └──────────────┬──────────────┘
                 ▼
  ┌─────────────────────────────┐
  │ SHOW (web UI)               │  render() grid, renderDetail() viewer
  │ <img> covers / <video> mp4  │  + fallback art + source badges
  └─────────────────────────────┘
```

The imported JSON contains **only post links**. Everything visual — cover, account name,
likes, the whole carousel, video files — is resolved at runtime and persisted so a restart
does not redo the work.

---

## 2. Where posts come from

`importPostsFromJson(event)` (index.html) parses the Instagram data-export JSON
(`label_values` entries) into `posts[]` with `url` + `shortcode`, plus any metadata the
export already carries (owner, date, selection). `restoreImportedPostsState()` then re-applies
the previous session's resolved state from `localStorage['stillSane.importedPostsV13']`
(a debounced `scheduleSaveImportedPostsState()` writes it back, 450 ms after each change).

A small summary cache is also **baked into the bundle** (`const clientSummaryCache={…}`) so
previously seen posts paint instantly on first load.

---

## 3. Resolution — how metadata and media URLs are obtained

One C# service, `Services/Instagram/InstagramResolver.cs`, does the work. One fetch fills
both the summary (card) and the full carousel (opened post). Results are cached in memory
(6 h good / 10 min failed) on top of the web-layer persistence.

`ResolveCoreAsync(shortcode, summaryOnly)` runs these layers **in order**:

| # | Method | Request | Gets | Identity |
|---|--------|---------|------|----------|
| 1 | Captioned embed JSON | `GET instagram.com/p/{sc}/embed/captioned/` | every slide, exact counts, caption (`sourceKind:"direct"`) | **`Twitterbot/1.0` UA** |
| 2 | Post page JSON | `GET instagram.com/p/{sc}/` (parallel with #1) | newer "Polaris" JSON, crawler-shaped HTML | `Twitterbot/1.0` UA |
| 3 | Open Graph tags | parsed from #2/#1 HTML | one cover, owner, likes, caption (`sourceKind:"api"` = og-only) | — |
| 4 | save-free.net | `POST save-free.net/api/ajaxSearch` (Hunter-encoded JS decoded) | plain CDN URLs incl. mp4s; merged slide-by-slide (`MergeSlides`, `sourceKind:"mixed"/"api"`) | third party |
| 5 | Media info JSON | `GET i.instagram.com/api/v1/media/{mediaId}/info/` with `X-IG-App-ID: 936619743392459` | full item JSON when both HTML paths failed | no session |

Why the weird UAs: Instagram serves a **login shell** to a real browser UA, but the true
public page (og: tags + embedded JSON) to link-preview crawlers — `InstagramHttp.BotUserAgent`
(`Twitterbot/1.0`), with `facebookexternalhit/1.1` as second identity. This is the same trick
the original Python backend used.

The two HTML pages are fetched **in parallel**, 7 s timeout each (`FetchTimeout`). The grid
resolves in **batches of 10** (`/api/resolve-batch`, 16 s budget); an opened post gets 12 s
(30 s when background-loaded). save-free is single-attempt, 8 s, and only consulted when the
direct result "cannot stand on its own" (`NeedsSaveFree`: no cover in summary mode; or no
slides / og-only carousel / video slide without mp4 in full mode).

Failure reasons surfaced today: `login_wall` (redirected to `/accounts/login`),
`http_401/404/…`, `no_media`, `bad_url`, `timeout`, `unreachable`. Failures are remembered
10 minutes (`summaryResolutionCoolingDown`) so a dead post is not hammered.

`/api/resolve-owners` resolves just the account (for full-list search) the same way.

---

## 4. Media delivery — how bytes actually get to `<img>`/`<video>`

**Images and download files** go through `Services/Api/MediaProxyEndpoint.cs`:

* Web UI calls `/api/image?url={cdnUrl}` (`wrapProxiedImageUrl`), or `/api/download-photo`
  for the Download button. Old browser-build `wsrv.nl` links are unwrapped first.
* `MediaProxyEndpoint` → `MediaCache` (disk, SHA-256 of the URL, in
  `%LocalAppData%\InStSanity\MediaCache`, written atomically) → on miss
  `InstagramHttp.CreateMediaGet` (Chrome UA, image `Accept`, `Sec-Fetch-*`, instagram.com
  referer) fetches the CDN and the payload is cached **forever by URL** — which is what
  survives Instagram's expiring signed CDN links.
* Responses: magic-byte MIME sniff (`MediaTypeDetector`), `Cache-Control: public, max-age=3600`.
  Failures answer `502 {"ok":false,…}` which the web layer treats as "fall back".

**Videos** are different: the detail viewer plays `pic.videoUrl` **directly from Instagram's
CDN** in `<video src=… poster=… controls preload="metadata">` — not proxied, not cached on
disk, subject to signed-URL expiry.

**Grid covers** use Instagram's smaller candidate first (`coverThumbnailUrl`, else the full
`coverUrl`), loaded as
`<img loading="lazy" fetchpriority="low" decoding="async" referrerpolicy="no-referrer">`.

Fallback chain on image error (`handlePostImageError`): proxy of the *full* cover →
`data-media-fallback` → `COVER_FALLBACK_DATA_URI` (gradient art) → `triggerCoverErrorResolution`
(one queued re-resolve of that post, max 2 concurrent). When the fallback art appears, the
card just looks "empty" — there is no explicit "this post is restricted" state.

The card's corner badge (Direct / API / Mixed) records where the *data* came from
(`sourceKind` from the resolver: "direct" = Instagram's own HTML, "api" = save-free/og).

---

## 5. Display — grid, pagination, detail viewer

* `render()` rebuilds `#postGrid` **from scratch** (`innerHTML = pageItems.map(buildCardHtml)`)
  for every page/filter/search change — 20 cards (`PAGE_SIZE`), then rebuilds
  `#paginationBar`, then runs `updateAllCardSrcBadges()` (twice per render).
* `goToPage(n)` = `render()` + `loadCurrentPagePosts()` (resolution of still-unknown covers
  for that page) + smooth `main.scrollTo({top:0})`.
* `updateCardInDom(idx)` surgically patches one card when its cover resolves — no full
  re-render, unless a search query is active (then `render()` runs again per batch).
* Detail view (`openPost` → `renderDetail`/`renderPictureMarkup`) shows each slide as
  `<img>` (proxied, lazy) or `<video>` (raw CDN, `preload="metadata"`, poster = proxied
  thumbnail). Opening a post triggers `resolveSinglePostVisual` for the full carousel
  (12 s / 30 s background).

---

## 6. Why some posts never show

### 6.1 Follower-only / private accounts
Instagram does not serve the post to an anonymous crawler at all: the embed and post page
redirect to `/accounts/login` → resolver records `login_wall` → save-free usually fails too
(it is not logged in as *your* account, and cannot see private media) → media-info JSON 401s.
Result today: fallback artwork card, no explanation, badge "—".

### 6.2 Age-restricted posts
Same wall, different reason: age-gated media is withheld from logged-out/crawler sessions,
so layers 1–3 return the login shell or an empty og card, and layer 4/5 usually cannot
substitute. Result today: same silent fallback art.

### 6.3 The missing piece (already designed!)
`InstagramHttp` carries a shared `CookieContainer` **by design** — its own remarks say a later
phase should copy the session cookies out of the embedded Instagram login into this container,
after which every request sees the logged-in view. That phase never landed; and until v1.24
there was no logged-in surface to copy *from*. Now there is: the **Browser tab's persistent
WebView2 profile** (see `WebViewBootstrapper.ProbeAccountAsync` / `ClearInstagramSessionAsync`
and `GET /api/instagram-session`).

---

## 7. Making restricted posts work

**Phase A — authenticated resolution (the big unlock).**

1. When the user is signed in on Instagram in the Browser tab, copy the profile's
   instagram.com cookies (`CoreWebView2.CookieManager.GetCookiesAsync`) into
   `InstagramHttp.Cookies` — same pattern as the existing account probe. Domain-scoped, so
   nothing leaks to save-free.
2. Add a session-aware resolve path in `InstagramResolver`: when cookies are present, fetch
   `/p/{sc}/` with a **normal browser UA** (or `i.instagram.com/api/v1/media/{id}/info/`
   with `X-IG-App-ID` + session) and parse the logged-in JSON — exact slides, mp4s, and the
   real `is_video`/carousel data. Keep the crawler path for logged-out users.
3. Outcomes:
   * **Follower-only**: resolves whenever the signed-in account follows that account. If it
     does not, no technique can see the media — so show an explicit state (below).
   * **Age-restricted**: resolves whenever the signed-in account is 18+ (Instagram serves the
     media to eligible logged-in sessions).
4. Also reuse the session in `MediaProxyEndpoint` media GETs (same shared client) — some
   signed CDN URLs and restricted CDNs respond better with session context.

**Phase B — honest UI for what still fails.**

* A distinct card state ("Restricted — private account" / "Age-restricted" / "Removed")
  from the resolver's `Reason` (`login_wall`, `http_404`, …) instead of anonymous fallback art.
* One-click **"Open in the Browser tab"** (the action already exists: `openCurrentPostInBuiltInBrowser`)
  and a **"Retry resolve"** button (cooldown exists: `summaryResolutionCoolingDown`).
* Filter chip "Unresolved (N)" so restricted posts are findable, not silently broken.

**Phase C — smaller hardening.**

* Treat save-free outages as degraded, not fatal (it already is single-attempt).
* On `login_wall` **while signed in**, refresh cookies once and retry — the WebView2 session
  may have just expired.

---

## 8. Reducing lag on page change and scrolling

Where the time goes today on `goToPage`:

1. **`render()` throws away 20 cards and re-builds them from strings** — HTML parse + style +
   layout for the whole grid, every page turn.
2. **20 proxy image fetches of full-size covers.** `/api/image` returns the *entire original*
   JPEG (1080×1350, often 100–300 KB) for a ~250 px thumbnail, fully buffered in memory
   (`ReadAsByteArrayAsync`) — no resizing, no streaming.
3. **Smooth scroll to top while the grid swaps** — the animation frames fight the
   re-layout/decode work, which is the stutter you feel.
4. Background resolve batches that call `render()` again while a search query is active.

Fixes, in order of impact:

**1. Thumbnail-size proxy (biggest win).** `/api/image?url=…&w=480` — server-side downscale
before caching (e.g. ImageSharp; `MediaCache` keys include the width). Grid thumbs drop
~10× in bytes and decode cost; full size stays for the detail view. Keep `coverThumbnailUrl`
as the preferred source when Instagram gives one.

**2. Stop rebuilding the whole grid.** Reuse card DOM across page turns:
* keep the last rendered page's nodes in a small cache keyed by page number, or
* rebuild but reuse `<img>` elements (`updateCardInDom` already shows the surgical style), or
* wrap the swap in `document.startViewTransition()` for an animated, non-blocking change.
Combined with instant scroll (`behavior:'auto'` — the smooth animation is most of the
perceived lag) this makes page turns feel immediate.

**3. Decode/layout discipline.** Already good: `loading="lazy"`, `decoding="async"`. Add
`content-visibility:auto` + `contain:layout paint` on `.card` (offscreen cards skip layout),
fixed `aspect-ratio` boxes (already present) plus explicit `width/height` attributes.

**4. Warm the next page.** On `goToPage`, prefetch the **next** page's covers during idle
(`requestIdleCallback` → `fetch('/api/image?…&w=480')` or `<link rel=prefetch>`), so the
next turn hits the browser cache. Also resolve page N+1 metadata in the background at low
priority after page N settles (the batch endpoint already supports 10-URL chunks).

**5. Fix the double work in `render()`.** `updateAllCardSrcBadges()` runs twice per render;
a search query triggers a full `render()` per resolve batch — debounce it (the same
450 ms-save pattern) so typing/scrolling is not interrupted.

**6. Video playback.** `preload="metadata"` is right; add `preload="none"` for cards' video
slides and re-resolve a slide's mp4 on `video.error` (signed URLs expire — the resolver
cache is 6 h, CDN URLs can die sooner). Longer term: proxy mp4s through
`/api/image?url=…` (or a `/api/media` route) with **HTTP Range/206** support — the loopback
host already serves ranges for static files — which caches replays and survives expiry.

**7. Cheap wins.** Serve `/api/image` with `ETag`/`304` (payload hash — `MediaCache` has
SHA-256 already) so revalidation after the 1 h `max-age` costs no bytes; and return
`immutable` for cached CDN URLs since they are content-addressed.

---

## 9. Suggested order of work

| Priority | Item | Effect |
|----------|------|--------|
| 1 | Thumbnail-size proxy (`&w=`) | grid load ~10× less data, far less decode |
| 2 | Instant scroll + card DOM reuse / View Transitions | page turns stop stuttering |
| 3 | Cookie handoff from WebView2 → `InstagramHttp` + session-aware resolve | follower-only (for followed accounts) and age-restricted posts resolve |
| 4 | Explicit "restricted" card states + Open-in-Browser / Retry CTAs | failures stop being silent |
| 5 | Next-page prefetch + batch scheduling | first paint of page N+1 is instant |
| 6 | Range-capable media proxy for mp4 + `video.error` re-resolve | stable, replayable video |
| 7 | ETag/304 + `immutable` cache headers | cheaper revisits |

Items 1–2 are pure web/host work; item 3 is the planned-but-missing authenticated phase and
is the only way to fix the follower/age cases at the source.
