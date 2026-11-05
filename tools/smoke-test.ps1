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
    foreach ($marker in @('In[st]Sanity', 'lsBoot', 'app-startup-blocked', 'appVersionButton', 'openDownloadFolderBtn', 'startup-layout-v1-4', 'window.ResizeObserver', 'window.visualViewport', 'id="videoDownloadNotice"', 'id="downloadHistoryToggleBtn"', 'id="detailDownloadHistoryToggleBtn"', 'class="filter icon-filter-btn source-toggle-button active-outline-control" id="detailDownloadHistoryToggleBtn"', 'class="filter icon-filter-btn source-toggle-button active-outline-control" id="detailSourceToggleBtn"', 'class="filter icon-filter-btn source-toggle-button active-outline-control" id="detailVideoToggleBtn"', 'class="filter icon-filter-btn source-toggle-button active-outline-control" data-role="video-toggle"', 'const DOWNLOAD_HISTORY_OFF_ICON=', 'button.innerHTML=showDownloadHistory?DOWNLOAD_HISTORY_ICON:DOWNLOAD_HISTORY_OFF_ICON', '.active-outline-control.active', '.download-history-item-mark{top:38px;left:10px;right:auto', 'function renderPostDownloadHistoryMark', 'function renderMediaDownloadHistoryMark', 'function recordPhotoDownloadHistory', 'window.__recordPhotoDownloadHistory=', 'browserPhotos:Object.create(null)', 'browserVideos:Object.create(null)', 'function recordVideoDownloadHistory', '(!hasContentIndex&&!hasVideoIndex)', 'window.__getVideoDownloadHistoryContext=getVideoDownloadHistoryContext', 'window.__recordVideoDownloadHistory=recordVideoDownloadHistory', '/api/download-history', 'window.showVideoDownloadNotice=showVideoDownloadNotice', 'window.getConfiguredDownloadFolder=getConfiguredDownloadFolder', 'id="separateVideoFolderBtn"', 'function toggleSeparateVideoFolder()', 'Separate video folder: ', 'id="separateVideoFolderBtn"', 'id="separateVideoFolderSettings"', 'id="videoFolder"', 'function chooseVideoFolder()', 'id="resizerNoDuplicatesBtn"', 'function toggleResizerNoDuplicates()', 'function isResizerNoDuplicatesEnabled()', 'id="browserNav"', 'id="resizerNav"', 'id="resizerPage"', 'id="photoEditorResizeTab"', 'id="photoEditorEditTab"', 'id="photoEditorMomentTab"', 'function showPhotoEditorTab(name,button)', 'id="momentCaptureVideoInput" type="file" accept="video/*" multiple aria-label="Choose videos for Moment capture"', 'id="momentCaptureFileList"', 'id="resizerFileList"', 'id="photoAdjustmentPhotoStrip"', 'height:39px;min-height:39px', 'id="momentCaptureSelectionToggleBtn"', 'id="momentCaptureClearVideosBtn"', 'id="momentCaptureFrameBtn"', 'id="detailOpenMomentCaptureBtn"', 'id="saveUrlOpenMomentCaptureBtn"', 'id="openSelectedVideosMomentCaptureBtn"', 'function countSelectedVideos()', 'function openSelectedLibraryVideosInMomentCapture()', 'function transferSelectedVideosToMomentCapture', 'function photoResizePreviewWorkerFunctions()', 'function photoPreviewSharpenDamageTexture(texture,amount)', 'id="resizerChooseImagesBtn"', 'function chooseResizerImages()', 'function choosePhotoEditImages()', 'function chooseMomentCaptureVideo()', 'function chooseBackgroundPicture()', 'function chooseBackgroundGif()', '/api/pick-native-files?kind=', '/api/picked-file?id=', '/api/resize-native-upload', 'class="file-picker-control file-picker-control--button"', 'class="native-file-picker-input" id="resizerImagesInput" type="file" accept="image/*" multiple aria-label="Choose images to resize"', 'id="photoEditImagesInput" type="file" accept="image/*" multiple aria-label="Choose images for Image Edit"', 'function handleResizerImagesPicked(selection)', 'id="resizerMaxSide"', 'id="resizerUpscaleToggleBtn"', 'function togglePhotoResizerUpscale()', 'RESIZER_UPSCALE_KEY', 'upscale:photoResizeUpscaleEnabled', 'const noDuplicates=isResizerNoDuplicatesEnabled()', 'Skipped (duplicate)', 'data.skippedCount', 'id="resizerStartBtn"', 'id="resizerSelectionToggleBtn"', 'id="resizerClearImagesBtn"', 'function togglePhotoResizeSelection()', 'class="icon-btn resizer-open-output-folder-btn" id="resizerOpenOutputFolderBtn"', 'function openPhotoResizerOutputFolder()', 'id="resizerOutputFolder"', 'Resized images location', 'Resized images folder', 'id="separateResizedImagesFolderBtn"', 'id="separateEditedImagesFolderBtn"', 'id="separateMomentCaptureFolderBtn"', 'settings-card-chevron', 'aria-expanded="false"', 'toggleUiSettingsPanel(this.closest(''.settings-card''))', 'function uploadResizerFiles', 'function startPhotoResizing', 'function syncPhotoOutputFolderDisplays()', 'function getConfiguredPhotoResizerOutputFolder(){return isSeparateResizedImagesFolderEnabled()?','function getConfiguredPhotoEditOutputFolder(){return isSeparateEditedImagesFolderEnabled()?','function getConfiguredMomentCaptureOutputFolder(){return isSeparateMomentCaptureFolderEnabled()?', 'id="resizerStartBtn" onclick="startPhotoResizing()" disabled>Resize</button>', '/api/resize-upload', '/api/resize-convert', 'id="browserAddressInput"', 'id="browserBackBtn"', 'id="browserForwardBtn"', 'id="browserReloadBtn"', '.browser-bar .browser-nav-btn svg{width:21px;height:21px}', '.browser-bar #browserReloadBtn svg{width:17px;height:17px}', 'id="topCollapseBtn"', '.icon-btn.collapse-window-btn svg{width:19px;height:19px}', 'id="directPostUrlInput"', 'padding:10px 42px 42px', 'function openInstagramBrowser', '/api/open-instagram-browser', '.save-url-post-modal{width:100%;max-width:100%;max-height:none;overflow:visible;margin:0 auto}', 'save-url-post-modal', 'function resetSavedPostsResolutionState(options={})', 'loadInstagramSession(true,true)', 'Reload saved posts using the current Instagram session', "card.classList.add('collapsed');", '${selPosts}/${selTotal}', 'function snapshotSelectedMediaIndices', 'video-select-control', 'toggleVideoTileSelection', 'function beginDownloadRun', 'function setDownloadProgressMessage', 'displayToastMessage(message,0,false,true)', 'displayToastMessage(message,6500,false,true)', 'Downloaded and saved', 'const batchSize=4;', '.status.download-progress{max-width:min(92vw,760px);white-space:normal;text-align:center;line-height:1.45;overflow-wrap:anywhere}', '.toast-slot .status{position:static', 'Version 2.099; open version history', "const APP_VERSION='2.099';", "version:'1.148',added:'No new features.',removed:'Rate-limit warnings without the upstream retry-delay value.',fixed:'The throttled Instagram 429 warning now includes the raw Retry-After response header, or says it was not provided, so rate-limit cooldowns can be observed without restoring per-request log spam.',tone:'green'", "version:'1.147',added:'No new features.',removed:'Repeated per-post upstream status messages while collection photo totals resolve.',fixed:'Collection summaries now run in smaller, slower background batches after visible cards finish. A 429 triggers exponential backoff up to five minutes, and rate-limited posts retry after the resolver cache expires, expected 404s are silent and other per-post status details are Debug-only, with a single throttled 429 warning. Carousel media stays lazy-loaded.',tone:'green'",  "version:'1.146',added:'No new features.',removed:'No removals.',fixed:'HTML responses from Instagram and Save-Free are detected before JSON parsing, so login/challenge pages use the existing fallback path instead of raising JsonReaderException.',tone:'green'", "version:'1.145',added:'Collection post/photo totals on All Posts.',removed:'The need to manually visit collection pages for photo totals.',fixed:'All Posts shows posts/photos, marks unresolved photo totals with a plus while lightweight summary-only background batches finish, and never preloads carousel media. All Posts and Selected keep matching widths.',tone:'green'", "version:'1.143',added:'No new features.',removed:'Collection photo totals waiting for manual page-by-page visits.',fixed:'Selecting the whole collection now resolves lightweight card summaries in background batches, so selected-photo counts refresh across every page without loading carousel media.',tone:'green'", "version:'1.142',added:'No new features.',removed:'Download progress pop-ups appearing over the pagination controls.',fixed:'Persistent download progress and photo counts stay inside the pages bar, between the page controls and Download actions.',tone:'green'", 'function getPostSelectablePhotoCount', 'post.allPhotosSelected=Boolean(selected)', 'post.selectedIndices=Array.from({length:Math.max(0,photoCount)},(_,index)=>index)', 'preserveDeclaredCounts', "version:'1.141',added:'Collection-wide selection now includes every photo in posts whose media is still lazy-loaded.',removed:'Select all stopping at each post’s first known photo when full carousel counts arrived later.',fixed:'Collection and page selections retain an all-photos intent without preloading media. As each post’s counts resolve, selection and download plans expand to its full carousel.',tone:'green'", "version:'1.140',added:'Selected now shows the number of selected posts and selected photos.',removed:'Photo batches saving out of order and download progress notices expiring mid-batch.',fixed:'Selected reads post/photo counts. Batch photo fetches remain concurrent while files are written in request order; download progress stays visible and reports photo preparation, processing, and saves through completion.',tone:'green'",  "version:'1.139',added:'No new features.',removed:'The native caption title and stale Saved Posts media surviving an Instagram sign-in change.',fixed:'Removed the native window title; settings panels now start collapsed. Sign-in/out and Reload posts force fresh session-aware post resolution. Save-via-URL retains its full-width layout.',tone:'green'", "version:'1.138',added:'No new features.',removed:'The infinity collection-selector glyph touching the SVG horizontal edges.',fixed:'Expanded the SVG viewBox and reserved horizontal breathing room so both sides of the infinity icon render fully without changing collection-selection behavior or button size.',tone:'green'", "version:'1.137',added:'No new features.',removed:'Empty Instagram caption-edge arrays aborting the resolver and exception details being hidden from the Debug output.',fixed:'Caption-edge access now checks for an item before indexing. Debug builds route ILogger output to Visual Studio, and resolver failures include the exception type.',tone:'green'", "version:'1.136',added:'No new features.',removed:'The blanket rejection of Instagram size-preview URLs and Open Graph-only media when opening a post.',fixed:'Temporarily restored display_url-first selection and allowed preview URLs through card, detail, full-size, and download paths. Target-post identity scoping remains unchanged for this rollback test.',tone:'yellow'", "version:'1.135',added:'No new features.',removed:'No diagnostic clue when size-limited previews were filtered and no full-size media reached the detail view.',fixed:'The empty detail state now distinguishes preview-only responses from resolver failures and displays the failure reason. Cropped preview URLs remain excluded from the viewer and downloads.',tone:'green'", "version:'1.134',added:'No new features.',removed:'Target media being discarded when display_url pointed to an Instagram size-preview URL despite full-size candidates being present.',fixed:'The resolver now skips /media/?size= preview URLs when choosing slide images and continues through the target post’s image_versions2/display_resources candidates. Preview-only URLs remain blocked from the viewer and downloads.',tone:'green'", "version:'1.133',added:'No new features.',removed:'Valid post media nested below a matched preview/identity wrapper being skipped.',fixed:'The resolver anchors to the requested shortcode or media ID, then searches only recognized media-holder fields inside that post subtree. Carousel payloads take priority over a single preview image, display_uri is supported, and unrelated page images remain excluded.',tone:'green'", "version:'1.132',added:'No new features.',removed:'Unverified page-wide scontent URLs being presented as carousel photos.',fixed:'Embedded Instagram media is accepted only from JSON matching the requested shortcode or its exact numeric media ID; arbitrary page-wide scontent URLs can no longer become post slides. Unrelated recommendations and account images are ignored. Save-Free/media-info fallbacks, logged-in behavior, and visible-card source-label updates remain intact.',tone:'green'", "version:'1.131',added:'No new features.',removed:'Logged-out carousel posts showing only a cropped preview, and visible-page source labels disappearing when a post was opened during resolution.',fixed:'Recover full carousels from Instagram scontent URLs embedded in public page HTML. Visible-page summary results now still update source labels when a post is opened, with unresolved labels retried sooner.',tone:'green'", "version:'1.130',added:'No new features.',removed:'The still oversized infinity and circled 1 selection icons.',fixed:'Reduced the collection and current-page selector icons by another 15%, without changing button sizes or layout.',tone:'green'", "version:'1.129',added:'No new features.',removed:'The oversized infinity and circled 1 collection/page selection icons.',fixed:'Reduced both selector icons by 15% while leaving button sizes and toolbar layout unchanged.',tone:'green'", "version:'1.128',added:'No new features.',removed:'Cropped Open Graph preview URLs being kept as full-size media, and API-only posts marked Mixed.',fixed:'Logged-out posts prefer Save-Free slides or Instagram media-info over OG previews; source badges reflect resolved slide origins, and cropped OG covers stay card-only if originals are unavailable. The authenticated verdict remains unchanged.',tone:'green'", "version:'1.127',added:'No new features.',removed:'Source labels appearing only after opening a post while logged out.',fixed:'Visible-page source labels now resolve for anonymous posts too, using the media-info fallback when public page parsing cannot determine provenance.',tone:'green'", "version:'1.126',added:'No new features.',removed:'The unpredictable startup window position.',fixed:'The app window now opens centered on the primary display’s usable area.',tone:'green'", "version:'1.125',added:'No new features.',removed:'The duplicate app name in the native Windows title bar.',fixed:'Cleared the native caption text while preserving the window icon and caption controls.',tone:'green'", "version:'1.124',added:'A toggle for current-page photo selection.',removed:'No removals.',fixed:'Clicking the current-page selector again deselects that page when all its photos are already selected, without affecting other pages.',tone:'green'", "version:'1.123'", 'id="allPostsFilter"', 'id="deselectAllBtn"', 'function showPhotoDownloadNotice', 'Downloading video', 'function updateAllPostsCollectionTotal()', 'width:180px; min-width:180px', 'min-height:37px; padding:0 12px', "version:'1.122',added:'Select-all actions for the full collection and current page.',removed:'The single-row library controls and icon-only deselect button.',fixed:'Split filters and selection controls into two rows aligned with Sort and Search. Selection can cover every photo in the collection or just the visible page, with a Deselect all posts action.',tone:'yellow'", 'toolbar-row-top', 'toolbar-row-selection', 'id="selectAllCollectionBtn"', 'id="selectCurrentPageBtn"', '.icon-filter-btn.scope-selection-btn svg { width:13.73px; height:13.73px; }', '#selectAllCollectionBtn.scope-selection-btn svg { width:15px; height:15px; }', 'viewBox="-1 0 26 24" width="15" height="15"', 'function selectCurrentPagePhotos()', 'Toggle selection of all photos on the current page', 'function needsSourceKindResolution(post)', 'Missing labels are resolved for posts on the visible page or when opened', 'function selectAllCollectionPhotos()', 'function selectCurrentPagePhotos()', "version:'1.121',added:'No new features.',removed:'No removals.',fixed:'Moved the backdrop blur onto the sidebar surface, matching the page bar’s 16px glass blur for themed palettes while retaining the default theme’s 20px blur. Text and controls stay sharp.',tone:'green'", "version:'1.120',added:'No new features.',removed:'The 60% sidebar surface treatment.',fixed:'Raised the blurred sidebar surface opacity to 82% across all themes, reducing background-image bleed-through without fading sidebar labels or controls.',tone:'green'", 'body .sidebar { isolation:isolate; background:transparent; backdrop-filter:blur(16px); }', 'background:var(--t-sidebar-bg); opacity:.82;', "version:'1.119',added:'No new features.',removed:'The opaque sidebar surface and off-theme blank media tiles.',fixed:'Sidebar and library surfaces now use theme-aware glass across all themes, including default, without fading sidebar contents. Blank/loading carousel media and export-link accents follow the active palette.',tone:'green'", "version:'1.118',added:'No new features.',removed:'The generic WebView2 right-click menu outside video.',fixed:'Preserved the native video menu (Download, playback speed, Picture in picture) while suppressing default menus on other right-click targets.',tone:'green'", "label.textContent='v.'+APP_VERSION;", 'transform:translateX(10pt);',  "version:'1.113',added:'No new features.',removed:'No removals.',fixed:'Fixed the first-load loading scene when the MAUI WebView viewport changes by rebuilding each variant at the newly measured dimensions, rather than only changing the SVG viewBox. Loading screens now default to Off for new installs; saved On/Off preferences remain in effect.',tone:'green'", 'id="lsScreensBtn" aria-pressed="false"', 'Loading screens: Off', 'var lsScreensEnabled = false;', "lsScreensEnabled = (v === '1');", 'function rebuildBootSceneAtTime(refreshTime)', 'host.replaceChild(nextStage,stage)', 'rebuildBootSceneAtTime(refreshTime)', "version:'1.112'", 'background:var(--t-input-bg,rgba(11,15,20,.82))', "version:'1.110',added:'No new features.',removed:'No removals.',fixed:'Release labels now use a three-digit minor sequence: 1.099 is followed by 1.100 and later releases stay in major version 1.',tone:'green'", "version:'1.105',added:'No new features.'", '.picture-zoom-btn:hover,.picture-zoom-btn:focus-visible', 'var(--t-btn-bg', "version:'1.111',added:'No new features.',removed:'No removals.',fixed:'Loading-screen loudness now defaults to 10%, the Not-yet taunt’s big-hall reverb send is 30% lower, and video downloads cancel rather than fall back if the Settings folder is unavailable; saved slider preferences remain unchanged.',tone:'yellow'", "version:'1.044'", "version:'1.043'", "version:'1.042'", "version:'1.041'", "version:'1.040'", "version:'1.039'", '.app-version-button { position:fixed; right:14px; bottom:8px;', 'justify-content:space-between;align-items:center;gap:14px;flex-wrap:nowrap;margin-top:19px', 'class="footer-actions"', '&w=480', 'wrapProxiedImageUrl(pic.rawUrl,true)', 'wrapProxiedImageUrl(pic.thumbnailUrl,true)', 'Saved Posts JSON', 'onDirectPostUrlInput', 'Downloading photos', 'Exit button: ${active', '.detail-head .detail-actions{align-self:flex-end}', 'deselectAllBtn', 'clearAllSelections', 'buildCardPostedHtml', 'meta-sep', 'id="toastSlot"', '.toast-slot{', '.card-meta-value.card-posted{', 'id="logoutConfirmModal"', 'showLogoutConfirm', 'confirmLogout', 'max-height:none;overflow:visible;margin:0 auto', 'version-tag', 'tone-green', 'tone-yellow', "pages bar.',tone:'yellow'", 'tone-red', 'version-tone-legend', 'startViewTransition', 'handleVideoError', 'restricted-cover', 'retryPostResolve', 'warmNextPage', 'data-raw-video', 'id="browserPage"', 'id="topAccountAvatar"', 'id="igLogoutBtn"', 'onTopLoginBtnClick()', 'My Account', 'igAccountAvatarFallback', 'account-status', 'status-dot', '/api/instagram-session', '/api/instagram-logout', '/api/browser-view', 'window.handleNotYetClick=handleNotYetClick;', '__instSanityNotYetAudioTrace', 'notYetVolumeScale', 'justify-content:flex-end', 'brandName(u,CX,H-14,14)', "E.bname.setAttribute('transform',", 'if(dodge&&dodge.n===5)return;', '.icon-btn svg{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:17px;height:17px;display:block}', "version:'1.090'", "version:'1.074'", "version:'1.073'", "version:'1.072'", "version:'1.071'", "version:'1.070'", "version:'1.069'", "version:'1.066'", "version:'1.065'", "version:'1.064'", "version:'1.063'", "version:'1.062'", "version:'1.061'", "version:'1.060'", "version:'1.059'", "version:'1.058',added:'No new features.',removed:'No removals.',fixed:'Reduce both Aatrox laughs by 10% — volume 0.88→0.792.'", "version:'1.057',added:'No new features.',removed:'No removals.',fixed:'Tweak laugh odds to 60/40 — fight-me 60%, Original Laugh 40%.'", "version:'1.056',added:'No new features.',removed:'No removals.',fixed:'Loading-screen trident hand 30% smaller on v1 — impHand inside tridentFork now scaleFac*0.7.'", "version:'1.055',added:'Added second laugh — Aatrox_Original_Laugh_0.ogg (no cuts) joins fight-me; weighted random 65%/35%, both through big hall with fade.'", "version:'1.054',added:'No new features.',removed:'No removals.',fixed:'Aatrox laugh extended to ~2.9s (was 2.5s) for even longer cut.'", "version:'1.053',added:'No new features.',removed:'No removals.',fixed:'Aatrox laugh extended to ~2.5s and starts ~0.38s earlier so it lands before the screen fades on all 5 variants.'", "version:'1.052',added:'No new features.',removed:'No removals.',fixed:'Loading-screen laugh swapped — Aatrox fight-me first ~2s (faded) replaces the synth demon laugh, still through the big 3.4s hall.'", "version:'1.051',added:'No new features.',removed:'No removals.',fixed:'Boot v3 Haul shoulders now ride with the sticker — tx/ty/rot/scale tracked per-frame so arms stay pinned to 34,118/126,118 through the bob + 12px drag.'", "version:'1.050',added:'No new features.',removed:'No removals.',fixed:'Trident loading screen nudge — fork/shaft moved ~6px right (CX+30.2→CX+36.2) so the prongs breathe clear of the sticker frame.'", "version:'1.049',added:'No new features.',removed:'No removals.',fixed:'All sticker arms now start from the same shoulder points (34,118/126,118) as the JSON-devour reference — v1 Trident (CX+27.5,CY+22.7), v3 Haul now over the sticker (dual 30.7,105.8/93.3,105.8), v5 (34,118), click poke (0.2125/0.7875×0.7375) and devour Trident/Snap unified; arm length and thickness unchanged.'", "version:'1.047',added:'No new features.',removed:'No removals.',fixed:'Download and Log in text on the warm themes (12-14) is now dark (#1a160f/#0f0f1a/#0a0f1e) instead of light for readable contrast on the light buttons.'", "version:'1.046',added:'No new features.',removed:'No removals.',fixed:'Theme picker now shows the four new themes (12-15) that were already in the build.'", "version:'1.045',added:'Four new themes: 12 Cinderforge Brass Soot (Twin Strike hammers+anvil), 13 Kelp Cathedral Brine Violet (Crystal Orbit), 14 Carnival Fairground Gold (Coin Medallion), 15 Sanctuary Deep Frost (Shrine Dome) with matched loading and restart art.',removed:'No removals.',fixed:'Download text now matches the sidebar button text in all four new themes (#fdf0d6/#e6e0f5/#fef3c7/#d6e2f5), and each theme carries its unique sticker shape.'",
            "version:'1.044',added:'No new features.',removed:'The wrapped pagination bar.',fixed:'The pages bar and Download now stay on one line and scroll instead of wrapping. On small screens the bar no longer stacks.'", "version:'1.043',added:'No new features.',removed:'The small click-tridents and close-in chevrons.',fixed:'Click animations keep their original trident and hand size (loading screens stay 35% smaller), the frozen arm is still fixed, and the loading-screen chevrons now sit at 60px from the centre.'", "version:'1.042',added:'No new features.',removed:'The frozen arm/trident frame on click.',fixed:'The extending-trident arms now scale with the sticker and no longer throw when redrawing the shaft, so the hook, trident and impact ring no longer freeze mid-click.'", "version:'1.041',added:'No new features.',removed:'The loud loading-screen default.',fixed:'The loading-screen soundtrack now starts at 25% instead of 100%.'", "version:'1.040',added:'No new features.',removed:'The oversized sticker, the off-color v4 background and the unscaled hands, trident and chevrons.',fixed:'The sticker is 35% smaller (v3 20%), v4 shares the others solid background, hands, trident and chevrons scale with the sticker and v5 elbows connect.'", "version:'1.039',added:'A Log Out confirmation pop-up.',removed:'The stray middle dot reopening after closing a post and the app forgetting your Instagram username on restart.',fixed:'The account name is remembered between launches and resolved from the signed-in session itself, post cards keep the date left and likes right after closing them, and Log Out asks for confirmation before clearing the session.'", "version:'1.038',added:'No new features.',removed:'The pop-up floating above the pages bar and the card likes sitting next to the date.',fixed:'The pop-up lives inside the pages bar between the page controls and Download, the card likes sit on the right of the Posted row, and the signed-in username is read from your profile link and profile page URL so the account finally names you.'", "version:'1.037',added:'A deselect-all icon button beside the Selected filter.',removed:'The selection and sort state sticking around after a restart, the cramped Posted row spacing, and the dot between the date and likes on post cards.',fixed:'The app reopens with a clean selection and Newest sort, restores your signed-in account and a warm Instagram page at startup, keeps the card actions bottom-aligned like the Save via URL card, and spreads the Posted rows out with the card likes pushed right.'", "version:'1.036',added:'No new features.',removed:'The empty gap under the link field in the Save via URL card and the tight Post link label.',fixed:'The Post link label breathes above its input, the resolved post tucks right under the link field, the card actions sit at the bottom of their header, and the exit prompt closes the in-app browser first so it never hides behind it.'", "version:'1.035',added:'No new features.',removed:'The empty divider strips above the first section in the settings cards, the Top-right wording on the exit toggle, and the download pop-up sitting over the pages bar.',fixed:'Settings sections start right after their card text, the toggle reads Exit button with its state, and the download and folder pop-ups land between the page controls and Download, the download one simply reading Downloading photos.'", "version:'1.034',added:'No new features.',removed:'The button that loaded a pasted link, the duplicate selected-photos bar, the sort label, and the gaps at the sides of the pages bar.',fixed:'The address bar reads the live page even after profile hops, the account button shows your name and picture, Restart closes the in-app browser, links load as you paste them, in-post thumbnails stay light, Exit and the account button trade places, and the tab reads Saved Posts JSON.'", "version:'1.033',added:'No new features.',removed:'The XAML parse error that stopped the app building.',fixed:'The version footer escapes its ampersand, so the shell XAML parses and the project compiles again.'", "version:'1.032',added:'No new features.',removed:'The address bar freeze and the anonymous Signed in label.',fixed:'The URL bar tracks every page you open, including Instagram in-app transitions, and the account probe finds your username and profile picture so the top-right button and Settings show your name.'", "version:'1.031',added:'No new features.',removed:'The Browser tab heading block, Go and close buttons and hint, the Save via URL card header and notes, and the empty strip above the Saved posts toolbar.',fixed:'The Browser tab is nearly all viewport, Save via URL is just the link field with a themed input and a matching IG icon, and the toolbar sits right under the top bar.'", "version:'1.030',added:'No new features.',removed:'The static Account and My profile labels, the plain top-right icon, and the undersized Log Out button.',fixed:'Your profile pic and name head the account card with a green-dot Signed in or red-dot signed-out status, Log Out matches Open Browser, and the top-right button shows your username and profile pic.'", "version:'1.029',added:'No new features.',removed:'Build errors around the browser events, cookie expiry copy, and thumbnail decoding.',fixed:'Browser event handlers match their delegates, WebView2 cookie expiry converts from its timestamp, and BitmapDecoder no longer pretends to be disposable.'", "version:'1.028',added:'The yellow mark now flags releases packed with many small changes.',removed:'Yellow standing only for medium-sized changes.',fixed:'Bundled-fix releases like 1.023 read yellow at a glance.'", "version:'1.027',added:'Color-coded version history: every release is soft green, yellow, or red by change size.',removed:'The plain unmarked version numbers in the history.',fixed:'Every release note now shows its change size at a glance.'", "version:'1.026',added:'Signed-in resolution via the browser session, restricted-post cards with Retry and Open in Browser, and background warming of the next page.',removed:'Full-size cover downloads for the grid and raw CDN video playback.',fixed:'Page turns no longer stutter, thumbnails are resized in-app, videos replay from cache, and media responses revalidate with 304s.'", "version:'1.025',added:'No new features.',removed:'The flat square background behind the app icon.',fixed:'Black hole loading text rides the sticker and matches the glitch screen; the icon art fills its frame with transparent corners.'", "version:'1.024',added:'A themed in-app Browser page with your account in the top bar and My Account settings.',removed:'The separate native browser window that covered the whole app.',fixed:'Docked the browser into the content area and moved Download and Open-folder into the pages bar.'", "version:'1.023',added:'No new features.',removed:'Loading screens reacting to the Imp idle toggle, and the old bars taskbar icon.',fixed:'Loading screens always animate, sticker clicks advance reliably, the chevrons wordmark matches the glitch screen, and the version sits bottom-right above the right-aligned pages bar.'", "version:'1.022',added:'No new features.',removed:'The icon sitting slightly off-center in its button.',fixed:'Centered button icons exactly, pixel for pixel.'", 'class="main-shell"', '.main-shell>.topbar{position:relative;z-index:20}', 'main{flex:1 1 auto;min-width:0;min-height:0;height:auto;overflow-y:auto;', "version:'1.021',added:'No new features.',removed:'Post cards drawing over the top-bar divider.',fixed:'Kept scrolling content strictly below the top-bar line.'", "version:'1.020',added:'No new features.',removed:'The sticky header background and blur.',fixed:'Kept the header pinned while restoring its transparent appearance.'", "version:'1.019',added:'A sticky top bar that keeps the main controls visible while scrolling.',removed:'The header scrolling out of view.',fixed:'Kept the top bar pinned while page content scrolls.'", "version:'1.018',added:'Distinctive sticker designs for themes 4, 5, and 8-11.',removed:'The previous crown and frame art in the refreshed themes.',fixed:'Changed only the Theme 4 crown, kept Theme 3 unchanged, and renamed the exit panel to LET ME OUT!.'", "version:'1.017',added:'A native in-app Instagram browser.',removed:'The simulated Instagram login form and startup session probe.',fixed:'Routed Instagram browsing through a persistent WebView2 profile; sign-in stays on Instagram.'", "version:'1.016',added:'No new features.',removed:'The fixed white ", 'Made its neutral text follow the active theme.', "version:'1.015',added:'Cursor-tracking eyes on the exit-prompt sticker.',removed:'Sidebar sticker click reactions while the prompt is open.',fixed:'Blocked click and JSON-throw animations during the prompt.'", "version:'1.014',added:'No new features.", 'CAUGHT_PROMPT_LEAD_MS=500', 'onclick="showLeavingPrompt()"', 'width:177px; height:177px', 'font:inherit', 'color:var(--t-muted,#94a3b8)', '#leavingYesButton:hover,#leavingNotYetButton:hover', 'leavingPromptTitleText', 'function setTextSliceCuts', 'function drawLeavingCackle', 'function updateLeavingStickerEyes(clientX,clientY)', 'updateLeavingStickerEyes(x,y);', 'leaving-sad-eyes-track', 'if(document.body.classList.contains(''leaving-prompt-open''))return;', 'data-concept="02"', 'leavingCackleBody', 'font-size:24.5px; line-height:1.2; text-align:left; justify-self:start', 'id="topExitAppBtn"', 'id="topExitModeToggle"', 'On: click the top-right exit icon to open the same prompt.', "leavingPromptRestoreBrowser=Boolean(browserPage&&browserPage.style.display==='block');", "if(leavingPromptRestoreBrowser)sendBrowserView({action:'hide'});", "sendBrowserView({action:'show'})", "const restoreBrowser=leavingPromptRestoreBrowser;", 'LET ME OUT!', 'Leaving already?', 'id="leavingNotYetButton"', "fetch('/api/exit-app'", 'background:linear-gradient(150deg,#11170f,#1a2115 58%,#10150e)', 'style="background:#A9C46A"', 'text-align:left; justify-self:start', 'grid-template-columns:minmax(0,1fr)', 'justify-content:center!important', "openFolderBtn.style.display=hasJsonLoaded?'grid':'none'", '11. Moss &amp; Brass', 'data-theme-id="v11"', "version:'1.005',added:'Moss & Brass", "v11:'#11170f'", 'body[data-theme="v11"] .ls-restart-ring', 'source-toggle-button', 'id="railToggle"', 'body[data-theme="v3"] .page-btn.active:hover:not(:disabled)')) {
        if (-not $document.Content.Contains($marker)) {
            throw "The served document does not contain the expected marker '$marker'."
        }
    }

    if ($document.Content.Contains('photo-adjustment-photo-label') -or
        -not $document.Content.Contains('id="photoAdjustmentPhotoStrip"')) {
        throw 'The redundant Image Edit Preview photo label must be absent while its photo strip remains.'
    }

    if (-not $document.Content.Contains('.photo-editor-tabs{display:flex;gap:8px;margin:0 0 14px;flex-wrap:wrap}') -or
        -not $document.Content.Contains('.photo-editor-tab-panel>.resizer-grid>.resizer-card:first-child,.photo-editor-tab-panel>.moment-capture-card:first-child{margin-top:0}') -or
        -not $document.Content.Contains('@media(min-width:851px){.photo-editor-resize-grid>.resizer-config-card{margin-top:0}}') -or
        -not $document.Content.Contains('#photoEditorResizePanel .resizer-picker-card .settings-card-head h2,#photoEditorMomentPanel .moment-capture-card-head h2{margin-bottom:6px}') -or
        -not $document.Content.Contains('#photoEditorEditPanel .resizer-adjustments-card .settings-card-head h2{margin-bottom:5px}')) {
        throw 'The tool panels, including the desktop Resize configuration card, must align at 14px and keep 14px clear glyph gaps between headings and descriptions.'
    }

    if (-not $document.Content.Contains('.resizer-card .settings-card-head,.moment-capture-card .settings-card-head{padding:17px 20px}') -or
        -not $document.Content.Contains('.resizer-card .setting-body,.moment-capture-card .setting-body{padding:18px 20px}')) {
        throw 'Moment capture header and body padding must match Resize and Image Edit.'
    }

    if (-not $document.Content.Contains('.photo-preview-panel{min-width:0;padding:12px;') -or
        -not $document.Content.Contains('.photo-adjustment-group{min-width:0;padding:12px;') -or
        -not $document.Content.Contains('.photo-editor-layout{display:grid;grid-template-columns:minmax(0,1.35fr) minmax(250px,.65fr);gap:12px;align-items:start}') -or
        -not $document.Content.Contains('.photo-adjustment-photo-picker{min-width:0;margin:0 0 12px}') -or
        -not $document.Content.Contains('.photo-preview-heading{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap;margin-bottom:12px;font-size:11px;color:var(--t-ink,#f1f5f9)}') -or
        $document.Content.Contains('.photo-adjustment-photo-picker{min-width:0;margin:0 0 8px}') -or
        $document.Content.Contains('.photo-adjustment-photo-picker{min-width:0;margin:0 0 14px}')) {
        throw 'Image Edit must use a consistent 12px gap for its queue-to-layout, preview-to-controls, and preview-heading spacing, with matching 12px panel insets.'
    }

    if (-not $document.Content.Contains('.photo-adjustment-groups{display:grid;grid-template-columns:minmax(0,1fr);gap:12px;min-width:0;max-height:min(82vh,900px);overflow-x:hidden;overflow-y:auto;overscroll-behavior:contain;padding:0;box-sizing:border-box;scrollbar-width:thin;')) {
        throw 'Image Edit adjustment groups must not reserve asymmetric right padding beside the scrollbar.'
    }

    if (-not $document.Content.Contains('.moment-capture-body .moment-capture-files-panel{margin:0 0 9px}') -or
        -not $document.Content.Contains('.resizer-photo-empty-note{flex:0 1 auto;min-width:0;max-width:100%;overflow:hidden;padding-left:6px;box-sizing:border-box;')) {
        throw 'Moment preview spacing must match the queue-toolbar gap, and all three empty queue messages must have left padding.'
    }

    if (-not $document.Content.Contains('.photo-editor-tab{border:1px solid var(--t-nav-border,') -or
        -not $document.Content.Contains('background:var(--t-nav-bg,linear-gradient') -or
        -not $document.Content.Contains('.photo-editor-tab.active{background:var(--t-nav-act-bg,') -or
        -not $document.Content.Contains('border-color:var(--t-nav-act-border,') -or
        -not $document.Content.Contains('color:var(--t-nav-act-ink,#99f6e4)')) {
        throw 'Image Edit tabs must use the active theme navigation colors for inactive and selected states.'
    }
    $restorePostsStart = $document.Content.IndexOf('async function restoreImportedPostsState(){')
    $restorePostsEnd = if ($restorePostsStart -ge 0) { $document.Content.IndexOf('async function resolvePostsBatchChunk(', $restorePostsStart) } else { -1 }
    $restorePostsSource = if ($restorePostsEnd -gt $restorePostsStart) {
        $document.Content.Substring($restorePostsStart, $restorePostsEnd - $restorePostsStart)
    } else { '' }
    if (-not $restorePostsSource.Contains('selectedIndices:[],selectedVideoIndices:[],allPhotosSelected:false,allVideosSelected:false') -or
        $restorePostsSource.Contains('Array.isArray(p.selectedIndices)') -or
        $restorePostsSource.Contains('Array.isArray(p.selectedVideoIndices)') -or
        $restorePostsSource.Contains('Boolean(p.allPhotosSelected)') -or
        $restorePostsSource.Contains('Boolean(p.allVideosSelected)')) {
        throw 'Saved Posts selection must be cleared when imported posts are restored at app startup.'
    }
    $browserSaveButtonThemed =
        $document.Content -match '(?s)\.browser-save-btn\{[^}]*border:1px solid var\(--t-nav-border,[^}]*background:var\(--t-nav-bg,[^}]*color:var\(--t-nav-ink,[^}]*\}' -and
        $document.Content -match '(?s)\.browser-save-btn:hover\{[^}]*background:var\(--t-nav-act-bg,[^}]*border-color:var\(--t-nav-act-border,[^}]*color:var\(--t-nav-act-ink,[^}]*\}'
    if (-not $browserSaveButtonThemed -or -not $document.Content.Contains('id="browserSaveBtn"')) {
        throw 'The Browser Save button must use theme-aware navigation colors in its normal and hover states.'
    }
    $nativePickerIds = @('resizerImagesInput', 'photoEditImagesInput', 'momentCaptureVideoInput', 'postJsonFile', 'uploadJsonModalFile', 'backgroundFile', 'backgroundGifFile')
    foreach ($pickerId in $nativePickerIds) {
        if (-not $document.Content.Contains(('id="' + $pickerId + '" type="file"'))) {
            throw "The native file picker '$pickerId' is missing."
        }
        if ($document.Content.Contains(("document.getElementById('$pickerId').click()"))) {
            throw "The native file picker '$pickerId' must not be opened through a synthetic click."
        }
    }
    if (-not $document.Content.Contains('.file-picker-control--button>.native-file-picker-input') -or
        -not $document.Content.Contains('.file-picker-control--dropzone>.native-file-picker-input') -or
        -not $document.Content.Contains('function handlePhotoEditImagesPicked(selection)') -or
        -not $document.Content.Contains("if(input)input.value='';") -or
        -not $document.Content.Contains('function importPostsFromJson(event)')) {
        throw 'File-picker controls must target native input overlays while preserving existing image-edit and JSON import handling.'
    }

    $videoFolderButtonStart = $document.Content.IndexOf('id="separateVideoFolderBtn"')
    if ($videoFolderButtonStart -lt 0) { throw 'The Separate video folder toggle is missing.' }
    $videoFolderSettingsStart = $document.Content.IndexOf('id="separateVideoFolderSettings"', $videoFolderButtonStart)
    $videoFolderActionStart = $document.Content.LastIndexOf('<div class="background-actions"', $videoFolderButtonStart)
    if ($videoFolderSettingsStart -le $videoFolderButtonStart -or $videoFolderActionStart -lt 0 -or
        -not $document.Content.Substring($videoFolderActionStart, $videoFolderSettingsStart - $videoFolderActionStart).Contains('margin-bottom:10px') -or
        -not $document.Content.Contains('function getBrowserQueueFolderPreferences()') -or
        -not $document.Content.Contains('window.getBrowserQueueFolderPreferences=getBrowserQueueFolderPreferences;')) {
        throw 'The Separate video folder toggle must be spaced 10px above its location label.'
    }

    if (-not $document.Content.Contains('const downloadHistorySyncedKeys=new Set();') -or
        -not $document.Content.Contains('function rememberDownloadHistorySyncKey(') -or
        -not $document.Content.Contains('function toggleSeparateVideoFolder(){setUiPref(''separateVideoFolder'',!isSeparateVideoFolderEnabled());syncSeparateVideoFolderSettings();scheduleDownloadHistorySync();')) {
        throw 'Download-history folder snapshots must be reused so toggling Separate video folder does not force a rescan on every click.'
    }

    if ($document.Content.Contains('webkitdirectory') -or
        $document.Content.Contains('Choose folder…') -or
        $document.Content.Contains('No folder selected') -or
        $document.Content.Contains('IMAGE TOOL')) {
        throw 'Photo Resizer must open a multi-image picker instead of selecting a folder, and the obsolete IMAGE TOOL label must stay removed.'
    }

    $resizerPageStart = $document.Content.IndexOf('<section class="content resizer-page" id="resizerPage"')
    $resizerPageEnd = $document.Content.IndexOf('<section class="content settings" id="settingsPage"', $resizerPageStart)
    if ($resizerPageStart -lt 0 -or $resizerPageEnd -le $resizerPageStart) {
        throw 'The Photo Resizer page markup could not be inspected.'
    }
    $resizerPageSource = $document.Content.Substring($resizerPageStart, $resizerPageEnd - $resizerPageStart)
    if ($resizerPageSource.Contains('id="resizerSelectionLabel"') -or
        $resizerPageSource.Contains('id="resizerSelectedCount"') -or
        $resizerPageSource.Contains('id="resizerUploadedCount"') -or
        $resizerPageSource.Contains('id="resizerUploadBtn"') -or
        $resizerPageSource.Contains('Upload selected files')) {
        throw 'Photo Resizer must not show redundant selection/upload counts or a separate Upload selected files action.'
    }
    if (-not $document.Content.Contains('>Edit (beta)</button>') -or
        -not $resizerPageSource.Contains('<h1>Image Edit</h1>') -or
        -not $document.Content.Contains("mode==='browser'?'Browser':mode==='resizer'?'Image Edit'")) {
        throw 'The sidebar must say Edit (beta) while the page heading and breadcrumb remain Image Edit.'
    }
    if (-not $resizerPageSource.Contains('Choose photos for Resize. Click or check a thumbnail to toggle it in this panel')) {
        throw 'The Resize panel must explain its independent thumbnail selection.'
    }
    $nativePickedFileStorePath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\NativePickedFileStore.cs'
    $nativePickedFileStoreSource = Get-Content -LiteralPath $nativePickedFileStorePath -Raw
    if (-not $resizerPageSource.Contains('id="photoEditChooseImagesBtn" onclick="choosePhotoEditImages()">Choose images…</button>') -or
        -not $resizerPageSource.Contains('id="photoEditImagesInput" type="file" accept="image/*" multiple aria-label="Choose images for Image Edit" onchange="handlePhotoEditImagesPicked(event)"') -or
        -not $resizerPageSource.Contains('id="photoEditSelectionToggleBtn" onclick="togglePhotoEditSelection()"') -or
        $resizerPageSource.Contains('id="photoEditSelectAllBtn"') -or
        $resizerPageSource.Contains('id="photoEditClearSelectionBtn"') -or
        -not $resizerPageSource.Contains('id="photoEditClearImagesBtn" onclick="resetPhotoEditQueue()"') -or
        -not $document.Content.Contains('function togglePhotoEditSelection()') -or
        -not $document.Content.Contains('function getPhotoEditDuplicateKey(file)') -or
        -not $document.Content.Contains('pathFingerprint') -or
        -not $nativePickedFileStoreSource.Contains('CreatePathFingerprint(record.Path)') -or
        -not $nativePickedFileStoreSource.Contains('HMACSHA256.HashData(_pathFingerprintKey, pathBytes)') -or
        -not $nativePickedFileStoreSource.Contains('string PathFingerprint') -or
        -not $resizerPageSource.Contains('id="photoEditFilePanel"') -or
        $resizerPageSource.Contains('id="photoEditOutputFolder"') -or
        -not $resizerPageSource.Contains('id="photoAdjustmentDownloadBtn" onclick="startPhotoEditDownload()"') -or
        -not $document.Content.Contains('function getConfiguredPhotoResizerOutputFolder(){return isSeparateResizedImagesFolderEnabled()?') -or
        -not $document.Content.Contains('function getConfiguredPhotoEditOutputFolder(){return isSeparateEditedImagesFolderEnabled()?') -or
        -not $document.Content.Contains('function getConfiguredMomentCaptureOutputFolder(){return isSeparateMomentCaptureFolderEnabled()?') -or
        -not $document.Content.Contains('function syncPhotoOutputFolderDisplays()') -or
        -not $document.Content.Contains('stillSane.photoResizerOutputFolderV1') -or
        -not $document.Content.Contains('stillSane.photoEditOutputFolderV1') -or
        -not $document.Content.Contains('stillSane.momentCaptureOutputFolderV1') -or
        -not $document.Content.Contains('id="separateResizedImagesFolderBtn"') -or
        -not $document.Content.Contains('id="separateEditedImagesFolderBtn"') -or
        -not $document.Content.Contains('id="separateMomentCaptureFolderBtn"') -or
        -not $document.Content.Contains('id="momentCaptureOpenFolderBtn" onclick="openMomentCaptureOutputFolder()"') -or
        -not $document.Content.Contains('function openMomentCaptureOutputFolder()') -or
        -not $document.Content.Contains('let photoEditQueue=[];') -or
        -not $document.Content.Contains('function resetPhotoEditQueue()')) {
        throw 'Resize, Image Edit, and Moment capture must retain independent queues and use their configured output folders.'
    }
    $momentCaptureActionsStart = $document.Content.IndexOf('<div class="moment-capture-actions">')
    $momentCaptureFrameButtonIndex = if ($momentCaptureActionsStart -ge 0) { $document.Content.IndexOf('id="momentCaptureFrameBtn"', $momentCaptureActionsStart) } else { -1 }
    $momentCaptureOpenFolderIndex = if ($momentCaptureActionsStart -ge 0) { $document.Content.IndexOf('id="momentCaptureOpenFolderBtn"', $momentCaptureActionsStart) } else { -1 }
    $momentCaptureActionsEnd = if ($momentCaptureOpenFolderIndex -ge 0) { $document.Content.IndexOf('</div>', $momentCaptureOpenFolderIndex) } else { -1 }
    if ($momentCaptureActionsStart -lt 0 -or $momentCaptureFrameButtonIndex -le $momentCaptureActionsStart -or
        $momentCaptureOpenFolderIndex -le $momentCaptureFrameButtonIndex -or $momentCaptureActionsEnd -le $momentCaptureOpenFolderIndex) {
        throw 'The Moment capture folder button must follow Capture & save frame in the action row.'
    }
    $photoEditHeadingStart = $resizerPageSource.IndexOf('<div class="settings-card-head photo-adjustments-card-head">')
    $photoEditHeadingEnd = if ($photoEditHeadingStart -ge 0) { $resizerPageSource.IndexOf('<div class="setting-body">', $photoEditHeadingStart) } else { -1 }
    $photoEditToolbarStart = $resizerPageSource.IndexOf('<div class="resizer-files-toolbar"><div class="photo-adjustment-actions">')
    $photoEditChoosePosition = if ($photoEditToolbarStart -ge 0) { $resizerPageSource.IndexOf('id="photoEditChooseImagesBtn"', $photoEditToolbarStart) } else { -1 }
    $photoEditDownloadPosition = if ($photoEditChoosePosition -ge 0) { $resizerPageSource.IndexOf('id="photoAdjustmentDownloadBtn"', $photoEditChoosePosition) } else { -1 }
    $photoEditFolderPosition = if ($photoEditDownloadPosition -ge 0) { $resizerPageSource.IndexOf('id="photoAdjustmentOpenFolderBtn"', $photoEditDownloadPosition) } else { -1 }
    $photoEditSelectionPosition = if ($photoEditFolderPosition -ge 0) { $resizerPageSource.IndexOf('id="photoEditSelectionToggleBtn"', $photoEditFolderPosition) } else { -1 }
    $photoEditClearPosition = if ($photoEditSelectionPosition -ge 0) { $resizerPageSource.IndexOf('id="photoEditClearImagesBtn"', $photoEditSelectionPosition) } else { -1 }
    $photoEditResetPosition = if ($photoEditHeadingStart -ge 0) { $resizerPageSource.IndexOf('id="photoAdjustmentResetBtn"', $photoEditHeadingStart) } else { -1 }
    if ($photoEditHeadingStart -lt 0 -or $photoEditHeadingEnd -le $photoEditHeadingStart -or
        $photoEditToolbarStart -le $photoEditHeadingEnd -or $photoEditChoosePosition -le $photoEditToolbarStart -or
        $photoEditDownloadPosition -le $photoEditChoosePosition -or $photoEditFolderPosition -le $photoEditDownloadPosition -or
        $photoEditSelectionPosition -le $photoEditFolderPosition -or $photoEditClearPosition -le $photoEditSelectionPosition -or
        $photoEditResetPosition -le $photoEditHeadingStart -or $photoEditResetPosition -ge $photoEditHeadingEnd -or
        -not $document.Content.Contains('.photo-adjustment-files-panel .resizer-files-toolbar>.photo-adjustment-selection-actions{margin-left:auto;align-self:flex-end}') -or
        -not $document.Content.Contains('.photo-adjustment-photo-picker{min-width:0;margin:0 0 12px}')) {
        throw 'Image Edit must order its actions, bottom-align selection controls, and preserve the 12px photo-queue gap.'
    }
    $photoEditHeadingSource = $resizerPageSource.Substring($photoEditHeadingStart, $photoEditHeadingEnd - $photoEditHeadingStart)
    if ($photoEditHeadingSource.Contains('id="photoEditChooseImagesBtn"') -or
        $photoEditHeadingSource.Contains('id="photoAdjustmentDownloadBtn"') -or
        $photoEditHeadingSource.Contains('id="photoAdjustmentOpenFolderBtn"')) {
        throw 'Choose images, Download, and Open folder must be removed from the Image Edit heading row.'
    }
    if ($resizerPageSource.Contains('id="photoEditAvailableCount"') -or
        $resizerPageSource.Contains('This Image Edit selection is independent of the Resize tab.') -or
        $resizerPageSource.Contains('Save edited images to')) {
        throw 'Image Edit must not repeat the queue-count prompt, selection instructions, or the Settings output folder already accessible from its toolbar.'
    }
    if (-not $document.Content.Contains('function renderPhotoFileStrip(queue,listId') -or
        -not $document.Content.Contains("renderPhotoFileStrip(photoResizeQueue,'resizerFileList'") -or
        -not $document.Content.Contains("renderPhotoFileStrip(photoEditQueue,'photoAdjustmentPhotoStrip'") -or
        -not $document.Content.Contains('.resizer-file-list,.photo-adjustment-photo-strip{display:flex;flex-flow:row nowrap;') -or
        -not $resizerPageSource.Contains('onscroll="syncResizerPhotoScrollButtons()" onwheel="scrollResizerPhotosByWheel(event)"') -or
        -not $resizerPageSource.Contains('onscroll="syncPhotoAdjustmentPhotoScrollButtons()" onwheel="scrollPhotoAdjustmentPhotosByWheel(event)"')) {
        throw 'Resize and Image Edit thumbnails must share the same horizontal row rendering and scroll behavior.'
    }
    $mainPickStart = $document.Content.IndexOf('function handleResizerImagesPicked(selection)')
    $editPickStart = $document.Content.IndexOf('function handlePhotoEditImagesPicked(selection)', $mainPickStart)
    $editPickEnd = $document.Content.IndexOf('async function uploadPhotoResizeQueueItems(', $editPickStart)
    $mainResizeStart = $document.Content.IndexOf('async function startPhotoResizing(){')
    $editDownloadStart = $document.Content.IndexOf('async function startPhotoEditDownload(){', $mainResizeStart)
    $mainResizeEnd = $editDownloadStart
    $editDownloadEnd = $document.Content.IndexOf('function resetResizerQueue(){', $editDownloadStart)
    if ($mainPickStart -lt 0 -or $editPickStart -le $mainPickStart -or $editPickEnd -le $editPickStart -or
        $mainResizeStart -lt 0 -or $mainResizeEnd -le $mainResizeStart -or $editDownloadStart -lt 0 -or $editDownloadEnd -le $editDownloadStart) {
        throw 'The independent Resize and Image Edit queue handlers could not be inspected.'
    }
    $mainPickSource = $document.Content.Substring($mainPickStart, $editPickStart - $mainPickStart)
    $editPickSource = $document.Content.Substring($editPickStart, $editPickEnd - $editPickStart)
    $mainResizeSource = $document.Content.Substring($mainResizeStart, $mainResizeEnd - $mainResizeStart)
    $editDownloadSource = $document.Content.Substring($editDownloadStart, $editDownloadEnd - $editDownloadStart)
    if ($mainPickSource.Contains('photoEditQueue') -or $editPickSource.Contains('photoResizeQueue') -or
        -not $editPickSource.Contains('photoEditQueue.push(...queue)') -or
        -not $editPickSource.Contains('seen.has(key)') -or
        $editPickSource.Contains('photoEditQueue=queue;') -or
        $editPickSource.Contains('revokePhotoResizeThumbnailUrls(photoEditQueue)') -or
        $editPickSource.Contains('resetPhotoEditPreviewState()') -or
        -not $mainResizeSource.Contains('getConfiguredPhotoResizerOutputFolder()') -or
        $mainResizeSource.Contains('getPhotoResizeAdjustmentPayload') -or $mainResizeSource.Contains('photoResizeApplyToBatch') -or
        $mainResizeSource.Contains('photoEditQueue') -or $editDownloadSource.Contains('photoResizeQueue') -or
        $editDownloadSource.Contains('resizerMaxSide') -or $editDownloadSource.Contains('photoResizeUpscaleEnabled') -or
        $editDownloadSource.Contains('isResizerNoDuplicatesEnabled') -or $editDownloadSource.Contains('getConfiguredPhotoResizerOutputFolder') -or
        -not $editDownloadSource.Contains('getPhotoResizeAdjustmentPayload()') -or
        -not $editDownloadSource.Contains('getConfiguredPhotoEditOutputFolder()') -or
        -not $editDownloadSource.Contains('maxSide:16384') -or
        -not $editDownloadSource.Contains('upscale:false')) {
        throw 'Edit selection and adjustments must stay isolated from the Resize queue and sizing configuration.'
    }
    $pickRowStart = $resizerPageSource.IndexOf('<div class="resizer-pick-row">')
    $pickRowEnd = $resizerPageSource.IndexOf('<div class="resizer-files-panel" id="resizerFilePanel">', $pickRowStart)
    $uploadActionsStart = $resizerPageSource.IndexOf('<div class="resizer-upload-actions">')
    $uploadActionsEnd = $resizerPageSource.IndexOf('</div>', $uploadActionsStart)
    $maxRowStart = $resizerPageSource.IndexOf('<div class="resizer-max-row">')
    $maxRowEnd = $resizerPageSource.IndexOf('</div>', $maxRowStart)
    if ($pickRowStart -lt 0 -or $pickRowEnd -le $pickRowStart -or
        $resizerPageSource.Substring($pickRowStart, $pickRowEnd - $pickRowStart).Contains('Start over') -or
        $resizerPageSource.Contains('id="resizerResetBtn"') -or
        $resizerPageSource.Contains('id="resizerSelectAllBtn"') -or
        $resizerPageSource.Contains('id="resizerClearSelectionBtn"') -or
        -not $resizerPageSource.Contains('id="resizerSelectionToggleBtn" onclick="togglePhotoResizeSelection()"') -or
        -not $resizerPageSource.Contains('id="resizerClearImagesBtn" onclick="resetResizerQueue()" disabled>Clear images</button>')) {
        throw 'Resize must replace Start over and its separate Select all/Clear controls with the shared selection toggle and Clear images action.'
    }
    if ($uploadActionsStart -lt 0 -or $uploadActionsEnd -le $uploadActionsStart) {
        throw 'The Resize/output-folder action row is missing.'
    }
    $uploadActions = $resizerPageSource.Substring($uploadActionsStart, $uploadActionsEnd - $uploadActionsStart)
    $convertPosition = $uploadActions.IndexOf('id="resizerStartBtn"')
    $folderPosition = $uploadActions.IndexOf('id="resizerOpenOutputFolderBtn"')
    if ($convertPosition -lt 0 -or $folderPosition -le $convertPosition -or
        -not $uploadActions.Contains('>Resize</button>')) {
        throw 'Resize must precede the Open folder button beneath the image list.'
    }
    if (-not $document.Content.Contains('body #resizerStartBtn{height:37px!important;min-height:37px!important;max-height:37px!important;box-sizing:border-box!important;border-width:1px!important}') -or
        -not $document.Content.Contains('.restricted-actions .primary,.restricted-actions .outline{height:28px;min-height:28px;max-height:28px;box-sizing:border-box;padding:0 10px;font-size:10px}') -or
        -not $document.Content.Contains('body #resizerOpenOutputFolderBtn{width:37px!important;min-width:37px!important;max-width:37px!important;height:37px!important;min-height:37px!important;max-height:37px!important;flex:0 0 37px!important;box-sizing:border-box!important;padding:0!important}') -or
        $document.Content.Contains('body #resizerOpenOutputFolderBtn::before{inset:0}') -or
        $document.Content.Contains('body #resizerOpenOutputFolderBtn::after{inset:0;filter:none}') -or
        $document.Content.Contains('body #uploadJsonToolbarBtn') -or
        $document.Content.Contains('body #resizerStartBtn::before') -or
        $document.Content.Contains('body #resizerStartBtn::after')) {
        throw 'Resize must retain the standard primary outline; the output-folder control must use the 37px icon-button size without an inset glow.'
    }
    if ($maxRowStart -lt 0 -or $maxRowEnd -le $maxRowStart) {
        throw 'The maximum-side and upscaler controls are missing.'
    }
    $maxRow = $resizerPageSource.Substring($maxRowStart, $maxRowEnd - $maxRowStart)
    $maxSidePosition = $maxRow.IndexOf('id="resizerMaxSide"')
    $pixelUnitPosition = $maxRow.IndexOf('>px</span>')
    $upscalePosition = $maxRow.IndexOf('id="resizerUpscaleToggleBtn"')
    if ($maxSidePosition -lt 0 -or $pixelUnitPosition -le $maxSidePosition -or $upscalePosition -le $pixelUnitPosition) {
        throw 'The upscaler toggle must follow the maximum-side field and px unit.'
    }
    $maxSideDownPosition = $maxRow.IndexOf('id="resizerMaxSideDownBtn"')
    $maxSideUpPosition = $maxRow.IndexOf('id="resizerMaxSideUpBtn"')
    if (-not $maxRow.Contains('step="50"') -or
        $maxSideDownPosition -lt 0 -or $maxSideUpPosition -le $maxSideDownPosition -or
        -not $maxRow.Contains('onclick="stepResizerMaxSide(-1)"') -or
        -not $maxRow.Contains('onclick="stepResizerMaxSide(1)"') -or
        -not $document.Content.Contains('function stepResizerMaxSide(direction)') -or
        -not $document.Content.Contains('#resizerMaxSide::-webkit-inner-spin-button,#resizerMaxSide::-webkit-outer-spin-button{-webkit-appearance:none;margin:0}') -or
        -not $document.Content.Contains('#resizerMaxSide{width:112px;box-sizing:border-box;appearance:textfield;-moz-appearance:textfield}')) {
        throw 'The native size spinner must be replaced with custom 50-pixel increment and decrement buttons.'
    }
    if (-not $maxRow.Contains('class="filter icon-filter-btn source-toggle-button active-outline-control resizer-upscale-toggle active"') -or
        -not $document.Content.Contains("button.classList.toggle('active',photoResizeUpscaleEnabled)") -or
        $document.Content.Contains('#resizerUpscaleToggleBtn.is-enabled')) {
        throw 'The upscaler toggle must share the Hide videos button active-outline styling.'
    }
    if ($resizerPageSource.Contains('resizer-confirm-actions')) {
        throw 'The old separate right-side action row must stay removed.'
    }

    if (-not $document.Content.Contains('body[data-theme="v3"] .icon-filter-btn.active-outline-control.active{color:var(--t-nav-act-ink,#090a0c)}')) {
        throw 'Theme 3 active toolbar icons must use the dark foreground on their light active buttons.'
    }
    $dyiHelpButtonStart = $document.Content.IndexOf('<button type="button" class="icon-btn" id="dyiHelpBtn"')
    $dyiHelpButtonEnd = $document.Content.IndexOf('</button>', $dyiHelpButtonStart)
    if ($dyiHelpButtonStart -lt 0 -or $dyiHelpButtonEnd -le $dyiHelpButtonStart) {
        throw 'The icon-only export-help button is missing.'
    }
    $dyiHelpButton = $document.Content.Substring($dyiHelpButtonStart, $dyiHelpButtonEnd - $dyiHelpButtonStart)
    if (-not $dyiHelpButton.Contains('class="dyi-help-icon"') -or
        -not $dyiHelpButton.Contains('aria-label="Open how to export instructions"') -or
        $dyiHelpButton.Contains('>Doc')) {
        throw 'The export-help button must be icon-only and accessible.'
    }
    $resizeStart = $document.Content.IndexOf('async function startPhotoResizing(){')
    $resizeReset = $document.Content.IndexOf('function resetResizerQueue(){', $resizeStart)
    if ($resizeStart -lt 0 -or $resizeReset -le $resizeStart) {
        throw 'The combined Resize workflow could not be inspected.'
    }
    $resizeStartSource = $document.Content.Substring($resizeStart, $resizeReset - $resizeStart)
    $uploadStep = $resizeStartSource.IndexOf('await uploadResizerFiles(pending)')
    $convertStep = $resizeStartSource.IndexOf("getApiUrls('/api/resize-convert')")
    if ($uploadStep -lt 0 -or $convertStep -le $uploadStep) {
        throw 'Resize must upload the selected images before conversion.'
    }
    if ($resizeStartSource.Contains('window.confirm(')) {
        throw 'Resize must proceed without a browser confirmation popup.'
    }

    $saveUrlRendererStart = $document.Content.IndexOf('function renderSaveUrlTab(){')
    $saveUrlRendererEnd = if ($saveUrlRendererStart -ge 0) { $document.Content.IndexOf('applySourceVisibility();updateTileSrcBadges()}', $saveUrlRendererStart) } else { -1 }
    if ($saveUrlRendererStart -lt 0 -or $saveUrlRendererEnd -le $saveUrlRendererStart) {
        throw 'The Save via URL post renderer could not be inspected.'
    }
    $saveUrlRendererSource = $document.Content.Substring($saveUrlRendererStart, $saveUrlRendererEnd - $saveUrlRendererStart)
    $saveUrlSourceToggle = $saveUrlRendererSource.IndexOf('data-role="source-toggle"')
    $saveUrlHistoryToggle = $saveUrlRendererSource.IndexOf('data-role="download-history-toggle"')
    $saveUrlVideoToggle = $saveUrlRendererSource.IndexOf('data-role="video-toggle"')
    if ($saveUrlSourceToggle -lt 0 -or $saveUrlHistoryToggle -le $saveUrlSourceToggle -or
        $saveUrlVideoToggle -le $saveUrlHistoryToggle -or
        -not $saveUrlRendererSource.Contains('onclick="toggleSourceBadges()"') -or
        -not $saveUrlRendererSource.Contains('onclick="toggleDownloadHistoryVisibility()"')) {
        throw 'Save via URL visibility controls must be source labels, download-history marks, then videos, sharing the Saved Posts JSON controls.'
    }

    $detailFolderStackStart = $document.Content.IndexOf('<div class="detail-actions-folder-stack">')
    $detailCloseButtonStart = $document.Content.IndexOf('class="icon-btn detail-close-btn" id="detailCloseBtn"')
    $detailToolbarFolderIndex = $document.Content.IndexOf('id="detailOpenFolderBtn"')
    $detailCloseButtonEnd = if ($detailCloseButtonStart -ge 0) { $document.Content.IndexOf('</button>', $detailCloseButtonStart) } else { -1 }
    if ($detailFolderStackStart -lt 0 -or $detailCloseButtonStart -le $detailFolderStackStart -or
        $detailToolbarFolderIndex -le $detailCloseButtonStart -or $detailCloseButtonEnd -le $detailCloseButtonStart) {
        throw 'The post-detail close button and Open folder button could not be inspected in the expected stack.'
    }
    $detailCloseButton = $document.Content.Substring($detailCloseButtonStart, $detailCloseButtonEnd - $detailCloseButtonStart)
    if (-not $detailCloseButton.Contains('onclick="closePost()"') -or
        -not $document.Content.Contains('.detail-actions-folder-stack{position:relative;display:flex;align-items:center;flex:0 0 auto}.detail-actions-folder-stack .detail-close-btn{position:absolute;right:0;bottom:calc(100% + 8px);z-index:6}')) {
        throw 'The detail close button must sit directly above the Open folder button.'
    }

    $resizeListStart = $document.Content.IndexOf('function renderPhotoResizeFileList(')
    $resizeControlsStart = $document.Content.IndexOf('function syncPhotoResizeControls({renderFiles=true}={})', $resizeListStart)
    if ($resizeListStart -lt 0 -or $resizeControlsStart -le $resizeListStart) {
        throw 'The Photo Resizer file-list renderer could not be inspected.'
    }
    $photoFileStripStart = $document.Content.IndexOf('function renderPhotoFileStrip(')
    $photoFileStripEnd = if ($photoFileStripStart -ge 0) { $document.Content.IndexOf('function renderPhotoResizeFileList(', $photoFileStripStart) } else { -1 }
    if ($photoFileStripStart -lt 0 -or $photoFileStripEnd -le $photoFileStripStart) {
        throw 'The shared photo thumbnail renderer could not be inspected.'
    }
    $photoFileStripSource = $document.Content.Substring($photoFileStripStart, $photoFileStripEnd - $photoFileStripStart)
    if ($photoFileStripSource.Contains('type="checkbox" disabled')) {
        throw 'Photo selection checkboxes must stay interactive after Resize or Download is clicked.'
    }
    $resizeControlsEnd = $document.Content.IndexOf('function resetPhotoResizeStaging(', $resizeControlsStart)
    if ($resizeControlsEnd -le $resizeControlsStart) {
        throw 'The Photo Resizer selection controls could not be inspected.'
    }
    $resizeControlsSource = $document.Content.Substring($resizeControlsStart, $resizeControlsEnd - $resizeControlsStart)
    if ($resizeControlsSource.Contains('selectionToggle.disabled=busy') -or
        -not $resizeControlsSource.Contains('selectionToggle.disabled=!hasFiles') -or
        -not $resizeControlsSource.Contains('clearImages.disabled=busy||!hasFiles')) {
        throw 'Resize selection must remain editable during a conversion while Clear images stays disabled until it is safe.'
    }
    $resizeSelectionHandlersStart = $document.Content.IndexOf('function setPhotoResizeSelected(')
    $resizeSelectionHandlersEnd = $document.Content.IndexOf('async function deletePhotoResizeSession(', $resizeSelectionHandlersStart)
    if ($resizeSelectionHandlersStart -lt 0 -or $resizeSelectionHandlersEnd -le $resizeSelectionHandlersStart) {
        throw 'The Photo Resizer selection handlers could not be inspected.'
    }
    $resizeSelectionHandlersSource = $document.Content.Substring($resizeSelectionHandlersStart, $resizeSelectionHandlersEnd - $resizeSelectionHandlersStart)
    if ($resizeSelectionHandlersSource.Contains('photoResizeBusy') -or
        $resizeSelectionHandlersSource.Contains('item.uploaded') -or
        -not $resizeSelectionHandlersSource.Contains('function togglePhotoResizeSelection()') -or
        -not $resizeSelectionHandlersSource.Contains('syncPhotoResizeSelectionTiles();') -or
        -not $resizeSelectionHandlersSource.Contains('syncPhotoResizeControls({renderFiles:false})') -or
        $resizeSelectionHandlersSource.Contains('syncPhotoResizeControls();')) {
        throw 'Resize selection must use a responsive all/clear toggle and update existing tiles without blocking during uploads.'
    }

    $editSelectionHandlersStart = $document.Content.IndexOf('function setPhotoEditSelected(')
    if ($editSelectionHandlersStart -lt 0) {
        throw 'Image Edit selection handlers could not be inspected.'
    }
    $editSelectionHandlersEnd = $document.Content.IndexOf('async function deletePhotoResizeSession(', $editSelectionHandlersStart)
    if ($editSelectionHandlersEnd -le $editSelectionHandlersStart) {
        throw 'Image Edit selection handlers could not be inspected.'
    }
    $editSelectionHandlersSource = $document.Content.Substring($editSelectionHandlersStart, $editSelectionHandlersEnd - $editSelectionHandlersStart)
    if (-not $editSelectionHandlersSource.Contains('function selectAllPhotoEditFiles(selected)') -or
        -not $editSelectionHandlersSource.Contains('function togglePhotoEditSelection()') -or
        -not $editSelectionHandlersSource.Contains('syncPhotoEditSelectionTiles();') -or
        -not $editSelectionHandlersSource.Contains('syncPhotoEditControls({renderFiles:false,refreshPreview:false})') -or
        $editSelectionHandlersSource.Contains('syncPhotoEditControls();')) {
        throw 'Image Edit bulk selection must update existing tiles without rebuilding the photo strip or refreshing its preview.'
    }

    $photoSelectionSyncStart = $document.Content.IndexOf('function syncPhotoSelectionTiles(')
    if ($photoSelectionSyncStart -lt 0) {
        throw 'The shared photo-selection synchronizer could not be inspected.'
    }
    $photoSelectionSyncEnd = $document.Content.IndexOf('function syncPhotoResizeSelectionTiles(', $photoSelectionSyncStart)
    if ($photoSelectionSyncEnd -le $photoSelectionSyncStart) {
        throw 'The shared photo-selection synchronizer could not be inspected.'
    }
    $photoSelectionSyncSource = $document.Content.Substring($photoSelectionSyncStart, $photoSelectionSyncEnd - $photoSelectionSyncStart)
    if (-not $document.Content.Contains('const PHOTO_SELECTION_SYNC_BATCH_SIZE=128;') -or
        -not $photoSelectionSyncSource.Contains('for(;index<end;index++)') -or
        -not $photoSelectionSyncSource.Contains('index+PHOTO_SELECTION_SYNC_BATCH_SIZE') -or
        -not $photoSelectionSyncSource.Contains('requestAnimationFrame(syncBatch)') -or
        -not $photoSelectionSyncSource.Contains('version!==syncState.version') -or
        -not $photoSelectionSyncSource.Contains('queue!==getCurrentQueue()') -or
        -not $photoSelectionSyncSource.Contains("list!==document.getElementById(listId)") -or
        $photoSelectionSyncSource.Contains('list.innerHTML')) {
        throw 'Bulk thumbnail selection must synchronize in bounded animation-frame batches and cancel stale work without rebuilding either strip.'
    }
    if (-not $document.Content.Contains("syncPhotoSelectionTiles(photoResizeQueue,'resizerFileList',photoResizeSelectionTileSync,()=>photoResizeQueue,options)") -or
        -not $document.Content.Contains("syncPhotoSelectionTiles(photoEditQueue,'photoAdjustmentPhotoStrip',photoEditSelectionTileSync,()=>photoEditQueue,options)") -or
        -not $document.Content.Contains('photoResizeSelectionTileSync.version++') -or
        -not $document.Content.Contains('photoEditSelectionTileSync.version++')) {
        throw 'Resize and Image Edit must share generation-guarded bulk selection sync while invalidating work after tile-strip renders.'
    }
    if (-not $document.Content.Contains('<video id="momentCaptureVideo" controls playsinline preload="metadata" hidden') -or
        $document.Content.Contains('id="momentCaptureStopBtn"') -or
        $document.Content.Contains('function stopMomentCaptureVideo()')) {
        throw 'Moment capture must keep native playback controls and remove the separate Stop button.'
    }
    $momentCaptureSourceStart = $document.Content.IndexOf('async function captureMomentFrame(){')
    if ($momentCaptureSourceStart -lt 0) {
        throw 'Moment capture frame handling could not be inspected.'
    }
    $momentCaptureSourceEnd = $document.Content.IndexOf('async function openFolderPathInExplorer(', $momentCaptureSourceStart)
    if ($momentCaptureSourceEnd -le $momentCaptureSourceStart) {
        throw 'Moment capture frame handling could not be inspected.'
    }
    $momentCaptureSource = $document.Content.Substring($momentCaptureSourceStart, $momentCaptureSourceEnd - $momentCaptureSourceStart)
    if (-not $document.Content.Contains('let momentCaptureQueue=[];') -or
        -not $document.Content.Contains('function toggleMomentCaptureVideoSelection()') -or
        -not $document.Content.Contains('function resetMomentCaptureQueue()') -or
        -not $document.Content.Contains('function openSelectedPostVideosInMomentCapture()') -or
        -not $document.Content.Contains('function openSelectedUrlVideosInMomentCapture()') -or
        -not $document.Content.Contains('function transferSelectedVideosToMomentCapture(') -or
        -not $document.Content.Contains('function addMomentCaptureRemoteVideos(sources)') -or
        -not $document.Content.Contains('function momentCaptureRemoteVideoSource(post,videoIndex,pic)') -or
        -not $document.Content.Contains('let momentCaptureFrameNumber=0;') -or
        -not $momentCaptureSource.Contains('const nextFrameNumber=(Number(activeItem.frameNumber)||0)+1;') -or
        -not $momentCaptureSource.Contains('const filename=`${momentCaptureBaseName(activeItem)}_frame_${nextFrameNumber}.jpg`;') -or
        -not $momentCaptureSource.Contains('activeItem.frameNumber=nextFrameNumber;') -or
        -not $momentCaptureSource.Contains('const folder=getConfiguredMomentCaptureOutputFolder();') -or
        -not $document.Content.Contains('id="momentCaptureOpenFolderBtn" onclick="openMomentCaptureOutputFolder()"') -or
        -not $momentCaptureSource.Contains('canvas.hidden=true;') -or
        $momentCaptureSource.Contains('canvas.hidden=false') -or
        $momentCaptureSource.Contains('toISOString()') -or
        $momentCaptureSource.Contains('const millis=')) {
        throw 'Moment capture must queue multiple videos, transfer selected videos from both post tabs, keep its processing canvas hidden, and save frames with short sequential names.'
    }
    $momentFooterButtonIndex = $document.Content.IndexOf('id="openSelectedVideosMomentCaptureBtn"')
    $momentFooterDownloadIndex = $document.Content.IndexOf('id="downloadBtn"')
    if ($momentFooterButtonIndex -lt 0 -or $momentFooterDownloadIndex -lt 0 -or
        $momentFooterButtonIndex -ge $momentFooterDownloadIndex -or
        -not $document.Content.Contains('id="openSelectedVideosMomentCaptureBtn" class="icon-btn"') -or
        -not $document.Content.Contains('function countSelectedVideos()') -or
        -not $document.Content.Contains('function openSelectedLibraryVideosInMomentCapture()') -or
        -not $document.Content.Contains('for(const post of posts)')) {
        throw 'The Saved Posts pages bar must place an icon-only Moment capture action beside Download and transfer all selected videos.'
    }
    if (-not $document.Content.Contains('class="resizer-file-list moment-capture-video-list" id="momentCaptureFileList"') -or
        -not $document.Content.Contains('height:39px;min-height:39px;max-height:none') -or
        $document.Content.Contains('.moment-capture-video-list{height:45px;min-height:45px}')) {
        throw 'Moment capture must use the same 39px thumbnail row as Resize and Image Edit, without a taller Moment-only override.'
    }
    if (-not $document.Content.Contains('function isMomentCaptureVideoUrl(rawUrl)') -or
        -not $document.Content.Contains('if(!value)return false;') -or
        -not $document.Content.Contains('revokeMomentCaptureQueueObjectUrls()')) {
        throw 'Moment capture must reject empty remote video URLs and release queued object URLs on page exit.'
    }
    if (-not $document.Content.Contains('#detailGrid .picture-art>video:fullscreen,#detailGrid .picture-art>video:-webkit-full-screen,#saveUrlGrid .picture-art>video:fullscreen,#saveUrlGrid .picture-art>video:-webkit-full-screen{position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;max-width:100vw!important;max-height:100vh!important;object-fit:contain!important;object-position:center!important;background:#000!important;z-index:9999!important}') -or
        -not $document.Content.Contains('controls preload="metadata" playsinline')) {
        throw 'Saved Posts and Save via URL native video enlargement must fill the app viewport while containing the full source frame.'
    }

    if (-not $document.Content.Contains('if(photoResizeBusy){photoResizeSelectionChangedDuringRun=true;return false}') -or
        -not $document.Content.Contains('if(selectionChanged)resetPhotoResizeStaging(false)') -or
        -not $resizeStartSource.Contains('const selectionSnapshot=photoResizeQueue.filter(item=>item.selected)')) {
        throw 'The active Resize batch must preserve its selection snapshot and prepare later selection edits for the next run.'
    }

    $downloadSummaryStart = $document.Content.IndexOf('function formatMediaDownloadSummary(')
    if ($downloadSummaryStart -lt 0) {
        throw 'The media download completion summary is missing.'
    }
    $downloadSummaryEnd = $document.Content.IndexOf('async function downloadUrlTabSelected()', $downloadSummaryStart)
    if ($downloadSummaryEnd -le $downloadSummaryStart) {
        throw 'The media download completion summary could not be inspected.'
    }
    $downloadSummarySource = $document.Content.Substring($downloadSummaryStart, $downloadSummaryEnd - $downloadSummaryStart)
    if ($downloadSummarySource.Contains('targetFolder')) {
        throw 'Download completion messages must not expose the saved folder path.'
    }

    $downloadBuilderStart = $document.Content.IndexOf('function buildDownloadItemsForPost(')
    if ($downloadBuilderStart -lt 0) {
        throw 'The selected-media filename builder is missing.'
    }
    $downloadBuilderEnd = $document.Content.IndexOf('async function performMediaDownload(', $downloadBuilderStart)
    if ($downloadBuilderEnd -le $downloadBuilderStart) {
        throw 'The download request handler is missing.'
    }
    $downloadBuilderSource = $document.Content.Substring($downloadBuilderStart, $downloadBuilderEnd - $downloadBuilderStart)
    if (-not $document.Content.Contains('<h2>Default folder</h2>') -or
        $document.Content.Contains('<h2>Saving folder</h2>') -or
        -not $document.Content.Contains('id="noDuplicatesBtn"') -or
        -not $document.Content.Contains('function toggleNoDuplicates()') -or
        -not $document.Content.Contains("getUiPref('noDuplicates',true)") -or
        -not $document.Content.Contains("getUiPref('resizerNoDuplicates',true)") -or
        -not $document.Content.Contains('id="photoEditNoDuplicatesBtn"') -or
        -not $document.Content.Contains('function togglePhotoEditNoDuplicates()') -or
        -not $document.Content.Contains("getUiPref('photoEditNoDuplicates',true)") -or
        -not $document.Content.Contains('const noDuplicates=isPhotoEditNoDuplicatesEnabled()') -or
        -not $document.Content.Contains('id="momentCaptureNoDuplicatesBtn"') -or
        -not $document.Content.Contains('function toggleMomentCaptureNoDuplicates()') -or
        -not $document.Content.Contains("getUiPref('momentCaptureNoDuplicates',true)") -or
        -not $document.Content.Contains('const noDuplicates=isMomentCaptureNoDuplicatesEnabled()') -or
        -not $document.Content.Contains('result.skippedCount)>0') -or
        -not $document.Content.Contains('function toggleSeparateVideoFolder()') -or
        -not $document.Content.Contains('function chooseVideoFolder()') -or
        -not $document.Content.Contains('video-select-control') -or
        -not $document.Content.Contains('function toggleVideoTileSelection') -or
        -not $document.Content.Contains('items:batch,noDuplicates') -or
        -not $document.Content.Contains('version:''2.099'',added:') -or
        -not $document.Content.Contains('toggleDetailVideoVisibility') -or
        -not $document.Content.Contains('toggleSaveUrlVideoVisibility') -or
        -not $document.Content.Contains('background:#0d1419') -or
        -not $document.Content.Contains('Skipped ${duplicateSkippedCount}') -or
        -not $document.Content.Contains('version:''2.098'',added:') -or
        -not $document.Content.Contains('version:''2.097'',added:') -or
        -not $document.Content.Contains('version:''2.096'',added:') -or
        -not $document.Content.Contains('version:''2.095'',added:') -or
        -not $document.Content.Contains('version:''2.094'',added:') -or
        -not $document.Content.Contains('version:''2.093'',added:') -or
        -not $document.Content.Contains('version:''2.092'',added:') -or
        -not $document.Content.Contains('version:''2.091'',added:') -or
        -not $document.Content.Contains('version:''2.090'',added:') -or
        -not $document.Content.Contains('version:''2.089'',added:') -or
        -not $document.Content.Contains('version:''2.088'',added:') -or
        -not $document.Content.Contains('version:''2.087'',added:') -or
        -not $document.Content.Contains('version:''2.086'',added:') -or
        -not $document.Content.Contains('version:''2.085'',added:') -or
        -not $document.Content.Contains('version:''2.084'',added:') -or
        -not $document.Content.Contains('version:''2.083'',added:') -or
        -not $document.Content.Contains('version:''2.082'',added:') -or
        -not $document.Content.Contains('version:''2.081'',added:') -or
        -not $document.Content.Contains('version:''2.080'',added:') -or
        -not $document.Content.Contains('version:''2.079'',added:') -or
        -not $document.Content.Contains('version:''2.078'',added:') -or
        -not $document.Content.Contains('version:''2.077'',added:') -or
        -not $document.Content.Contains('version:''2.076'',added:') -or
        -not $document.Content.Contains('version:''2.075'',added:') -or
        -not $document.Content.Contains('version:''2.074'',added:') -or
        -not $document.Content.Contains('version:''2.073'',added:') -or
        -not $document.Content.Contains('version:''2.072'',added:') -or
        -not $document.Content.Contains('version:''2.071'',added:') -or
        -not $document.Content.Contains('version:''2.070'',added:') -or
        -not $document.Content.Contains('version:''2.069'',added:') -or
        -not $document.Content.Contains('version:''2.068'',added:') -or
        -not $document.Content.Contains('version:''2.066'',added:') -or
        -not $document.Content.Contains('version:''2.065'',added:') -or
        -not $document.Content.Contains('version:''2.064'',added:') -or
        -not $document.Content.Contains('version:''2.062'',added:') -or
        -not $document.Content.Contains('version:''2.061'',added:') -or
        -not $document.Content.Contains('version:''2.060'',added:') -or
        -not $document.Content.Contains('version:''2.059'',added:') -or
        -not $document.Content.Contains('version:''2.058'',added:') -or
        -not $document.Content.Contains('version:''2.057'',added:') -or
        -not $document.Content.Contains('version:''2.056'',added:') -or
        -not $downloadBuilderSource.Contains('filename:`${filenameBase}_ph${photoOrd+1}${ext}`') -or
        -not $downloadBuilderSource.Contains('filename:`${filenameBase}_v${videoOrd+1}.mp4`') -or
        -not $downloadBuilderSource.Contains('if(!hideVideos&&selectedVideoIndices.has(videoOrd))') -or
        -not $downloadBuilderSource.Contains('contentIndex:null') -or
        $downloadBuilderSource.Contains('_photo_')) {
        throw 'Download and Resizer duplicate controls, visible-video selection, account-aware photo/video filenames, and the current release note must be present.'
    }

    $expectedVintagePreset = "vintage:{label:'Vintage',values:{exposure:25,brightness:8,contrast:22,gamma:-23,highlights:-14,shadows:-90,temperature:-8,tint:-6,saturation:15,vibrance:-35,sharpness:4,clarity:0,texture:6,blur:0,denoise:0,sepia:0,invert:2,posterize:4,pixelate:0,duotone:25,duotoneShadowColor:'#1B9FC0',duotoneHighlightColor:'#72B8F3',halftone:0,halftoneSize:4,colorize:0,colorizeColor:'#D94F70',grain:100,grainSize:10}}"
    if (-not $document.Content.Contains($expectedVintagePreset)) {
        throw 'The Vintage one-click look must match the supplied Image Edit settings.'
    }
    $photoAdjustmentLoadStart = $document.Content.IndexOf('function loadPhotoResizeAdjustments(){')
    if ($photoAdjustmentLoadStart -lt 0) {
        throw 'Image Edit startup adjustment initialization could not be inspected.'
    }
    $photoAdjustmentLoadEnd = $document.Content.IndexOf('function updatePhotoResizeAdjustment(name,rawValue){', $photoAdjustmentLoadStart)
    if ($photoAdjustmentLoadEnd -le $photoAdjustmentLoadStart) {
        throw 'Image Edit startup adjustment initialization could not be inspected.'
    }
    $photoAdjustmentLoadSource = $document.Content.Substring($photoAdjustmentLoadStart, $photoAdjustmentLoadEnd - $photoAdjustmentLoadStart)
    if (-not $photoAdjustmentLoadSource.Contains('photoResizeAdjustments={...PHOTO_ADJUSTMENT_DEFAULTS};') -or
        -not $photoAdjustmentLoadSource.Contains('photoResizeLookStrength=0;') -or
        -not $photoAdjustmentLoadSource.Contains('localStorage.removeItem(RESIZER_ADJUSTMENTS_KEY);') -or
        -not $photoAdjustmentLoadSource.Contains('localStorage.removeItem(RESIZER_LOOK_STATE_KEY);') -or
        $document.Content.Contains('localStorage.getItem(RESIZER_ADJUSTMENTS_KEY)') -or
        $document.Content.Contains('localStorage.getItem(RESIZER_LOOK_STATE_KEY)') -or
        $document.Content.Contains('localStorage.setItem(RESIZER_ADJUSTMENTS_KEY') -or
        $document.Content.Contains('localStorage.setItem(RESIZER_LOOK_STATE_KEY') -or
        -not $document.Content.Contains('Effects reset after an app restart; saved look presets remain.')) {
        throw 'Active Image Edit effects must start neutral and remain session-only, while custom look presets stay saved.'
    }
    if (-not $document.Content.Contains('id="photoResizePreviewOriginalCanvas"') -or
        -not $document.Content.Contains('id="photoResizePreviewCompare"') -or
        -not $document.Content.Contains('function handlePhotoResizePreviewCompareMove(event)') -or
        -not $document.Content.Contains('function positionPhotoResizePreviewCompare()') -or
        -not $document.Content.Contains('clip-path:inset(0 0 0 var(--photo-compare-clip,50%))') -or
        -not $document.Content.Contains('photoResizeLookOverrides[name]=photoResizeAdjustments[name]') -or
        -not $document.Content.Contains('function updatePhotoResizeLookPreset()') -or
        -not $document.Content.Contains('function resetPhotoResizeLookPreset()') -or
        -not $document.Content.Contains('stillSane.photoResizeLookPresetsV1') -or
        -not $document.Content.Contains('id="photoResizeLookUpdateBtn"') -or
        -not $document.Content.Contains('aria-label="Update selected one-click look"') -or
        -not $document.Content.Contains('id="photoResizeLookResetBtn"') -or
        -not $document.Content.Contains('aria-label="Restore selected look defaults"') -or
        -not $document.Content.Contains('.leaving-sticker-slot { display:grid; place-items:center; width:177px; height:177px; margin:14px auto 18px; opacity:var(--sticker-opacity,.5)')) {
        throw 'Image Edit must preserve the interactive before/after preview and strength, persist editable look presets with icon-only actions, and apply Sticker opacity to the leaving prompt.'
    }
    $previewToolbarStart = $document.Content.IndexOf('<div class="photo-preview-tools">')
    $previewToolbarEnd = if ($previewToolbarStart -ge 0) { $document.Content.IndexOf('</div>', $previewToolbarStart) } else { -1 }
    $previewToolbarSource = if ($previewToolbarEnd -gt $previewToolbarStart) {
        $document.Content.Substring($previewToolbarStart, $previewToolbarEnd - $previewToolbarStart)
    } else { '' }
    $compareButtonPosition = $previewToolbarSource.IndexOf('id="photoResizePreviewCompareToggle"')
    $zoomOutPosition = $previewToolbarSource.IndexOf('id="photoResizePreviewZoomOut"')
    $zoomInPosition = $previewToolbarSource.IndexOf('id="photoResizePreviewZoomIn"')
    $originalButtonPosition = $previewToolbarSource.IndexOf('id="photoResizePreviewToggle"')
    if ($compareButtonPosition -lt 0 -or $zoomOutPosition -lt 0 -or $zoomInPosition -lt 0 -or $originalButtonPosition -lt 0 -or
        $compareButtonPosition -ge $zoomOutPosition -or $zoomOutPosition -ge $zoomInPosition -or $zoomInPosition -ge $originalButtonPosition -or
        -not $previewToolbarSource.Contains('aria-label="Hide before-and-after comparison"') -or
        -not $previewToolbarSource.Contains('id="photoResizePreviewCompareToggle" onclick="togglePhotoResizePreviewCompare()"') -or
        -not $previewToolbarSource.Contains('id="photoResizePreviewToggle" onclick="togglePhotoResizePreviewMode()" aria-label="Show original"') -or
        -not $previewToolbarSource.Contains('id="photoResizePreviewCompareToggle" onclick="togglePhotoResizePreviewCompare()" aria-label="Hide before-and-after comparison" title="Hide before-and-after comparison" aria-pressed="true" disabled><svg') -or
        -not $previewToolbarSource.Contains('id="photoResizePreviewToggle" onclick="togglePhotoResizePreviewMode()" aria-label="Show original" title="Show original" aria-pressed="false" disabled><svg') -or
        -not $previewToolbarSource.Contains('</svg></button>') -or
        $document.Content.Contains('id="photoResizePreviewZoomFit"') -or
        -not $document.Content.Contains('let photoResizePreviewCompareEnabled=true;') -or
        -not $document.Content.Contains('function togglePhotoResizePreviewCompare()') -or
        -not $document.Content.Contains('!photoResizePreviewCompareEnabled') -or
        -not $document.Content.Contains('function resetPhotoResizePreviewZoom()') -or
        -not $document.Content.Contains("wrap.addEventListener('dblclick'")) {
        throw 'The preview toolbar must place an icon-only comparison toggle before zoom, remove the Fit button, and make Show original icon-only.'
    }
    $previewModeStart = $document.Content.IndexOf('function togglePhotoResizePreviewMode(){')
    $previewModeEnd = if ($previewModeStart -ge 0) { $document.Content.IndexOf('function schedulePhotoResizePreviewRender(){', $previewModeStart) } else { -1 }
    $previewModeSource = if ($previewModeEnd -gt $previewModeStart) {
        $document.Content.Substring($previewModeStart, $previewModeEnd - $previewModeStart)
    } else { '' }
    if (-not $previewModeSource.Contains('syncPhotoResizePreviewModeVisibility();') -or
        -not $previewModeSource.Contains('applyPhotoResizePreviewZoom();') -or
        $previewModeSource.Contains('schedulePhotoResizePreviewRender()') -or
        $previewModeSource.Contains('renderPhotoResizePreview()')) {
        throw 'Switching original/edited preview mode must only change canvas visibility, never re-render the image.'
    }
    if ($document.Content -match 'apply adjustments to all selected images' -or
        -not $document.Content.Contains('applyAdjustmentsToBatch:true')) {
        throw 'Image Edit must apply adjustments to checked images by default without a redundant opt-in button.'
    }
    $photoAdjustmentHeaderStart = $document.Content.IndexOf('<div class="settings-card-head photo-adjustments-card-head">')
    $photoAdjustmentHeaderEnd = if ($photoAdjustmentHeaderStart -ge 0) { $document.Content.IndexOf('<div class="setting-body">', $photoAdjustmentHeaderStart) } else { -1 }
    $photoAdjustmentHeaderSource = if ($photoAdjustmentHeaderEnd -gt $photoAdjustmentHeaderStart) {
        $document.Content.Substring($photoAdjustmentHeaderStart, $photoAdjustmentHeaderEnd - $photoAdjustmentHeaderStart)
    } else { '' }
    $photoAdjustmentFolderPosition = $photoAdjustmentHeaderSource.IndexOf('id="photoAdjustmentOpenFolderBtn"')
    $photoAdjustmentResetPosition = $photoAdjustmentHeaderSource.IndexOf('id="photoAdjustmentResetBtn"')
    $compareToggleFunctionStart = $document.Content.IndexOf('function togglePhotoResizePreviewCompare()')
    $compareToggleFunctionEnd = if ($compareToggleFunctionStart -ge 0) { $document.Content.IndexOf('function togglePhotoResizePreviewMode()', $compareToggleFunctionStart) } else { -1 }
    $compareToggleFunctionSource = if ($compareToggleFunctionEnd -gt $compareToggleFunctionStart) {
        $document.Content.Substring($compareToggleFunctionStart, $compareToggleFunctionEnd - $compareToggleFunctionStart)
    } else { '' }
    if (-not $document.Content.Contains('let photoResizePreviewCompareHasPosition=false;') -or
        $compareToggleFunctionStart -lt 0 -or $compareToggleFunctionEnd -le $compareToggleFunctionStart -or
        $compareToggleFunctionSource.Contains('photoResizePreviewOriginal=') -or
        -not $document.Content.Contains('function freezePhotoResizePreviewCompare()') -or
        -not $document.Content.Contains('photoResizePreviewCompareFrozen=Boolean(photoResizePreviewCompareEnabled&&photoResizePreviewCompareHasPosition&&!photoResizePreviewOriginal);') -or
        -not $document.Content.Contains("wrap.addEventListener('pointerleave',()=>freezePhotoResizePreviewCompare());") -or
        -not $document.Content.Contains('photoResizePreviewCompareEnabled=!photoResizePreviewCompareEnabled;') -or
        -not $document.Content.Contains('photoResizePreviewComparePointer=null;') -or
        -not $document.Content.Contains('photoResizePreviewCompareFrozen=false;') -or
        -not $document.Content.Contains('hidePhotoResizePreviewCompare(false);') -or
        -not $document.Content.Contains("compareToggle.classList.toggle('active',photoResizePreviewCompareEnabled)") -or
        -not $document.Content.Contains("originalToggle.classList.toggle('active',photoResizePreviewOriginal)") -or
        -not $document.Content.Contains('class="filter icon-filter-btn source-toggle-button active-outline-control photo-preview-icon-toggle active"') -or
        -not $document.Content.Contains('swapPhotoResizePreviewRedBlueChannels(pixels);') -or
        -not $document.Content.Contains('id="photoAdjustmentPhotoStrip"') -or
        -not $document.Content.Contains('function renderPhotoAdjustmentPhotoStrip()') -or
        -not $document.Content.Contains('function scrollPhotoAdjustmentPhotos(direction)') -or
        -not $document.Content.Contains('overflow-x:auto;overflow-y:hidden;padding:4px') -or
        -not $document.Content.Contains('<h2>Resize photos</h2>') -or
        -not $document.Content.Contains('<h2 id="photoAdjustmentsHeading">Image adjustments ') -or
        $photoAdjustmentFolderPosition -lt 0 -or $photoAdjustmentResetPosition -le $photoAdjustmentFolderPosition -or
        -not $photoAdjustmentHeaderSource.Contains('aria-label="Reset adjustments"><svg') -or
        $document.Content.Contains('id="photoAdjustmentResetBtn" onclick="resetPhotoResizeAdjustments()">Reset adjustments</button>') -or
        -not $document.Content.Contains('.photo-preview-icon-toggle{display:grid!important;place-items:center!important;width:37px!important;') -or
        -not $document.Content.Contains('.photo-preview-zoom-btn{display:inline-grid;place-items:center;width:37px;') -or
        -not $document.Content.Contains('.photo-look-icon-btn{position:relative;width:37px;') -or
        -not $document.Content.Contains('.photo-adjustment-open-folder-btn,.photo-adjustment-reset{position:relative!important;display:grid!important;place-items:center!important;width:37px!important;') -or
        -not $document.Content.Contains('.resizer-stepper-btn{display:grid;place-items:center;flex:0 0 37px;width:37px;min-width:37px;max-width:37px;height:37px;') -or
        -not $document.Content.Contains('.resizer-upscale-toggle{display:grid!important;place-items:center!important;width:37px!important;')) {
        throw 'Image Edit must freeze the divider on pointerleave while enabled, hide comparison when disabled without changing original/edited mode, use active-outline states, preserve the raw-photo strip, align colors, use 37px icon controls, and place icon-only Reset after Open folder.'
    }
    $browserAddressWrapStart = $document.Content.IndexOf('<div class="browser-address-wrap">')
    $browserAddressWrapEnd = if ($browserAddressWrapStart -ge 0) { $document.Content.IndexOf('</div>', $browserAddressWrapStart) } else { -1 }
    $browserOverlayTogglePosition = $document.Content.IndexOf('id="browserOverlayToggleBtn"')
    $browserViewportPosition = $document.Content.IndexOf('<div class="browser-viewport" id="browserViewport">')
    if ($browserAddressWrapStart -lt 0 -or $browserAddressWrapEnd -le $browserAddressWrapStart -or
        $browserOverlayTogglePosition -le $browserAddressWrapEnd -or $browserViewportPosition -le $browserOverlayTogglePosition -or
        -not $document.Content.Contains('body .nav,body .filter,body .settings-tab,body .page-btn,body .crop-ratio,body .ig-chrome-btn{background:var(--t-nav-bg);border-color:var(--t-nav-border);color:var(--t-nav-ink)}') -or
        -not $document.Content.Contains('body .nav.active,body .filter.active{background:var(--t-nav-act-bg);border-color:var(--t-nav-act-border);color:var(--t-nav-act-ink);box-shadow:inset 2px 0 var(--t-nav-act-bar)}') -or
        $document.Content.Contains('body .source-toggle-button{background:var(--t-nav-bg),var(--t-card-bg);') -or
        $document.Content.Contains('body .source-toggle-button.active{background:var(--t-nav-act-bg),var(--t-card-bg)}') -or
        -not $document.Content.Contains('class="filter icon-filter-btn source-toggle-button active-outline-control browser-overlay-toggle active" id="browserOverlayToggleBtn"') -or
        -not $document.Content.Contains('id="browserOverlayToggleBtn" type="button" onclick="toggleBrowserMediaOverlay()" title="Disable Browser overlay" aria-label="Disable Browser overlay" aria-pressed="true"><svg') -or
        -not $document.Content.Contains("const BROWSER_MEDIA_OVERLAY_STORAGE_KEY='stillSane.browserMediaOverlayEnabledV1'") -or
        -not $document.Content.Contains('function toggleBrowserMediaOverlay()') -or
        -not $document.Content.Contains("action:'media-overlay'")) {
        throw 'The Browser overlay toggle must share the app filter styling and retain its accessible enabled preference.'
    }
    if (-not $document.Content.Contains('<path d="M17.7 6.4V5.9A1.8 1.8 0 0 0 15.9 4.1H5.2A1.8 1.8 0 0 0 3.4 5.9v8.4a1.8 1.8 0 0 0 1.8 1.8h.6"/>') -or
        -not $document.Content.Contains('<rect x="6.3" y="6.4" width="14.2" height="12" rx="2"/>') -or
        -not $document.Content.Contains('<path d="M13.35 12.5h5.4M13.35 13.95h5.4M13.35 15.5h5.4"/>')) {
        throw 'The Browser overlay toggle must use the supplied stacked-card icon with three lines.'
    }
    $browserOverlaySyncStart = $document.Content.IndexOf('function syncBrowserMediaOverlayToggle(){')
    $browserOverlaySyncEnd = if ($browserOverlaySyncStart -ge 0) { $document.Content.IndexOf('async function sendBrowserMediaOverlayEnabled(', $browserOverlaySyncStart) } else { -1 }
    $browserOverlaySyncSource = if ($browserOverlaySyncEnd -gt $browserOverlaySyncStart) {
        $document.Content.Substring($browserOverlaySyncStart, $browserOverlaySyncEnd - $browserOverlaySyncStart)
    } else { '' }
    $browserOverlayIconStart = $document.Content.IndexOf("const BROWSER_MEDIA_OVERLAY_ICON='")
    $browserOverlayIconEnd = if ($browserOverlayIconStart -ge 0) { $document.Content.IndexOf("';", $browserOverlayIconStart) } else { -1 }
    $browserOverlayIconSource = if ($browserOverlayIconEnd -gt $browserOverlayIconStart) {
        $document.Content.Substring($browserOverlayIconStart, $browserOverlayIconEnd - $browserOverlayIconStart)
    } else { '' }
    if (-not $browserOverlayIconSource.Contains('M17.7 6.4V5.9A1.8 1.8 0 0 0 15.9 4.1H5.2') -or
        -not $browserOverlayIconSource.Contains('<rect x="6.3" y="6.4" width="14.2" height="12" rx="2"/>') -or
        -not $browserOverlayIconSource.Contains('M13.35 12.5h5.4M13.35 13.95h5.4M13.35 15.5h5.4') -or
        -not $browserOverlaySyncSource.Contains('button.innerHTML=BROWSER_MEDIA_OVERLAY_ICON;') -or
        $browserOverlaySyncSource.Contains('SOURCE_EYE_ICON') -or
        $browserOverlaySyncSource.Contains('SOURCE_EYE_OFF_ICON')) {
        throw 'The Browser overlay runtime sync must render its stacked-card icon with three lines, not the source-label eye icons.'
    }
    $openInstagramBrowserStart = $document.Content.IndexOf('async function openInstagramBrowser(targetUrl=')
    $openInstagramBrowserEnd = if ($openInstagramBrowserStart -ge 0) { $document.Content.IndexOf('var igSession=', $openInstagramBrowserStart) } else { -1 }
    $openInstagramBrowserSource = if ($openInstagramBrowserEnd -gt $openInstagramBrowserStart) {
        $document.Content.Substring($openInstagramBrowserStart, $openInstagramBrowserEnd - $openInstagramBrowserStart)
    } else { '' }
    $closePostPosition = $openInstagramBrowserSource.IndexOf('closePost()')
    $closeFullSizePosition = $openInstagramBrowserSource.IndexOf('closePhotoFullSize()')
    $showBrowserPosition = $openInstagramBrowserSource.IndexOf("showPage('browser')")
    $browserLayoutWaitPosition = $openInstagramBrowserSource.IndexOf('requestAnimationFrame')
    $openBrowserEndpointPosition = $openInstagramBrowserSource.IndexOf("getApiUrls('/api/open-instagram-browser')")
    if ($closePostPosition -lt 0 -or $closeFullSizePosition -le $closePostPosition -or
        $showBrowserPosition -le $closeFullSizePosition -or $browserLayoutWaitPosition -le $showBrowserPosition -or
        $openBrowserEndpointPosition -le $browserLayoutWaitPosition -or
        -not $openInstagramBrowserSource.Contains('reportBrowserRegion()')) {
        throw 'Open in Instagram browser must close the blocking view and activate Browser before opening the native view.'
    }
    $oldPhotoTexturePath = Join-Path $PSScriptRoot '..\src\InStSanity\Resources\Raw\wwwroot\old-photo-damage-texture.png'
    if (-not $document.Content.Contains('id="photoAdjOldPhoto"') -or
        -not $document.Content.Contains('data-adjustment="oldPhoto"') -or
        -not $document.Content.Contains('id="photoAdjOldPhotoSeed"') -or
        -not $document.Content.Contains('data-adjustment="oldPhotoSeed"') -or
        -not $document.Content.Contains('function randomizeOldPhotoSeed()') -or
        -not $document.Content.Contains("'colorizeColor','oldPhotoSeed']);") -or
        -not $document.Content.Contains('oldPhoto:[0,100]') -or
        -not $document.Content.Contains('oldPhotoSeed:[0,2147483647]') -or
        -not $document.Content.Contains('photoPreviewTextureMapping(texture,width,height,settings.oldPhotoSeed)') -or
        -not $document.Content.Contains('function ensurePhotoOldPhotoTexture()') -or
        -not $document.Content.Contains('photoPreviewTextureSample(texture,textureMapping,x,y,width,height)') -or
        -not $document.Content.Contains("if(key!=='oldPhoto'&&key!=='oldPhotoSeed')values[key]=photoResizeAdjustments[key];") -or
        $document.Content.Contains('data-photo-look="oldPhoto"') -or
        -not (Test-Path -LiteralPath $oldPhotoTexturePath)) {
        throw 'Image Edit must expose Old Photo damage as a separate finishing slider and include its supplied texture asset.'
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

    $overlayScriptPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Browser\InstagramMediaOverlayScript.cs'
    $queueServicePath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Browser\BrowserMediaQueueService.cs'
    $selectedDownloadServicePath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Downloads\SelectedMediaDownloadService.cs'
    $mainPagePath = Join-Path $PSScriptRoot '..\src\InStSanity\MainPage.xaml.cs'
    $browserViewEndpointPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Browser\InstagramBrowserViewEndpoint.cs'
    $browserWindowServicePath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Browser\InstagramBrowserWindowService.cs'
    $overlayScriptSource = Get-Content -LiteralPath $overlayScriptPath -Raw
    $overlayScriptLines = $overlayScriptSource -split '\r?\n'
    $rawStringOpeningLine = -1
    $rawStringClosingLine = -1
    for ($lineIndex = 0; $lineIndex -lt $overlayScriptLines.Count; $lineIndex++) {
        if ($rawStringOpeningLine -lt 0 -and $overlayScriptLines[$lineIndex].Contains('const string Source = """')) {
            $rawStringOpeningLine = $lineIndex
            continue
        }
        if ($rawStringOpeningLine -ge 0 -and $overlayScriptLines[$lineIndex].Trim() -ceq '""";') {
            $rawStringClosingLine = $lineIndex
            break
        }
    }
    if ($rawStringOpeningLine -lt 0 -or $rawStringClosingLine -le $rawStringOpeningLine) {
        throw 'The Instagram overlay script must have a complete raw string literal.'
    }
    $rawStringClose = $overlayScriptLines[$rawStringClosingLine]
    $rawStringIndentLength = $rawStringClose.Length - $rawStringClose.TrimStart().Length
    $rawStringIndent = $rawStringClose.Substring(0, $rawStringIndentLength)
    if ($rawStringIndentLength -eq 0) {
        throw 'The Instagram overlay raw string must have an indented closing delimiter.'
    }
    for ($lineIndex = $rawStringOpeningLine + 1; $lineIndex -lt $rawStringClosingLine; $lineIndex++) {
        $rawStringLine = $overlayScriptLines[$lineIndex]
        if ([string]::IsNullOrWhiteSpace($rawStringLine)) { continue }
        if ($rawStringLine.Length -lt $rawStringIndentLength -or
            $rawStringLine.Substring(0, $rawStringIndentLength) -cne $rawStringIndent) {
            throw "Instagram overlay raw-string line $($lineIndex + 1) uses different indentation from its closing delimiter."
        }
    }
    $queueServiceSource = Get-Content -LiteralPath $queueServicePath -Raw
    $selectedDownloadServiceSource = Get-Content -LiteralPath $selectedDownloadServicePath -Raw
    $mainPageSource = Get-Content -LiteralPath $mainPagePath -Raw
    $browserViewEndpointSource = Get-Content -LiteralPath $browserViewEndpointPath -Raw
    $browserWindowServiceSource = Get-Content -LiteralPath $browserWindowServicePath -Raw
    if (-not $overlayScriptSource.Contains("? 'Cancel queue download'") -or
        $overlayScriptSource.Contains('Cancel ${state.currentIndex}/')) {
        throw 'The Browser download-queue Save/Cancel action must not show an item counter.'
    }
    $queueControlsAre32 =
        $overlayScriptSource -match '(?s)\.isg-action\{[^}]*flex:0 0 32px[^}]*width:32px[^}]*height:32px' -and
        $overlayScriptSource -match '(?s)\.isg-icon-action\{[^}]*flex:0 0 32px[^}]*width:32px[^}]*height:32px' -and
        $overlayScriptSource -match '(?s)\.isg-row-action\{[^}]*width:32px!important[^}]*height:32px!important'
    if (-not $queueControlsAre32 -or
        -not $overlayScriptSource.Contains('.isg-icon-action svg{display:block;width:16px;height:16px}') -or
        -not $overlayScriptSource.Contains('.isg-row-action svg{display:block;width:16px;height:16px}')) {
        throw 'Every Browser download-queue button must use the compact 32px size with proportionate icons.'
    }
    $queueCountIsInlineText =
        $overlayScriptSource -match '(?s)\.isg-count\{[^}]*flex:0 0 auto[^}]*border:0[^}]*border-radius:0[^}]*background:none[^}]*white-space:nowrap[^}]*font-variant-numeric:tabular-nums' -and
        $overlayScriptSource.Contains('aria-live="polite" aria-atomic="true">0 items</span>') -and
        $overlayScriptSource.Contains("itemCount === 1 ? ' item' : ' items'")
    if (-not $queueCountIsInlineText) {
        throw 'The Browser queue count must be a plain, accessible item-count label rather than a circular badge.'
    }
    if (-not $bootstrapperSource.Contains('core.WebMessageReceived') -or
        -not $bootstrapperSource.Contains('AddScriptToExecuteOnDocumentCreatedAsync') -or
        -not $bootstrapperSource.Contains('typeof window.__instSanityMediaOverlayInstall') -or
        -not $bootstrapperSource.Contains('InstagramMediaOverlayScript.Source') -or
        -not $bootstrapperSource.Contains('onOverlayMessage') -or
        -not $bootstrapperSource.Contains('action == "overlay-debug"') -or
        -not $bootstrapperSource.Contains('action == "cursor-position"') -or
        -not $bootstrapperSource.Contains('GetOverlayDebugSummary') -or
        -not $bootstrapperSource.Contains('message[..6_000]') -or
        -not $bootstrapperSource.Contains('instagram-browser-hover.log') -or
        -not $overlayScriptSource.Contains('[InStSanity:InstagramOverlay]') -or
        -not $overlayScriptSource.Contains("action: 'overlay-debug'") -or
        -not $overlayScriptSource.Contains("action: 'cursor-position'") -or
        -not $overlayScriptSource.Contains('relayStickerCursorToMainApp') -or
        -not $overlayScriptSource.Contains('elementsFromPoint') -or
        -not $overlayScriptSource.Contains('DOMContentLoaded') -or
        -not $overlayScriptSource.Contains('install.waitingForDom') -or
        -not $overlayScriptSource.Contains("document.addEventListener('mouseover', onPointerOver, true)") -or
        -not $overlayScriptSource.Contains("document.addEventListener('mousemove', onPointerMove") -or
        -not $overlayScriptSource.Contains("document.addEventListener('load', event =>") -or
        -not $overlayScriptSource.Contains('refreshMediaUnderPointer') -or
        -not $overlayScriptSource.Contains('queue connection is unavailable') -or
        -not $overlayScriptSource.Contains("postMessage('toggle-media'") -or
        -not $overlayScriptSource.Contains("postMessage('start-download'") -or
        -not $overlayScriptSource.Contains("postMessage('clear-queue'") -or
        -not $overlayScriptSource.Contains("postMessage('remove-media'") -or
        -not $overlayScriptSource.Contains('queuePanelEverOpened') -or
        -not $overlayScriptSource.Contains('function navigateToQueuedMedia(item)') -or
        -not $overlayScriptSource.Contains("openButton.setAttribute('aria-label'") -or
        -not $overlayScriptSource.Contains("removeButton.setAttribute('aria-label'") -or
        -not $overlayScriptSource.Contains("requestFolderOpen('open-download-folder'") -or
        -not $overlayScriptSource.Contains("requestFolderOpen('open-video-folder'") -or
        -not $overlayScriptSource.Contains('function requestFolderOpen(action)') -or
        -not $overlayScriptSource.Contains('if (event.button !== 0) suppressFolderClickUntil = performance.now() + 750;') -or
        -not $overlayScriptSource.Contains('if (event.button !== 0 || performance.now() < suppressFolderClickUntil) return;') -or
        -not $overlayScriptSource.Contains('suppressFolderClickUntil = performance.now() + 750;') -or
        -not $mainPageSource.Contains('TryAcceptBrowserFolderOpenAction(overlayAction)') -or
        -not $mainPageSource.Contains('BrowserFolderOpenDuplicateWindowMs = 750') -or
        -not $overlayScriptSource.Contains("selectButton.textContent = isSelected ? '✓ Queued' : '+ Add';") -or
        $overlayScriptSource.Contains('+ Add to queue') -or
        -not $overlayScriptSource.Contains('isg-open-folder') -or
        -not $overlayScriptSource.Contains('isg-collapse') -or
        -not $overlayScriptSource.Contains('tray.dataset.collapsed') -or
        -not $overlayScriptSource.Contains('aria-expanded') -or
        -not $mainPageSource.Contains('open-download-folder') -or
        -not $mainPageSource.Contains('IsTrustedInstagramBrowserSource(source)') -or
        -not $mainPageSource.Contains('OpenDownloadFolderFromBrowserAsync') -or
        -not $overlayScriptSource.Contains('queue-toggle-click') -or
        -not $overlayScriptSource.Contains('rememberToggleRequest(requestId, candidate)') -or
        -not $overlayScriptSource.Contains('queuedSelectionMatchesAttempt(item, attempt)') -or
        -not $overlayScriptSource.Contains('queued-media-validation-error-suppressed') -or
        -not $mainPageSource.Contains('GetOverlayRequestId(message)') -or
        -not $mainPageSource.Contains('errorRequestId: errorRequestId') -or
        -not $mainPageSource.Contains('{errorRequestIdJson})') -or
        -not $overlayScriptSource.Contains('safeUrlSummary') -or
        -not $overlayScriptSource.Contains('postUrlMatchesShortcode') -or
        -not $overlayScriptSource.Contains('const pageShortcode = parseShortcode(location.pathname);') -or
        -not $overlayScriptSource.Contains('trackSlide(root, shortcode') -or
        -not $overlayScriptSource.Contains('const slideByRoot = new WeakMap();') -or
        -not $overlayScriptSource.Contains('function slideTrackingKey(root, shortcode)') -or
        -not $overlayScriptSource.Contains('function mediaPathIdentity(value)') -or
        -not $overlayScriptSource.Contains('function hasPostPermalink(element)') -or
        -not $overlayScriptSource.Contains('for (let ancestor = element.parentElement; ancestor && ancestor !== boundary && ancestor !== document.body; ancestor = ancestor.parentElement)') -or
        -not $overlayScriptSource.Contains('postRootFor(directMedia || target)') -or
        -not $overlayScriptSource.Contains('const boundary = dialog || main || document.body;') -or
        -not $overlayScriptSource.Contains('element.closest(''article,[role="article"]'')') -or
        -not $overlayScriptSource.Contains("window.addEventListener('popstate', refreshAfterRouteChange)") -or
        -not $overlayScriptSource.Contains('function refreshAfterRouteChange()') -or
        -not $overlayScriptSource.Contains("debugLog('route-changed'") -or
        -not $overlayScriptSource.Contains('if (!routePollTimer)') -or
        -not $overlayScriptSource.Contains('const identity = item.rawUrl ? mediaPathIdentity(item.rawUrl) : `slide:') -or
        -not $overlayScriptSource.Contains('if (typeof item.key === ''string'' && item.key) return item.key;') -or
        -not $overlayScriptSource.Contains("function positionButton(media, candidate, forcePosition = false, reason = 'candidate')") -or
        -not $overlayScriptSource.Contains('min-width:76px;box-sizing:border-box') -or
        -not $overlayScriptSource.Contains('function measureSelectButtonWidth()') -or
        -not $overlayScriptSource.Contains('const width = measureSelectButtonWidth();') -or
        -not $overlayScriptSource.Contains('positionButton(activeMedia, activeCandidate, forcePosition, reason)') -or
        -not $overlayScriptSource.Contains("let buttonAnchorShortcode = '';") -or
        -not $overlayScriptSource.Contains('let buttonAnchorSlideIndex = null;') -or
        -not $overlayScriptSource.Contains('const samePostAnchor = buttonAnchorShortcode === candidate.shortcode && buttonAnchorRootId === candidateRootId') -or
        -not $overlayScriptSource.Contains('const anchorWasAvailable = buttonAnchorShortcode === next.shortcode && buttonAnchorRootId === nextRootId') -or
        -not $overlayScriptSource.Contains('const candidateRootId = candidateRootIds.get(candidate) || 0;') -or
        -not $overlayScriptSource.Contains('const candidateRootIds = new WeakMap();') -or
        -not $overlayScriptSource.Contains('candidateDiagnosticsForLog?.postIdentitySource') -or
        -not $overlayScriptSource.Contains('candidateDiagnosticsForLog?.nearestPermalinkDistance') -or
        -not $overlayScriptSource.Contains('clipBoundsDisagreedWithHitStack: candidateDiagnosticsForLog?.clipBoundsDisagreedWithHitStack === true') -or
        -not $overlayScriptSource.Contains('function diagnosticPostToken(shortcode)') -or
        -not $overlayScriptSource.Contains('postToken: candidateDiagnosticsForLog?.postToken ?? 0') -or
        -not $overlayScriptSource.Contains("function inspectMediaTarget(target, point = null, reason = 'pointer')") -or
        -not $overlayScriptSource.Contains('pointer: point && Number.isFinite(point.x)') -or
        -not $overlayScriptSource.Contains('visualViewport,') -or
        -not $overlayScriptSource.Contains('pageScroll: { x: Math.round(window.scrollX || 0), y: Math.round(window.scrollY || 0) }') -or
        -not $overlayScriptSource.Contains('hitStackTags: hits.slice(0, 6)') -or
        -not $overlayScriptSource.Contains('activeSelector:') -or
        -not $overlayScriptSource.Contains('inspectMediaTarget(target, lastPointer, reason)') -or
        -not $overlayScriptSource.Contains('const anchorSlideChanged = anchorWasAvailable') -or
        -not $overlayScriptSource.Contains("debugLog('selector-anchor-preserved'") -or
        -not $overlayScriptSource.Contains("debugLog('selector-positioned'") -or
        -not $overlayScriptSource.Contains('selectorWasVisible') -or
        -not $overlayScriptSource.Contains("function onDocumentScroll(event)") -or
        -not $overlayScriptSource.Contains("window.addEventListener('scroll', onDocumentScroll, { passive: true })") -or
        -not $overlayScriptSource.Contains('if (!selectorVisible || !activeMedia || !activeCandidate)') -or
        -not $overlayScriptSource.Contains('if (!activeMedia || !activeCandidate || !document.contains(activeMedia))') -or
        -not $overlayScriptSource.Contains("debugLog('horizontal-scroll-anchor-held'") -or
        -not $overlayScriptSource.Contains('Math.abs(deltaLeft) > 0.5 && Math.abs(deltaTop) < 0.5') -or
        -not $overlayScriptSource.Contains("debugLog('media-inspection'") -or
        -not $overlayScriptSource.Contains('const inspectionLogKeys = new WeakMap();') -or
        -not $overlayScriptSource.Contains('if (!loggedSignatures.has(signature))') -or
        -not $overlayScriptSource.Contains('candidateRejectReasons') -or
        -not $overlayScriptSource.Contains('candidateRejectDetails') -or
        -not $overlayScriptSource.Contains('hitTestRejectReasons') -or
        -not $overlayScriptSource.Contains('hitTestRejectExamples') -or
        -not $overlayScriptSource.Contains('function isRelatedMediaFrameTarget(media, eventTarget)') -or
        -not $overlayScriptSource.Contains('relatedMediaFrameHit') -or
        -not $overlayScriptSource.Contains('function findRelatedPostPermalink(media, root') -or
        -not $overlayScriptSource.Contains('postRootPermalinkCount') -or
        -not $overlayScriptSource.Contains('allDocumentPostPermalinks()') -or
        -not $overlayScriptSource.Contains("return reject('post-shortcode-missing'") -or
        -not $overlayScriptSource.Contains('itemKey(inspection.candidate) !== itemKey(activeCandidate)') -or
        -not $overlayScriptSource.Contains("if (!rawUrl && !isVideo) return reject('photo-source-unavailable');") -or
        -not $overlayScriptSource.Contains('const root = postRootFor(control);') -or
        -not $overlayScriptSource.Contains('parseShortcode(location.pathname) || findPostShortcode(control, root)') -or
        -not $overlayScriptSource.Contains('const postPattern = /^\/(?:[A-Za-z0-9._]{1,30}\/)?(?:p|reel|reels|tv)\/([A-Za-z0-9_-]{1,64})\/?$/i;') -or
        -not $overlayScriptSource.Contains('const parsed = new URL(value, location.href);') -or
        -not $queueServiceSource.Contains('invalid_media_post_url') -or
        -not $queueServiceSource.Contains('^/(?:(?<account>[A-Za-z0-9._]{1,30})/)?(?:p|reel|reels|tv)/(?<shortcode>') -or
        -not $queueServiceSource.Contains('invalid_media_raw_url') -or
        -not $queueServiceSource.Contains('invalid_media_slide_index') -or
        -not $overlayScriptSource.Contains('findPostPermalink') -or
        -not $overlayScriptSource.Contains('postUrl,') -or
        -not $overlayScriptSource.Contains('item.isVideo ? ''VIDEO'' : ''PHOTO''') -or
        -not $queueServiceSource.Contains('public const int MaxItems = 50') -or
        -not $queueServiceSource.Contains('TryValidateInstagramSource') -or
        -not $queueServiceSource.Contains('TryValidateInstagramPostUrl') -or
        -not $queueServiceSource.Contains('var mediaIdentity = Uri.TryCreate(rawUrl') -or
        -not $queueServiceSource.Contains('identityUri.AbsolutePath') -or
        -not $queueServiceSource.Contains('$"slide:{slideIndex}"') -or
        -not $queueServiceSource.Contains('var key = $"{shortcode}|{type}|{mediaIdentity}";') -or
        -not $queueServiceSource.Contains('$"{account}_{shortcode}_v{mediaIndex + 1}{extension}"') -or
        -not $queueServiceSource.Contains('$"{account}_{shortcode}_ph{mediaIndex + 1}{extension}"') -or
        -not $queueServiceSource.Contains('int QueuePhotoIndex { get; init; }') -or
        -not $queueServiceSource.Contains('NextQueuedPhotoIndex(item.Shortcode)') -or
        -not $queueServiceSource.Contains('Where(candidate => !candidate.IsVideo &&') -or
        -not $queueServiceSource.Contains('string.Equals(candidate.Shortcode, shortcode, StringComparison.Ordinal)') -or
        -not $queueServiceSource.Contains('.DefaultIfEmpty(-1)') -or
        -not $queueServiceSource.Contains('.Max() + 1;') -or
        -not $queueServiceSource.Contains('QueuePhotoIndex = queuePhotoIndex') -or
        -not $queueServiceSource.Contains('Filename = BuildQueueFilename(item.Account, item.Shortcode, false, queuePhotoIndex)') -or
        $queueServiceSource.Contains('identitySuffix') -or
        $queueServiceSource.Contains('_full_{identitySuffix}') -or
        -not $queueServiceSource.Contains('Task<(string Url, int SlideIndex)?> ResolveQueuedPhotoUrlAsync') -or
        -not $queueServiceSource.Contains('return (Url: imageUrl, SlideIndex: slideIndex);') -or
        -not $queueServiceSource.Contains('var resolvedPhoto = await ResolveQueuedPhotoUrlAsync(item, cancellation.Token)') -or
        -not $queueServiceSource.Contains('SlideIndex = resolvedSlideIndex, Filename = resolvedFilename') -or
        -not $queueServiceSource.Contains('BuildQueueFilename(item.Account, item.Shortcode, false, item.QueuePhotoIndex)') -or
        -not $queueServiceSource.Contains('UpdateResolvedQueueItem(item)') -or
        -not $queueServiceSource.Contains('static string BuildQueueFilename(string account, string shortcode, bool isVideo, int mediaIndex)') -or
        -not $queueServiceSource.Contains('if (matchingIndices.Count == 1) return matchingIndices[0];') -or
        -not $queueServiceSource.Contains('FindQueuedPhotoSlide') -or
        -not $queueServiceSource.Contains('var media = post.Pictures[slideIndex]') -or
        -not $queueServiceSource.Contains('IsFullSizeResolvedPhoto') -or
        -not $queueServiceSource.Contains('ResolvedMedia media) => media.HasFullSizeImage') -or
        -not $queueServiceSource.Contains('photo_full_size_unavailable') -or
        -not $queueServiceSource.Contains('Always resolve the post''s selected slide') -or
        -not $queueServiceSource.Contains('ResolveQueuedVideoUrlAsync') -or
        -not $queueServiceSource.Contains('var resolvedVideoUrl = await ResolveQueuedVideoUrlAsync(item, cancellation.Token)') -or
        -not $queueServiceSource.Contains('FindQueuedVideoSlide(item, post.Pictures)') -or
        -not $queueServiceSource.Contains('HasSameMediaPath(item.ThumbnailUrl, pictures[index].ImgUrl)') -or
        -not $queueServiceSource.Contains('IsDownloadableVideoUrl(item.RawUrl)') -or
        -not $queueServiceSource.Contains('downloadItem with { RawUrl = item.RawUrl }') -or
        -not $queueServiceSource.Contains('IsUnsupportedVideoStream') -or
        -not $queueServiceSource.Contains('video_source_unavailable') -or
        -not $overlayScriptSource.Contains('download_folders_unavailable:') -or
        -not $overlayScriptSource.Contains('video_folder_unavailable:') -or
        -not $queueServiceSource.Contains('if (isVideo && string.IsNullOrWhiteSpace(videoFolder))') -or
        -not $queueServiceSource.Contains('folderErrors.Add(true)') -or
        -not $queueServiceSource.Contains('IsAllowedBrowserMediaUrl') -or
        -not $queueServiceSource.Contains('ResetForLogout') -or
        -not $queueServiceSource.Contains('public bool Remove(string key)') -or
        -not $queueServiceSource.Contains('bool IsDownloaded = false') -or
        -not $queueServiceSource.Contains('bool WasSkipped = false') -or
        -not $queueServiceSource.Contains('batch = _items.Where(item => !item.IsDownloaded).ToArray();') -or
        -not $queueServiceSource.Contains('IsDownloaded = true') -or
        -not $queueServiceSource.Contains('WasSkipped = result.Skipped') -or
        $queueServiceSource.Contains('_items.RemoveAt(queueIndex)') -or
        -not $mainPageSource.Contains('item.QueuePhotoIndex,') -or
        -not $mainPageSource.Contains('item.IsDownloaded,') -or
        -not $mainPageSource.Contains('item.WasSkipped,') -or
        -not $overlayScriptSource.Contains('item.queuePhotoIndex') -or
        -not $overlayScriptSource.Contains('queued photo ${queueOrdinal}') -or
        -not $overlayScriptSource.Contains('item.isDownloaded === true') -or
        -not $overlayScriptSource.Contains('item.wasSkipped === true') -or
        -not $overlayScriptSource.Contains("item.wasSkipped === true ? 'Already saved' : 'Downloaded'") -or
        -not $overlayScriptSource.Contains("item.isDownloaded === true ? 'isg-item isg-complete' : 'isg-item'") -or
        -not $overlayScriptSource.Contains('start.disabled = !state.isDownloading && !hasPendingItems;') -or
        -not $overlayScriptSource.Contains('queue_complete:') -or
        -not $queueServiceSource.Contains('queue_complete') -or
        -not $queueServiceSource.Contains('bool HasBeenOpened);') -or
        -not $mainPageSource.Contains('hasBeenOpened = snapshot.HasBeenOpened') -or
        -not $queueServiceSource.Contains('Browser queue item {index + 1} failed unexpectedly') -or
        -not $queueServiceSource.Contains('Browser queue batch stopped unexpectedly') -or
        -not $queueServiceSource.Contains('download stopped; remaining items stay queued') -or
        -not $overlayScriptSource.Contains('duplicate-queue-action-suppressed') -or
        -not $overlayScriptSource.Contains('now - lastQueueActionAt < 750') -or
        -not $queueServiceSource.Contains('DownloadOneAsync') -or
        -not $selectedDownloadServiceSource.Contains('DownloadBatchAsync') -or
        -not $selectedDownloadServiceSource.Contains('DownloadOneAsync') -or
        -not $selectedDownloadServiceSource.Contains('AllowAutoRedirect = false') -or
        -not $selectedDownloadServiceSource.Contains('SendMediaGetAsync') -or
        -not $selectedDownloadServiceSource.Contains('IsBrowserQueue') -or
        -not $selectedDownloadServiceSource.Contains('item.IsBrowserQueue') -or
        -not $selectedDownloadServiceSource.Contains('Math.Max(0, item.PhotoIndex ?? index) + 1') -or
        -not $queueServiceSource.Contains('$"{account}_{shortcode}_v{mediaIndex + 1}{extension}"') -or
        -not $queueServiceSource.Contains('$"{account}_{shortcode}_ph{mediaIndex + 1}{extension}"') -or
        -not $mainPageSource.Contains('AttachInstagramBrowser(browserView, OnBrowserOverlayMessage)') -or
        -not $mainPageSource.Contains('item.Key,') -or
        -not $mainPageSource.Contains('TryReadBrowserCursorPosition') -or
        -not $mainPageSource.Contains('ForwardBrowserCursorToSticker') -or
        -not $mainPageSource.Contains('getBrowserQueueFolderPreferences') -or
        -not $mainPageSource.Contains('settingsReady') -or
        -not $mainPageSource.Contains('video_folder_unavailable') -or
        -not $mainPageSource.Contains('EnsureSessionCookiesAsync(force: true)') -or
        -not $mainPageSource.Contains('RecordBrowserQueueDownloadHistoryAsync')) {
        throw 'The themed Instagram photo/video selection overlay, bounded host queue, shared sequential downloader, folder preferences, and history bridge must be wired.'
    }
    if (-not $browserViewEndpointSource.Contains('case "media-overlay":') -or
        -not $browserViewEndpointSource.Contains('RequestMediaOverlayEnabled(enabledElement.GetBoolean())') -or
        -not $browserWindowServiceSource.Contains('public bool RequestMediaOverlayEnabled(bool enabled)') -or
        -not $browserWindowServiceSource.Contains('MediaOverlayEnabled = enabled') -or
        -not $overlayScriptSource.Contains('mediaOverlayEnabled: true') -or
        -not $overlayScriptSource.Contains("setMediaOverlayEnabled(snapshot.mediaOverlayEnabled, 'native-state-sync')") -or
        -not $overlayScriptSource.Contains('if (!state.mediaOverlayEnabled || !inspection?.media || !inspection.candidate) return false;') -or
        -not $overlayScriptSource.Contains("if (tray) tray.dataset.open = 'false';") -or
        -not $overlayScriptSource.Contains('tray.dataset.open = String(state.mediaOverlayEnabled && queuePanelEverOpened);') -or
        -not $mainPageSource.Contains('case "media-overlay-on":') -or
        -not $mainPageSource.Contains('mediaOverlayEnabled = _browserWindow.GetState().MediaOverlayEnabled')) {
        throw 'The Browser media-overlay toggle must hide both selector and queue panel while disabled, retain queued items, and preserve downloads.'
    }
    $downloadFolderGlyph = 'M3 6.5A1.5 1.5 0 0 1 4.5 5H10l2 2h7.5A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z'
    if (-not $overlayScriptSource.Contains($downloadFolderGlyph) -or -not $document.Content.Contains($downloadFolderGlyph)) {
        throw 'The Browser queue folder shortcut must reuse the exact existing download-folder icon.'
    }
    if (-not $overlayScriptSource.Contains('class="isg-action isg-icon-action isg-start"') -or
        -not $overlayScriptSource.Contains('class="isg-action isg-action-secondary isg-icon-action isg-clear"') -or
        -not $overlayScriptSource.Contains('title="Save queued media" aria-label="Save queued media"') -or
        -not $overlayScriptSource.Contains('title="Clear download queue" aria-label="Clear download queue"') -or
        -not $overlayScriptSource.Contains('start.innerHTML = startIcon;') -or
        -not $overlayScriptSource.Contains('start.setAttribute(''aria-label'', startLabel);') -or
        -not $overlayScriptSource.Contains('start.title = startLabel;') -or
        -not $overlayScriptSource.Contains('Cancel queue download') -or
        -not $overlayScriptSource.Contains('Retry failed queue items')) {
        throw 'Browser queue Save/Clear controls must be icon-only and keep accessible labels and tooltips.'
    }
    if (-not $overlayScriptSource.Contains('function topmostVisibleDialog()') -or
        -not $overlayScriptSource.Contains("dialog.getAttribute('aria-hidden') === 'true'") -or
        -not $overlayScriptSource.Contains('function isOverlayAllowedTarget(target)') -or
        -not $overlayScriptSource.Contains('if (visibleDialog) return target instanceof Element && visibleDialog.contains(target);') -or
        -not $overlayScriptSource.Contains("return pathname === '/' || postPattern.test(pathname);") -or
        -not $overlayScriptSource.Contains('if (!isOverlayAllowedTarget(target)) return { media: null, candidate: null };') -or
        -not $overlayScriptSource.Contains('if (!isOverlayAllowedTarget(event.target)) { clearActiveSelector(); return; }') -or
        -not $overlayScriptSource.Contains('function trustedMediaUrl(value)') -or
        -not $overlayScriptSource.Contains("source[src],source[data-src]") -or
        -not $overlayScriptSource.Contains('function mediaContainsPoint(media, point, hitStack = null, eventTarget = null)') -or
        -not $overlayScriptSource.Contains('const hitInMedia = hits.some(hit => hit instanceof Element && (hit === media || media.contains(hit)));') -or
        -not $overlayScriptSource.Contains('const eventTargetsMedia = eventTarget instanceof Element && (eventTarget === media || media.contains(eventTarget) || eventTarget.contains(media));') -or
        -not $overlayScriptSource.Contains('style.overflowX') -or
        -not $overlayScriptSource.Contains('style.overflowY') -or
        -not $overlayScriptSource.Contains("style.display === 'inline' || style.display === 'contents'") -or
        -not $overlayScriptSource.Contains('const hits = Array.isArray(hitStack) ? hitStack : pointHitStack(point);') -or
        -not $overlayScriptSource.Contains('const hitInMedia = hits.some(hit => hit instanceof Element && (hit === media || media.contains(hit)));') -or
        -not $overlayScriptSource.Contains('if (!hitInMedia) return reject(''outside-horizontal-clip'', { geometry });') -or
        -not $overlayScriptSource.Contains('if (!hitInMedia) return reject(''outside-vertical-clip'', { geometry });') -or
        -not $overlayScriptSource.Contains('diagnostics.clipBoundsDisagreedWithHitStack = true;') -or
        -not $overlayScriptSource.Contains('if (!mediaContainsPoint(possibleMedia, point, hits, eventTarget, hitDiagnostics)) {') -or
        -not $overlayScriptSource.Contains('const photoItems = state.items.filter(item => item.isVideo !== true);') -or
        -not $overlayScriptSource.Contains('const videoItems = state.items.filter(item => item.isVideo === true);') -or
        -not $overlayScriptSource.Contains("[['Photos', photoItems], ['Videos', videoItems]]") -or
        -not $overlayScriptSource.Contains('.isg-group-heading') -or
        -not $overlayScriptSource.Contains('width:min(320px,calc(100vw - 24px))') -or
        -not $overlayScriptSource.Contains('max-height:min(360px,calc(100vh - 24px))') -or
        -not $overlayScriptSource.Contains('opacity:.8;pointer-events:none') -or
        -not $overlayScriptSource.Contains('opacity:.8!important') -or
        -not $overlayScriptSource.Contains('background:transparent!important;background-image:none!important') -or
        -not $overlayScriptSource.Contains('backdrop-filter:blur(16px)')) {
        throw 'The Browser overlay must hit-test visible clipped media, keep its button stable across carousel slides, refresh reused feed media, queue and resolve videos, group media, and use translucent blurred surfaces.'
    }
    if (-not $document.Content.Contains('window.updateStickerEyesFromExternalCursor=function')) {
        throw 'Native Instagram browser pointer coordinates must be forwarded to the sidebar sticker eyes.'
    }
    $versionHistoryStart = $document.Content.IndexOf('function showVersionHistory()')
    if ($versionHistoryStart -lt 0) {
        throw 'Version history browser visibility handlers are missing.'
    }
    $versionHistoryEnd = $document.Content.IndexOf('function startAppAfterLoadingScreen()', $versionHistoryStart)
    if ($versionHistoryEnd -le $versionHistoryStart) {
        throw 'Version history browser visibility handlers are missing.'
    }
    $versionHistorySource = $document.Content.Substring($versionHistoryStart, $versionHistoryEnd - $versionHistoryStart)
    if (-not $versionHistorySource.Contains("versionHistoryRestoreBrowser=Boolean(browserPage&&browserPage.style.display==='block')") -or
        -not $versionHistorySource.Contains("sendBrowserView({action:'hide'})") -or
        -not $versionHistorySource.Contains('reportBrowserRegion()') -or
        -not $versionHistorySource.Contains("sendBrowserView({action:'show'})")) {
        throw 'Opening Version history in the Browser tab must hide the native WebView and restore it when closed.'
    }
    if ($overlayScriptSource.Contains("debugLog('hover-check'") -or
        $overlayScriptSource.Contains("debugLog('select-button-positioned'") -or
        $overlayScriptSource.Contains("debugLog('select-button-hide-scheduled'") -or
        $overlayScriptSource.Contains("debugLog('select-button-hide-cancelled'") -or
        $overlayScriptSource.Contains("debugLog('select-button-hidden'") -or
        $overlayScriptSource.Contains("debugLog('hover-exit'") -or
        $overlayScriptSource.Contains("debugLog('media-refresh-skipped'")) {
        throw 'The Browser overlay must not emit per-hover or per-position diagnostic events.'
    }
    $cursorMessageBranch = $bootstrapperSource.IndexOf('if (action == "cursor-position")', [System.StringComparison]::Ordinal)
    $genericMessageLog = $bootstrapperSource.IndexOf('LogInstagramBrowserOverlay($"WebMessageReceived action={action}', [System.StringComparison]::Ordinal)
    if ($cursorMessageBranch -lt 0 -or $genericMessageLog -lt 0 -or $cursorMessageBranch -gt $genericMessageLog) {
        throw 'High-rate cursor relay messages must be forwarded before generic native bridge logging.'
    }

    $historyEndpointPath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\DownloadHistoryEndpoint.cs'
    $historyEndpointSource = Get-Content -LiteralPath $historyEndpointPath -Raw
    if (-not $historyEndpointSource.Contains('videoIndex = file.Index') -or
        $historyEndpointSource.Contains('contentIndex = file.Index')) {
        throw 'Video download history must be keyed by the video ordinal, not the post content index.'
    }
    if (-not $bootstrapperSource.Contains('int? ContentIndex') -or
        -not $bootstrapperSource.Contains('historyContext.VideoIndex ?? historyContext.ContentIndex ?? 0')) {
        throw 'Native video downloads must retain account_shortcode_vN naming when contentIndex is unavailable.'
    }

    $folderOpenerPath = Join-Path $PSScriptRoot '..\src\InStSanity\Platforms\Windows\WindowsDownloadFolderOpener.cs'
    $folderOpenerSource = Get-Content -LiteralPath $folderOpenerPath -Raw
    if (-not $folderOpenerSource.Contains('TryActivateExistingExplorerWindow(normalizedFolder)') -or
        -not $folderOpenerSource.Contains('shellApplication.Windows()') -or
        -not $folderOpenerSource.Contains('explorerWindow.Document.Folder.Self.Path') -or
        -not $folderOpenerSource.Contains('Path.TrimEndingDirectorySeparator') -or
        -not $folderOpenerSource.Contains('StringComparison.OrdinalIgnoreCase') -or
        -not $folderOpenerSource.Contains('SetForegroundWindow(windowHandle)')) {
        throw 'Open folder must focus an existing Explorer window for the normalized path before launching another window.'
    }

    $projectPath = Join-Path $PSScriptRoot '..\src\InStSanity\InStSanity.csproj'
    $projectSource = Get-Content -LiteralPath $projectPath -Raw
    if (-not $projectSource.Contains('<ApplicationDisplayVersion>2.099</ApplicationDisplayVersion>') -or
        -not $projectSource.Contains('<ApplicationVersion>168</ApplicationVersion>')) {
        throw 'The display version and integer application version code must be bumped for this release.'
    }

    if (-not $document.Content.Contains('async function resolvePostsBatchChunk') -or
        -not $document.Content.Contains('/api/resolve-batch?urls=') -or
        -not $document.Content.Contains('batchOk=await resolvePostsBatchChunk(chunk,token,controller?.signal||null)') -or
        -not $document.Content.Contains('Cover previews and photo/video counts load automatically')) {
        throw 'Saved-post cards must automatically resolve visible-page cover previews and counts.'
    }

    $resizeUploadSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\ResizeUploadEndpoint.cs') -Raw
    $resizeConvertSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\ResizeConvertEndpoint.cs') -Raw
    $resizeServiceSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Images\PhotoResizeService.cs') -Raw
    $imageResizerSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\ImageResizer.cs') -Raw
    $photoAdjustmentsSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Images\PhotoImageAdjustments.cs') -Raw
    $mauiSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\MauiProgram.cs') -Raw
    $windowsAppSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Platforms\Windows\App.xaml.cs') -Raw
    $appDiagnosticsSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Platforms\Windows\AppDiagnostics.cs') -Raw
    $nativePickerServiceSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Platforms\Windows\WindowsNativeFilePickerService.cs') -Raw
    $nativePickEndpointSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\PickNativeFilesEndpoint.cs') -Raw
    $nativeFileContentEndpointSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\NativePickedFileContentEndpoint.cs') -Raw
    $nativeResizeUploadEndpointSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\InStSanity\Services\Api\ResizeNativeFileUploadEndpoint.cs') -Raw
    $nativeVideoPickerMultiple = $nativePickerServiceSource -match '(?s)"video"\s*=>\s*new\(\s*global::Windows\.Storage\.Pickers\.PickerLocationId\.VideosLibrary\s*,\s*VideoExtensions\s*,\s*AllowMultiple:\s*true\s*\)'
    if (-not $mauiSource.Contains('INativeFilePickerService, WindowsNativeFilePickerService') -or
        -not $mauiSource.Contains('NativePickedFileStore') -or
        -not $mauiSource.Contains('PickNativeFilesEndpoint') -or
        -not $mauiSource.Contains('NativePickedFileContentEndpoint') -or
        -not $mauiSource.Contains('ResizeNativeFileUploadEndpoint') -or
        -not $nativePickerServiceSource.Contains('FileOpenPicker') -or
        -not $nativePickerServiceSource.Contains('InitializeWithWindow.Initialize(picker, windowHandle)') -or
        -not $nativePickerServiceSource.Contains('PickMultipleFilesAsync()') -or
        -not $nativeVideoPickerMultiple -or
        -not $nativePickEndpointSource.Contains('Route => "/api/pick-native-files"') -or
        -not $nativeFileContentEndpointSource.Contains('Route => "/api/picked-file"') -or
        -not $nativeFileContentEndpointSource.Contains('Content-Range') -or
        -not $nativeResizeUploadEndpointSource.Contains('Route => "/api/resize-native-upload"')) {
        throw 'Photo/video selection must use the registered owner-window native picker and app-local file routes.'
    }
    if (-not $windowsAppSource.Contains('AppDiagnostics.Initialize()') -or
        -not $windowsAppSource.Contains('UnhandledException += OnWinUiUnhandledException') -or
        -not $appDiagnosticsSource.Contains('app-diagnostics.log') -or
        -not $appDiagnosticsSource.Contains('AppDomain.CurrentDomain.UnhandledException') -or
        -not $appDiagnosticsSource.Contains('TaskScheduler.UnobservedTaskException') -or
        -not $bootstrapperSource.Contains('core.ProcessFailed +=') -or
        -not $bootstrapperSource.Contains('photo.pointerdown') -or
        -not $bootstrapperSource.Contains('photo.click') -or
        -not $bootstrapperSource.Contains('javascript.error') -or
        -not $bootstrapperSource.Contains('javascript.unhandledrejection') -or
        -not $nativePickerServiceSource.Contains('picker.dialog.opening') -or
        -not $nativePickerServiceSource.Contains('picker.dialog.returned') -or
        -not $nativePickerServiceSource.Contains('picker.metadata.complete') -or
        -not $nativeFileContentEndpointSource.Contains('picked-file.image-read') -or
        -not $document.Content.Contains('native-picker.request') -or
        -not $document.Content.Contains('native-picker.selected') -or
        -not $document.Content.Contains('photo.preview.decode-start') -or
        -not $document.Content.Contains('photo.preview.canvas-allocate') -or
        -not $document.Content.Contains('const maxSide=2400;') -or
        -not $document.Content.Contains('Preview is scaled to a maximum 2,400px long edge for performance; downloads process the source image.') -or
        -not $document.Content.Contains('photo.preview.pixels-ready') -or
        -not $document.Content.Contains('Crash diagnosis log: %LOCALAPPDATA%')) {
        throw 'Crash diagnosis must persist unhandled exceptions, WebView failures, image clicks, preview stages, and native-picker checkpoints.'
    }
    $photoAdjustmentHasEditsStart = $photoAdjustmentsSource.IndexOf('public bool HasEdits =>')
    $photoAdjustmentHasEditsSource = ''
    if ($photoAdjustmentHasEditsStart -ge 0) {
        $photoAdjustmentHasEditsEnd = $photoAdjustmentsSource.IndexOf('internal sealed record PhotoImageDamageTexture', $photoAdjustmentHasEditsStart)
        if ($photoAdjustmentHasEditsEnd -gt $photoAdjustmentHasEditsStart) {
            $photoAdjustmentHasEditsSource = $photoAdjustmentsSource.Substring($photoAdjustmentHasEditsStart, $photoAdjustmentHasEditsEnd - $photoAdjustmentHasEditsStart)
        }
    }
    if (-not $photoAdjustmentHasEditsSource -or $photoAdjustmentHasEditsSource.Contains('OldPhotoSeed')) {
        throw 'Old Photo seed changes must not count as an active image edit.'
    }
    if (-not $resizeUploadSource.Contains('Route => "/api/resize-upload"') -or
        -not $resizeConvertSource.Contains('Route => "/api/resize-convert"') -or
        -not $resizeServiceSource.Contains('FileMode.CreateNew') -or
        -not $resizeServiceSource.Contains('TryResizeToMaxSideAsync') -or
        -not $resizeConvertSource.Contains('TryGetProperty(root, "upscale"') -or
        -not $resizeConvertSource.Contains('TryGetProperty(root, "noDuplicates"') -or
        -not $resizeServiceSource.Contains('bool upscale') -or
        -not $resizeServiceSource.Contains('bool noDuplicates') -or
        -not $resizeServiceSource.Contains('FindExistingResizedFilePath') -or
        -not $resizeServiceSource.Contains('BuildAccountAwarePhotoStem') -or
        -not $resizeServiceSource.Contains('TryReadNumberedSuffix(stem, "_ph"') -or
        -not $resizeServiceSource.Contains('return safeStem + outputSuffix + duplicateSuffix + extension.ToLowerInvariant();') -or
        -not $resizeServiceSource.Contains('Status == "skipped"') -or
        -not $resizeServiceSource.Contains('int SkippedCount') -or
        -not $resizeServiceSource.Contains('suffix == 1 ? filename : $"{stem} ({suffix}){extension}"') -or
        -not $imageResizerSource.Contains('largestSide < targetSide && !upscale') -or
        -not $imageResizerSource.Contains('catch when (!adjustments.HasEdits)') -or
        -not $imageResizerSource.Contains('return new ImageResizeResult(payload, originalExtension, WasResized: false, WasEdited: false);') -or
        -not $imageResizerSource.Contains('ScaledHeight = targetHeight') -or
        -not $imageResizerSource.Contains('OldPhotoTextureAssetPath = "wwwroot/old-photo-damage-texture.png"') -or
        -not $imageResizerSource.Contains('adjustments.OldPhoto > 0') -or
        -not $imageResizerSource.Contains('GetOldPhotoTextureAsync()') -or
        -not $photoAdjustmentsSource.Contains('OldPhoto != 0') -or
        -not $photoAdjustmentsSource.Contains('settings.OldPhoto > 0') -or
        -not $photoAdjustmentsSource.Contains('SharpenDamageTexture(oldPhotoTexture, settings.Sharpness)') -or
        -not $photoAdjustmentsSource.Contains('OldPhotoSeed { get; init; } = 1') -or
        -not $photoAdjustmentsSource.Contains('settings.OldPhotoSeed') -or
        -not $resizeConvertSource.Contains('TryReadNumber(element, "oldPhoto", 0, 100, 0, out var oldPhoto)') -or
        -not $resizeConvertSource.Contains('TryReadNumber(element, "oldPhotoSeed", 0, int.MaxValue, 1, out var oldPhotoSeed)') -or
        -not $resizeConvertSource.Contains('OldPhotoSeed = (int)oldPhotoSeed') -or
        -not $resizeServiceSource.Contains('~/Pictures/Instagram') -or
        -not $mauiSource.Contains('AddSingleton<PhotoResizeService>')) {
        throw 'The photo-resizer endpoints, staged upload, non-overwriting output, max-side conversion, and service registration must be present.'
    }
    $resizeImageMethodStart = $imageResizerSource.IndexOf('public static async Task<ImageResizeResult?> TryResizeToMaxSideAsync(')
    $resizeImageMethodEnd = if ($resizeImageMethodStart -ge 0) { $imageResizerSource.IndexOf('static Task<PhotoImageDamageTexture?> GetOldPhotoTextureAsync()', $resizeImageMethodStart) } else { -1 }
    if ($resizeImageMethodStart -lt 0 -or $resizeImageMethodEnd -le $resizeImageMethodStart) {
        throw 'The image resize/conversion method could not be inspected.'
    }
    $resizeImageMethodSource = $imageResizerSource.Substring($resizeImageMethodStart, $resizeImageMethodEnd - $resizeImageMethodStart)
    if (-not $resizeImageMethodSource.Contains('decoder.GetPixelDataAsync(') -or
        -not $resizeImageMethodSource.Contains('encoder.SetPixelData(') -or
        $resizeImageMethodSource.Contains('GetSoftwareBitmapAsync(')) {
        throw 'No-adjustment conversion must use the same GetPixelDataAsync/SetPixelData pipeline as adjusted exports, not the failing SoftwareBitmap path.'
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
        -not $document.Content.Contains('async function resolvePostsBatchChunk') -or
        -not $document.Content.Contains('function selectAllCollectionPhotos(){return}') -or
        -not $document.Content.Contains("collectionBtn.disabled=true;collectionBtn.setAttribute('aria-disabled','true')") -or
        -not $document.Content.Contains('All Posts (${posts.length})')) {
        throw 'Visible-page card summaries must load, while full-collection summary sweeps and select-all-everywhere remain disabled.'
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
        -not $downloadHistoryEndpointSource.Contains('File.GetLastWriteTimeUtc(path)') -or
        -not $downloadHistoryEndpointSource.Contains('"_resized"') -or
        -not $downloadHistoryEndpointSource.Contains('"_edited"')) {
        throw 'The history endpoint must import timestamps from legacy/current, resized, and edited numbered files without reading media contents.'
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
    $startAppStart = $document.Content.IndexOf('function startAppAfterLoadingScreen()')
    $startAppEnd = $document.Content.IndexOf('window.startAppAfterLoadingScreen=startAppAfterLoadingScreen;', $startAppStart)
    $startAppSource = if ($startAppStart -ge 0 -and $startAppEnd -gt $startAppStart) {
        $document.Content.Substring($startAppStart, $startAppEnd - $startAppStart)
    } else { '' }
    $sessionPollPosition = $document.Content.IndexOf('setInterval(loadInstagramSession,5000);')
    if ($mainPageCodeSource.Contains('InitializeInstagramSessionAsync') -or
        -not $mainPageCodeSource.Contains('RestoreKnownAccount();') -or
        -not $startAppSource.Contains('setTimeout(()=>loadInstagramSession(true),250);') -or
        $sessionPollPosition -lt $startAppStart -or
        $sessionPollPosition -ge $startAppEnd -or
        $document.Content.Contains('setTimeout(()=>loadInstagramSession(true),1200);')) {
        throw 'Restore the cached Instagram identity at startup, but defer creating/refreshing the native browser and its polling until after the boot animation.'
    }
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
        $mediaBuildSource.Contains('HasFullSizeImage = HasFullSizeImageCandidate(slide),') -and
        $resolverSource.Contains('static bool HasFullSizeImageCandidate(JsonNode slide)') -and
        $resolverSource.Contains('BestCandidate(slide["image_versions2"]?["candidates"]).Length > 0') -and
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
        -not $document.Content.Contains('recordPhotoDownloadHistory(item,file)') -or
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
    if (-not $openGraphSource.Contains('FullResolved = false,') -or
        -not $openGraphSource.Contains('HasFullSizeImage = false,')) {
        throw 'An Open Graph cover must remain a preview, not count as full-resolution media.'
    }

    $saveFreePath = Join-Path $PSScriptRoot '..\src\InStSanity\Services\Instagram\SaveFreeClient.cs'
    $saveFreeSource = Get-Content -LiteralPath $saveFreePath -Raw
    if (-not ($saveFreeSource.Contains('var bestUrl = ExtractJwtUrl(bestToken);') -and
        $saveFreeSource.Contains('bestUrl.Length > 0 ? bestUrl : thumbnailUrl;') -and
        $saveFreeSource.Contains('HasFullSizeImage = !isVideo && bestUrl.Length > 0 && (options.Count > 0 || download.Success),') -and
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

        $accountNamedRequest = @{
            folder = $downloadFolder
            noDuplicates = $true
            items = @(@{
                account = 'arena_user'
                shortcode = 'namedpost'
                photoIndex = 2
                isVideo = $false
                dataUri = "data:image/png;base64,$pngBase64"
            })
        } | ConvertTo-Json -Depth 8 -Compress
        $accountNamedResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-selected" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $accountNamedRequest `
            -TimeoutSec 60
        if (-not $accountNamedResult.ok -or $accountNamedResult.savedCount -ne 1 -or
            -not (Test-Path -LiteralPath (Join-Path $downloadFolder 'arena_user_namedpost_ph3.png'))) {
            throw "An account-aware photo without a client filename did not use account_shortcode_phN: $($accountNamedResult | ConvertTo-Json -Compress)."
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

        $accountPhotoHistoryFile = Join-Path $downloadFolder 'account_smoke_ph3.jpg'
        $resizedPhotoHistoryFile = Join-Path $downloadFolder 'account_smoke_ph4_resized.jpg'
        $editedPhotoHistoryFile = Join-Path $downloadFolder 'account_smoke_ph5_edited.jpg'
        $accountVideoHistoryFile = Join-Path $downloadFolder 'account_smoke_v2.mp4'
        [System.IO.File]::WriteAllBytes($accountPhotoHistoryFile, [byte[]](1, 2, 3))
        [System.IO.File]::WriteAllBytes($resizedPhotoHistoryFile, [byte[]](1, 2, 3))
        [System.IO.File]::WriteAllBytes($editedPhotoHistoryFile, [byte[]](1, 2, 3))
        [System.IO.File]::WriteAllBytes($accountVideoHistoryFile, [byte[]](1, 2, 3))
        $historyRequest = @{ folder = $downloadFolder; shortcodes = @('smoke') } | ConvertTo-Json -Depth 4 -Compress
        $historyResult = Invoke-RestMethod `
            -Uri "$baseUrl/api/download-history" `
            -Method Post `
            -ContentType 'application/json' `
            -Body $historyRequest `
            -TimeoutSec 30
        $historyFiles = @($historyResult.history.smoke.photos)
        $historyVideos = @($historyResult.history.smoke.videos)
        $legacyHistoryFile = $historyFiles | Where-Object { $_.photoIndex -eq 1 } | Select-Object -First 1
        $accountPhotoHistoryRecord = $historyFiles | Where-Object { $_.photoIndex -eq 2 } | Select-Object -First 1
        $resizedPhotoHistoryRecord = $historyFiles | Where-Object { $_.photoIndex -eq 3 } | Select-Object -First 1
        $editedPhotoHistoryRecord = $historyFiles | Where-Object { $_.photoIndex -eq 4 } | Select-Object -First 1
        $accountVideoHistoryRecord = $historyVideos | Where-Object { $_.videoIndex -eq 1 } | Select-Object -First 1
        if (-not $historyResult.ok -or $historyFiles.Count -ne 5 -or
            -not ($historyFiles | Where-Object { $_.photoIndex -eq 0 -and $_.downloadedAtUtc }) -or
            -not $legacyHistoryFile -or -not $legacyHistoryFile.downloadedAtUtc -or
            $legacyHistoryFile.filename -ne 'smoke_photo_2.jpg' -or
            -not $accountPhotoHistoryRecord -or $accountPhotoHistoryRecord.filename -ne 'account_smoke_ph3.jpg' -or
            -not $resizedPhotoHistoryRecord -or $resizedPhotoHistoryRecord.filename -ne 'account_smoke_ph4_resized.jpg' -or
            -not $editedPhotoHistoryRecord -or $editedPhotoHistoryRecord.filename -ne 'account_smoke_ph5_edited.jpg' -or
            -not $accountVideoHistoryRecord -or $accountVideoHistoryRecord.filename -ne 'account_smoke_v2.mp4' -or
            -not [System.IO.File]::Exists((Join-Path $downloadFolder $legacyHistoryFile.filename))) {
            throw "The history scan did not identify account-aware photo/video names, resized/edited variants, duplicate-suffixed files, and legacy numbered photos: $($historyResult | ConvertTo-Json -Compress)."
        }

        Write-Detail "download save/skip/unique-suffix behavior and account-aware/legacy/resized/edited POST /api/download-history names and timestamps verified"
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
