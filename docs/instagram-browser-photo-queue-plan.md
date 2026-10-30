# Instagram Browser Media Selection and Download Queue

**Status:** implementation in source; Windows build and real-app acceptance remain pending.
**Release:** v2.000 (build 69).
**Target:** the Windows MAUI app and its docked Instagram WebView2 Browser.

## Product behavior (confirmed)

- Select individual **photos and videos** in Instagram post, reel, and mixed-carousel views using the same on-media control.
- Selections accumulate across posts in one in-memory queue; the stable identity is shortcode + media type + zero-based slide index, not a signed CDN URL.
- A themed tray shows the queue, progress, per-item errors, Clear, Save/Retry, and Cancel while downloading. It is hidden when the queue is empty and idle.
- Save one item at a time, in queue order, to the photo or video folder configured in Settings. Honor the existing No duplicates preference and safe downloader behavior.
- Remove saved or duplicate-skipped items after the batch; keep failed items (with an error label) for retry. A retry operates on the remaining queue.
- Preserve the queue through Browser tab switches, hides, and page reloads. Clear it on explicit Clear, logout, or process exit. Do not persist it across app restarts.
- Mixed carousels use the current slide’s content index for stable selection and Browser history marks.

## User experience

1. The native Instagram WebView remains docked over the app’s existing Browser viewport. The injected control appears for a visible, identifiable image or video in a post/reel page or feed/profile card with a canonical Instagram permalink and a supported HTTPS media URL.
2. Hover a media item and choose **Add to queue**. It changes to **Queued** when selected; clicking it again removes that item. Navigating among slides and posts does not discard earlier selections.
3. The lower-right tray is styled from the active app theme. It shows media thumbnails, shortcode/slide labels, status, and retryable errors. Save runs the queue sequentially; Cancel stops remaining work, and Clear is disabled during a run.
4. Photos use the Settings photo folder; videos use the separate video folder when enabled, otherwise the configured photo folder. If the selected folder cannot be resolved or created, the affected item remains queued.
5. Successful files and duplicate skips are removed from the queue. Failed items remain visible and can be retried; an all-success queue becomes empty and the tray hides.

## Architecture

### Native WebView bridge

- `Platforms/Windows/WebViewBootstrapper.cs` registers `InstagramMediaOverlayScript` before document creation and re-runs it after navigation. If script registration races the first navigation, the navigation-completed hook evaluates the full source in the current document. The injected installer waits for the DOM root, supports both pointer and mouse hover, and retries detection after lazy media loads. It renders even if the native bridge is unavailable (then shows a connection notice on use); `CoreWebView2.WebMessageReceived` is subscribed only for the Instagram WebView.
- The page sends versioned JSON actions: `overlay-ready`, `toggle-media`, `clear-queue`, `start-download`, and `cancel-download`. The host checks the HTTPS Instagram source origin, canonical Instagram post permalink/shortcode (including selections made from feed cards), JSON size/depth, media type/index, queue limit, and HTTPS Instagram/CDN media URLs. The overlay parses only an exact post/reel path, avoiding shortcode matches in query strings or nested paths, and falls back to the current canonical post path when viewing a post directly. Every redirect hop is revalidated before it is followed; the page cannot supply a folder or access a loopback/file URL.
- `MainPage` reads only the needed Settings-folder/no-duplicate preferences from the app WebView and sends queue snapshots and the current theme palette back to the Instagram document. It records successful Browser downloads through the existing photo/video history writers.

### Hover diagnostics

- The injected script writes timestamped lifecycle and hover traces to the WebView console and sends compact `overlay-debug` bridge messages, including listener/install state, target class, media hit-test path/rejection reasons, button visibility, click-candidate summaries, and host-side queue validation errors. If Instagram targets a wrapper instead of the media element, the resolver also checks the hit-test stack and media descendants under the pointer.
- Windows captures those marked diagnostic messages plus native WebView2 hookup, navigation, injection, and queue-bridge events in `%LOCALAPPDATA%\InStSanity\Logs\instagram-browser-hover.log`. The file rotates at 2 MB. It records media host and dimensions but omits signed media URLs and post shortcodes.
- To share a reproduction, hover the problem image/video, close the app, then attach `instagram-browser-hover.log` from that folder.

### Queue and downloader

- `BrowserMediaQueueService` is the in-memory source of truth (maximum 50 selections). It survives navigation and view hiding, snapshots the current order, performs serial downloads, removes successes/skips, retains failures, and resets on logout.
- `SelectedMediaDownloadService` contains the shared safe download path for `/api/download-selected` and Browser selections: folder resolution/creation, HTTPS host validation for Browser URLs, cache-aware image fetch, video staging, size checks, extension detection, filename sanitization, conflict handling, and timestamps.
- The Library endpoint retains its existing four-way fetch concurrency and ordered writes. The Browser queue calls the service’s one-item method serially; it does not call a cross-origin app API from Instagram’s page.
- Browser history uses content-index-specific maps in the existing local download-history store so a video/photo mark remains associated with the correct slide in mixed carousels.

### Queue lifetime and result handling

- Queue state exists only in the process-scoped service. Browser reloads receive a fresh native snapshot; app restart creates an empty queue.
- Clear is explicit and unavailable while a batch is active. Logout clears selections and cancels an in-flight download. App exit needs no disk cleanup because the queue is never persisted.
- A network, URL, size, or folder error is reported on the affected item and leaves that item queued. Successfully written files are never re-added during retry.

## Validation and release checklist

- `tools/smoke-test.ps1` checks release markers, Browser injection/bridge markers, queue validation, shared sequential downloader wiring, and existing native video-menu behavior.
- Validate all inline JavaScript and the injected script with Node, XML-parse XAML/project files, and run `git diff --check`.
- Run a Windows MAUI build and real-app acceptance when the Windows/.NET environment is available. Acceptance must cover signed-in/public posts, photo/video and mixed carousels, retry/cancel/folder failures, tab switches, hide/show, reload, logout, app restart, theme changes, duplicate files, and no regressions to the Library flow or the native video-player Download action.

**Current limitation:** the sandbox has no .NET/Windows toolchain, so a Windows build and WebView2 runtime acceptance cannot be claimed here.
