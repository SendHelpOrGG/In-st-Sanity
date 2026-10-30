namespace InStSanity.Services.Browser;

/// <summary>Injected into the native Instagram WebView2 to add media-select controls and the themed session queue.</summary>
internal static class InstagramMediaOverlayScript
{
	internal const string Source = """
	(() => {
	  const diagnosticPrefix = '[InStSanity:InstagramOverlay]';
	  function debugLog(event, details = {}) {
	    const record = { at: new Date().toISOString(), event, ...details };
	    try { console.info(`${diagnosticPrefix} ${JSON.stringify(record)}`); } catch (_) { }
	    try {
	      const bridge = window.chrome?.webview;
	      if (typeof bridge?.postMessage === 'function') bridge.postMessage({ v: 1, action: 'overlay-debug', ...record });
	    } catch (_) { }
	  }
	  function routeKind() {
	    const match = String(location.pathname || '').match(/^\/(p|reels|reel|tv)\//i);
	    return match ? match[1].toLowerCase() : 'feed-or-other';
	  }

	  if (window !== window.top) return;
	  if (!/^https:\/\/(?:www\.)?instagram\.com$/i.test(location.origin)) return;
	  debugLog('script-entered', { origin: location.origin, route: routeKind() });
	  if (window.__instSanityMediaOverlayInstall) {
	    debugLog('script-already-present', { installed: window.__instSanityMediaOverlayInstalled === true });
	    return;
	  }

	  const postPattern = /^\/(?:p|reel|reels|tv)\/([A-Za-z0-9_-]{1,64})\/?$/i;
	  const state = { items: [], isDownloading: false, currentIndex: 0, total: 0, status: '', theme: {} };
	  const slideByRoot = new WeakMap();
	  let activeMedia = null;
	  let activeCandidate = null;
	  let buttonAnchorShortcode = '';
	  let buttonAnchorSlideIndex = null;
	  let lastObservedRoute = location.pathname;
	  let routePollTimer = 0;
	  let lastPositionLogAt = 0;
	  let routeEpoch = 0;
	  let nextDiagnosticRootId = 1;
	  const inspectionLogKeys = new WeakMap();
	  const scrollOffsets = new WeakMap();
	  const horizontalScrollLogged = new WeakSet();
	  const diagnosticRootIds = new WeakMap();
	  let hideTimer = 0;
	  let mutationRefreshTimer = 0;
	  let selectButton = null;
	  let tray = null;
	  let trayCollapsed = false;
	  let lastPointer = null;
	  let lastMoveInspectionAt = 0;
	  let pendingStickerCursor = null;
	  let stickerCursorFrame = 0;
	  let lastStickerCursorSentAt = -Infinity;

	  const css = `
	    #inst-sanity-select{position:fixed;z-index:2147483646;display:none;align-items:center;gap:6px;border:1px solid var(--isg-border,#ffffff55);border-radius:999px;padding:7px 10px;appearance:none;-webkit-appearance:none;background:transparent!important;background-image:none!important;color:var(--isg-button-ink,#091318);font:650 11px/1.1 "Segoe UI",sans-serif;box-shadow:0 5px 22px #0008;cursor:pointer;white-space:nowrap;--isg-surface:var(--isg-button-bg,linear-gradient(135deg,#99f6e4,#2dd4bf));isolation:isolate;backdrop-filter:blur(16px);-webkit-backdrop-filter:blur(16px)}
	    #inst-sanity-select::before,#inst-sanity-tray::before,#inst-sanity-notice::before,.isg-action::before,.isg-item::before{content:"";position:absolute;inset:0;z-index:-1;border-radius:inherit;background:var(--isg-surface,var(--isg-panel,rgba(17,24,32,.97)));opacity:.8;pointer-events:none}
	    #inst-sanity-select::before{opacity:.8!important}
	    #inst-sanity-select:hover{filter:brightness(1.08);transform:translateY(-1px)}
	    #inst-sanity-select[data-selected="true"]{--isg-surface:var(--isg-panel,#151b23);color:var(--isg-ink,#f8fafc);border-color:var(--isg-accent,#99f6e4)}
	    #inst-sanity-tray{position:fixed;z-index:2147483645;right:12px;bottom:12px;width:min(320px,calc(100vw - 24px));max-height:min(360px,calc(100vh - 24px));display:none;flex-direction:column;overflow:hidden;border:1px solid var(--isg-border,#ffffff30);border-radius:14px;background:transparent;--isg-surface:var(--isg-panel,rgba(17,24,32,.97));color:var(--isg-ink,#f8fafc);font:11px/1.4 "Segoe UI",sans-serif;box-shadow:0 14px 42px #0009;isolation:isolate;backdrop-filter:blur(16px);-webkit-backdrop-filter:blur(16px)}
	    #inst-sanity-tray[data-open="true"]{display:flex}
	    #inst-sanity-tray *{box-sizing:border-box}
	    .isg-head{display:flex;align-items:center;gap:8px;padding:8px 10px 7px;border-bottom:1px solid var(--isg-border,#ffffff22)}
	    .isg-title{flex:1;min-width:0;font-size:12px;font-weight:700;letter-spacing:.1px}
	    .isg-count{display:inline-flex;align-items:center;justify-content:center;min-width:22px;height:20px;padding:0 6px;border-radius:999px;background:color-mix(in srgb,var(--isg-accent,#2dd4bf) 80%,transparent);color:var(--isg-button-ink,#091318);font-weight:750;font-size:10px}
	    .isg-actions{display:flex;gap:5px;align-items:center}
	    .isg-icon-action{display:inline-flex;align-items:center;justify-content:center;flex:0 0 28px;width:28px;height:28px;min-width:28px;padding:0}
	    .isg-icon-action svg{display:block;width:16px;height:16px}
	    .isg-collapse svg{transition:transform .15s ease}
	    #inst-sanity-tray[data-collapsed="true"] .isg-collapse svg{transform:rotate(180deg)}
	    #inst-sanity-tray[data-collapsed="true"] .isg-items,#inst-sanity-tray[data-collapsed="true"] .isg-footer{display:none}
	    .isg-action{position:relative;isolation:isolate;border:1px solid var(--isg-border,#ffffff30);border-radius:7px;padding:5px 8px;background:transparent;--isg-surface:var(--isg-button-bg,linear-gradient(135deg,#99f6e4,#2dd4bf));color:var(--isg-button-ink,#091318);font:650 10px/1.2 "Segoe UI",sans-serif;cursor:pointer;white-space:nowrap;backdrop-filter:blur(12px);-webkit-backdrop-filter:blur(12px)}
	    .isg-action:disabled{opacity:.5;cursor:default}
	    .isg-action-secondary{--isg-surface:var(--isg-panel,#151b23);color:var(--isg-ink,#f8fafc)}
	    .isg-items{display:flex;flex-direction:column;gap:7px;padding:7px 9px;overflow:auto;min-height:0}
	    .isg-group{display:flex;flex-direction:column;gap:4px;min-width:0}
	    .isg-group-heading{padding:0 2px 3px;border-bottom:1px solid var(--isg-border,#ffffff22);color:var(--isg-muted,#9fb1c1);font-size:9px;font-weight:750;letter-spacing:.45px;text-transform:uppercase}
	    .isg-group-items{display:flex;flex-direction:column;gap:5px}
	    .isg-item{position:relative;isolation:isolate;display:flex;align-items:center;gap:8px;min-height:44px;padding:5px;border:1px solid var(--isg-border,#ffffff20);border-radius:9px;background:transparent;--isg-surface:var(--isg-panel,rgba(17,24,32,.97));backdrop-filter:blur(16px);-webkit-backdrop-filter:blur(16px)}
	    .isg-thumb{width:34px;height:34px;flex:0 0 34px;border-radius:6px;object-fit:cover;background:rgba(255,255,255,.08)}
	    .isg-thumb-fallback{display:grid;place-items:center;color:var(--isg-accent2,var(--isg-accent,#99f6e4));font-size:9px;font-weight:800;letter-spacing:.25px}
	    .isg-info{min-width:0;flex:1}
	    .isg-name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:10px;font-weight:650}
	    .isg-meta{margin-top:1px;color:var(--isg-muted,#9fb1c1);font-size:9px}
	    .isg-error{margin-top:1px;color:#ff9a9a;font-size:9px}
	    .isg-empty{padding:6px 3px 8px;color:var(--isg-muted,#9fb1c1);font-size:10px}
	    .isg-footer{padding:6px 10px 8px;color:var(--isg-muted,#9fb1c1);font-size:9px}
	    #inst-sanity-notice{position:fixed;z-index:2147483647;left:50%;bottom:18px;max-width:min(460px,calc(100vw - 32px));transform:translate(-50%,12px);opacity:0;pointer-events:none;border:1px solid var(--isg-border,#ffffff30);border-radius:9px;padding:8px 11px;background:transparent;--isg-surface:var(--isg-panel,rgba(17,24,32,.97));color:var(--isg-ink,#f8fafc);font:600 11px/1.4 "Segoe UI",sans-serif;box-shadow:0 7px 28px #0008;transition:opacity .18s,transform .18s;isolation:isolate;backdrop-filter:blur(16px);-webkit-backdrop-filter:blur(16px)}
	    #inst-sanity-notice[data-show="true"]{opacity:1;transform:translate(-50%,0)}
	    @media(max-width:540px){#inst-sanity-tray{right:8px;bottom:8px;width:min(320px,calc(100vw - 16px));max-height:min(360px,calc(100vh - 16px))}#inst-sanity-select{padding:7px 10px;font-size:11px}}
	  `;

	  function postMessage(action, payload = {}) {
	    try {
	      const bridge = window.chrome?.webview;
	      if (typeof bridge?.postMessage !== 'function') {
	        debugLog('bridge-unavailable', { action });
	        if (action !== 'overlay-ready') showNotice('The In-st-Sanity queue connection is unavailable.');
	        return false;
	      }
	      bridge.postMessage({ v: 1, action, ...payload });
	      debugLog('bridge-message-sent', { action });
	      return true;
	    } catch (error) {
	      debugLog('bridge-post-failed', { action, errorName: String(error?.name || 'Error') });
	      if (action !== 'overlay-ready') showNotice('The In-st-Sanity queue connection is unavailable.');
	      return false;
	    }
	  }

	  function mediaPathIdentity(value) {
	    const raw = String(value || '');
	    try { return new URL(raw, location.href).pathname || raw; } catch (_) { return raw; }
	  }
	  function itemKey(item) {
	    if (typeof item.key === 'string' && item.key) return item.key;
	    const type = item.mediaType || (item.isVideo ? 'video' : 'photo');
	    const identity = item.rawUrl ? mediaPathIdentity(item.rawUrl) : `slide:${Math.max(0, Number(item.slideIndex) || 0)}`;
	    return `${item.shortcode}|${type}|${identity}`;
	  }
	  function safeText(value, fallback = '') { return String(value ?? fallback).slice(0, 180); }
	  function safeUrlSummary(value) {
	    if (typeof value !== 'string' || !value) return { present: false };
	    try {
	      const url = new URL(value, location.href);
	      const postMatch = url.pathname.match(postPattern);
	      return { present: true, protocol: url.protocol, host: url.host, route: postMatch ? postMatch[0].split('/')[1] : 'other', shortcodeLength: postMatch?.[1]?.length || 0, length: value.length };
	    } catch (_) { return { present: true, parseable: false, length: value.length }; }
	  }
	  function clearTimer() { if (hideTimer) clearTimeout(hideTimer); hideTimer = 0; }

	  function ensureUi() {
	    if (!document.getElementById('inst-sanity-overlay-style')) {
	      const style = document.createElement('style');
	      style.id = 'inst-sanity-overlay-style';
	      style.textContent = css;
	      (document.head || document.documentElement).appendChild(style);
	    }

	    selectButton = document.getElementById('inst-sanity-select');
	    if (!selectButton) {
	      selectButton = document.createElement('button');
	      selectButton.id = 'inst-sanity-select';
	      selectButton.type = 'button';
	      selectButton.setAttribute('aria-label', 'Add media to the download queue');
	      selectButton.addEventListener('pointerdown', event => event.stopPropagation());
	      selectButton.addEventListener('click', event => {
	        event.preventDefault(); event.stopPropagation(); clearTimer();
	        debugLog('queue-toggle-click', {
	          candidatePresent: !!activeCandidate,
	          mediaType: activeCandidate?.mediaType || '',
	          shortcodeLength: String(activeCandidate?.shortcode || '').length,
	          postUrlMatchesShortcode: !!activeCandidate && parseShortcode(activeCandidate.postUrl) === activeCandidate.shortcode,
	          postUrl: safeUrlSummary(activeCandidate?.postUrl),
	          mediaUrl: safeUrlSummary(activeCandidate?.rawUrl),
	          slideIndex: activeCandidate?.slideIndex ?? null,
	        });
	        if (!activeCandidate) return;
	        postMessage('toggle-media', activeCandidate);
	      });
	      document.documentElement.appendChild(selectButton);
	    }

	    tray = document.getElementById('inst-sanity-tray');
	    if (!tray) {
	      tray = document.createElement('section');
	      tray.id = 'inst-sanity-tray';
	      tray.setAttribute('role', 'region');
	      tray.setAttribute('aria-label', 'In-st-Sanity media download queue');
	      tray.innerHTML = '<div class="isg-head"><div class="isg-title">Download queue</div><span class="isg-count">0</span><div class="isg-actions"><button class="isg-action isg-start" type="button">Save</button><button class="isg-action isg-action-secondary isg-clear" type="button">Clear</button><button class="isg-action isg-action-secondary isg-icon-action isg-open-folder" type="button" title="Open download folder" aria-label="Open download folder"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5H10l2 2h7.5A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z"/><path d="M3.5 9h17"/></svg></button><button class="isg-action isg-action-secondary isg-icon-action isg-collapse" type="button" title="Collapse download queue" aria-label="Collapse download queue" aria-expanded="true"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg></button></div></div><div class="isg-items"></div><div class="isg-footer"></div>';
	      tray.dataset.collapsed = String(trayCollapsed);
	      tray.querySelector('.isg-start').addEventListener('click', () => {
	        if (state.isDownloading) postMessage('cancel-download');
	        else postMessage('start-download');
	      });
	      tray.querySelector('.isg-clear').addEventListener('click', () => postMessage('clear-queue'));
	      tray.querySelector('.isg-open-folder').addEventListener('click', event => {
	        event.preventDefault(); event.stopPropagation(); postMessage('open-download-folder');
	      });
	      const collapseButton = tray.querySelector('.isg-collapse');
	      collapseButton.addEventListener('click', event => {
	        event.preventDefault(); event.stopPropagation();
	        trayCollapsed = !trayCollapsed;
	        tray.dataset.collapsed = String(trayCollapsed);
	        const expanded = !trayCollapsed;
	        collapseButton.setAttribute('aria-expanded', String(expanded));
	        collapseButton.setAttribute('aria-label', expanded ? 'Collapse download queue' : 'Expand download queue');
	        collapseButton.title = expanded ? 'Collapse download queue' : 'Expand download queue';
	      });
	      tray.addEventListener('pointerdown', event => event.stopPropagation());
	      document.documentElement.appendChild(tray);
	    }
	  }

	  function applyTheme(theme) {
	    if (!theme || typeof theme !== 'object') return;
	    const root = document.documentElement;
	    const values = {
	      '--isg-panel': theme.panel,
	      '--isg-border': theme.border,
	      '--isg-ink': theme.ink,
	      '--isg-muted': theme.muted,
	      '--isg-accent': theme.accent,
	      '--isg-accent2': theme.accent2,
	      '--isg-button-bg': theme.buttonBg,
	      '--isg-button-ink': theme.buttonInk,
	    };
	    for (const [name, value] of Object.entries(values)) {
	      if (typeof value === 'string' && value.length < 600) root.style.setProperty(name, value);
	    }
	  }

	  function showNotice(message) {
	    ensureUi();
	    let notice = document.getElementById('inst-sanity-notice');
	    if (!notice) {
	      notice = document.createElement('div');
	      notice.id = 'inst-sanity-notice';
	      notice.setAttribute('role', 'status');
	      document.documentElement.appendChild(notice);
	    }
	    notice.textContent = safeText(message, '');
	    notice.dataset.show = 'true';
	    clearTimeout(notice.__hideTimer);
	    notice.__hideTimer = setTimeout(() => { notice.dataset.show = 'false'; }, 2600);
	  }

	  function renderState(snapshot, error) {
	    ensureUi();
	    if (snapshot && typeof snapshot === 'object') {
	      state.items = Array.isArray(snapshot.items) ? snapshot.items : [];
	      state.isDownloading = snapshot.isDownloading === true;
	      state.currentIndex = Number(snapshot.currentIndex) || 0;
	      state.total = Number(snapshot.total) || 0;
	      state.status = safeText(snapshot.status, '');
	    }
	    if (snapshot?.theme) { state.theme = snapshot.theme; applyTheme(snapshot.theme); }

	    const selected = new Set(state.items.map(itemKey));
	    if (activeCandidate && selectButton) {
	      const key = itemKey(activeCandidate);
	      const isSelected = selected.has(key);
	      selectButton.dataset.selected = String(isSelected);
	      selectButton.textContent = isSelected ? '✓ Queued' : '+ Add';
	    }

	    const hasItems = state.items.length > 0;
	    tray.dataset.open = String(hasItems || state.isDownloading);
	    tray.querySelector('.isg-count').textContent = String(state.items.length);
	    const start = tray.querySelector('.isg-start');
	    start.textContent = state.isDownloading
	      ? `Cancel ${state.currentIndex}/${state.total || state.items.length}`
	      : state.items.some(item => item.error) ? `Retry ${state.items.length}` : `Save ${state.items.length}`;
	    start.disabled = !state.isDownloading && !hasItems;
	    const clear = tray.querySelector('.isg-clear');
	    clear.disabled = state.isDownloading || !hasItems;

	    const list = tray.querySelector('.isg-items');
	    list.replaceChildren();
	    if (!hasItems && state.isDownloading) {
	      const empty = document.createElement('div'); empty.className = 'isg-empty'; empty.textContent = 'Finishing the current download…'; list.appendChild(empty);
	    } else if (!hasItems) {
	      const empty = document.createElement('div'); empty.className = 'isg-empty'; empty.textContent = 'Hover over a post photo or video and add it to the queue.'; list.appendChild(empty);
	    } else {
	      const photoItems = state.items.filter(item => item.isVideo !== true);
	      const videoItems = state.items.filter(item => item.isVideo === true);
	      for (const [label, items] of [['Photos', photoItems], ['Videos', videoItems]]) {
	        if (!items.length) continue;
	        const group = document.createElement('section'); group.className = 'isg-group';
	        const heading = document.createElement('div'); heading.className = 'isg-group-heading'; heading.textContent = `${label} (${items.length})`;
	        const rows = document.createElement('div'); rows.className = 'isg-group-items';
	        for (const item of items) {
	          const row = document.createElement('div'); row.className = 'isg-item';
	          if (item.thumbnailUrl) {
	            const image = document.createElement('img'); image.className = 'isg-thumb'; image.alt = ''; image.loading = 'lazy'; image.src = item.thumbnailUrl; row.appendChild(image);
	          } else {
	            const fallback = document.createElement('div'); fallback.className = 'isg-thumb isg-thumb-fallback'; fallback.textContent = item.isVideo ? 'VIDEO' : 'PHOTO'; row.appendChild(fallback);
	          }
	          const info = document.createElement('div'); info.className = 'isg-info';
	          const name = document.createElement('div'); name.className = 'isg-name'; name.textContent = item.filename || `${item.shortcode} · ${item.isVideo ? 'Video' : 'Photo'} ${Number(item.slideIndex) + 1}`;
	          const meta = document.createElement('div'); meta.className = 'isg-meta'; meta.textContent = `${item.isVideo ? 'Video' : 'Photo'} · ${item.shortcode} · slide ${Number(item.slideIndex) + 1}`;
	          info.append(name, meta);
	          if (item.error) { const failure = document.createElement('div'); failure.className = 'isg-error'; failure.textContent = safeText(item.error, 'Download failed'); info.appendChild(failure); }
	          row.appendChild(info); rows.appendChild(row);
	        }
	        group.append(heading, rows); list.appendChild(group);
	      }
	    }
	    tray.querySelector('.isg-footer').textContent = state.isDownloading
	      ? state.status || 'Downloading in order…'
	      : hasItems ? state.status || 'Photos and videos are saved in the folders selected in Settings.' : '';

	    if (error) showNotice(errorMessage(error));
	  }

	  function errorMessage(code) {
	    if (String(code).startsWith('invalid_media_')) return 'That media item could not be selected.';
	    return ({
	      queue_full: 'The queue is full. Save or clear items before adding more.',
	      queue_busy: 'Wait for the current queue download to finish.',
	      queue_empty: 'Add at least one photo or video first.',
	      invalid_media: 'That media item could not be selected.',
	      untrusted_source: 'Media selection is only available on Instagram pages.',
	      bridge_unavailable: 'Could not reach the download service. Your selections are still queued.'
	    })[String(code)] || 'Could not update the media queue.';
	  }

	  function parseShortcode(url) {
	    const value = String(url || '');
	    if (!value) return '';
	    try {
	      const parsed = new URL(value, location.href);
	      const match = parsed.pathname.match(postPattern);
	      return match ? match[1] : '';
	    } catch (_) { return ''; }
	  }

	  function hasPostPermalink(element) {
	    if (!element) return false;
	    try {
	      if (element.matches?.('a[href]') && parseShortcode(element.href)) return true;
	      return Array.from(element.querySelectorAll?.('a[href]') || []).some(link => !!parseShortcode(link.href));
	    } catch (_) { return false; }
	  }

	  function postRootFor(element) {
	    if (!(element instanceof Element)) return document.body;
	    const article = element.closest('article,[role="article"]');
	    if (article) return article;
	    const dialog = element.closest('[role="dialog"]');
	    const main = element.closest('main');
	    const boundary = dialog || main || document.body;

	    // Some Instagram feed layouts no longer use <article>. Find the nearest
	    // ancestor that owns a post permalink rather than treating the whole <main>
	    // feed (or an entire post dialog) as one post.
	    for (let ancestor = element.parentElement; ancestor && ancestor !== boundary && ancestor !== document.body; ancestor = ancestor.parentElement) {
	      if (!ancestor.matches('a[href]') && hasPostPermalink(ancestor)) return ancestor;
	    }
	    return boundary;
	  }

	  function findPostShortcode(media, root) {
	    const pageShortcode = parseShortcode(location.pathname);
	    if (pageShortcode) return pageShortcode;
	    const nearby = media.closest('a[href]');
	    const direct = nearby && parseShortcode(nearby.href);
	    if (direct) return direct;
	    for (const link of root.querySelectorAll('a[href]')) {
	      const shortcode = parseShortcode(link.href);
	      if (shortcode) return shortcode;
	    }
	    return parseShortcode(location.pathname);
	  }

	  function findPostPermalink(media, root, shortcode) {
	    const nearby = media.closest('a[href]');
	    if (nearby && parseShortcode(nearby.href) === shortcode) return nearby.href;
	    for (const link of root.querySelectorAll('a[href]')) {
	      if (parseShortcode(link.href) === shortcode) return link.href;
	    }
	    return parseShortcode(location.pathname) === shortcode ? location.origin + location.pathname : '';
	  }

	  function mediaVisibility(element) {
	    const rect = element.getBoundingClientRect();
	    const geometry = {
	      width: Math.round(rect.width), height: Math.round(rect.height),
	      left: Math.round(rect.left), top: Math.round(rect.top),
	      right: Math.round(rect.right), bottom: Math.round(rect.bottom),
	    };
	    if (rect.width < 90 || rect.height < 90) return { reason: 'below-hover-size', geometry };
	    if (rect.bottom <= 0 || rect.right <= 0 || rect.top >= innerHeight || rect.left >= innerWidth) return { reason: 'outside-viewport', geometry };
	    const style = getComputedStyle(element);
	    if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity || 1) <= 0.05) {
	      return { reason: 'hidden-by-style', geometry, display: style.display, visibility: style.visibility, opacity: style.opacity };
	    }
	    return { reason: 'visible', geometry };
	  }

	  function trustedMediaUrl(value) {
	    const raw = String(value || '').trim();
	    if (!raw || raw.length > 4096) return '';
	    try {
	      const url = new URL(raw, location.href);
	      if (url.protocol !== 'https:' || url.username || url.password || (url.port && url.port !== '443') || /\.(?:m3u8|mpd)$/i.test(url.pathname)) return '';
	      const host = url.hostname.toLowerCase().replace(/\.$/, '');
	      const allowedHost = host === 'instagram.com' || host.endsWith('.instagram.com') ||
	        host === 'fbcdn.net' || host.endsWith('.fbcdn.net') ||
	        host === 'cdninstagram.com' || host.endsWith('.cdninstagram.com');
	      return allowedHost ? url.href : '';
	    } catch (_) { return ''; }
	  }

	  function mediaUrl(element, isVideo) {
	    const sources = [];
	    const addSource = value => { if (typeof value === 'string' && value.trim()) sources.push(value); };
	    addSource(element.currentSrc);
	    addSource(element.getAttribute?.('src'));
	    addSource(element.src);
	    if (isVideo) {
	      for (const attribute of ['data-video-url', 'data-raw-video', 'data-src']) addSource(element.getAttribute?.(attribute));
	      let sourceElements = [];
	      try { sourceElements = Array.from(element.querySelectorAll('source[src],source[data-src]')); } catch (_) { }
	      for (const source of sourceElements) {
	        addSource(source.getAttribute?.('src'));
	        addSource(source.getAttribute?.('data-src'));
	        addSource(source.src);
	      }
	    }
	    for (const source of sources) {
	      const safe = trustedMediaUrl(source);
	      if (safe) return safe;
	    }
	    return '';
	  }

	  function carouselInfo(root) {
	    const labels = Array.from(root.querySelectorAll('button[aria-label], [role="button"][aria-label]'))
	      .map(button => ({ button, match: String(button.getAttribute('aria-label') || '').match(/(?:go to|slide)\s*(?:slide\s*)?(\d+)/i) }))
	      .filter(value => value.match);
	    const count = labels.reduce((maximum, value) => Math.max(maximum, Number(value.match[1]) || 0), 0) || null;
	    const active = labels.find(value => { const current = value.button.getAttribute('aria-current'); return (current && current !== 'false') || value.button.getAttribute('aria-pressed') === 'true' || value.button.getAttribute('aria-selected') === 'true' || value.button.dataset.active === 'true'; });
	    return { count, activeIndex: active ? Math.max(0, Number(active.match[1]) - 1) : null };
	  }

	  function slideTrackingKey(root, shortcode) {
	    const view = parseShortcode(location.pathname) === shortcode ? 'opened-route'
	      : root?.closest?.('[role="dialog"]') ? 'opened-dialog' : 'inline';
	    return `${view}|${shortcode}`;
	  }

	  function slideMapFor(root) {
	    let map = slideByRoot.get(root);
	    if (!map) { map = new Map(); slideByRoot.set(root, map); }
	    return map;
	  }

	  function currentSlideIndex(root, shortcode, carousel) {
	    const tracked = slideByRoot.get(root)?.get(slideTrackingKey(root, shortcode));
	    if (Number.isInteger(tracked)) return tracked;
	    return Number.isInteger(carousel.activeIndex) ? carousel.activeIndex : 0;
	  }

	  function trackSlide(root, shortcode, index) {
	    slideMapFor(root).set(slideTrackingKey(root, shortcode), index);
	  }

	  function slideInfo(root, shortcode) {
	    const carousel = carouselInfo(root);
	    let index = currentSlideIndex(root, shortcode, carousel);
	    if (!Number.isFinite(index) || index < 0) index = 0;
	    if (carousel.count) index = Math.min(index, carousel.count - 1);
	    return { slideIndex: index, contentCount: carousel.count };
	  }

	  function candidateFor(media, diagnostics = null) {
	    const reject = reason => { if (diagnostics) diagnostics.reason = reason; return null; };
	    try {
	      if (!media) return reject('missing-media');
	      const tag = String(media.tagName || '').toUpperCase();
	      const isVideo = tag === 'VIDEO';
	      if (!isVideo && tag !== 'IMG') return reject('not-photo-or-video');
	      const visibility = mediaVisibility(media);
	      if (visibility.reason !== 'visible') return reject(visibility.reason || 'not-visible');
	      if (!isVideo && (media.naturalWidth < 160 || media.naturalHeight < 160)) return reject('image-too-small');
	      if (!isVideo && /profile|avatar|logo|icon/i.test(media.alt || '')) return reject('decorative-image');
	      if (isVideo && media.closest('#inst-sanity-tray')) return reject('queue-video');
	      const rawUrl = mediaUrl(media, isVideo);
	      if (!rawUrl && !isVideo) return reject('photo-source-unavailable');
	      if (rawUrl && (!/^https:\/\//i.test(rawUrl) || /\.m3u8(?:$|\?)/i.test(rawUrl))) return reject('unsupported-source');

	      const root = postRootFor(media);
	      const shortcode = findPostShortcode(media, root);
	      if (!shortcode) return reject('post-shortcode-missing');
	      const postUrl = findPostPermalink(media, root, shortcode);
	      if (!postUrl) return reject('post-permalink-missing');
	      const { slideIndex, contentCount } = slideInfo(root, shortcode);
	      const thumbnailUrl = isVideo ? (media.poster || '') : (media.currentSrc || media.src || '');
	      return {
	        shortcode,
	        postUrl,
	        mediaType: isVideo ? 'video' : 'photo',
	        slideIndex,
	        contentCount,
	        rawUrl,
	        thumbnailUrl: /^https:\/\//i.test(thumbnailUrl) ? thumbnailUrl : '',
	      };
	    } catch (_) { return reject('candidate-exception'); }
	  }

	  function diagnosticRootId(root) {
	    if (!(root instanceof Element)) return 0;
	    let id = diagnosticRootIds.get(root);
	    if (!id) { id = nextDiagnosticRootId++; diagnosticRootIds.set(root, id); }
	    return id;
	  }

	  function rememberScrollPositions(element) {
	    for (let ancestor = element; ancestor instanceof Element; ancestor = ancestor.parentElement) {
	      scrollOffsets.set(ancestor, { left: Number(ancestor.scrollLeft) || 0, top: Number(ancestor.scrollTop) || 0 });
	    }
	  }

	  function positionButton(media, candidate, forcePosition = false, reason = 'candidate') {
	    if (!selectButton || !media || !candidate) return;
	    const hasSamePostAnchor = buttonAnchorShortcode === candidate.shortcode && selectButton.style.left && selectButton.style.top;
	    if (forcePosition || !hasSamePostAnchor) {
	      const rect = media.getBoundingClientRect();
	      const width = selectButton.offsetWidth || 134;
	      const left = Math.max(8, Math.min(innerWidth - width - 8, rect.right - width - 10));
	      const top = Math.max(8, Math.min(innerHeight - 42, rect.top + 10));
	      const anchorChanged = buttonAnchorShortcode !== candidate.shortcode;
	      const positionChanged = selectButton.style.left !== `${left}px` || selectButton.style.top !== `${top}px`;
	      selectButton.style.left = `${left}px`;
	      selectButton.style.top = `${top}px`;
	      buttonAnchorShortcode = candidate.shortcode;
	      const now = Date.now();
	      if (anchorChanged || positionChanged) {
	        if (reason !== 'scroll' || now - lastPositionLogAt >= 900) {
	          lastPositionLogAt = now;
	          debugLog('selector-positioned', {
	            reason,
	            shortcodeLength: String(candidate.shortcode || '').length,
	            mediaType: candidate.mediaType,
	            slideIndex: candidate.slideIndex,
	            left: Math.round(left),
	            top: Math.round(top),
	          });
	        }
	      }
	    }
	    buttonAnchorSlideIndex = Number.isInteger(candidate.slideIndex) ? candidate.slideIndex : null;
	    selectButton.style.display = 'inline-flex';
	    const key = itemKey(candidate);
	    const isSelected = state.items.some(item => itemKey(item) === key);
	    selectButton.dataset.selected = String(isSelected);
	    selectButton.textContent = isSelected ? '✓ Queued' : '+ Add';
	    activeCandidate = candidate;
	  }

	  function mediaContainsPoint(media, point, hitStack = null, eventTarget = null) {
	    if (!(media instanceof Element) || !point || !Number.isFinite(point.x) || !Number.isFinite(point.y)) return false;
	    try {
	      const rect = media.getBoundingClientRect();
	      if (point.x < rect.left || point.x > rect.right || point.y < rect.top || point.y > rect.bottom) return false;

	      let visibleOpacity = 1;
	      for (let ancestor = media; ancestor; ancestor = ancestor.parentElement) {
	        const style = getComputedStyle(ancestor);
	        if (style.display === 'none' || style.visibility === 'hidden' || style.visibility === 'collapse') return false;
	        const opacity = Number(style.opacity);
	        if (Number.isFinite(opacity)) visibleOpacity *= opacity;
	        if (visibleOpacity <= 0.05) return false;

	        if (ancestor === media || style.display === 'inline' || style.display === 'contents') continue;
	        const clipsX = ['hidden', 'clip', 'scroll', 'auto', 'overlay'].includes(style.overflowX);
	        const clipsY = ['hidden', 'clip', 'scroll', 'auto', 'overlay'].includes(style.overflowY);
	        if (!clipsX && !clipsY) continue;
	        const clipRect = ancestor.getBoundingClientRect();
	        const scaleX = ancestor.offsetWidth > 0 ? clipRect.width / ancestor.offsetWidth : 1;
	        const scaleY = ancestor.offsetHeight > 0 ? clipRect.height / ancestor.offsetHeight : 1;
	        const clipLeft = clipRect.left + (ancestor.clientLeft || 0) * scaleX;
	        const clipTop = clipRect.top + (ancestor.clientTop || 0) * scaleY;
	        const clipRight = clipLeft + (ancestor.clientWidth || 0) * scaleX;
	        const clipBottom = clipTop + (ancestor.clientHeight || 0) * scaleY;
	        if (clipsX && (point.x < clipLeft || point.x > clipRight)) return false;
	        if (clipsY && (point.y < clipTop || point.y > clipBottom)) return false;
	      }

	      const hits = Array.isArray(hitStack) ? hitStack : pointHitStack(point);
	      const hitInMedia = hits.some(hit => hit instanceof Element && (hit === media || media.contains(hit)));
	      const eventTargetsMedia = eventTarget instanceof Element && (eventTarget === media || media.contains(eventTarget) || eventTarget.contains(media));
	      return hits.length === 0 || hitInMedia || eventTargetsMedia;
	    } catch (_) { return false; }
	  }

	  function pointHitStack(point) {
	    if (!point || !Number.isFinite(point.x) || !Number.isFinite(point.y)) return [];
	    try {
	      if (typeof document.elementsFromPoint === 'function') {
	        const hits = document.elementsFromPoint(point.x, point.y);
	        if (hits?.length) return Array.from(hits);
	      }
	    } catch (_) { }
	    try {
	      const hit = document.elementFromPoint(point.x, point.y);
	      return hit ? [hit] : [];
	    } catch (_) { return []; }
	  }

	  function inspectMediaTarget(target, point = null) {
	    if (!(target instanceof Element) || target.closest('#inst-sanity-tray,#inst-sanity-select')) {
	      return { media: null, candidate: null };
	    }

	    let media = null;
	    let candidate = null;
	    const hits = pointHitStack(point);
	    const seen = new Set();
	    let mediaChecks = 0;
	    let hitTestRejected = 0;
	    let candidateRejected = 0;
	    const candidateRejectReasons = {};
	    const directMedia = target.closest('img,video');
	    const tryMedia = possibleMedia => {
	      if (!possibleMedia || seen.has(possibleMedia) || possibleMedia.closest('#inst-sanity-tray,#inst-sanity-select')) return false;
	      seen.add(possibleMedia);
	      mediaChecks++;
	      const eventTarget = possibleMedia === directMedia || possibleMedia.contains(target) || target.contains(possibleMedia) ? target : null;
	      if (!mediaContainsPoint(possibleMedia, point, hits, eventTarget)) { hitTestRejected++; return false; }
	      const candidateDiagnostics = {};
	      const found = candidateFor(possibleMedia, candidateDiagnostics);
	      if (!found) {
	        candidateRejected++;
	        const rejection = candidateDiagnostics.reason || 'unknown';
	        candidateRejectReasons[rejection] = (candidateRejectReasons[rejection] || 0) + 1;
	        return false;
	      }
	      media = possibleMedia;
	      candidate = found;
	      return true;
	    };

	    if (directMedia) tryMedia(directMedia);

	    if (!candidate) {
	      for (const hit of hits) {
	        if (tryMedia(hit?.closest?.('img,video'))) break;
	      }
	    }

	    const targetMedia = target.querySelectorAll('img,video');
	    if (!candidate && targetMedia.length <= 32) {
	      for (const descendant of targetMedia) {
	        if (tryMedia(descendant)) break;
	      }
	    }

	    const scope = target.closest('article,[role="article"]') ||
	      hits.map(hit => hit?.closest?.('article,[role="article"]')).find(Boolean) ||
	      postRootFor(directMedia || target);
	    const scopedMedia = scope?.querySelectorAll?.('img,video') || [];
	    if (!candidate && scope !== document.body && scopedMedia.length <= 48) {
	      for (const scoped of scopedMedia) {
	        if (tryMedia(scoped)) break;
	      }
	    }

	    const result = candidate ? 'candidate-found' : mediaChecks === 0 ? 'no-media-found' :
	      candidateRejected ? 'candidate-rejected' : hitTestRejected ? 'pointer-hit-rejected' : 'no-eligible-media';
	    const rootId = diagnosticRootId(scope);
	    const rejectionSignature = Object.keys(candidateRejectReasons).sort().join(',');
	    const inspectionIdentity = candidate ? itemKey(candidate) : `${result}|${rejectionSignature}`;
	    const signature = `${routeEpoch}|${rootId}|${inspectionIdentity}`;
	    let loggedSignatures = scope instanceof Element ? inspectionLogKeys.get(scope) : null;
	    if (!loggedSignatures) {
	      loggedSignatures = new Set();
	      if (scope instanceof Element) inspectionLogKeys.set(scope, loggedSignatures);
	    }
	    if (!loggedSignatures.has(signature)) {
	      if (loggedSignatures.size >= 128) loggedSignatures.clear();
	      loggedSignatures.add(signature);
	      debugLog('media-inspection', {
	        route: routeKind(),
	        rootId,
	        rootTag: String(scope?.tagName || 'unknown').toLowerCase(),
	        rootRole: safeText(scope?.getAttribute?.('role') || ''),
	        targetTag: String(target.tagName || 'unknown').toLowerCase(),
	        result,
	        mediaChecks,
	        hitTestRejected,
	        candidateRejected,
	        candidateRejectReasons,
	        hitStackCount: hits.length,
	        targetMediaCount: targetMedia.length,
	        scopedMediaCount: scopedMedia.length,
	        candidate: candidate ? {
	          mediaType: candidate.mediaType,
	          slideIndex: candidate.slideIndex,
	          shortcodeLength: String(candidate.shortcode || '').length,
	          postUrlMatchesShortcode: parseShortcode(candidate.postUrl) === candidate.shortcode,
	          sourceAvailable: !!candidate.rawUrl,
	        } : null,
	      });
	    }

	    return { media: candidate ? media : null, candidate };
	  }

	  function activateInspectedCandidate(inspection, forcePosition = false, reason = 'candidate') {
	    if (!inspection?.media || !inspection.candidate) return false;
	    const previous = activeCandidate;
	    const next = inspection.candidate;
	    const samePost = !!previous && previous.shortcode === next.shortcode;
	    const slideChanged = samePost && (previous.slideIndex !== next.slideIndex || itemKey(previous) !== itemKey(next));
	    const anchorWasAvailable = buttonAnchorShortcode === next.shortcode && !!selectButton?.style.left && !!selectButton?.style.top;
	    const previousAnchorSlideIndex = buttonAnchorSlideIndex;
	    const selectorWasVisible = selectButton?.style.display !== 'none';
	    const anchorSlideChanged = anchorWasAvailable && Number.isInteger(previousAnchorSlideIndex) && previousAnchorSlideIndex !== next.slideIndex;
	    clearTimer();
	    activeMedia = inspection.media;
	    activeCandidate = next;
	    rememberScrollPositions(activeMedia);
	    positionButton(activeMedia, activeCandidate, forcePosition, reason);
	    if ((slideChanged || anchorSlideChanged) && anchorWasAvailable && !forcePosition) {
	      debugLog('selector-anchor-preserved', {
	        mediaType: next.mediaType,
	        shortcodeLength: String(next.shortcode || '').length,
	        fromSlideIndex: Number.isInteger(previousAnchorSlideIndex) ? previousAnchorSlideIndex : previous?.slideIndex ?? null,
	        toSlideIndex: next.slideIndex,
	        selectorWasVisible,
	        left: selectButton?.style.left || '',
	        top: selectButton?.style.top || '',
	      });
	    }
	    return true;
	  }

	  function hideSelectSoon() {
	    clearTimer();
	    hideTimer = setTimeout(() => {
	      if (selectButton && !selectButton.matches(':hover')) {
	        selectButton.style.display = 'none'; activeMedia = null; activeCandidate = null;
	      }
	    }, 220);
	  }

	  function onPointerOver(event) {
	    lastPointer = { x: event.clientX, y: event.clientY };
	    if (event.target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) { clearTimer(); return; }
	    const inspection = inspectMediaTarget(event.target, lastPointer);
	    if (!activateInspectedCandidate(inspection) && activeMedia && !selectButton?.matches(':hover')) hideSelectSoon();
	  }

	  function relayStickerCursorToMainApp(event) {
	    const bridge = window.chrome?.webview;
	    if (typeof bridge?.postMessage !== 'function' || !Number.isFinite(event.clientX) || !Number.isFinite(event.clientY)) return;
	    pendingStickerCursor = { x: event.clientX, y: event.clientY };
	    if (stickerCursorFrame) return;
	    stickerCursorFrame = window.requestAnimationFrame(() => {
	      stickerCursorFrame = 0;
	      const point = pendingStickerCursor;
	      pendingStickerCursor = null;
	      const now = performance.now();
	      if (!point || now - lastStickerCursorSentAt < 30) return;
	      lastStickerCursorSentAt = now;
	      try { bridge.postMessage({ v: 1, action: 'cursor-position', x: point.x, y: point.y }); } catch (_) { }
	    });
	  }

	  function onPointerMove(event) {
	    lastPointer = { x: event.clientX, y: event.clientY };
	    relayStickerCursorToMainApp(event);
	    if (event.target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) return;
	    const now = Date.now();
	    if (now - lastMoveInspectionAt < 100) return;
	    lastMoveInspectionAt = now;
	    const inspection = inspectMediaTarget(event.target, lastPointer);
	    if (inspection.media && (inspection.media !== activeMedia || !activeCandidate ||
	        itemKey(inspection.candidate) !== itemKey(activeCandidate) || inspection.candidate.slideIndex !== activeCandidate.slideIndex)) {
	      activateInspectedCandidate(inspection);
	    } else if (!inspection.media && activeMedia && !selectButton?.matches(':hover')) hideSelectSoon();
	  }

	  function onPointerOut(event) {
	    if (event.target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) return;
	    if (activeMedia && (event.target === activeMedia || activeMedia.contains(event.target))) {
	      const next = event.relatedTarget;
	      if (next && (activeMedia.contains(next) || next === selectButton || selectButton?.contains(next))) return;
	      hideSelectSoon();
	    }
	  }

	  function updatePosition(reason = 'layout') {
	    if (activeMedia && activeCandidate && document.contains(activeMedia) && selectButton?.style.display !== 'none') {
	      if (lastPointer && !selectButton?.matches(':hover') && !mediaContainsPoint(activeMedia, lastPointer)) {
	        refreshMediaUnderPointer(true, reason);
	        return;
	      }
	      const candidate = candidateFor(activeMedia);
	      if (candidate) {
	        activeCandidate = candidate;
	        rememberScrollPositions(activeMedia);
	        positionButton(activeMedia, candidate, true, reason);
	      } else hideSelectSoon();
	    }
	  }

	  function onDocumentScroll(event) {
	    const target = event?.target;
	    if (target instanceof Element) {
	      const current = { left: Number(target.scrollLeft) || 0, top: Number(target.scrollTop) || 0 };
	      const previous = scrollOffsets.get(target);
	      scrollOffsets.set(target, current);
	      const deltaLeft = previous ? current.left - previous.left : 0;
	      const deltaTop = previous ? current.top - previous.top : 0;
	      if (previous && Math.abs(deltaLeft) > 0.5 && Math.abs(deltaTop) < 0.5) {
	        if (activeMedia && target.contains(activeMedia) && selectButton?.style.display !== 'none' && !horizontalScrollLogged.has(target)) {
	          horizontalScrollLogged.add(target);
	          debugLog('horizontal-scroll-anchor-held', {
	            containerId: diagnosticRootId(target),
	            containerTag: String(target.tagName || 'unknown').toLowerCase(),
	            deltaLeft: Math.round(deltaLeft),
	            shortcodeLength: String(activeCandidate?.shortcode || '').length,
	            slideIndex: activeCandidate?.slideIndex ?? null,
	          });
	        }
	        return;
	      }
	    }
	    if (selectButton?.style.display === 'none') {
	      buttonAnchorShortcode = '';
	      buttonAnchorSlideIndex = null;
	    }
	    updatePosition('scroll');
	  }

	  function refreshAfterRouteChange() {
	    if (location.pathname !== lastObservedRoute) {
	      lastObservedRoute = location.pathname;
	      routeEpoch++;
	      debugLog('route-changed', { route: routeKind() });
	    }
	    if (selectButton?.style.display === 'none') {
	      buttonAnchorShortcode = '';
	      buttonAnchorSlideIndex = null;
	    }
	    refreshMediaUnderPointer(true, 'route-change');
	  }

	  function refreshMediaUnderPointer(forcePosition = false, reason = 'refresh') {
	    if (!lastPointer) return;
	    const target = document.elementFromPoint(lastPointer.x, lastPointer.y);
	    if (target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) return;
	    const inspection = inspectMediaTarget(target, lastPointer);
	    if (!activateInspectedCandidate(inspection, forcePosition, reason) && activeMedia && !selectButton?.matches(':hover')) {
	      hideSelectSoon();
	    }
	  }

	  function updateCarouselIndex(event) {
	    const control = event.target?.closest?.('button,[role="button"]');
	    if (!control) return;
	    const label = String(control.getAttribute('aria-label') || '').toLowerCase();
	    const root = postRootFor(control);
	    const shortcode = parseShortcode(location.pathname) || findPostShortcode(control, root);
	    if (!shortcode) return;
	    const exact = label.match(/(?:go to|slide)\s*(?:slide\s*)?(\d+)/i);
	    if (exact) { trackSlide(root, shortcode, Math.max(0, Number(exact[1]) - 1)); return; }
	    const current = currentSlideIndex(root, shortcode, carouselInfo(root));
	    if (/next|forward/.test(label)) trackSlide(root, shortcode, Math.min(100, current + 1));
	    else if (/previous|prev|back/.test(label)) trackSlide(root, shortcode, Math.max(0, current - 1));
	  }

	  function receiveQueueState(snapshot, error, theme) {
	    debugLog('queue-state-received', {
	      itemCount: Array.isArray(snapshot?.items) ? snapshot.items.length : null,
	      isDownloading: snapshot?.isDownloading === true,
	      error: error ? String(error).slice(0, 80) : null,
	    });
	    const wasDownloading = state.isDownloading;
	    if (snapshot && theme) snapshot = { ...snapshot, theme };
	    renderState(snapshot, error);
	    if (!state.isDownloading && !state.items.length && state.status && (wasDownloading || /saved|cancelled/i.test(state.status))) showNotice(state.status);
	  }
	  function install() {
	    if (window.__instSanityMediaOverlayInstalled) {
	      debugLog('install-already-complete');
	      postMessage('overlay-ready');
	      return;
	    }
	    if (!document.documentElement) {
	      debugLog('install-waiting-for-dom', { readyState: document.readyState || 'unknown' });
	      if (!install.waitingForDom) {
	        install.waitingForDom = true;
	        document.addEventListener('DOMContentLoaded', () => {
	          debugLog('dom-content-loaded');
	          install.waitingForDom = false;
	          install();
	        }, { once: true });
	      }
	      return;
	    }
	    debugLog('install-start', { readyState: document.readyState || 'unknown' });
	    ensureUi();
	    window.__instSanityMediaOverlayInstalled = true;
	    window.__instSanityReceiveQueueState = receiveQueueState;
	    document.addEventListener('pointerover', onPointerOver, true);
	    document.addEventListener('pointermove', onPointerMove, { capture: true, passive: true });
	    document.addEventListener('pointerout', onPointerOut, true);
	    document.addEventListener('mouseover', onPointerOver, true);
	    document.addEventListener('mousemove', onPointerMove, { capture: true, passive: true });
	    document.addEventListener('mouseout', onPointerOut, true);
	    document.addEventListener('load', event => {
	      if (event.target?.matches?.('img,video')) refreshMediaUnderPointer(false, 'media-load');
	    }, true);
	    document.addEventListener('loadedmetadata', event => {
	      if (event.target?.tagName === 'VIDEO') refreshMediaUnderPointer(false, 'video-metadata');
	    }, true);
	    document.addEventListener('click', updateCarouselIndex, true);
	    document.addEventListener('scroll', onDocumentScroll, true);
	    window.addEventListener('resize', () => {
	      if (selectButton?.style.display === 'none') {
	        buttonAnchorShortcode = '';
	        buttonAnchorSlideIndex = null;
	      }
	      updatePosition('resize');
	    }, { passive: true });
	    window.addEventListener('popstate', refreshAfterRouteChange);
	    if (!routePollTimer) {
	      routePollTimer = window.setInterval(() => {
	        if (location.pathname === lastObservedRoute) return;
	        refreshAfterRouteChange();
	      }, 400);
	    }
	    const observer = new MutationObserver(() => {
	      if (mutationRefreshTimer) return;
	      mutationRefreshTimer = setTimeout(() => {
	        mutationRefreshTimer = 0;
	        if (!document.getElementById('inst-sanity-select')) { debugLog('overlay-ui-removed-reinstalling'); window.__instSanityMediaOverlayInstalled = false; install(); return; }
	        refreshMediaUnderPointer(false, 'mutation');
	      }, 80);
	    });
	    observer.observe(document.documentElement, {
	      childList: true, subtree: true, attributes: true,
	      attributeFilter: ['src', 'alt', 'aria-label', 'aria-current', 'aria-pressed', 'aria-selected', 'data-active']
	    });
	    debugLog('install-complete', {
	      buttonPresent: !!document.getElementById('inst-sanity-select'),
	      trayPresent: !!document.getElementById('inst-sanity-tray'),
	      stylePresent: !!document.getElementById('inst-sanity-overlay-style'),
	      bridgeAvailable: typeof window.chrome?.webview?.postMessage === 'function',
	      listeners: ['pointerover', 'pointermove', 'pointerout', 'mouseover', 'mousemove', 'mouseout', 'load', 'loadedmetadata'],
	    });
	    postMessage('overlay-ready');
	  }
	  window.__instSanityMediaOverlayInstall = install;
	  install();
	})();
	""";
}
