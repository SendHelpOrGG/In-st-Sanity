# Instagram Browser Photo Selection and Download Queue — Implementation Plan

**Status:** proposal only; no feature code has been implemented.
**Target:** the existing Windows MAUI app and its docked Instagram WebView2 browser.
**Related mockup:** `overlay-queue-concept.png` (illustrative; Instagram content and account are placeholders).

## 1. Goal

Let a user select individual photos while browsing Instagram in In[st]Sanity’s built-in Browser, collect the selected photos in an in-app queue, and save them sequentially to the folder configured in Settings. The current page, navigation controls, profile area, and browser viewport stay as they are; the new controls are overlays on the Instagram media and browser viewport, styled to match the active In[st]Sanity theme.

The feature is for photos only. It must not take over video playback, Instagram’s video-player Download action, the existing Library selected-photo flow, or Save via URL.

## 2. Decisions still needed before implementation

The concept does not settle these product behaviors, so they should be confirmed before the queue model is finalized:

1. **Queue scope:** should selections survive navigation between posts, or be limited to the currently open post/carousel? The mockup shows a tray while browsing but does not decide this.
2. **After a download:** should successful items disappear immediately while failed items remain available to retry? Recommended behavior: remove successful items, retain failed items with their error state, and clear the tray when everything succeeds.
3. **Browser close/reload:** should an in-memory queue be cleared when the Browser is closed or reloaded, or restored for the current app session? Recommended first version: keep it in memory while the Browser stays open; clear it on explicit Clear, logout, or app exit. Persistence across app restarts is not needed for the first release.
4. **Video slides in mixed carousels:** recommended behavior is to expose Select only while a photo slide is active and never enqueue video media.

Until these are answered, implementation should keep queue scope configurable in the queue service and avoid persisting selections to disk.

## 3. User-facing behavior

### 3.1 Opening and browsing

1. The user opens the existing **Browser** page and signs in or navigates using the existing Instagram WebView2 profile.
2. The native Instagram WebView remains docked over the existing `#browserViewport`; the app’s sidebar, top bar, address controls, account display, and navigation are unchanged.
3. The feature is active only on supported Instagram post pages. Login pages, profiles, search pages, stories, reels, video-only posts, and other non-photo views do not receive a photo-selection control. If the page is unsupported or Instagram’s markup cannot be interpreted safely, the overlay hides itself without affecting normal browsing.

### 3.2 Selecting a photo

1. On a supported photo slide, show a compact **Select** control over the photo, preferably near its upper-right corner. It must not cover the carousel’s own arrows or block scrolling, opening the post, or Instagram controls.
2. Clicking **Select** toggles only the currently visible photo. The button changes to a checked **Selected** state; clicking it again removes that photo from the queue.
3. When the carousel advances, the control follows the newly visible slide and reflects whether that exact slide is already queued. Previously selected slides remain selected according to the confirmed queue-scope rule.
4. Each item is deduplicated. Returning to an already selected slide must not add it a second time.
5. The UI must identify the slide with the post shortcode and slide index, not just its current signed CDN URL; Instagram can rotate or refresh media URLs during navigation.

### 3.3 Queue tray

1. When at least one photo is queued, show a compact tray anchored to the lower edge of the Instagram WebView viewport. It should remain visible while the user browses, without replacing the app’s Browser chrome.
2. The tray shows the selected count, a small set of representative thumbnails, **Clear**, and **Download**. On narrow viewports it may collapse the thumbnail strip while keeping the count and primary action visible.
3. **Download** is disabled when the queue is empty. Clear removes the queued items and returns all visible Select controls to their unselected state.
4. If the queue is empty, the tray is hidden rather than occupying browser space.
5. The tray uses the active In[st]Sanity theme. It must respect the app’s selected theme and button text colors rather than hard-code the colors in the mockup.

### 3.4 Download and result states

1. On **Download**, freeze a snapshot of the queue in its current order. Disable selection changes or make them apply only to the next queue while the snapshot is running; do not silently reorder the active batch.
2. Read the folder selected in Settings and pass it to the existing safe folder-resolution path. Do not fall back to WebView2’s default Downloads folder.
3. Save one item at a time, in the order shown in the queue. Show progress such as **Downloading 2 of 5** and disable the Download button while work is active. A Cancel action may stop the remaining items; a file already being written should finish or be cleaned up safely.
4. Reuse the existing filename sanitization, MIME-based extension selection, unique-name conflict handling, size limits, and Settings-folder creation behavior.
5. Report per-item success or failure. On complete success, show a short confirmation naming the destination and clear the queue. On partial failure, retain failed items for retry and remove successes (subject to the product decision above). Never report success for an item that was not written.

## 4. Proposed architecture

### 4.1 Keep the existing two-WebView layout

