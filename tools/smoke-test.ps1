<#
.SYNOPSIS
    Boots the published app and verifies that it serves the web layer.

.DESCRIPTION
    Two phases:

    1. Backend (always). Launches the app with INSTSANITY_HEADLESS=1 — no WebView, no
       desktop session needed — and asserts that the loopback host answers, that the web
       document is served with the expected markers, that range requests work (media
       playback depends on them) and that the /api/* pipeline responds. This is fully
       deterministic and is what CI gates on.

    2. UI (optional, -IncludeUi). Launches the app normally and checks that it stays alive
       while still answering /__health, which is as far as a headless runner can go without
       inspecting pixels. Needs the Evergreen WebView2 runtime.

    Failures are reported with ::error::/::warning:: workflow commands so they surface as
    GitHub annotations, in addition to the log file.

.PARAMETER Exe
    Path to InStSanity.exe (the published, unpackaged build).

.PARAMETER Port
    Port the host is expected to listen on. Defaults to 8000, the port the web layer
    prefers; if that port was busy the app serves on the next free one and these checks
    fail fast.

.PARAMETER TimeoutSeconds
    How long to wait for the host to answer /__health in each phase.

.PARAMETER IncludeUi
    Also run the WebView phase.

.EXAMPLE
    pwsh -File tools/smoke-test.ps1 -Exe artifacts/InStSanity-win-x64/InStSanity.exe -IncludeUi
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Exe,

    [int] $Port = 8000,

    [int] $TimeoutSeconds = 90,

    [switch] $IncludeUi
)

$ErrorActionPreference = 'Stop'
$logPath = 'smoke-test.log'
$baseUrl = "http://localhost:$Port"

function Write-Step {
    param([string] $Message, [string] $Colour = 'Cyan')
    Write-Host "==> $Message" -ForegroundColor $Colour
    Add-Content -Path $logPath -Value "==> $Message"
}

function Write-Detail {
    param([string] $Message)
    Write-Host "    $Message"
    Add-Content -Path $logPath -Value "    $Message"
}

function Write-Annotation {
    param([string] $Level, [string] $Title, [string] $Message)
    # Workflow command: readable from the check run's annotations even when job logs are
    # not reachable (self-hosted/proxied environments).
    $flat = ($Message -replace "`r?`n", ' ').Trim()
    Write-Host "::$Level title=$Title::$flat"
}

function Wait-ForHost {
    param([System.Diagnostics.Process] $Process)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            throw "The app exited early with code $($Process.ExitCode) (see the log above)."
        }

        try {
            return Invoke-RestMethod -Uri "$baseUrl/__health" -TimeoutSec 5
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }

    throw "No answer from $baseUrl/__health within $TimeoutSeconds s."
}

function Start-App {
    param([bool] $Headless)

    $environment = @{}
    if ($Headless) { $environment['INSTSANITY_HEADLESS'] = '1' }

    # Set for this process so the child inherits it; Start-Process -Environment needs PS 7.4.
    $env:INSTSANITY_HEADLESS = if ($Headless) { '1' } else { $null }

    $process = Start-Process -FilePath $Exe -PassThru

    if (-not $Headless) { Remove-Item Env:\INSTSANITY_HEADLESS -ErrorAction SilentlyContinue }

    return $process
}

Set-Content -Path $logPath -Value "smoke test $(Get-Date -Format o)"

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Annotation 'error' 'smoke-test' "Executable not found: $Exe"
    throw "Executable not found: $Exe"
}

$Exe = (Resolve-Path -LiteralPath $Exe).Path

# ── phase 1: backend, deterministic ────────────────────────────────────────────
$process = $null
try {
    Write-Step "phase 1: launching $Exe (headless)"
    $process = Start-App -Headless $true

    Write-Step "waiting for $baseUrl/__health"
    $health = Wait-ForHost -Process $process

    Write-Detail "host      ok=$($health.ok), port=$($health.port), uptime=$($health.uptimeSeconds)s"
    Write-Detail "web root  $($health.webRoot) (exists=$($health.webRootExists))"
    Write-Detail "endpoints $($health.endpoints -join ', ')"

    if ($health.endpoints -notcontains '/api/browse-folder') {
        throw "The native folder picker route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/download-selected') {
        throw "The selected-photo download route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/download-history') {
        throw "The persistent download-history route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/resize-upload' -or
        $health.endpoints -notcontains '/api/resize-convert') {
        throw "The photo-resizer upload or conversion route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/open-download-folder') {
        throw "The Open folder route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/exit-app') {
        throw "The app-exit route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/open-instagram-browser') {
        throw "The native Instagram browser route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/browser-view') {
        throw "The Browser page control route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/download-history') {
        throw "The download-history route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/instagram-session') {
        throw "The Instagram session route is missing from the running app."
    }

    if ($health.endpoints -notcontains '/api/instagram-logout') {
        throw "The Instagram logout route is missing from the running app."
    }

    if (-not $health.webRootExists) {
        throw "The app is running but the web layer was not found next to the executable (looked in $($health.webRoot))."
    }

    Write-Step 'fetching the web app document'
    $document = Invoke-WebRequest -Uri "$baseUrl/" -TimeoutSec 15 -UseBasicParsing

    if ($document.StatusCode -ne 200) { throw "GET / returned $($document.StatusCode)." }

    # String.Contains, not -like: the marker contains brackets, which PowerShell wildcards
    # would read as a character class.
    foreach ($marker in @('In[st]Sanity', 'lsBoot', 'app-startup-blocked', 'appVersionButton', 'openDownloadFolderBtn', 'startup-layout-v1-4', 'window.ResizeObserver', 'window.visualViewport', 'id="videoDownloadNotice"', 'id="downloadHistoryToggleBtn"', 'id="detailDownloadHistoryToggleBtn"', 'const DOWNLOAD_HISTORY_OFF_ICON=', 'button.innerHTML=showDownloadHistory?DOWNLOAD_HISTORY_ICON:DOWNLOAD_HISTORY_OFF_ICON', '.active-outline-control.active', '.download-history-item-mark{top:48px;left:10px;right:auto', 'function renderPostDownloadHistoryMark', 'function renderMediaDownloadHistoryMark', 'function recordPhotoDownloadHistory', 'function recordVideoDownloadHistory', 'window.__getVideoDownloadHistoryContext=getVideoDownloadHistoryContext', 'window.__recordVideoDownloadHistory=recordVideoDownloadHistory', '/api/download-history', 'window.showVideoDownloadNotice=showVideoDownloadNotice', 'window.getConfiguredDownloadFolder=getConfiguredDownloadFolder', 'id="browserNav"', 'id="resizerNav"', 'id="resizerPage"', 'id="resizerFolderInput"', 'webkitdirectory', 'id="resizerMaxSide"', 'id="resizerUploadedCount"', 'id="resizerStartBtn"', 'id="resizerOutputFolder"', 'window.confirm(', 'function uploadResizerFiles', 'function startPhotoResizing', 'function chooseResizerOutputFolder', 'Start converting', '/api/resize-upload', '/api/resize-convert', 'id="browserAddressInput"', 'id="browserBackBtn"', 'id="browserForwardBtn"', 'id="browserReloadBtn"', '.browser-bar .browser-nav-btn svg{width:21px;height:21px}', '.browser-bar #browserReloadBtn svg{width:17px;height:17px}', 'id="topCollapseBtn"', '.icon-btn.collapse-window-btn svg{width:19px;height:19px}', 'id="directPostUrlInput"', 'padding:10px 42px 42px', 'function openInstagramBrowser', '/api/open-instagram-browser', '.save-url-post-modal{width:100%;max-width:100%;max-height:none;overflow:visible;margin:0 auto}', 'save-url-post-modal', 'function resetSavedPostsResolutionState(options={})', 'loadInstagramSession(true,true)', 'Reload saved posts using the current Instagram session', "card.classList.add('collapsed');", '${selPosts}/${selTotal}', 'function snapshotSelectedPhotoIndices', 'function beginDownloadRun', 'function setDownloadProgressMessage', 'displayToastMessage(message,0,false,true)', 'displayToastMessage(message,6500,false,true)', 'Downloaded and saved', 'const batchSize=4;', '.status.download-progress{max-width:min(92vw,760px);white-space:normal;text-align:center;line-height:1.45;overflow-wrap:anywhere}', '.toast-slot .status{position:static', 'Version 1.153; open version history', "const APP_VERSION='1.153';", "version:'1.148',added:'No new features.',removed:'Rate-limit warnings without the upstream retry-delay value.',fixed:'The throttled Instagram 429 warning now includes the raw Retry-After response header, or says it was not provided, so rate-limit cooldowns can be observed without restoring per-request log spam.',tone:'green'", "version:'1.147',added:'No new features.',removed:'Repeated per-post upstream status messages while collection photo totals resolve.',fixed:'Collection summaries now run in smaller, slower background batches after visible cards finish. A 429 triggers exponential backoff up to five minutes, and rate-limited posts retry after the resolver cache expires, expected 404s are silent and other per-post status details are Debug-only, with a single throttled 429 warning. Carousel media stays lazy-loaded.',tone:'green'",  "version:'1.146',added:'No new features.',removed:'No removals.',fixed:'HTML responses from Instagram and Save-Free are detected before JSON parsing, so login/challenge pages use the existing fallback path instead of raising JsonReaderException.',tone:'green'", "version:'1.145',added:'Collection post/photo totals on All Posts.',removed:'The need to manually visit collection pages for photo totals.',fixed:'All Posts shows posts/photos, marks unresolved photo totals with a plus while lightweight summary-only background batches finish, and never preloads carousel media. All Posts and Selected keep matching widths.',tone:'green'", "version:'1.143',added:'No new features.',removed:'Collection photo totals waiting for manual page-by-page visits.',fixed:'Selecting the whole collection now resolves lightweight card summaries in background batches, so selected-photo counts refresh across every page without loading carousel media.',tone:'green'", "version:'1.142',added:'No new features.',removed:'Download progress pop-ups appearing over the pagination controls.',fixed:'Persistent download progress and photo counts stay inside the pages bar, between the page controls and Download actions.',tone:'green'", 'function getPostSelectablePhotoCount', 'post.allPhotosSelected=Boolean(selected)', 'post.selectedIndices=Array.from({length:Math.max(0,photoCount)},(_,index)=>index)', 'preserveDeclaredCounts', 'selectedPhotoCount+=selectedIndicesForPost.length-selectedPhotoIndices.size', 'const needsAllPhotos=allPhotosSelected', "version:'1.141',added:'Collection-wide selection now includes every photo in posts whose media is still lazy-loaded.',removed:'Select all stopping at each post’s first known photo when full carousel counts arrived later.',fixed:'Collection and page selections retain an all-photos intent without preloading media. As each post’s counts resolve, selection and download plans expand to its full carousel.',tone:'green'", "version:'1.140',added:'Selected now shows the number of selected posts and selected photos.',removed:'Photo batches saving out of order and download progress notices expiring mid-batch.',fixed:'Selected reads post/photo counts. Batch photo fetches remain concurrent while files are written in request order; download progress stays visible and reports photo preparation, processing, and saves through completion.',tone:'green'",  "version:'1.139',added:'No new features.',removed:'The native caption title and stale Saved Posts media surviving an Instagram sign-in change.',fixed:'Removed the native window title; settings panels now start collapsed. Sign-in/out and Reload posts force fresh session-aware post resolution. Save-via-URL retains its full-width layout.',tone:'green'", "version:'1.138',added:'No new features.',removed:'The infinity collection-selector glyph touching the SVG horizontal edges.',fixed:'Expanded the SVG viewBox and reserved horizontal breathing room so both sides of the infinity icon render fully without changing collection-selection behavior or button size.',tone:'green'", "version:'1.137',added:'No new features.',removed:'Empty Instagram caption-edge arrays aborting the resolver and exception details being hidden from the Debug output.',fixed:'Caption-edge access now checks for an item before indexing. Debug builds route ILogger output to Visual Studio, and resolver failures include the exception type.',tone:'green'", "version:'1.136',added:'No new features.',removed:'The blanket rejection of Instagram size-preview URLs and Open Graph-only media when opening a post.',fixed:'Temporarily restored display_url-first selection and allowed preview URLs through card, detail, full-size, and download paths. Target-post identity scoping remains unchanged for this rollback test.',tone:'yellow'", "version:'1.135',added:'No new features.',removed:'No diagnostic clue when size-limited previews were filtered and no full-size media reached the detail view.',fixed:'The empty detail state now distinguishes preview-only responses from resolver failures and displays the failure reason. Cropped preview URLs remain excluded from the viewer and downloads.',tone:'green'", "version:'1.134',added:'No new features.',removed:'Target media being discarded when display_url pointed to an Instagram size-preview URL despite full-size candidates being present.',fixed:'The resolver now skips /media/?size= preview URLs when choosing slide images and continues through the target post’s image_versions2/display_resources candidates. Preview-only URLs remain blocked from the viewer and downloads.',tone:'green'", "version:'1.133',added:'No new features.',removed:'Valid post media nested below a matched preview/identity wrapper being skipped.',fixed:'The resolver anchors to the requested shortcode or media ID, then searches only recognized media-holder fields inside that post subtree. Carousel payloads take priority over a single preview image, display_uri is supported, and unrelated page images remain excluded.',tone:'green'"", "version:'1.132',added:'No new features.',removed:'Unverified page-wide scontent URLs being presented as carousel photos.',fixed:'Embedded Instagram media is accepted only from JSON matching the requested shortcode or its exact numeric media ID; arbitrary page-wide scontent URLs can no longer become post slides. Unrelated recommendations and account images are ignored. Save-Free/media-info fallbacks, logged-in behavior, and visible-card source-label updates remain intact.',tone:'green'"", "version:'1.131',added:'No new features.',removed:'Logged-out carousel posts showing only a cropped preview, and visible-page source labels disappearing when a post was opened during resolution.',fixed:'Recover full carousels from Instagram scontent URLs embedded in public page HTML. Visible-page summary results now still update source labels when a post is opened, with unresolved labels retried sooner.',tone:'green'", "version:'1.130',added:'No new features.',removed:'The still oversized infinity and circled 1 selection icons.',fixed:'Reduced the collection and current-page selector icons by another 15%, without changing button sizes or layout.',tone:'green'", "version:'1.129',added:'No new features.',removed:'The oversized infinity and circled 1 collection/page selection icons.',fixed:'Reduced both selector icons by 15% while leaving button sizes and toolbar layout unchanged.',tone:'green'", "version:'1.128',added:'No new features.',removed:'Cropped Open Graph preview URLs being kept as full-size media, and API-only posts marked Mixed.',fixed:'Logged-out posts prefer Save-Free slides or Instagram media-info over OG previews; source badges reflect resolved slide origins, and cropped OG covers stay card-only if originals are unavailable. The authenticated verdict remains unchanged.',tone:'green'", "version:'1.127',added:'No new features.',removed:'Source labels appearing only after opening a post while logged out.',fixed:'Visible-page source labels now resolve for anonymous posts too, using the media-info fallback when public page parsing cannot determine provenance.',tone:'green'", "version:'1.126',added:'No new features.',removed:'The unpredictable startup window position.',fixed:'The app window now opens centered on the primary display’s usable area.',tone:'green'", "version:'1.125',added:'No new features.',removed:'The duplicate app name in the native Windows title bar.',fixed:'Cleared the native caption text while preserving the window icon and caption controls.',tone:'green'", "version:'1.124',added:'A toggle for current-page photo selection.',removed:'No removals.',fixed:'Clicking the current-page selector again deselects that page when all its photos are already selected, without affecting other pages.',tone:'green'", "version:'1.123',added:'A detail-header status for photo and video downloads.',removed:'Text on the Deselect all posts icon button, generic selection glyphs, and the sidebar resize handle.',fixed:'Removed persisted sidebar resizing; the sidebar stays at its fixed 244px width. Collapse window now uses two monochrome diagonal arrows with larger inward-facing tips. The collection-wide selector uses an infinity icon and the current-page selector a circled 1. Selection buttons now play one click sound. Back and Forward use matching curved history arrows in monochrome; Reload is a compact 17px circular arrow. All Posts and Selected (N) share a width. Detail downloads show “Downloading N photos” or “Downloading video” at the Select all photos button’s height.',tone:'yellow'", 'id="allPostsFilter"', 'id="deselectAllBtn"', 'function showPhotoDownloadNotice', 'Downloading video', 'function updateAllPostsCollectionTotal()', 'function resolveCollectionSummariesInBackground()', 'resolvePostsBatchChunk(chunk,token,null,true,true)', 'All Posts (${posts.length}/${photoCountLabel})', 'collectionSummaryResolutionList!==posts', 'width:180px; min-width:180px', 'min-height:37px; padding:0 12px', "version:'1.122',added:'Select-all actions for the full collection and current page.',removed:'The single-row library controls and icon-only deselect button.',fixed:'Split filters and selection controls into two rows aligned with Sort and Search. Selection can cover every photo in the collection or just the visible page, with a Deselect all posts action.',tone:'yellow'", 'toolbar-row-top', 'toolbar-row-selection', 'id="selectAllCollectionBtn"', 'id="selectCurrentPageBtn"', '.icon-filter-btn.scope-selection-btn svg { width:13.73px; height:13.73px; }', '#selectAllCollectionBtn.scope-selection-btn svg { width:15px; height:15px; }', 'viewBox="-1 0 26 24" width="15" height="15"', 'function selectCurrentPagePhotos()', 'Toggle selection of all photos on the current page', 'function needsSourceKindResolution(post)', 'Missing labels are resolved for posts on the visible page or when opened', 'Deselected all photos on this page', 'function selectAllCollectionPhotos()', 'if(selectedCount>0)resolveSelectedCollectionSummariesInBackground()', 'function resolveSelectedCollectionSummariesInBackground', 'resolvePostsBatchChunk(chunk,token,null,true,true)', 'function selectCurrentPagePhotos()', "version:'1.121',added:'No new features.',removed:'No removals.',fixed:'Moved the backdrop blur onto the sidebar surface, matching the page bar’s 16px glass blur for themed palettes while retaining the default theme’s 20px blur. Text and controls stay sharp.',tone:'green'", "version:'1.120',added:'No new features.',removed:'The 60% sidebar surface treatment.',fixed:'Raised the blurred sidebar surface opacity to 82% across all themes, reducing background-image bleed-through without fading sidebar labels or controls.',tone:'green'", 'body .sidebar { isolation:isolate; background:transparent; backdrop-filter:blur(16px); }', 'background:var(--t-sidebar-bg); opacity:.82;', "version:'1.119',added:'No new features.',removed:'The opaque sidebar surface and off-theme blank media tiles.',fixed:'Sidebar and library surfaces now use theme-aware glass across all themes, including default, without fading sidebar contents. Blank/loading carousel media and export-link accents follow the active palette.',tone:'green'", "version:'1.118',added:'No new features.',removed:'The generic WebView2 right-click menu outside video.',fixed:'Preserved the native video menu (Download, playback speed, Picture in picture) while suppressing default menus on other right-click targets.',tone:'green'", "label.textContent='v.'+APP_VERSION;", 'transform:translateX(10pt);',  "version:'1.113',added:'No new features.',removed:'No removals.',fixed:'Fixed the first-load loading scene when the MAUI WebView viewport changes by rebuilding each variant at the newly measured dimensions, rather than only changing the SVG viewBox. Loading screens now default to Off for new installs; saved On/Off preferences remain in effect.',tone:'green'", 'id="lsScreensBtn" aria-pressed="false"', 'Loading screens: Off', 'var lsScreensEnabled = false;', "lsScreensEnabled = (v === '1');", 'function rebuildBootSceneAtTime(refreshTime)', 'host.replaceChild(nextStage,stage)', 'rebuildBootSceneAtTime(refreshTime)', "version:'1.112',added:'No new features.',removed:'No removals.',fixed:'The photo full-size button now uses active-theme colors, and the Browser tab’s exit prompt hides the native WebView; choosing “Not yet” restores it.',tone:'green'", 'background:var(--t-input-bg,rgba(11,15,20,.82))', "version:'1.110',added:'No new features.',removed:'No removals.',fixed:'Release labels now use a three-digit minor sequence: 1.099 is followed by 1.100 and later releases stay in major version 1.',tone:'green'", "version:'1.105',added:'No new features.'", '.picture-zoom-btn:hover,.picture-zoom-btn:focus-visible', 'var(--t-btn-bg', "version:'1.111',added:'No new features.',removed:'No removals.',fixed:'Loading-screen loudness now defaults to 10%, the Not-yet taunt’s big-hall reverb send is 30% lower, and video downloads cancel rather than fall back if the Settings folder is unavailable; saved slider preferences remain unchanged.',tone:'yellow'", "version:'1.044'", "version:'1.043'", "version:'1.042'", "version:'1.041'", "version:'1.040'", "version:'1.039'", '.app-version-button { position:fixed; right:14px; bottom:8px;', 'justify-content:space-between;align-items:center;gap:14px;flex-wrap:nowrap;margin-top:19px', 'class="footer-actions"', '&w=480', 'wrapProxiedImageUrl(pic.rawUrl,true)', 'wrapProxiedImageUrl(pic.thumbnailUrl,true)', 'Saved Posts JSON', 'onDirectPostUrlInput', 'Downloading photos', 'Exit button: ${active', '.detail-head .detail-actions{align-self:flex-end}', 'deselectAllBtn', 'clearAllSelections', 'buildCardPostedHtml', 'meta-sep', 'id="toastSlot"', '.toast-slot{', '.card-meta-value.card-posted{', 'id="logoutConfirmModal"', 'showLogoutConfirm', 'confirmLogout', 'max-height:none;overflow:visible;margin:0 auto', 'version-tag', 'tone-green', 'tone-yellow', "pages bar.',tone:'yellow'", 'tone-red', 'version-tone-legend', 'startViewTransition', 'handleVideoError', 'restricted-cover', 'retryPostResolve', 'warmNextPage', 'data-raw-video', 'id="browserPage"', 'id="topAccountAvatar"', 'id="igLogoutBtn"', 'onTopLoginBtnClick()', 'My Account', 'igAccountAvatarFallback', 'account-status', 'status-dot', '/api/instagram-session', '/api/instagram-logout', '/api/browser-view', 'window.handleNotYetClick=handleNotYetClick;', '__instSanityNotYetAudioTrace', 'notYetVolumeScale', 'justify-content:flex-end', 'brandName(u,CX,H-14,14)', "E.bname.setAttribute('transform',", 'if(dodge&&dodge.n===5)return;', '.icon-btn svg{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:17px;height:17px;display:block}', version:'1.090',added:'No new features.',removed:'No removals.',fixed:'Fixed blank black screen in preview and desktop — two syntax errors in the leaving-prompt and sticker-settings IIFE left the app unable to start; preview now loads again.',tone:'green'}, {version:'1.074',added:'One-click Save from Browser.',removed:'No removals.',fixed:'Browser URL bar now has a Save button at its end — it sends the current Instagram post or reel to Save via URL instantly; other pages show a quick warning toast.',tone:'green'}, {version:'1.073',added:'No new features.',removed:'No removals.',fixed:'Card now shows the correct photo/video count before you open it — a summary that came from Open Graph alone no longer freezes the badge at 1. The batch summary now falls back to save-free for counts, API-single guesses retry quickly, and opening a post heals and persists the true count.',tone:'green'}, {version:'1.072',added:'Reworked image source badges — provenance now drives the verdict.',removed:'Broken transport-sniffing that marked every proxied image as API and let card/detail disagree.',fixed:'Direct means every slide came from Instagram scontent, API means save-free had to supply it, Mixed is hybrid (direct+api in one post) or a photo+video carousel; card and detail tiles now share the same per-slide origin, video-only stays Direct, and the stray API-mislabelling/unknown fallthrough is gone.',tone:'red'}, version:'1.071',added:'No new features.',removed:'No removals.',fixed:'Video tile no longer shows a browser Download in its three-dot menu — stopping the stray download to the system folder and the top-right pop-up so only the chosen save folder is used.',tone:'green'}, version:'1.070',added:'No new features.',removed:'No removals.',fixed:'Save via URL tab no longer remembers the last post after reload — clearing its stored state on save and restore.',tone:'green'}, version:'1.069',added:'No new features.',removed:'No removals.',fixed:'Renamed historical versions 1.1–1.9 to 1.01–1.09 for consistent two-digit minor formatting.',tone:'green'}, version:'1.066',added:'All 16 sidepanel stickers rebuilt — rev9 identity pass with new silhouettes, props and live animations (equaliser, embers, glitch, thunder, hammer clash).',removed:'No removals.',fixed:'Implemented sticker-preview rev9 for every theme (default + v1–v15); added CSS keyframes + JS timers for thunder/clash and eye tracking.',tone:'red'}, version:'1.065',added:'No new features.',removed:'The Showing X–Y of Z page count and the Typography label/hint inside UI scale.',fixed:'UI settings Font size → UI scale (beta); Background moved to second below Theme; pagination now shows only Prev/Next + page numbers.',tone:'yellow'}, version:'1.064',added:'No new features.',removed:'No new features.',fixed:'Second Aatrox laugh (40% Original Laugh) 10% quieter — 0.792→0.713, fight-me stays 0.792.',tone:'green'}, version:'1.063',added:'No new features.',removed:'No new features.',fixed:'Restore loading screen — revert broken text-wrapper scaling, keep wordmark fixed everywhere via font-size scaling.',tone:'green'}, version:'1.062',added:'No new features.',removed:'No new features.',fixed:'Fix font scaling that froze the loading screen — wrap only body text and skip title/head/svg so the boot animation can finish.',tone:'green'}, version:'1.061',added:'No new features.',removed:'No new features.',fixed:'Font scaling now leaves the In[st]Sanity wordmark everywhere untouched and scales only text (via inline text wrappers + transform) so buttons, cards and modals keep their size and proportions.',tone:'green'}, version:'1.060',added:'No new features.',removed:'The whole-UI zoom on font scaling.',fixed:'Font size now scales only text (per-element font-size via --app-font-scale) instead of zooming the entire layout; slider hint updated.',tone:'green'}, version:'1.059',added:'Font size control in UI settings with reset to default.',removed:'Best-effort by design notice.',fixed:'Loading-screen loudness now 13% by default; post carousels Select all photos and Download now share the same 35px button size.',tone:'yellow'}, "version:'1.058',added:'No new features.',removed:'No removals.',fixed:'Reduce both Aatrox laughs by 10% — volume 0.88→0.792.'", "version:'1.057',added:'No new features.',removed:'No removals.',fixed:'Tweak laugh odds to 60/40 — fight-me 60%, Original Laugh 40%.'", "version:'1.056',added:'No new features.',removed:'No removals.',fixed:'Loading-screen trident hand 30% smaller on v1 — impHand inside tridentFork now scaleFac*0.7.'", "version:'1.055',added:'Added second laugh — Aatrox_Original_Laugh_0.ogg (no cuts) joins fight-me; weighted random 65%/35%, both through big hall with fade.'", "version:'1.054',added:'No new features.',removed:'No removals.',fixed:'Aatrox laugh extended to ~2.9s (was 2.5s) for even longer cut.'", "version:'1.053',added:'No new features.',removed:'No removals.',fixed:'Aatrox laugh extended to ~2.5s and starts ~0.38s earlier so it lands before the screen fades on all 5 variants.'", "version:'1.052',added:'No new features.',removed:'No removals.',fixed:'Loading-screen laugh swapped — Aatrox fight-me first ~2s (faded) replaces the synth demon laugh, still through the big 3.4s hall.'", "version:'1.051',added:'No new features.',removed:'No removals.',fixed:'Boot v3 Haul shoulders now ride with the sticker — tx/ty/rot/scale tracked per-frame so arms stay pinned to 34,118/126,118 through the bob + 12px drag.'", "version:'1.050',added:'No new features.',removed:'No removals.',fixed:'Trident loading screen nudge — fork/shaft moved ~6px right (CX+30.2→CX+36.2) so the prongs breathe clear of the sticker frame.'", "version:'1.049',added:'No new features.',removed:'No removals.',fixed:'All sticker arms now start from the same shoulder points (34,118/126,118) as the JSON-devour reference — v1 Trident (CX+27.5,CY+22.7), v3 Haul now over the sticker (dual 30.7,105.8/93.3,105.8), v5 (34,118), click poke (0.2125/0.7875×0.7375) and devour Trident/Snap unified; arm length and thickness unchanged.'", "version:'1.047',added:'No new features.',removed:'No removals.',fixed:'Download and Log in text on the warm themes (12-14) is now dark (#1a160f/#0f0f1a/#0a0f1e) instead of light for readable contrast on the light buttons.'", "version:'1.046',added:'No new features.',removed:'No removals.',fixed:'Theme picker now shows the four new themes (12-15) that were already in the build.'", "version:'1.045',added:'Four new themes: 12 Cinderforge Brass Soot (Twin Strike hammers+anvil), 13 Kelp Cathedral Brine Violet (Crystal Orbit), 14 Carnival Fairground Gold (Coin Medallion), 15 Sanctuary Deep Frost (Shrine Dome) with matched loading and restart art.',removed:'No removals.',fixed:'Download text now matches the sidebar button text in all four new themes (#fdf0d6/#e6e0f5/#fef3c7/#d6e2f5), and each theme carries its unique sticker shape.'"",
            "version:'1.044',added:'No new features.',removed:'The wrapped pagination bar.',fixed:'The pages bar and Download now stay on one line and scroll instead of wrapping. On small screens the bar no longer stacks.'", "version:'1.043',added:'No new features.',removed:'The small click-tridents and close-in chevrons.',fixed:'Click animations keep their original trident and hand size (loading screens stay 35% smaller), the frozen arm is still fixed, and the loading-screen chevrons now sit at 60px from the centre.'", "version:'1.042',added:'No new features.',removed:'The frozen arm/trident frame on click.',fixed:'The extending-trident arms now scale with the sticker and no longer throw when redrawing the shaft, so the hook, trident and impact ring no longer freeze mid-click.'", "version:'1.041',added:'No new features.',removed:'The loud loading-screen default.',fixed:'The loading-screen soundtrack now starts at 25% instead of 100%.'", "version:'1.040',added:'No new features.',removed:'The oversized sticker, the off-color v4 background and the unscaled hands, trident and chevrons.',fixed:'The sticker is 35% smaller (v3 20%), v4 shares the others solid background, hands, trident and chevrons scale with the sticker and v5 elbows connect.'", "version:'1.039',added:'A Log Out confirmation pop-up.',removed:'The stray middle dot reopening after closing a post and the app forgetting your Instagram username on restart.',fixed:'The account name is remembered between launches and resolved from the signed-in session itself, post cards keep the date left and likes right after closing them, and Log Out asks for confirmation before clearing the session.'", "version:'1.038',added:'No new features.',removed:'The pop-up floating above the pages bar and the card likes sitting next to the date.',fixed:'The pop-up lives inside the pages bar between the page controls and Download, the card likes sit on the right of the Posted row, and the signed-in username is read from your profile link and profile page URL so the account finally names you.'", "version:'1.037',added:'A deselect-all icon button beside the Selected filter.',removed:'The selection and sort state sticking around after a restart, the cramped Posted row spacing, and the dot between the date and likes on post cards.',fixed:'The app reopens with a clean selection and Newest sort, restores your signed-in account and a warm Instagram page at startup, keeps the card actions bottom-aligned like the Save via URL card, and spreads the Posted rows out with the card likes pushed right.'", "version:'1.036',added:'No new features.',removed:'The empty gap under the link field in the Save via URL card and the tight Post link label.',fixed:'The Post link label breathes above its input, the resolved post tucks right under the link field, the card actions sit at the bottom of their header, and the exit prompt closes the in-app browser first so it never hides behind it.'", "version:'1.035',added:'No new features.',removed:'The empty divider strips above the first section in the settings cards, the Top-right wording on the exit toggle, and the download pop-up sitting over the pages bar.',fixed:'Settings sections start right after their card text, the toggle reads Exit button with its state, and the download and folder pop-ups land between the page controls and Download, the download one simply reading Downloading photos.'", "version:'1.034',added:'No new features.',removed:'The button that loaded a pasted link, the duplicate selected-photos bar, the sort label, and the gaps at the sides of the pages bar.',fixed:'The address bar reads the live page even after profile hops, the account button shows your name and picture, Restart closes the in-app browser, links load as you paste them, in-post thumbnails stay light, Exit and the account button trade places, and the tab reads Saved Posts JSON.'", "version:'1.033',added:'No new features.',removed:'The XAML parse error that stopped the app building.',fixed:'The version footer escapes its ampersand, so the shell XAML parses and the project compiles again.'", "version:'1.032',added:'No new features.',removed:'The address bar freeze and the anonymous Signed in label.',fixed:'The URL bar tracks every page you open, including Instagram in-app transitions, and the account probe finds your username and profile picture so the top-right button and Settings show your name.'", "version:'1.031',added:'No new features.',removed:'The Browser tab heading block, Go and close buttons and hint, the Save via URL card header and notes, and the empty strip above the Saved posts toolbar.',fixed:'The Browser tab is nearly all viewport, Save via URL is just the link field with a themed input and a matching IG icon, and the toolbar sits right under the top bar.'", "version:'1.030',added:'No new features.',removed:'The static Account and My profile labels, the plain top-right icon, and the undersized Log Out button.',fixed:'Your profile pic and name head the account card with a green-dot Signed in or red-dot signed-out status, Log Out matches Open Browser, and the top-right button shows your username and profile pic.'", "version:'1.029',added:'No new features.',removed:'Build errors around the browser events, cookie expiry copy, and thumbnail decoding.',fixed:'Browser event handlers match their delegates, WebView2 cookie expiry converts from its timestamp, and BitmapDecoder no longer pretends to be disposable.'", "version:'1.028',added:'The yellow mark now flags releases packed with many small changes.',removed:'Yellow standing only for medium-sized changes.',fixed:'Bundled-fix releases like 1.23 read yellow at a glance.'", "version:'1.027',added:'Color-coded version history: every release is soft green, yellow, or red by change size.',removed:'The plain unmarked version numbers in the history.',fixed:'Every release note now shows its change size at a glance.'", "version:'1.026',added:'Signed-in resolution via the browser session, restricted-post cards with Retry and Open in Browser, and background warming of the next page.',removed:'Full-size cover downloads for the grid and raw CDN video playback.',fixed:'Page turns no longer stutter, thumbnails are resized in-app, videos replay from cache, and media responses revalidate with 304s.'", "version:'1.025',added:'No new features.',removed:'The flat square background behind the app icon.',fixed:'Black hole loading text rides the sticker and matches the glitch screen; the icon art fills its frame with transparent corners.'", "version:'1.024',added:'A themed in-app Browser page with your account in the top bar and My Account settings.',removed:'The separate native browser window that covered the whole app.',fixed:'Docked the browser into the content area and moved Download and Open-folder into the pages bar.'", "version:'1.023',added:'No new features.',removed:'Loading screens reacting to the Imp idle toggle, and the old bars taskbar icon.',fixed:'Loading screens always animate, sticker clicks advance reliably, the chevrons wordmark matches the glitch screen, and the version sits bottom-right above the right-aligned pages bar.'", "version:'1.022',added:'No new features.',removed:'The icon sitting slightly off-center in its button.',fixed:'Centered button icons exactly, pixel for pixel.'", 'class="main-shell"', '.main-shell>.topbar{position:relative;z-index:20}', 'main{flex:1 1 auto;min-width:0;min-height:0;height:auto;overflow-y:auto;', "version:'1.021',added:'No new features.',removed:'Post cards drawing over the top-bar divider.',fixed:'Kept scrolling content strictly below the top-bar line.'", "version:'1.020',added:'No new features.',removed:'The sticky header background and blur.',fixed:'Kept the header pinned while restoring its transparent appearance.'", "version:'1.019',added:'A sticky top bar that keeps the main controls visible while scrolling.',removed:'The header scrolling out of view.',fixed:'Kept the top bar pinned while page content scrolls.'", "version:'1.018',added:'Distinctive sticker designs for themes 4, 5, and 8-11.',removed:'The previous crown and frame art in the refreshed themes.',fixed:'Changed only the Theme 4 crown, kept Theme 3 unchanged, and renamed the exit panel to LET ME OUT!.'", "version:'1.017',added:'A native in-app Instagram browser.',removed:'The simulated Instagram login form and startup session probe.',fixed:'Routed Instagram browsing through a persistent WebView2 profile; sign-in stays on Instagram.'", "version:'1.016',added:'No new features.',removed:'The fixed white ", 'Made its neutral text follow the active theme.', "version:'1.015',added:'Cursor-tracking eyes on the exit-prompt sticker.',removed:'Sidebar sticker click reactions while the prompt is open.',fixed:'Blocked click and JSON-throw animations during the prompt.'", "version:'1.014',added:'No new features.", 'CAUGHT_PROMPT_LEAD_MS=500', 'onclick="showLeavingPrompt()"', 'width:177px; height:177px', 'font:inherit', 'color:var(--t-muted,#94a3b8)', '#leavingYesButton:hover,#leavingNotYetButton:hover', 'leavingPromptTitleText', 'function setTextSliceCuts', 'function drawLeavingCackle', 'function updateLeavingStickerEyes(clientX,clientY)', 'updateLeavingStickerEyes(e.clientX,e.clientY)', 'leaving-sad-eyes-track', 'if(document.body.classList.contains(''leaving-prompt-open''))return;', 'data-concept="02"', 'leavingCackleBody', 'font-size:24.5px; line-height:1.2; text-align:left; justify-self:start', 'id="topExitAppBtn"', 'id="topExitModeToggle"', 'On: click the top-right exit icon to open the same prompt.', "leavingPromptRestoreBrowser=Boolean(browserPage&&browserPage.style.display==='block');", "if(leavingPromptRestoreBrowser)sendBrowserView({action:'hide'});", "sendBrowserView({action:'show'})", "const restoreBrowser=leavingPromptRestoreBrowser;", 'LET ME OUT!', 'Leaving already?', 'id="leavingNotYetButton"', "fetch('/api/exit-app'", 'background:linear-gradient(150deg,#11170f,#1a2115 58%,#10150e)', 'style="background:#A9C46A"', 'text-align:left; justify-self:start', 'grid-template-columns:minmax(0,1fr)', 'justify-content:center!important', "openFolderBtn.style.display=hasJsonLoaded?'grid':'none'", '11. Moss &amp; Brass', 'data-theme-id="v11"', "version:'1.005',added:'Moss & Brass", "v11:'#11170f'", 'body[data-theme="v11"] .ls-restart-ring', 'source-toggle-button', 'id="railToggle"', 'body[data-theme="v3"] .page-btn.active:hover:not(:disabled)')) {
        if (-not $document.Content.Contains($marker)) {
            throw "The served document does not contain the expected marker '$marker'."
        }
    }

    $downloadSummaryStart = $document.Content.IndexOf('function formatPhotoDownloadSummary(')
    if ($downloadSummaryStart -lt 0) {
        throw 'The photo download completion summary is missing.'
    }
    $downloadSummaryEnd = $document.Content.IndexOf('function snapshotSelectedPhotoIndices', $downloadSummaryStart)
    if ($downloadSummaryEnd -le $downloadSummaryStart) {
        throw 'The photo download completion summary could not be inspected.'
    }
    $downloadSummarySource = $document.Content.Substring($downloadSummaryStart, $downloadSummaryEnd - $downloadSummaryStart)
    if ($downloadSummarySource.Contains('targetFolder')) {
        throw 'Download completion messages must not expose the saved folder path.'
    }

    $downloadBuilderStart = $document.Content.IndexOf('function buildDownloadItemsForPost(')
    if ($downloadBuilderStart -lt 0) {
        throw 'The photo filename builder is missing.'
    }
    $downloadBuilderEnd = $document.Content.IndexOf('async function performPhotoDownload(', $downloadBuilderStart)
    if ($downloadBuilderEnd -le $downloadBuilderStart) {
        throw 'The download request handler is missing.'
    }
    $downloadBuilderSource = $document.Content.Substring($downloadBuilderStart, $downloadBuilderEnd - $downloadBuilderStart)
    if (-not $document.Content.Contains('id="noDuplicatesBtn"') -or
        -not $document.Content.Contains('function toggleNoDuplicates()') -or
        -not $document.Content.Contains("getUiPref('noDuplicates',true)") -or
        -not $document.Content.Contains('items:batch,noDuplicates') -or
        -not $document.Content.Contains('version:''1.153'',added:') -or
        -not $downloadBuilderSource.Contains('filename:`${sc}_${photoOrd+1}${ext}`') -or
        $downloadBuilderSource.Contains('_photo_')) {
        throw 'Default-on No duplicates controls, the current release note, and shortcode_N filenames must be present.'
    }

    if (-not $document.Content.Contains('grid-template-columns:244px minmax(0,1fr)')) {
        throw 'The sidebar must retain its fixed 244px layout width.'
    }
    if ($document.Content -match 'sidebar-resize|sidebarResize|startSidebarResize|keyboardSidebarResize|setSidebarWidth|sidebarWidthV2') {
        throw 'Unexpected sidebar resizing behavior is still present in the web document.'
    }

    $bootstrapperPath = Join-Path $PSScriptRoot '..\src\InStSanity\Platforms\Windows\WebViewBootstrapper.cs'
    $bootstrapperSource = Get-Content -LiteralPath $bootstrapperPath -Raw
    $videoMenuPreserved = (
        $bootstrapperSource -match 'core\.Settings\.AreDefaultContextMenusEnabled\s*=\s*true\s*;' -and
        $bootstrapperSource -match 'core\.ContextMenuRequested\s*\+=' -and
        $bootstrapperSource -match 'ContextMenuTarget\.Kind\s*!=\s*CoreWebView2ContextMenuTargetKind\.Video' -and
        $bootstrapperSource -match 'args\.Handled\s*=\s*true\s*;' -and
        $bootstrapperSource -match 'CoreWebView2DownloadState\.Completed' -and
        $bootstrapperSource.Contains('__getVideoDownloadHistoryContext') -and
        $bootstrapperSource.Contains('__recordVideoDownloadHistory') -and
        $bootstrapperSource -notmatch 'AreDefaultContextMenusEnabled\s*=\s*false\s*;'
    )
    if (-not $videoMenuPreserved) {
        throw 'The native video context menu must remain available while generic WebView2 menus are suppressed.'
    }

    $projectPath = Join-Path $PSScriptRoot '..\src\InStSanity\InStSanity.csproj'
    $projectSource = Get-Content -LiteralPath $projectPath -Raw
    if (-not $projectSource.Contains('<ApplicationDisplayVersion>1.153</ApplicationDisplayVersion>') -or
        -not $projectSource.Contains('<ApplicationVersion>22</ApplicationVersion>')) {
        throw 'The display version and integer application version code must be bumped for this release.'
    }

    $resizeUploadSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\ResizeUploadEndpoint.cs') -Raw
    $resizeConvertSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\ResizeConvertEndpoint.cs') -Raw
    $resizeServiceSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Images\PhotoResizeService.cs') -Raw
    $imageResizerSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\ImageResizer.cs') -Raw
    $mauiSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\MauiProgram.cs') -Raw
    if (-not $resizeUploadSource.Contains('Route => "/api/resize-upload"') -or
        -not $resizeConvertSource.Contains('Route => "/api/resize-convert"') -or
        -not $resizeServiceSource.Contains('FileMode.CreateNew') -or
        -not $resizeServiceSource.Contains('TryResizeToMaxSideAsync') -or
        -not $imageResizerSource.Contains('ScaledHeight = targetHeight') -or
        -not $resizeServiceSource.Contains('~/Pictures/Instagram/Resized') -or
        -not $mauiSource.Contains('AddSingleton<PhotoResizeService>')) {
        throw 'The photo-resizer endpoints, staged upload, non-overwriting output, max-side conversion, and service registration must be present.'
    }
    $instagramHttpPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\InstagramHttp.cs'
    $instagramHttpSource = Get-Content -LiteralPath $instagramHttpPath -Raw
    $instagramResolverSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\InstagramResolver.cs') -Raw
    $saveFreeSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\SaveFreeClient.cs') -Raw
    if (-not $instagramHttpSource.Contains('internal static bool HasJsonValuePrefix') -or
        -not $instagramResolverSource.Contains('HasJsonValuePrefix(body)') -or
        -not $saveFreeSource.Contains('HasJsonValuePrefix(body)')) {
        throw 'Upstream HTML responses must be rejected before JSON parsing.'
    }
    if (-not $instagramResolverSource.Contains('void LogRateLimitOnce(HttpResponseMessage response, string source)') -or
        -not $instagramResolverSource.Contains('response.Headers.TryGetValues("Retry-After"') -or
        -not $instagramResolverSource.Contains('Retry-After: {RetryAfter}') -or
        -not $instagramResolverSource.Contains('repeat notices are suppressed for 1 minute') -or
        -not $instagramResolverSource.Contains('if (infoReason == "http_429")') -or
        -not $instagramResolverSource.Contains('response.StatusCode != HttpStatusCode.NotFound') -or
        $instagramResolverSource -match '_logger\.LogInformation\(\s*"(?:Media info: upstream|Resolve fetch: upstream)') {
        throw 'Expected 404s must stay quiet, and 429 logging must include the captured Retry-After header without flooding the log.'
    }

    if ($document.Content -notmatch '#libraryPage #allPostsFilter,\s*#libraryPage #selectedFilter \{ width:180px; min-width:180px;') {
        throw 'All Posts and Selected must retain matching widths that fit their count labels.'
    }
    if ($document.Content.Contains('resolveCollectionSummariesInBackground') -or
        $document.Content.Contains('async function resolvePostsBatchChunk') -or
        -not $document.Content.Contains('function selectAllCollectionPhotos(){return}') -or
        -not $document.Content.Contains("collectionBtn.disabled=true;collectionBtn.setAttribute('aria-disabled','true')") -or
        -not $document.Content.Contains('All Posts (${posts.length})')) {
        throw 'Automatic collection-wide media counts and select-all-everywhere must stay disabled while the user troubleshoots.'
    }

    $sourceResolverStart = $document.Content.IndexOf('async function resolveOwnersChunk', [System.StringComparison]::Ordinal)
    $sourceResolverEnd = $document.Content.IndexOf('function startBackgroundAllPostsIndexing', $sourceResolverStart, [System.StringComparison]::Ordinal)
    if ($sourceResolverStart -lt 0 -or $sourceResolverEnd -le $sourceResolverStart) {
        throw 'The lightweight visible-card source resolver was not found.'
    }
    $sourceResolver = $document.Content.Substring($sourceResolverStart, $sourceResolverEnd - $sourceResolverStart)
    if (-not $sourceResolver.Contains('summary-only endpoint supplies source labels/account names, not photo totals or carousel media') -or
        -not $document.Content.Contains("resolveOwnersChunk(chunk,token,'currentPage',controller?.signal||null)")) {
        throw 'Visible-card source labels must remain lightweight without triggering automatic photo-count resolution.'
    }

    $downloadEndpointPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\DownloadSelectedEndpoint.cs'
    $downloadEndpointSource = Get-Content -LiteralPath $downloadEndpointPath -Raw
    if ($downloadEndpointSource -notmatch 'MaxDegreeOfParallelism\s*=\s*Parallelism' -or
        $downloadEndpointSource -notmatch 'writeTurns' -or
        $downloadEndpointSource -notmatch 'writeTurn\.WaitAsync\(cancellationToken\)') {
        throw 'Selected photo payloads may download concurrently, but files must be written in request order.'
    }
    if ($downloadEndpointSource -notmatch 'ReadBoolean\(root, "noDuplicates"\)' -or
        $downloadEndpointSource -notmatch 'FindExistingPhotoPath' -or
        $downloadEndpointSource -notmatch 'Skipped: true' -or
        $downloadEndpointSource -notmatch 'DownloadedAtUtc' -or
        $downloadEndpointSource -notmatch 'var skippedCount\s*=\s*files.Count\(file => file.Skipped\)') {
        throw 'The download endpoint must preserve no-duplicates semantics and return timestamps for saved or skipped files.'
    }

    $downloadHistoryEndpointPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\DownloadHistoryEndpoint.cs'
    $downloadHistoryEndpointSource = Get-Content -LiteralPath $downloadHistoryEndpointPath -Raw
    if (-not $downloadHistoryEndpointSource.Contains('POST /api/download-history') -or
        -not $downloadHistoryEndpointSource.Contains('TryParseNumberedFile') -or
        -not $downloadHistoryEndpointSource.Contains('File.GetLastWriteTimeUtc(path)')) {
        throw 'The history endpoint must import timestamps from legacy/current numbered files without reading media contents.'
    }

    if (-not $projectSource.Contains('<PackageReference Include="Microsoft.Extensions.Logging.Debug" Version="10.0.0" />')) {
        throw 'Debug logging must include its Microsoft.Extensions.Logging.Debug package.'
    }
    if ($projectSource -notmatch '<Content Include="Resources\\AppIcon\\appicon\.ico"[^>]*CopyToPublishDirectory="Never"') {
        throw 'The source app icon must not be published twice; MauiIcon supplies the publish appicon.ico.'
    }

    if (-not $projectSource.Contains('<SatelliteResourceLanguages>en;en-GB;en-US</SatelliteResourceLanguages>')) {
        throw 'The publish must preserve only English satellite resource cultures.'
    }
    $englishMuiPatternsPresent = $projectSource.Contains('Include="$(PublishDir)en\*.mui"') -and $projectSource.Contains('Include="$(PublishDir)en-*\*.mui"')
    if (-not ($englishMuiPatternsPresent -and $projectSource.Contains('Name="RemoveNonEnglishWindowsAppSdkLanguageFolders"') -and $projectSource.Contains('AfterTargets="Publish"') -and $projectSource.Contains('RemoveDir Directories="@(_NonEnglishMuiDirectory)"'))) {
        throw 'Non-English Windows App SDK MUI folders must be removed after publish.'
    }

    $programPath = Join-Path $PSScriptRoot '..\src\InStSanity\MauiProgram.cs'
    $programSource = Get-Content -LiteralPath $programPath -Raw
    if ($programSource -notmatch 'appWindow\.Title\s*=\s*string\.Empty\s*;') {
        throw 'The duplicate app name must be hidden from the native Windows title bar.'
    }

    $appPath = Join-Path $PSScriptRoot '..\src\InStSanity\App.xaml.cs'
    $appSource = Get-Content -LiteralPath $appPath -Raw
    if ($appSource -notmatch 'Title\s*=\s*headless\s*\?\s*"In-st-Sanity \(headless\)"\s*:\s*string\.Empty') {
        throw 'The native application window title must be empty for the desktop app.'
    }
    $mainPagePath = Join-Path $PSScriptRoot '..\src\InStSanity\MainPage.xaml'
    $mainPageSource = Get-Content -LiteralPath $mainPagePath -Raw
    if ($mainPageSource -notmatch 'Title=""') {
        throw 'The native page title must not restore the app name to the title bar.'
    }

    $sessionEndpointPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Browser\InstagramSessionEndpoint.cs'
    $sessionEndpointSource = Get-Content -LiteralPath $sessionEndpointPath -Raw
    $resolverPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\InstagramResolver.cs'
    $resolverSource = Get-Content -LiteralPath $resolverPath -Raw
    if ($sessionEndpointSource -notmatch 'QueryValue\("refresh"\)' -or
        $sessionEndpointSource -notmatch 'RefreshSessionAsync' -or
        $resolverSource -notmatch 'void ClearCache\(') {
        throw 'A forced session refresh must sync the native browser session and invalidate resolver cache.'
    }
    $logoutEndpointPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Browser\InstagramLogoutEndpoint.cs'
    $logoutEndpointSource = Get-Content -LiteralPath $logoutEndpointPath -Raw
    $mainPageCodePath = Join-Path $PSScriptRoot '..\src\InStSanity\MainPage.xaml.cs'
    $mainPageCodeSource = Get-Content -LiteralPath $mainPageCodePath -Raw
    if ($logoutEndpointSource -notmatch 'await\s+browserWindow\.LogoutAsync\(\)' -or
        $mainPageCodeSource -notmatch '_instagramHttp\.ClearInstagramCookies\(\)' -or
        $mainPageCodeSource -notmatch '_instagramResolver\.ClearCache\(\)') {
        throw 'Logout must await clearing the native and HTTP cookies and the resolver cache.'
    }

    if ($programSource -notmatch 'DisplayArea\.GetFromPoint\(' -or
        $programSource -notmatch 'workArea\.X\s*\+\s*\(workArea\.Width\s*-\s*windowSize\.Width\)\s*/\s*2' -or
        $programSource -notmatch 'workArea\.Y\s*\+\s*\(workArea\.Height\s*-\s*windowSize\.Height\)\s*/\s*2' -or
        $programSource -notmatch 'appWindow\.Move\(new Windows\.Graphics\.PointInt32\(x, y\)\)') {
        throw 'The app window must open centered on the primary display work area.'
    }

    $resolverPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\InstagramResolver.cs'
    $resolverSource = Get-Content -LiteralPath $resolverPath -Raw
    if (-not ($resolverSource.Contains('static JsonNode? FirstArrayItem(JsonNode? node)') -and
        $resolverSource.Contains('FirstArrayItem(media["edge_media_to_caption"]?["edges"])') -and
        $resolverSource.Contains('FirstArrayItem(identityAnchor?["edge_media_to_caption"]?["edges"])') -and
        -not $resolverSource.Contains('?["edges"]?[0]'))) {
        throw 'Empty Instagram caption-edge arrays must not be indexed directly.'
    }
    if (-not $resolverSource.Contains('reason = $"exception:{ex.GetType().Name}";')) {
        throw 'Resolver failure responses must identify the caught exception type.'
    }
    $mauiProgramPath = Join-Path $PSScriptRoot '..\src\InStSanity\MauiProgram.cs'
    $mauiProgramSource = Get-Content -LiteralPath $mauiProgramPath -Raw
    if (-not ($mauiProgramSource.Contains('#if DEBUG') -and $mauiProgramSource.Contains('builder.Logging.AddDebug();'))) {
        throw 'Debug builds must send ILogger output to Visual Studio Output.'
    }
    $resolveCoreStart = $resolverSource.IndexOf('async Task<(ResolvedPost? Post, string Reason)> ResolveCoreAsync', [System.StringComparison]::Ordinal)
    if ($resolveCoreStart -lt 0) { throw 'InstagramResolver.ResolveCoreAsync was not found.' }
    $resolveCoreEnd = $resolverSource.IndexOf('static bool NeedsSaveFree', $resolveCoreStart, [System.StringComparison]::Ordinal)
    if ($resolveCoreEnd -le $resolveCoreStart) { throw 'The resolver core boundary was not found.' }
    $resolveCoreSource = $resolverSource.Substring($resolveCoreStart, $resolveCoreEnd - $resolveCoreStart)
    $saveFreeMergeIndex = $resolveCoreSource.IndexOf('var pictures = MergeSlides(directPictures, fallback);', [System.StringComparison]::Ordinal)
    $mediaInfoPreviewFallbackIndex = $resolveCoreSource.IndexOf('if (direct is not null && !string.Equals(direct.SourceKind, "direct", StringComparison.Ordinal))', [System.StringComparison]::Ordinal)
    $slideSourceClassificationPresent = (
        $resolveCoreSource.Contains('pictures.Any(picture => string.Equals(picture.Origin, "direct", StringComparison.Ordinal))') -and
        $resolveCoreSource.Contains('pictures.Any(picture => string.Equals(picture.Origin, "api", StringComparison.Ordinal))') -and
        $resolveCoreSource.Contains('var sourceKind = authed')
    )
    $onlyDirectInstagramMediaIsMerged = (
        $resolveCoreSource.Contains('var directPictures = direct is not null &&') -and
        $resolveCoreSource.Contains('string.Equals(direct.SourceKind, "direct", StringComparison.Ordinal)')
    )
    $loggedInVerdictPreserved = (
        $resolveCoreSource.Contains('var sourceKind = authed') -and
        $resolveCoreSource.Contains('? (direct is null ? "api" : "mixed")')
    )
    if ($saveFreeMergeIndex -lt 0 -or -not $slideSourceClassificationPresent -or -not $onlyDirectInstagramMediaIsMerged -or -not $loggedInVerdictPreserved) {
        throw 'Save-Free media must replace anonymous OG preview slides while preserving the authenticated source verdict.'
    }
    if ($mediaInfoPreviewFallbackIndex -lt 0 -or
        $resolveCoreSource.IndexOf('FetchMediaInfoAsync(shortcode', $mediaInfoPreviewFallbackIndex, [System.StringComparison]::Ordinal) -lt 0) {
        throw 'When Save-Free has no slides, anonymous OG previews must yield to the media-info endpoint before fallback.'
    }
    # Regression: DNS_eFQSpn8 had unrelated page-wide scontent URLs counted as carousel slides.
    # A URL found somewhere in the HTML is not proof of membership; the JSON media object must
    # identify the requested shortcode (or its exact numeric ID) before it can supply slides.
    if ($resolveCoreSource.Contains('ExtractScontentPhotosFromHtml') -or
        $resolverSource.Contains('ScontentUrlRegex') -or
        $resolverSource.Contains('ContainsCarouselMarkup')) {
        throw 'Arbitrary page-wide scontent URLs must not be promoted to post carousel slides.'
    }
    $pageJsonStart = $resolverSource.IndexOf('static ResolvedPost? ParseFromPageJson(string html, string shortcode)', [System.StringComparison]::Ordinal)
    if ($pageJsonStart -lt 0) { throw 'The target-matched Instagram media JSON parser was not found.' }
    $identityStart = $resolverSource.IndexOf('static bool MatchesPostIdentity', $pageJsonStart, [System.StringComparison]::Ordinal)
    $payloadStart = $resolverSource.IndexOf('static JsonNode? FindMediaPayloadWithinPost', $identityStart, [System.StringComparison]::Ordinal)
    $mediaBuilderStart = $resolverSource.IndexOf('static ResolvedPost? BuildFromMedia', $payloadStart, [System.StringComparison]::Ordinal)
    if ($identityStart -le $pageJsonStart -or $payloadStart -le $identityStart -or $mediaBuilderStart -le $payloadStart) {
        throw 'The target identity and scoped media-payload parser boundaries were not found.'
    }
    $pageJsonSource = $resolverSource.Substring($pageJsonStart, $identityStart - $pageJsonStart)
    $identitySource = $resolverSource.Substring($identityStart, $payloadStart - $identityStart)
    $payloadSource = $resolverSource.Substring($payloadStart, $mediaBuilderStart - $payloadStart)
    if (-not ($pageJsonSource.Contains('ContextJsonRegex.Matches(html)') -and
        $pageJsonSource.Contains('var roots = new List<JsonNode>();') -and
        $pageJsonSource.Contains('Search every JSON block for a target carousel before accepting any single') -and
        $pageJsonSource.Contains('var expectedMediaId = ShortcodeToMediaId(shortcode);') -and
        $pageJsonSource.Contains('FindPostMediaNode(root, 0, shortcode, expectedMediaId, true, out var identityAnchor)') -and
        $pageJsonSource.Contains('FindPostMediaNode(root, 0, shortcode, expectedMediaId, false, out identityAnchor)') -and
        $pageJsonSource.Contains('BuildFromMedia(media, shortcode, identityAnchor)') -and
        $pageJsonSource.Contains('MatchesPostIdentity(obj, shortcode, expectedMediaId)') -and
        $pageJsonSource.Contains('FindMediaPayloadWithinPost(obj, 0, preferCarousel, shortcode)') -and
        $pageJsonSource.Contains('body.Contains("display_uri", StringComparison.Ordinal)') -and
        $identitySource.Contains('(!hasCode || string.Equals(code, shortcode, StringComparison.Ordinal))') -and
        $identitySource.Contains('(!hasEmbeddedShortcode || string.Equals(embeddedShortcode, shortcode, StringComparison.Ordinal))') -and
        $identitySource.Contains('if (hasCode || hasEmbeddedShortcode)') -and
        $identitySource.Contains('embeddedMediaId?.StartsWith(expectedMediaId + "_", StringComparison.Ordinal) == true') -and
        $payloadSource.Contains('HasConflictingShortcode(obj, shortcode)') -and
        $payloadSource.Contains('HasCarouselPayloadFields(obj)') -and
        $payloadSource.Contains('HasMediaPayloadFields(obj)') -and
        $payloadSource.Contains('return !preferCarousel && HasMediaPayloadFields(obj) ? obj : null;') -and
        $payloadSource.Contains('IsPostMediaContainerKey(pair.Key)') -and
        $payloadSource.Contains('"xdt_shortcode_media"') -and
        $payloadSource.Contains('"preview_media"') -and
        $payloadSource.Contains('Traverse only recognized post/media-holder keys') -and
        $payloadSource.Contains('recommendation') -and
        $payloadSource.Contains('arbitrary page branches') -and
        $resolverSource.Contains('direct = Text(slide["display_uri"]);'))) {
        throw 'Only recognized media fields scoped beneath the requested post identity may supply carousel slides.'
    }
    $carouselBuildEnd = $resolverSource.IndexOf('static ResolvedPost? ParseFromOpenGraph', $mediaBuilderStart, [System.StringComparison]::Ordinal)
    if ($carouselBuildEnd -le $mediaBuilderStart) { throw 'The media-builder boundary was not found.' }
    $mediaBuildSource = $resolverSource.Substring($mediaBuilderStart, $carouselBuildEnd - $mediaBuilderStart)
    if (-not ($mediaBuildSource.Contains('if (media["carousel_media"] is JsonArray carousel)') -and
        $mediaBuildSource.Contains('foreach (var slide in carousel)') -and
        $mediaBuildSource.Contains('media["edge_sidecar_to_children"]?["edges"] is JsonArray edges') -and
        $mediaBuildSource.Contains('if (edge?["node"] is JsonNode slide)') -and
        $mediaBuildSource.Contains('foreach (var slide in slides)') -and
        $mediaBuildSource.Contains('pictures.Add(new ResolvedMedia'))) {
        throw 'The target media holder must yield its true carousel child list, not only its cover preview.'
    }
    $pickImageStart = $resolverSource.IndexOf('static string PickImage(JsonNode slide)', [System.StringComparison]::Ordinal)
    $bestCandidateStart = $resolverSource.IndexOf('static string BestCandidate', $pickImageStart, [System.StringComparison]::Ordinal)
    $pickVideoStart = $resolverSource.IndexOf('static string PickVideoUrl', $bestCandidateStart, [System.StringComparison]::Ordinal)
    if ($pickImageStart -lt 0 -or $bestCandidateStart -le $pickImageStart -or $pickVideoStart -le $bestCandidateStart) {
        throw 'The post image-candidate selector boundaries were not found.'
    }
    $pickImageSource = $resolverSource.Substring($pickImageStart, $bestCandidateStart - $pickImageStart)
    $bestCandidateSource = $resolverSource.Substring($bestCandidateStart, $pickVideoStart - $bestCandidateStart)
    if (-not ($pickImageSource.Contains('var direct = Text(slide["display_url"]);') -and
        $pickImageSource.IndexOf('Text(slide["display_url"])', [System.StringComparison]::Ordinal) -lt $pickImageSource.IndexOf('BestCandidate(slide["image_versions2"]', [System.StringComparison]::Ordinal) -and
        -not $resolverSource.Contains('IsCroppedPreviewUrl') -and
        -not $bestCandidateSource.Contains('/media/?size='))) {
        throw 'The preview fallback experiment must restore display_url-first selection without size-preview rejection.'
    }

    if (-not $document.Content.Contains("item.contentCount") -or
        -not $document.Content.Contains('recordPhotoDownloadHistory(batch[index],file)') -or
        -not $document.Content.Contains('function renderMediaDownloadHistoryMark(post,pic,contentIndex)') -or
        -not $document.Content.Contains('window.__getVideoDownloadHistoryContext=getVideoDownloadHistoryContext')) {
        throw 'Successful photo/video downloads must persist per-media timestamps and render per-item history marks.'
    }
    if ($resolveCoreSource.Contains('if (!summaryOnly && !authed)') -or
        $resolveCoreSource.Contains('"full_media_unavailable"') -or
        -not $resolveCoreSource.Contains('return (direct, string.Empty);')) {
        throw 'The rollback experiment must allow an Open Graph preview through after media-info fails.'
    }

    $openGraphStart = $resolverSource.IndexOf('static ResolvedPost? ParseFromOpenGraph', [System.StringComparison]::Ordinal)
    if ($openGraphStart -lt 0) { throw 'InstagramResolver.ParseFromOpenGraph was not found.' }
    $openGraphEnd = $resolverSource.IndexOf('static string PickThumbnailImage', $openGraphStart, [System.StringComparison]::Ordinal)
    if ($openGraphEnd -le $openGraphStart) { throw 'The Open Graph parser boundary was not found.' }
    $openGraphSource = $resolverSource.Substring($openGraphStart, $openGraphEnd - $openGraphStart)
    if (-not $openGraphSource.Contains('FullResolved = false,')) {
        throw 'An Open Graph cover must remain a preview, not count as full-resolution media.'
    }

    $saveFreePath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\SaveFreeClient.cs'
    $saveFreeSource = Get-Content -LiteralPath $saveFreePath -Raw
    if (-not ($saveFreeSource.Contains('var bestUrl = ExtractJwtUrl(bestToken);') -and
        $saveFreeSource.Contains('bestUrl.Length > 0 ? bestUrl : thumbnailUrl;') -and
        $saveFreeSource.Contains('ImgUrl = imageUrl,') -and
        $saveFreeSource.Contains('RawUrl = imageUrl,'))) {
        throw 'Save-Free full media and the viewer/download raw URL must use its selected best image URL.'
    }

    $previewMediaFallbackPresent = (
        $document.Content.Contains('if(data.fullResolved!==undefined)post.fullResolved=Boolean(data.fullResolved);') -and
        $document.Content.Contains('Boolean(post.fullResolved||post.serverResolved)&&!post.coverIsVideo') -and
        $document.Content.Contains('Full-size media unavailable.') -and
        $document.Content.Contains('const pics=(Array.isArray(data.pictures)?data.pictures:[]).filter(Boolean);') -and
        $document.Content.Contains('const validCover=rawCover;') -and
        $document.Content.Contains("const coverRaw=post.coverUrl||'';") -and
        $document.Content.Contains('The resolver returned no target-matched media (reason: ') -and
        $document.Content.Contains("post.resolveFailedReason=post.resolveFailedReason||resolutionFailure||'unresolved';") -and
        -not $document.Content.Contains('croppedPreviewCount=') -and
        -not $document.Content.Contains("post.resolveFailedReason='cropped_preview_only'")
    )
    if (-not $previewMediaFallbackPresent) {
        throw 'The rollback experiment must pass Instagram preview media through to the detail view and download flow.'
    }

    if ($document.Content.Contains('version-tone-dot')) {
        throw 'The on-screen version label must not contain a change-tone dot.'
    }

    if ($document.Content.Contains('stickerAnimEnabled === false) animsOn = false')) {
        throw 'The Imp idle & eyes toggle must not affect loading screens.'
    }

    if ($document.Content.Contains('justify-content:flex-end;align-items:center;gap:14px;flex-wrap:wrap;margin-top:19px')) {
        throw 'The v1.23 right-aligned pages bar must be gone: pagination left, Download and Open-folder right.'
    }

    if ($document.Content.Contains('const BRAND_Y=CY+95')) {
        throw 'The black hole wordmark must ride the sticker at the glitch screen position.'
    }

    if ($document.Content.Contains('id="igSettingsStatusInput"')) {
        throw 'Settings must show My Account with pic, name, and Log Out, not the old Browser profile input.'
    }

    if ($document.Content.Contains("requestAnimationFrame(()=>document.getElementById('leavingNotYetButton')?.focus")) {
        throw 'The exit prompt must not autofocus a button.'
    }

    $saveUrlNavIndex = $document.Content.IndexOf('id="saveUrlNav"', [System.StringComparison]::Ordinal)
    $browserNavIndex = $document.Content.IndexOf('id="browserNav"', [System.StringComparison]::Ordinal)
    $sidebarStickerIndex = $document.Content.IndexOf('id="sidebarStickerSlot"', [System.StringComparison]::Ordinal)
    $saveUrlCloseIndex = if ($saveUrlNavIndex -ge 0) { $document.Content.IndexOf('</button>', $saveUrlNavIndex, [System.StringComparison]::Ordinal) } else { -1 }
    $browserButtonIndex = if ($browserNavIndex -ge 0) { $document.Content.LastIndexOf('<button', $browserNavIndex, [System.StringComparison]::Ordinal) } else { -1 }
    $betweenSaveAndBrowser = if ($saveUrlCloseIndex -ge 0 -and $browserButtonIndex -gt $saveUrlCloseIndex) {
        $document.Content.Substring($saveUrlCloseIndex + 9, $browserButtonIndex - ($saveUrlCloseIndex + 9))
    } else { 'invalid' }
    if ($saveUrlNavIndex -lt 0 -or $saveUrlCloseIndex -lt 0 -or $browserButtonIndex -le $saveUrlCloseIndex -or $browserNavIndex -le $saveUrlNavIndex -or $sidebarStickerIndex -le $browserNavIndex -or $betweenSaveAndBrowser -notmatch '^\s*$') {
        throw 'The Browser button must sit directly below Save via URL and above the sidebar sticker.'
    }

    if ($document.Content.Contains('id="igVpPass"') -or
        $document.Content.Contains('/api/browser/action') -or
        $document.Content.Contains('/api/browser/state') -or
        $document.Content.Contains('syncIgSessionState(false)')) {
        throw 'The app must not ship the simulated Instagram credential form or startup session probe.'
    }

    foreach ($obsolete in @('Choose your posts', 'id="libraryFooterNote"', '</svg>Open folder</button>', '>Hide source</button>', '>Show source</button>', 'id="sidebarRestore"', 'Load post', 'Sort by:', 'Download selected', 'max-width:calc(1180px + 84px)', 'Top-right exit button')) {
        if ($document.Content.Contains($obsolete)) {
            throw "The served document still contains obsolete library UI '$obsolete'."
        }
    }

    Write-Detail "document  GET / -> 200, $($document.RawContentLength) bytes, markers present"

    Write-Step 'checking range support (media playback depends on it)'
    $range = Invoke-WebRequest -Uri "$baseUrl/" -Headers @{ Range = 'bytes=0-99' } -TimeoutSec 15 -UseBasicParsing

    if ($range.StatusCode -ne 206) { throw "A range request returned $($range.StatusCode), expected 206." }

    Write-Detail "range     bytes=0-99 -> 206 ($($range.RawContentLength) bytes)"

    Write-Step 'exercising the /api/* pipeline'
    $api = Invoke-WebRequest -Uri "$baseUrl/api/image" -TimeoutSec 15 -UseBasicParsing -SkipHttpErrorCheck

    if ($api.StatusCode -ne 400) { throw "/api/image without a url returned $($api.StatusCode), expected 400." }

    Write-Detail "api       GET /api/image -> 400 ($($api.Content))"

    Write-Step 'checking native Instagram browser validation without opening a page'
    $browserRejected = Invoke-WebRequest `
        -Uri "$baseUrl/api/open-instagram-browser" `
        -Method Post `
        -ContentType 'application/json' `
        -Body '{"url":"https://example.org/"}' `
        -TimeoutSec 15 `
        -SkipHttpErrorCheck

    if ($browserRejected.StatusCode -ne 400) {
        throw "The Instagram browser accepted a non-Instagram URL (status $($browserRejected.StatusCode), expected 400)."
    }

    $browserHeadless = Invoke-WebRequest `
        -Uri "$baseUrl/api/open-instagram-browser" `
        -Method Post `
        -ContentType 'application/json' `
        -Body '{"url":""}' `
        -TimeoutSec 15 `
        -SkipHttpErrorCheck

    if ($browserHeadless.StatusCode -ne 503) {
        throw "The browser route without a desktop page returned $($browserHeadless.StatusCode), expected 503."
    }

    Write-Detail 'browser   non-Instagram URL rejected; headless open returns 503 without launching a browser'

    Write-Step 'verifying a selected photo is written to disk'
    $downloadFolder = Join-Path $env:TEMP "InStSanity-download-smoke-$([Guid]::NewGuid().ToString('N'))"
    $pngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/WMcAAAAASUVORK5CYII='
    $downloadFile = Join-Path $downloadFolder 'smoke_1.png'

    try {
        $downloadRequest = @{
            folder = $downloadFolder
            noDuplicates = $true
            items = @(@{
                shortcode = 'smoke'
                photoIndex = 0
                filename = 'smoke_1.png'
                rawUrl = ''
                dataUri = "data:image/png;base64,$pngBase64"
                svgMarkup = ''
            })
        } | ConvertTo-Json -Depth 8 -Compress

        $downloadResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-selected" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $downloadRequest `
            -TimeoutSec 60

        if (-not $downloadResult.ok -or $downloadResult.savedCount -ne 1 -or $downloadResult.skippedCount -ne 0 -or $downloadResult.failedCount -ne 0) {
            throw "Download endpoint did not report one saved image: $($downloadResult | ConvertTo-Json -Compress)."
        }
        if (-not $downloadResult.files[0].downloadedAtUtc) {
            throw 'The saved image result did not include its UTC download timestamp.'
        }

        if (-not (Test-Path -LiteralPath $downloadFile)) {
            throw "Download endpoint reported success but did not create $downloadFile."
        }

        $savedBytes = [System.IO.File]::ReadAllBytes($downloadFile)
        if ([Convert]::ToBase64String($savedBytes) -ne $pngBase64) {
            throw 'The saved image bytes do not match the submitted image.'
        }

        $duplicateResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-selected" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $downloadRequest `
            -TimeoutSec 60
        if (-not $duplicateResult.ok -or $duplicateResult.savedCount -ne 0 -or
            $duplicateResult.skippedCount -ne 1 -or $duplicateResult.failedCount -ne 0) {
            throw "No-duplicates mode did not report the existing photo as skipped: $($duplicateResult | ConvertTo-Json -Compress)."
        }
        if (-not $duplicateResult.files[0].skipped -or -not $duplicateResult.files[0].downloadedAtUtc) {
            throw 'The duplicate-skip result did not return the existing file timestamp.'
        }
        $downloadFiles = @(Get-ChildItem -LiteralPath $downloadFolder -File)
        if ($downloadFiles.Count -ne 1) {
            throw 'No-duplicates mode created an extra file instead of skipping the existing photo.'
        }

        $downloadRequestWithoutNoDuplicates = $downloadRequest.Replace('"noDuplicates":true', '"noDuplicates":false')
        $duplicateAllowedResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-selected" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $downloadRequestWithoutNoDuplicates `
            -TimeoutSec 60
        if (-not $duplicateAllowedResult.ok -or $duplicateAllowedResult.savedCount -ne 1 -or
            $duplicateAllowedResult.skippedCount -ne 0 -or $duplicateAllowedResult.failedCount -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $downloadFolder 'smoke_1 (2).png'))) {
            throw "Turning No duplicates off did not preserve the unique-suffix download behavior: $($duplicateAllowedResult | ConvertTo-Json -Compress)."
        }

        $legacyFile = Join-Path $downloadFolder 'smoke_photo_2.jpg'
        [System.IO.File]::WriteAllBytes($legacyFile, [byte[]](1, 2, 3))
        $legacyRequest = @{
            folder = $downloadFolder
            noDuplicates = $true
            items = @(@{
                shortcode = 'smoke'
                photoIndex = 1
                filename = 'smoke_2.jpg'
                rawUrl = ''
                dataUri = "data:image/png;base64,$pngBase64"
                svgMarkup = ''
            })
        } | ConvertTo-Json -Depth 8 -Compress
        $legacySkipResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-selected" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $legacyRequest `
            -TimeoutSec 60
        if (-not $legacySkipResult.ok -or $legacySkipResult.savedCount -ne 0 -or
            $legacySkipResult.skippedCount -ne 1 -or $legacySkipResult.failedCount -ne 0 -or
            -not $legacySkipResult.files[0].skipped -or -not $legacySkipResult.files[0].downloadedAtUtc) {
            throw "No-duplicates mode did not recognize the previous shortcode_photo_N filename: $($legacySkipResult | ConvertTo-Json -Compress)."
        }

        $historyRequest = @{ folder = $downloadFolder; shortcodes = @('smoke') } | ConvertTo-Json -Depth 4 -Compress
        $historyResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-history" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $historyRequest `
            -TimeoutSec 30
        $historyFiles = @($historyResult.history.smoke.photos)
        $legacyHistoryFile = $historyFiles | Where-Object { $_.photoIndex -eq 1 } | Select-Object -First 1
        if (-not $historyResult.ok -or $historyFiles.Count -ne 2 -or
            -not ($historyFiles | Where-Object { $_.photoIndex -eq 0 -and $_.downloadedAtUtc }) -or
            -not $legacyHistoryFile -or -not $legacyHistoryFile.downloadedAtUtc -or
            $legacyHistoryFile.filename -ne 'smoke_photo_2.jpg' -or
            -not [System.IO.File]::Exists((Join-Path $downloadFolder $legacyHistoryFile.filename))) {
            throw "The history scan did not identify current, duplicate-suffixed, and legacy numbered photo files: $($historyResult | ConvertTo-Json -Compress)."
        }

        Write-Detail "download  save/skip/unique-suffix and legacy-filename behavior plus POST /api/download-history timestamps verified"
    }
    finally {
        if (Test-Path -LiteralPath $downloadFolder) {
            Remove-Item -LiteralPath $downloadFolder -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Step 'phase 1 passed' 'Green'
}
catch {
    Write-Annotation 'error' 'smoke-test-backend' $_.Exception.Message

    if (Test-Path -LiteralPath $logPath) {
        Get-Content -LiteralPath $logPath | Select-Object -Last 8 | ForEach-Object {
            Write-Annotation 'warning' 'smoke-test-log' $_
        }
    }

    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        Write-Step "stopping pid $($process.Id)"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

# ── phase 2: the WebView actually loads (best effort, needs a desktop session) ──
if (-not $IncludeUi) {
    Write-Step 'phase 2 skipped (-IncludeUi not set)'
    Write-Step 'all checks passed' 'Green'
    return
}

$process = $null
try {
    Write-Step 'phase 2: launching with the WebView'
    $process = Start-App -Headless $false

    $health = Wait-ForHost -Process $process
    Write-Detail "host      ok=$($health.ok), port=$($health.port)"

    # Let the WebView initialise; a crash here (missing runtime, no desktop) shows up as
    # the process going away.
    Start-Sleep -Seconds 10

    if ($process.HasExited) {
        throw "The app exited (code $($process.ExitCode)) after the host came up — the WebView phase did not survive."
    }

    $stillAlive = Invoke-RestMethod -Uri "$baseUrl/__health" -TimeoutSec 5
    Write-Detail "ui        process alive after 10s, host still answering (uptime=$($stillAlive.uptimeSeconds)s)"

    Write-Step 'phase 2 passed' 'Green'
    Write-Step 'all checks passed' 'Green'
}
catch {
    Write-Annotation 'error' 'smoke-test-ui' $_.Exception.Message
    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        Write-Step "stopping pid $($process.Id)"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
