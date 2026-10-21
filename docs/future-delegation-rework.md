# Future Delegation Rework — Readability & SRP Plan

> **Status:** Proposal / parking doc. No code changes yet.  
> **Context:** `index.html` is intentionally the ported web app served by `LoopbackHost`; the hybrid approach was pragmatic. With ~8.7k lines in `wwwroot/index.html` and 1.3k in `InstagramResolver`, a full rewrite is out of scope. This doc records *what to split, when, and how* for incremental work.

---

## 1. Why we need it

| Signal | Current | Pain |
|---|---|---|
| `MainPage.xaml.cs` | **555 lines** — View + controller + poller + account cache | Hard to test, violates SRP, every Instagram fix touches the Page |
| `InstagramResolver.cs` | **1 338 lines** — shortcode codec, HTTP, HTML→JSON, merge, cache | Can't test parsing without HTTP, God-class |
| `LoopbackHost.cs` | **714 lines** — `TcpListener` + routing + static serving + range | Mixes networking with business |
| `DownloadSelectedEndpoint.cs` | **686 lines** — validation + file IO | Untestable IO path |
| `wwwroot/index.html` | **8 725 lines** — themes, stickers, boot, storage, UI | One `ship_v15.py` edit broke sticker script (`</svg>` truncation, LS_SOLID miss) |

Goal is **readability, testability, safe theming** without a big-bang rewrite.

---

## 2. What “good” looks like

* **MVVM kept:** `Views/` folder for `MainPage.xaml`, `HeadlessPage`, `AppShell`; `ViewModels/` stays; `Models/` holds DTOs (`ResolvedPost`, `ResolvedMedia`).
* **Services are small, single-purpose** — each class < 250 lines, interface + unit tests.
* **Web layer is modular** — `index.html` → `js/themes.js`, `js/stickers.js`, `js/boot.js`, `css/app.css` loaded as `MauiAsset`s or bundled at build.

---

## 3. Proposed decomposition

### 3.1 `MainPage.xaml.cs` → 4 pieces

Current file does: `EnsureInstagramBrowserView`, `WatchInstagramBrowserUrlAsync` (1 s `location.href` poll), `RefreshInstagramAccountAsync` → `ProbeAccountAsync` → `FetchCurrentAccountAsync` → `Preferences` cache.

```
MainPage (thin)
  ├── InstagramBrowserHostService   — creates/docks 2nd WebView, attaches WebViewBootstrapper,
  │                                   TrackSourceChangesAsync, Region (Margin/Width/Height)
  ├── InstagramAccountService        — Probe + Http fallback + knownAccount cache,
  │                                   Preferences.Get/Set(RestoreKnownAccount/SaveKnownAccount)
  ├── InstagramBrowserNavigationService — Navigate, CanGoBack/Forward, policy check
  └── UrlWatcherService              — Polling loop + debounce, raises UrlChanged
```

**Benefit:** `MainPage.OnInstagramBrowserOpenRequested` becomes 5 lines; polling doesn't live in the View.

### 3.2 `InstagramResolver.cs` → layered

Current: `ParseShortcode` ↔ `FetchHtmlAsync` ↔ `ParseFromPageJson` ↔ `BuildFromMedia` ↔ `MergeSlides` ↔ cache.

```
InstagramResolver (facade: ResolveAsync / ResolveSummaryAsync)
  ├── ShortcodeCodec              — ShortcodeAlphabet, ParseShortcode, ShortcodeToMediaId, ShortcodeTimestamp
  ├── ResolverCache               — ConcurrentDictionary<string,CacheEntry>, Attempts/Resolved/Failed, FailureLifetime
  ├── InstagramPageFetcher        — FetchHtmlAsync, FetchMediaInfoAsync (X-IG-App-ID, timeout 7s)
  ├── InstagramPageParser         — ContextJsonRegex, JsonScriptRegex, FindMediaNode, ParseFromOpenGraph
  ├── ResolvedPostBuilder         — BuildFromMedia, WithPictures, NeedsSaveFree, WithShortcodeTimestamp
  └── SlideMerger                 — MergeSlides
```