The web app and Instagram site run in separate WebViews. The app’s `index.html` owns the themed Browser page and reports the viewport rectangle through the existing `/api/browser-view` flow. `MainPage` positions `InstagramBrowserOverlay` over that rectangle, and `WebViewBootstrapper.AttachInstagramBrowser` configures the separate Instagram WebView2 control.

The photo control and tray should be injected into the Instagram document (or, if WebView2 stacking makes that unreliable, into a dedicated native overlay view above `InstagramBrowserHost`). Do not add the tray behind the native WebView in the app’s `index.html`; that layer is already covered by the docked browser.

### 4.2 Inject an idempotent page script

1. Add a small, self-contained overlay script to the Instagram-browser bootstrap path. Install it with WebView2’s document-created script mechanism and run an idempotent attach check after navigation completes.
2. Restrict activation to the already-approved Instagram origin and supported post/reel URL patterns. Keep the existing navigation policy in force; the overlay must not broaden which sites the native browser can open.
3. Detect the active post and photo using accessible DOM landmarks and carousel controls where possible. Avoid depending on a single generated class name. Use a `MutationObserver` plus URL/history-change detection because Instagram is a single-page app and often changes slides or routes without a full navigation.
4. Reconcile the current active media after carousel movement, page transitions, lazy image loading, and responsive layout changes. The observer must be debounced and must not repeatedly add duplicate controls.
5. Add the Select control to a stable media container, not to a transient thumbnail. Hide it for video elements, video-only posts, and media that cannot be identified with confidence.
6. Put injected styles and elements in an isolated root (preferably a Shadow DOM host) with a unique `data-instsanity-*` marker. This reduces CSS collisions and makes teardown/re-injection deterministic.
7. Do not replace Instagram event handlers or intercept ordinary page interactions. The overlay’s pointer target should be only its own controls; its non-interactive area should not block the site.

### 4.3 Stable queue identity and host message contract

The native host should be the source of truth for queue state. The injected page reports user intent; it does not choose a download folder or invoke an app-local API directly.

Proposed messages are narrowly typed JSON objects, for example:

- `overlay-ready` — script version and current page state.
- `toggle-photo` — post shortcode, zero-based slide index, observed image URL, optional display label, and a request ID.
- `clear-queue` — explicit request to clear current selections.
- `start-download` / `cancel-download` — queue actions.
- Host-to-page `queue-state` — ordered items, selected count, current status, and progress.
- Host-to-page `download-result` — per-item success/failure and final summary.

Attach a `CoreWebView2.WebMessageReceived` handler only to the Instagram browser WebView. Validate the sender’s current origin, message size, JSON depth, action name, required fields, and each media URL. Accept only HTTPS media URLs on supported Instagram/CDN hosts, reject video URLs and arbitrary local/loopback/file URLs, cap the number of selected items, and never expose cookies or credentials to page JavaScript. Treat every page message as untrusted even though the overlay script created it.

Use a stable deduplication key based on the Instagram post shortcode plus slide index (and, if needed, a non-reversible media fingerprint). Do not key solely on a signed CDN URL, because its query signature can change. Send queue snapshots back through WebView2’s native message channel so the injected UI always reflects host state after navigation or a DOM rerender.

### 4.4 Theme hand-off

The app’s theme lives in the local web UI, while the Instagram page is a separate origin. On Browser open or theme change, obtain only the non-sensitive theme palette needed for the overlay from the app WebView (for example, through a small `window.getBrowserOverlayTheme()` helper in `index.html`) and pass it to the Instagram WebView through the host bridge. Do not copy arbitrary local storage or session data. Fall back to a safe neutral palette if the theme cannot be read.

### 4.5 Reuse the selected-photo save pipeline

The existing `POST /api/download-selected` endpoint already resolves the configured folder and handles image bytes, validation, filenames, extensions, unique names, and results. It currently runs a batch with parallelism, so it must not be called in a way that contradicts the Browser queue’s sequential-download promise.

Before implementation, extract the reusable selected-photo write/download logic into a host-side service (for example, `SelectedPhotoDownloadService`) used by both the current endpoint and the Browser queue. Keep the Library’s existing batch behavior unchanged. The Browser queue calls the shared service serially, with one item in flight at a time. This avoids calling a loopback HTTP endpoint from Instagram’s cross-origin page and avoids duplicating path, security, or file-writing logic.

Resolve the Settings folder through the existing app preference bridge (`window.getConfiguredDownloadFolder`) or its backing preference service, then use `DownloadSelectedEndpoint.ResolveFolder`/the extracted shared service. The folder path must come from the app host, not from the Instagram page.

## 5. Implementation steps

### Step 0 — Confirm behavior decisions

