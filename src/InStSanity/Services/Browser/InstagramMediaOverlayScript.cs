namespace InStSanity.Services.Browser;

/// <summary>Injected into the native Instagram WebView2 to add media-select controls and the themed session queue.</summary>
internal static class InstagramMediaOverlayScript
{
	internal const string Source = """
	(() => {
	  const diagnosticPrefix = '[InStSanity:InstagramOverlay]';
	  const diagnosticElementIds = new WeakMap();
	  let nextDiagnosticElementId = 0;
	  let lastHoverDiagnosticKey = '';
	  let lastHoverDiagnosticAt = 0;
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
	  function diagnosticElementId(element) {
	    if (!element || typeof element !== 'object') return null;
	    let id = diagnosticElementIds.get(element);
	    if (!id) { id = ++nextDiagnosticElementId; diagnosticElementIds.set(element, id); }
	    return id;
	  }
	  function logHoverInspection(input, target, media, details) {
	    const reason = details?.reason || 'unknown';
	    const targetId = diagnosticElementId(target);
	    const mediaId = diagnosticElementId(media);
	    const key = `${targetId || 'none'}|${mediaId || 'none'}|${reason}`;
	    const now = Date.now();
	    if (key === lastHoverDiagnosticKey && now - lastHoverDiagnosticAt < 1500) return;
	    lastHoverDiagnosticKey = key;
	    lastHoverDiagnosticAt = now;
	    debugLog('hover-check', {
	      input,
	      route: routeKind(),
	      targetId,
	      targetTag: String(target?.tagName || target?.nodeName || 'unknown'),
	      targetClass: typeof target?.className === 'string' ? target.className.slice(0, 120) : '',
	      targetRole: String(target?.getAttribute?.('role') || ''),
	      mediaId,
	      mediaTag: media ? String(media.tagName || 'unknown') : null,
	      ...details,
	    });
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
	  const slideByPost = new Map();
	  let activeMedia = null;
	  let activeCandidate = null;
	  let hideTimer = 0;
	  let mutationRefreshTimer = 0;
	  let selectButton = null;
	  let tray = null;
	  let lastPointer = null;
	  let lastMoveInspectionAt = 0;

	  const css = `
	    #inst-sanity-select{position:fixed;z-index:2147483646;display:none;align-items:center;gap:7px;border:1px solid var(--isg-border,#ffffff55);border-radius:999px;padding:8px 12px;background:var(--isg-button-bg,linear-gradient(135deg,#99f6e4,#2dd4bf));color:var(--isg-button-ink,#091318);font:600 12px/1.1 "Segoe UI",sans-serif;box-shadow:0 5px 22px #0008;cursor:pointer;white-space:nowrap;backdrop-filter:blur(12px)}
	    #inst-sanity-select:hover{filter:brightness(1.08);transform:translateY(-1px)}
	    #inst-sanity-select[data-selected="true"]{background:var(--isg-panel,#151b23);color:var(--isg-ink,#f8fafc);border-color:var(--isg-accent,#99f6e4)}
	    #inst-sanity-tray{position:fixed;z-index:2147483645;right:16px;bottom:16px;width:min(380px,calc(100vw - 32px));max-height:min(440px,calc(100vh - 32px));display:none;flex-direction:column;overflow:hidden;border:1px solid var(--isg-border,#ffffff30);border-radius:16px;background:var(--isg-panel,rgba(17,24,32,.97));color:var(--isg-ink,#f8fafc);font:12px/1.45 "Segoe UI",sans-serif;box-shadow:0 14px 42px #0009;backdrop-filter:blur(18px)}
	    #inst-sanity-tray[data-open="true"]{display:flex}
	    #inst-sanity-tray *{box-sizing:border-box}
	    .isg-head{display:flex;align-items:center;gap:10px;padding:13px 14px 11px;border-bottom:1px solid var(--isg-border,#ffffff22)}
	    .isg-title{flex:1;min-width:0;font-size:13px;font-weight:700;letter-spacing:.15px}
	    .isg-count{display:inline-flex;align-items:center;justify-content:center;min-width:25px;height:23px;padding:0 7px;border-radius:999px;background:var(--isg-accent,#2dd4bf);color:var(--isg-button-ink,#091318);font-weight:750;font-size:11px}
	    .isg-actions{display:flex;gap:7px;align-items:center}
	    .isg-action{border:1px solid var(--isg-border,#ffffff30);border-radius:8px;padding:7px 10px;background:var(--isg-button-bg,linear-gradient(135deg,#99f6e4,#2dd4bf));color:var(--isg-button-ink,#091318);font:650 11px/1.2 "Segoe UI",sans-serif;cursor:pointer;white-space:nowrap}
	    .isg-action:disabled{opacity:.5;cursor:default}
	    .isg-action-secondary{background:transparent;color:var(--isg-ink,#f8fafc)}
	    .isg-items{display:flex;flex-direction:column;gap:7px;padding:10px 12px;overflow:auto;min-height:0}
	    .isg-item{display:flex;align-items:center;gap:10px;min-height:54px;padding:7px;border:1px solid var(--isg-border,#ffffff20);border-radius:11px;background:rgba(255,255,255,.035)}
	    .isg-thumb{width:40px;height:40px;flex:0 0 40px;border-radius:7px;object-fit:cover;background:rgba(255,255,255,.08)}
	    .isg-thumb-fallback{display:grid;place-items:center;color:var(--isg-accent2,var(--isg-accent,#99f6e4));font-size:10px;font-weight:800;letter-spacing:.3px}
	    .isg-info{min-width:0;flex:1}
	    .isg-name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:650}
	    .isg-meta{margin-top:2px;color:var(--isg-muted,#9fb1c1);font-size:10px}
	    .isg-error{margin-top:2px;color:#ff9a9a;font-size:10px}
	    .isg-empty{padding:8px 4px 10px;color:var(--isg-muted,#9fb1c1);font-size:11px}
	    .isg-footer{padding:8px 14px 12px;color:var(--isg-muted,#9fb1c1);font-size:10px}
	    #inst-sanity-notice{position:fixed;z-index:2147483647;left:50%;bottom:22px;max-width:min(460px,calc(100vw - 32px));transform:translate(-50%,12px);opacity:0;pointer-events:none;border:1px solid var(--isg-border,#ffffff30);border-radius:10px;padding:9px 13px;background:var(--isg-panel,rgba(17,24,32,.97));color:var(--isg-ink,#f8fafc);font:600 11px/1.4 "Segoe UI",sans-serif;box-shadow:0 7px 28px #0008;transition:opacity .18s,transform .18s}
	    #inst-sanity-notice[data-show="true"]{opacity:1;transform:translate(-50%,0)}
	    @media(max-width:540px){#inst-sanity-tray{right:8px;bottom:8px;width:calc(100vw - 16px)}#inst-sanity-select{padding:7px 10px;font-size:11px}}
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

	  function itemKey(item) { return `${item.shortcode}|${item.mediaType}|${item.slideIndex}`; }
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
	      selectButton.setAttribute('aria-label', 'Add media to In-st-Sanity download queue');
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
	      tray.innerHTML = '<div class="isg-head"><div class="isg-title">Download queue</div><span class="isg-count">0</span><div class="isg-actions"><button class="isg-action isg-start" type="button">Save</button><button class="isg-action isg-action-secondary isg-clear" type="button">Clear</button></div></div><div class="isg-items"></div><div class="isg-footer"></div>';
	      tray.querySelector('.isg-start').addEventListener('click', () => {
	        if (state.isDownloading) postMessage('cancel-download');
	        else postMessage('start-download');
	      });
	      tray.querySelector('.isg-clear').addEventListener('click', () => postMessage('clear-queue'));
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

	    const selected = new Set(state.items.map(item => item.key || itemKey({
	      shortcode: item.shortcode, mediaType: item.isVideo ? 'video' : 'photo', slideIndex: item.slideIndex
	    })));
	    if (activeCandidate && selectButton) {
	      const key = itemKey(activeCandidate);
	      const isSelected = selected.has(key);
	      selectButton.dataset.selected = String(isSelected);
	      selectButton.textContent = isSelected ? '✓ Queued' : '+ Add to queue';
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
	      for (const item of state.items) {
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
	        row.appendChild(info); list.appendChild(row);
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

	  function postRootFor(media) {
	    return media.closest('article') || media.closest('[role="dialog"]') || media.closest('main') || document.body;
	  }

	  function findPostShortcode(media, root) {
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

	  function mediaUrl(element, isVideo) {
	    if (isVideo) {
	      return element.currentSrc || element.src || element.querySelector('source[src]')?.src || '';
	    }
	    return element.currentSrc || element.src || '';
	  }

	  function slideInfo(root, shortcode, media) {
	    const labels = Array.from(root.querySelectorAll('button[aria-label], [role="button"][aria-label]'))
	      .map(button => ({ button, match: String(button.getAttribute('aria-label') || '').match(/(?:go to|slide)\s*(?:slide\s*)?(\d+)/i) }))
	      .filter(value => value.match);
	    const count = labels.reduce((maximum, value) => Math.max(maximum, Number(value.match[1]) || 0), 0) || null;
	    const active = labels.find(value => { const current = value.button.getAttribute('aria-current'); return (current && current !== 'false') || value.button.getAttribute('aria-pressed') === 'true' || value.button.getAttribute('aria-selected') === 'true' || value.button.dataset.active === 'true'; });
	    const tracked = slideByPost.get(shortcode);
	    let index = active ? Number(active.match[1]) - 1 : Number.isInteger(tracked) ? tracked : 0;
	    if (!Number.isFinite(index) || index < 0) index = 0;
	    if (count) index = Math.min(index, count - 1);
	    return { slideIndex: index, contentCount: count };
	  }

	  function candidateFor(media, diagnostic = null) {
	    const reject = (reason, details = {}) => {
	      if (diagnostic) Object.assign(diagnostic, { reason, ...details });
	      return null;
	    };
	    try {
	      if (!media) return reject('no-media-element');
	      const tag = String(media.tagName || '').toUpperCase();
	      const isVideo = tag === 'VIDEO';
	      if (!isVideo && tag !== 'IMG') return reject('unsupported-element', { tag });
	      const visibility = mediaVisibility(media);
	      if (visibility.reason !== 'visible') return reject(visibility.reason, visibility);
	      if (!isVideo && media.naturalWidth < 160) return reject('image-natural-width-too-small', { naturalWidth: media.naturalWidth, naturalHeight: media.naturalHeight, ...visibility.geometry });
	      if (!isVideo && media.naturalHeight < 160) return reject('image-natural-height-too-small', { naturalWidth: media.naturalWidth, naturalHeight: media.naturalHeight, ...visibility.geometry });
	      if (!isVideo && /profile|avatar|logo|icon/i.test(media.alt || '')) return reject('image-alt-excluded', { ...visibility.geometry });
	      if (isVideo && media.closest('#inst-sanity-tray')) return reject('video-inside-queue-ui');
	      const rawUrl = mediaUrl(media, isVideo);
	      if (!rawUrl) return reject('media-url-not-ready', { mediaType: isVideo ? 'video' : 'photo', ...visibility.geometry });
	      let mediaHost = '';
	      let mediaProtocol = '';
	      try { const parsed = new URL(rawUrl, location.href); mediaHost = parsed.host; mediaProtocol = parsed.protocol; } catch (_) { }
	      if (!/^https:\/\//i.test(rawUrl)) return reject('media-url-not-https', { mediaProtocol, mediaHost });
	      if (/\.m3u8(?:$|\?)/i.test(rawUrl)) return reject('hls-playlist-not-selectable', { mediaHost });

	      const root = postRootFor(media);
	      const shortcode = findPostShortcode(media, root);
	      if (!shortcode) return reject('post-shortcode-not-found', { mediaType: isVideo ? 'video' : 'photo', mediaHost, ...visibility.geometry });
	      const postUrl = findPostPermalink(media, root, shortcode);
	      if (!postUrl) return reject('post-permalink-not-found', { mediaType: isVideo ? 'video' : 'photo', mediaHost, ...visibility.geometry });
	      const { slideIndex, contentCount } = slideInfo(root, shortcode, media);
	      const thumbnailUrl = isVideo ? (media.poster || '') : (media.currentSrc || media.src || '');
	      if (diagnostic) Object.assign(diagnostic, {
	        reason: 'eligible', mediaType: isVideo ? 'video' : 'photo', mediaHost,
	        slideIndex, contentCount, ...visibility.geometry,
	      });
	      return {
	        shortcode,
	        postUrl,
	        mediaType: isVideo ? 'video' : 'photo',
	        slideIndex,
	        contentCount,
	        rawUrl,
	        thumbnailUrl: /^https:\/\//i.test(thumbnailUrl) ? thumbnailUrl : '',
	      };
	    } catch (error) {
	      return reject('candidate-evaluation-exception', {
	        errorName: String(error?.name || 'Error'),
	        errorMessage: String(error?.message || error).slice(0, 140),
	      });
	    }
	  }

	  function positionButton(media, candidate) {
	    if (!selectButton || !media || !candidate) return;
	    const rect = media.getBoundingClientRect();
	    const width = selectButton.offsetWidth || 134;
	    const left = Math.max(8, Math.min(innerWidth - width - 8, rect.right - width - 10));
	    const top = Math.max(8, Math.min(innerHeight - 42, rect.top + 10));
	    selectButton.style.left = `${left}px`;
	    selectButton.style.top = `${top}px`;
	    selectButton.style.display = 'inline-flex';
	    const key = itemKey(candidate);
	    const isSelected = state.items.some(item => (item.key || itemKey({ shortcode: item.shortcode, mediaType: item.isVideo ? 'video' : 'photo', slideIndex: item.slideIndex })) === key);
	    selectButton.dataset.selected = String(isSelected);
	    selectButton.textContent = isSelected ? '✓ Queued' : '+ Add to queue';
	    activeCandidate = candidate;
	  }

	  function mediaContainsPoint(media, point) {
	    if (!point || !Number.isFinite(point.x) || !Number.isFinite(point.y)) return true;
	    try {
	      const rect = media.getBoundingClientRect();
	      return point.x >= rect.left && point.x <= rect.right && point.y >= rect.top && point.y <= rect.bottom;
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

	  function inspectMediaTarget(target, input = 'unknown', point = null) {
	    let media = null;
	    let candidate = null;
	    const diagnostic = {};
	    if (!(target instanceof Element)) {
	      diagnostic.reason = 'event-target-not-element';
	    } else if (target.closest('#inst-sanity-tray,#inst-sanity-select')) {
	      diagnostic.reason = 'overlay-ui-target';
	    } else {
	      const seen = new Set();
	      let observedMedia = null;
	      let lastRejected = null;
	      const tryMedia = (possibleMedia, detectionPath) => {
	        if (!possibleMedia || seen.has(possibleMedia) || possibleMedia.closest('#inst-sanity-tray,#inst-sanity-select')) return false;
	        seen.add(possibleMedia);
	        observedMedia ||= possibleMedia;
	        const details = {};
	        const found = candidateFor(possibleMedia, details);
	        if (!found) { lastRejected ||= { details, detectionPath }; return false; }
	        media = possibleMedia;
	        candidate = found;
	        Object.assign(diagnostic, details, { detectionPath, searchedMediaCount: seen.size });
	        return true;
	      };

	      const directMedia = target.closest('img,video');
	      if (directMedia) tryMedia(directMedia, 'event-target-ancestor');

	      const hits = pointHitStack(point);
	      diagnostic.hitStackCount = hits.length;
	      diagnostic.hitStackTags = hits.slice(0, 6).map(hit => String(hit?.tagName || hit?.nodeName || 'unknown'));
	      if (!candidate) {
	        for (const hit of hits) {
	          if (tryMedia(hit?.closest?.('img,video'), 'elements-from-point')) break;
	        }
	      }

	      const targetMedia = target.querySelectorAll('img,video');
	      diagnostic.targetDescendantMediaCount = targetMedia.length;
	      if (!candidate && targetMedia.length <= 32) {
	        for (const descendant of targetMedia) {
	          if (mediaContainsPoint(descendant, point) && tryMedia(descendant, 'event-target-descendant')) break;
	        }
	      } else if (targetMedia.length > 32) diagnostic.targetDescendantSearchSkipped = 'too-many-media-elements';

	      const scope = target.closest('article,[role="dialog"],main') ||
	        hits.map(hit => hit?.closest?.('article,[role="dialog"],main')).find(Boolean) || document.body;
	      const scopedMedia = scope?.querySelectorAll?.('img,video') || [];
	      diagnostic.scopeTag = String(scope?.tagName || 'unknown');
	      diagnostic.scopeClass = typeof scope?.className === 'string' ? scope.className.slice(0, 120) : '';
	      diagnostic.scopeMediaCount = scopedMedia.length;
	      if (!candidate && scope !== document.body && scopedMedia.length <= 48) {
	        for (const scoped of scopedMedia) {
	          if (mediaContainsPoint(scoped, point) && tryMedia(scoped, 'post-scope-point-match')) break;
	        }
	      } else if (scopedMedia.length > 48) diagnostic.scopeSearchSkipped = 'too-many-media-elements';

	      if (!candidate) {
	        if (lastRejected) Object.assign(diagnostic, lastRejected.details, {
	          detectionPath: lastRejected.detectionPath,
	          searchedMediaCount: seen.size,
	        });
	        else Object.assign(diagnostic, {
	          reason: directMedia ? 'no-eligible-media-at-pointer' : 'no-img-video-at-pointer',
	          detectionPath: 'point-and-post-scope-search',
	          searchedMediaCount: seen.size,
	        });
	        observedMedia ||= directMedia;
	      }
	      media ||= observedMedia;
	    }
	    logHoverInspection(input, target, media, diagnostic);
	    return { media: candidate ? media : null, observedMedia: media, candidate, diagnostic };
	  }

	  function activateInspectedCandidate(inspection, input) {
	    if (!inspection?.media || !inspection.candidate) return false;
	    clearTimer();
	    activeMedia = inspection.media;
	    activeCandidate = inspection.candidate;
	    positionButton(activeMedia, activeCandidate);
	    debugLog('select-button-positioned', {
	      input,
	      mediaType: activeCandidate.mediaType,
	      slideIndex: activeCandidate.slideIndex,
	      visible: selectButton?.style.display === 'inline-flex',
	      left: selectButton?.style.left || '',
	      top: selectButton?.style.top || '',
	    });
	    return true;
	  }

	  function hideSelectSoon(reason = 'pointer-left-media') {
	    clearTimer();
	    debugLog('select-button-hide-scheduled', { reason, delayMs: 220 });
	    hideTimer = setTimeout(() => {
	      if (selectButton && !selectButton.matches(':hover')) {
	        selectButton.style.display = 'none'; activeMedia = null; activeCandidate = null;
	        debugLog('select-button-hidden', { reason });
	      } else {
	        debugLog('select-button-hide-cancelled', { reason, buttonHovered: !!selectButton?.matches(':hover') });
	      }
	    }, 220);
	  }

	  function onPointerOver(event) {
	    lastPointer = { x: event.clientX, y: event.clientY };
	    if (event.target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) { clearTimer(); return; }
	    const inspection = inspectMediaTarget(event.target, event.type, lastPointer);
	    activateInspectedCandidate(inspection, event.type);
	  }

	  function onPointerMove(event) {
	    lastPointer = { x: event.clientX, y: event.clientY };
	    if (event.target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) return;
	    const now = Date.now();
	    if (now - lastMoveInspectionAt < 100) return;
	    lastMoveInspectionAt = now;
	    const inspection = inspectMediaTarget(event.target, event.type, lastPointer);
	    if (inspection.media && inspection.media !== activeMedia) activateInspectedCandidate(inspection, event.type);
	  }

	  function onPointerOut(event) {
	    if (event.target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) return;
	    if (activeMedia && (event.target === activeMedia || activeMedia.contains(event.target))) {
	      const next = event.relatedTarget;
	      debugLog('hover-exit', {
	        input: event.type,
	        mediaTag: String(activeMedia.tagName || 'unknown'),
	        relatedTag: String(next?.tagName || next?.nodeName || 'none'),
	      });
	      if (next && (activeMedia.contains(next) || next === selectButton || selectButton?.contains(next))) return;
	      hideSelectSoon('pointer-left-media');
	    }
	  }

	  function updatePosition() {
	    if (activeMedia && activeCandidate && document.contains(activeMedia) && selectButton?.style.display !== 'none') {
	      const candidate = candidateFor(activeMedia);
	      if (candidate) { activeCandidate = candidate; positionButton(activeMedia, candidate); }
	      else hideSelectSoon('candidate-lost-during-position-update');
	    }
	  }

	  function refreshMediaUnderPointer(input = 'refresh') {
	    if (!lastPointer) { debugLog('media-refresh-skipped', { input, reason: 'no-pointer-position' }); return; }
	    const target = document.elementFromPoint(lastPointer.x, lastPointer.y);
	    if (target?.closest?.('#inst-sanity-tray,#inst-sanity-select')) return;
	    const inspection = inspectMediaTarget(target, input, lastPointer);
	    if (!activateInspectedCandidate(inspection, input) && activeMedia && !selectButton?.matches(':hover')) {
	      hideSelectSoon(`no-eligible-media-at-pointer:${input}`);
	    }
	  }

	  function updateCarouselIndex(event) {
	    const control = event.target?.closest?.('button,[role="button"]');
	    if (!control) return;
	    const label = String(control.getAttribute('aria-label') || '').toLowerCase();
	    const root = control.closest('article,[role="dialog"],main') || document.body;
	    const shortcode = findPostShortcode(control, root) || parseShortcode(location.pathname);
	    if (!shortcode) return;
	    const exact = label.match(/(?:go to|slide)\s*(?:slide\s*)?(\d+)/i);
	    if (exact) { slideByPost.set(shortcode, Math.max(0, Number(exact[1]) - 1)); return; }
	    const current = Number.isInteger(slideByPost.get(shortcode)) ? slideByPost.get(shortcode) : 0;
	    if (/next|forward/.test(label)) slideByPost.set(shortcode, Math.min(100, current + 1));
	    else if (/previous|prev|back/.test(label)) slideByPost.set(shortcode, Math.max(0, current - 1));
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
	      if (event.target?.matches?.('img,video')) refreshMediaUnderPointer('media-load');
	    }, true);
	    document.addEventListener('loadedmetadata', event => {
	      if (event.target?.tagName === 'VIDEO') refreshMediaUnderPointer('video-metadata');
	    }, true);
	    document.addEventListener('click', updateCarouselIndex, true);
	    document.addEventListener('scroll', updatePosition, true);
	    window.addEventListener('resize', updatePosition, { passive: true });
	    const observer = new MutationObserver(() => {
	      if (mutationRefreshTimer) return;
	      mutationRefreshTimer = setTimeout(() => {
	        mutationRefreshTimer = 0;
	        if (!document.getElementById('inst-sanity-select')) { debugLog('overlay-ui-removed-reinstalling'); window.__instSanityMediaOverlayInstalled = false; install(); return; }
	        refreshMediaUnderPointer('dom-mutation');
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