Each piece unit-testable with fixture HTML (no `InstagramHttp`).

### 3.3 `LoopbackHost.cs` → host vs router

```
LoopbackHost               — Bind(), AcceptLoopAsync, lifecycle
  ├── StaticFileServer    — ResolveWebRoot (newest wwwroot), ServeStaticAsync, TryParseRange, path-traversal guard
  ├── ApiRouter           — RouteAsync, /__health, /api/* → IApiEndpoint, 501
  └── WireFormat          — ReadRequestAsync, ParseRequestAsync, WriteResponseAsync
```

Static serving can then be tested via `HttpRequestInfo` fakes.

### 3.4 `DownloadSelectedEndpoint.cs` / `SaveFreeClient.cs`

```
DownloadSelectedEndpoint
  ├── DownloadValidator   — indices, shortcode sanitization
  ├── DownloadWriter      — File.WriteAllBytes, folder picker, progress toast
  └── SaveFreeClient      — FetchAsync, MergeSlides (shared with Resolver)
```

Separates validation (unit-testable) from IO (needs integration test).

### 3.5 `wwwroot/index.html` — incremental, not rewrite

Phase 0 (safe now): extract `<style id="lsBootStyle">` → `css/boot.css`, extract `STICKER_*` palettes → `js/stickers.js` as `MauiAsset`.

Phase 1 (later): `LS_SOLID`/`LS_BOOT_BG`/`THEME_STICKER_SVGS` → `js/themes.js` (prevents ship.py insertion bugs like missing LS_SOLID for v12-15 that fell back to accent `#d6b46a`).

Phase 2: `boot.js` (6 variants) + `brandGlitch.js` out of inline `<script>`.

Do **not** inline them back into one file at build until tests cover 12-15 picker + boot.

---

## 4. Non-goals

* No full XAML rewrite of the SPA — too big, high regression risk for themes/loading screens.
* No new dependencies (`CommunityToolkit.Mvvm` optional, but `BaseViewModel` hand-rolled is fine).
* No `Views/`/`Models/` rename until C# split lands — cosmetic churn before structure change hurts `git blame`.

---

## 5. Migration strategy (incremental PRs)

1. **PR A — MainPage slice:** Extract `InstagramAccountService` + `UrlWatcherService` behind `IInstagramAccountService`. No behavior change, `MainPage.xaml.cs` drops to ~150 lines. Add `Tests/MainPageTests` for Restore/SaveKnownAccount.
2. **PR B — Resolver slice:** Extract `ShortcodeCodec` (pure funcs) + `ResolverCache`. Existing tests keep passing.
3. **PR C — Parser slice:** Move `FindMediaNode`/`ParseFromOpenGraph` to `InstagramPageParser` with golden-file HTML fixtures from `ig_browser_engine.py`.
4. **PR D — Web layer slice:** Move CSS/JS to `wwwroot/js/` + update `ResolveWebRoot` to probe `wwwroot/js`. Smoke-test still `curl :8080/ | grep LS_SOLID`.

Each PR < 400 lines diff, version bump green/yellow per rubric.

---

## 6. Current metrics (2026-10-21, v1.48)

```
8725  wwwroot/index.html
1338  InstagramResolver.cs
 714  LoopbackHost.cs
 686  DownloadSelectedEndpoint.cs
 555  MainPage.xaml.cs
 503  SaveFreeClient.cs
 349  WebViewBootstrapper.cs
```

Target after rework: no file > 400 lines except `index.html` intermediate (< 2k after split).

---

## 7. References

* `MauiProgram.cs:CreateMauiApp` — DI wiring `LoopbackHost` + `IApiEndpoint`s
* `MainPageViewModel.cs` — `AppUrl = LoopbackHost.Url`
* `LoopbackHost.cs:ResolveWebRoot` — newest `wwwroot` wins
* `docs/hybrid-port-plan.md` — original port rationale (keep same-origin `localhost`)
* `docs/web-layer.md` — SPA responsibilities

---

*Author: In-st-Sanity maintainers — parking doc from 2026-10-21 MVVM/hybrid discussion.*