Confirm queue scope, post-download retention of failures, and close/reload behavior. Record those choices in tests before coding. Do not infer cross-post persistence from the concept image.

### Step 1 — Define data contracts and limits

Add typed queue item/state/result models and a versioned WebMessage schema. Define the stable identity, allowed hosts, max queue length, payload limits, image-only rule, cancellation semantics, and queue ordering. Add validation tests before wiring the browser UI.

### Step 2 — Extract reusable photo download service

Refactor the existing `/api/download-selected` implementation so its current behavior and response remain unchanged, but safe folder resolution, fetch/cache, MIME detection, filename sanitization, unique writes, and item results can be called by both the endpoint and a serial Browser queue. Keep the Library endpoint’s current concurrency setting; add a serial host-side method for Browser batches.

### Step 3 — Add the WebView2 host bridge

Extend the Instagram browser bootstrap to install the injected script and register a bounded `WebMessageReceived` handler. Validate origin and JSON schema, route toggle/clear/start/cancel actions to a queue service, and post host state back to the page. Unsubscribe/tear down cleanly when the WebView is disposed or the session is cleared.

### Step 4 — Build the in-memory queue service

Implement add/remove/toggle, stable deduplication, ordering, queue snapshots, clear, session reset, busy state, progress, partial failure, and cancellation. Keep the selected queue in memory only for the first release. Apply the confirmed post-scope rule in one place so route changes cannot accidentally alter selection behavior.

### Step 5 — Add supported-post detection and Select overlay

Implement the injected page script with idempotent initialization, post/carousel recognition, active-slide tracking, photo-only eligibility, accessible Select/Selected semantics, and observer cleanup. Verify that it does not cover or break Instagram carousel controls, profile navigation, login, scrolling, or context menus.

### Step 6 — Add the queue tray and progress states

Render the tray inside the browser viewport, bind it to host queue-state messages, and implement empty/selected/downloading/success/partial-failure/cancelled states. Apply the active theme palette, test desktop and resized browser viewports, and ensure the tray stays within the clipped viewport rather than covering the app’s top bar or sidebar.

### Step 7 — Connect Settings-folder downloads

On Download, obtain the configured Settings folder from the host, snapshot the queue, and call the shared downloader serially. Feed per-item progress/results back to the injected UI. Preserve deterministic queue order and use the existing conflict-safe naming behavior. If the folder cannot be resolved or created, show a clear error and keep the items queued.

### Step 8 — Automated and static validation

- Run JavaScript syntax checks on the injected script and every inline script.
- Run unit tests for queue identity, duplicate removal, order, limits, origin/media validation, failure retention, cancel, filenames, and folder resolution.
- Test the extracted service and existing `/api/download-selected` for regressions, especially its current Library concurrency and response shape.
- Extend `tools/smoke-test.ps1` to verify the injection/bootstrap markers, message validation, queue route/service, and no change to existing browser/video-download markers.
- Parse XAML and run `git diff --check`.
- Run a Windows MAUI build when the Windows/.NET toolchain is available.

### Step 9 — Actual-app acceptance

Use the real Windows MAUI app and its real Instagram WebView2 session; a static preview is not acceptance evidence. Test a public and signed-in post, a private post available to the account, single-photo and multi-photo carousels, mixed photo/video carousels, video-only posts, profile-to-post SPA navigation, carousel next/previous, page reload, logout, browser resize, theme changes, and unavailable/expired images. Confirm each saved file appears in the Settings folder, collisions receive unique names, items save one at a time in tray order, and errors/cancellation do not corrupt the remaining queue. Recheck the existing video-player three-dot Download action and the Library selected-photo flow.

### Step 10 — Versioned release and documentation

Record the implemented behavior in version history using the repository’s version rubric (green small, yellow bundled, red new/rework), update the required four version-marker files before committing, and commit the feature separately from unrelated workspace changes.

## 6. Acceptance criteria

The feature is ready only when all of the following hold:

1. A Select control appears only for a confidently identified active photo in the built-in Instagram browser.
2. Selecting/unselecting a slide updates its check state and the queue count exactly once, including after carousel navigation and DOM rerenders.
3. The queue tray is app-themed, stays inside the native browser viewport, and remains usable at the app’s supported window sizes.
4. Download saves the queued photos sequentially in visible queue order to the Settings folder, with safe extensions and non-conflicting filenames.
5. Per-item failures are surfaced without losing retryable items; folder failures do not silently switch destinations.
6. No Instagram cookies/passwords are exposed to injected JavaScript; no arbitrary page message can download a non-Instagram URL or access local files.
7. The existing Library download flow, login/session, browser navigation, video download action, video notice, and folder-open action continue to work.
8. Acceptance is confirmed in the actual Windows MAUI app, not only in a static preview.
