#!/usr/bin/env python3
import concurrent.futures
import datetime
import hashlib
import html
import json
import os
import re
import subprocess
import threading
import time
import urllib.parse
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from ig_browser_engine import (
    BROWSER_MGR,
    extract_photos_with_session,
    extract_scontent_photos_from_html,
    load_ig_session,
)

WORKSPACE = Path(os.environ.get("STILL_SANE_HOME") or Path(__file__).resolve().parent)
INDEX_FILE = WORKSPACE / "index.html"
CACHE_FILE = WORKSPACE / "resolved_posts_cache.json"
IMG_CACHE_DIR = Path("/tmp/ig_image_cache")
IMG_CACHE_DIR.mkdir(parents=True, exist_ok=True)
CACHE_LOCK = threading.Lock()
IN_FLIGHT_LOCK = threading.Lock()
IN_FLIGHT_EVENTS: dict[str, threading.Event] = {}
RESOLVE_POOL = concurrent.futures.ThreadPoolExecutor(max_workers=24)
MEM_CACHE: dict = {
    "by_shortcode": {},
    "summaries_by_shortcode": {},
    "imported_entries": [],
    "last_file_name": "",
}

BOT_UAS = [
    "Twitterbot/1.0",
    "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)",
]


def shortcode_to_timestamp(sc: str) -> float | None:
    alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
    if not sc or len(sc) < 10:
        return None
    media_id = 0
    for ch in sc[:11]:
        idx = alphabet.find(ch)
        if idx == -1:
            return None
        media_id = media_id * 64 + idx
    ts_sec = ((media_id >> 23) + 1314220021721) / 1000.0
    return ts_sec if 1286323200 < ts_sec < 2200000000 else None


def init_cache() -> None:
    global MEM_CACHE
    if CACHE_FILE.exists():
        try:
            raw = json.loads(CACHE_FILE.read_text(encoding="utf-8"))
            if isinstance(raw, dict):
                by_sc = raw.get("by_shortcode") or {}
                cleaned = {}
                for k, v in by_sc.items():
                    if not isinstance(v, dict):
                        continue
                    pics = v.get("pictures") or []
                    if v.get("fullResolved") and v.get("sourceKind") and any(p.get("imgUrl") for p in pics if isinstance(p, dict)):
                        cleaned[k] = v
                raw_sum = raw.get("summaries_by_shortcode") or {}
                cleaned_sum = {
                    k: v
                    for k, v in raw_sum.items()
                    if isinstance(v, dict) and v.get("summaryResolved") and v.get("sourceKind")
                }
                cleaned_owners = {
                    str(k): v
                    for k, v in (raw.get("owners_by_shortcode") or {}).items()
                    if isinstance(v, dict) and (v.get("owner") or v.get("ownerFull"))
                }
                MEM_CACHE = {
                    "by_shortcode": cleaned,
                    "summaries_by_shortcode": cleaned_sum,
                    "owners_by_shortcode": cleaned_owners,
                    "imported_entries": raw.get("imported_entries") or [],
                    "last_file_name": raw.get("last_file_name") or "",
                }
        except Exception:
            pass


def persist_cache() -> None:
    try:
        CACHE_FILE.write_text(json.dumps(MEM_CACHE, ensure_ascii=False), encoding="utf-8")
    except Exception:
        pass


init_cache()


def fetch_text(url: str, ua: str = "Twitterbot/1.0", timeout: int = 6, max_bytes: int = 0) -> str:
    try:
        req = urllib.request.Request(
            url,
            headers={
                "User-Agent": ua,
                "Accept-Language": "en-US,en;q=0.9",
            },
        )
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read(max_bytes) if max_bytes > 0 else resp.read()
            return raw.decode("utf-8", errors="ignore")
    except Exception:
        return ""


def fetch_embed_html(shortcode: str) -> str:
    url = f"https://www.instagram.com/p/{shortcode}/embed/captioned/"
    return fetch_text(url, ua=BOT_UAS[0], timeout=5)


def fetch_page_html(shortcode: str) -> str:
    url = f"https://www.instagram.com/p/{shortcode}/"
    for ua in BOT_UAS:
        txt = fetch_text(url, ua=ua, timeout=5, max_bytes=98304)
        if txt and ("og:title" in txt or "og:image" in txt or "taken_at" in txt):
            return txt
    return ""


def decode_hunter_js(js: str) -> str:
    m = re.search(r'\}\("([^"]+)",(\d+),"([^"]+)",(\d+),(\d+),(\d+)\)\)', js or "")
    if not m:
        return ""
    h, _u, n, t, e, _r = m.group(1), int(m.group(2)), m.group(3), int(m.group(4)), int(m.group(5)), int(m.group(6))
    alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+/"

    def conv(d: str, base_from: int, base_to: int) -> str:
        g = alphabet[:base_from]
        i = alphabet[:base_to]
        j = 0
        for idx, ch in enumerate(reversed(d)):
            pos = g.find(ch)
            if pos != -1:
                j += pos * (base_from ** idx)
        if j == 0:
            return "0"
        k = []
        while j > 0:
            k.append(i[j % base_to])
            j //= base_to
        return "".join(reversed(k))

    res = []
    idx = 0
    if e >= len(n):
        return ""
    delim = n[e]
    while idx < len(h):
        next_d = h.find(delim, idx)
        if next_d == -1:
            break
        s = h[idx:next_d]
        for j_idx, ch in enumerate(n):
            s = s.replace(ch, str(j_idx))
        try:
            val = int(conv(s, e, 10)) - t
            res.append(chr(val))
        except Exception:
            pass
        idx = next_d + 1
    return "".join(res)


def is_video_cdn_url(url: str) -> bool:
    import base64
    if not url:
        return False
    u = str(url)
    if ".mp4" in u or "/o1/v/t" in u:
        return True
    m = re.search(r"[?&]efg=([^&\"'\s]+)", u)
    if m:
        raw_efg = urllib.parse.unquote(m.group(1))
        if any(tok in raw_efg for tok in ("dmlkZW8", "LnZpZGVv", "X3ZpZGVv", "Q0xJUFM", "IkNMSVBT")):
            return True
        try:
            padded = raw_efg + "=" * ((4 - len(raw_efg) % 4) % 4)
            dec = base64.urlsafe_b64decode(padded).decode("utf-8", errors="ignore").lower()
            if "video" in dec or "clips." in dec:
                return True
        except Exception:
            pass
    return False


def is_video_html(html: str) -> bool:
    if not html:
        return False
    return bool(
        re.search(
            r'PlayButtonSprite|WatchOnInstagram|EmbeddedMediaVideo|property=["\']og:video|content=["\']video|Instagram\s+reel|instagram\.com/(?:[^/"\'?#]+/)?(?:reel|reels|tv)/',
            html,
            re.I,
        )
    )


def fetch_via_savefree(shortcode: str) -> list:
    import base64
    ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
    p_url = f"https://www.instagram.com/p/{shortcode}/"
    data = urllib.parse.urlencode({"q": p_url, "t": "media", "lang": "en", "v": "v2"}).encode("utf-8")

    def extract_jwt_url(tok_url: str) -> str:
        jwt_m = re.search(r"token=([^.&]+)\.([^.&]+)\.", tok_url or "")
        if jwt_m:
            b64 = jwt_m.group(2) + "=" * ((4 - len(jwt_m.group(2)) % 4) % 4)
            try:
                payload = json.loads(base64.urlsafe_b64decode(b64).decode("utf-8"))
                return str(payload.get("url") or "")
            except Exception:
                pass
        return tok_url or ""

    for attempt in range(2):
        try:
            req = urllib.request.Request(
                "https://save-free.net/api/ajaxSearch",
                data=data,
                headers={
                    "User-Agent": ua,
                    "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                    "Origin": "https://save-free.net",
                    "Referer": "https://save-free.net/en",
                },
            )
            with urllib.request.urlopen(req, timeout=8) as resp:
                res = json.loads(resp.read().decode("utf-8", errors="ignore"))
            if res.get("status") != "ok" or not res.get("data"):
                if attempt == 0:
                    time.sleep(0.2)
                    continue
                return []
            raw_data = str(res.get("data") or "")
            decoded = decode_hunter_js(raw_data)
            if decoded:
                inner_m = re.search(r'innerHTML\s*=\s*"((?:\\.|[^"\\])*)"', decoded)
                inner_html = (
                    urllib.parse.unquote(json.loads('"' + inner_m.group(1) + '"'))
                    if inner_m
                    else urllib.parse.unquote(decoded)
                )
            else:
                inner_html = raw_data
            items = re.findall(r"<li>([\s\S]*?)</li>", inner_html)

            slides = []
            for item in items:
                opts = re.findall(r'<option value="([^"]+)">([^<]+)</option>', item)
                thumb_m = re.search(r'<img[^>]+src="([^"]+)"', item)
                dl_m = re.search(r'<a[^>]+href="([^"]+)"[^>]*class="[^"]*abutton', item)
                thumb_raw = extract_jwt_url(thumb_m.group(1) if thumb_m else "")
                best_tok = opts[0][0] if opts else (dl_m.group(1) if dl_m else (thumb_m.group(1) if thumb_m else ""))
                best_raw = extract_jwt_url(best_tok)
                is_vid = (
                    "icon-dlvideo" in item
                    or "Download Video" in item
                    or is_video_cdn_url(best_raw)
                    or is_video_cdn_url(thumb_raw)
                )
                img_url = thumb_raw if (is_vid and thumb_raw) else (best_raw or thumb_raw)
                vid_url = best_raw if is_vid else ""
                if img_url:
                    slides.append((img_url, is_vid, vid_url))
            if slides:
                return slides
        except Exception:
            if attempt == 0:
                time.sleep(0.2)
    return []


def find_polaris_media(obj):
    if isinstance(obj, list):
        for item in obj:
            res = find_polaris_media(item)
            if res:
                return res
        return None
    if not isinstance(obj, dict):
        return None
    web_items = ((obj.get("xdt_api__v1__media__shortcode__web_info") or {}).get("items")) or []
    if web_items and isinstance(web_items[0], dict):
        return web_items[0]
    if obj.get("__isXIGPolarisMedia") and obj.get("taken_at") and (
        obj.get("carousel_media") or obj.get("image_versions2") or obj.get("display_uri")
    ):
        return obj
    for val in obj.values():
        if isinstance(val, (dict, list)):
            res = find_polaris_media(val)
            if res:
                return res
    return None


def pick_best_polaris_image(item: dict) -> str:
    if not isinstance(item, dict):
        return ""
    cands = (item.get("image_versions2") or {}).get("candidates") or []
    if isinstance(cands, list) and cands:
        for c in cands:
            if isinstance(c, dict) and c.get("width") in (640, 720, 1080) and c.get("url"):
                return str(c["url"])
        if isinstance(cands[0], dict) and cands[0].get("url"):
            return str(cands[0]["url"])
    return str(item.get("display_uri") or item.get("display_url") or item.get("thumbnail_src") or "")


def parse_polaris_from_html(page_html: str):
    if not page_html:
        return None
    for m in re.finditer(r'<script[^>]*type=["\']application/json["\'][^>]*>([\s\S]*?)</script>', page_html, re.I):
        raw = m.group(1)
        if '"taken_at"' not in raw:
            continue
        if '"carousel_media"' not in raw and '"image_versions2"' not in raw and '"display_uri"' not in raw:
            continue
        try:
            found = find_polaris_media(json.loads(raw))
            if found:
                return found
        except Exception:
            pass
    return None


def extract_meta(page_html: str, prop: str) -> str:
    if not page_html:
        return ""
    m = re.search(
        rf'<meta[^>]+(?:property|name)=["\']{re.escape(prop)}["\'][^>]+content=["\']([^"\']*)["\']',
        page_html,
        re.I,
    )
    if not m:
        m = re.search(
            rf'<meta[^>]+content=["\']([^"\']*)["\'][^>]+(?:property|name)=["\']{re.escape(prop)}["\']',
            page_html,
            re.I,
        )
    return html.unescape(m.group(1)).strip() if m else ""


def extract_canonical_url(page_html: str) -> str:
    if not page_html:
        return ""
    m = re.search(r'<link[^>]+rel=["\']canonical["\'][^>]+href=["\']([^"\']+)["\']', page_html, re.I)
    if not m:
        m = re.search(r'<link[^>]+href=["\']([^"\']+)["\'][^>]+rel=["\']canonical["\']', page_html, re.I)
    return html.unescape(m.group(1)).strip() if m else ""


def format_relative_age(ts_sec: float) -> str:
    dt = datetime.datetime.fromtimestamp(ts_sec, tz=datetime.timezone.utc)
    now = datetime.datetime.now(datetime.timezone.utc)
    diff_sec = max(60.0, (now - dt).total_seconds())
    if diff_sec < 3600:
        mins = max(1, int(round(diff_sec / 60)))
        return f"{mins} minute{'' if mins == 1 else 's'} ago"
    if diff_sec < 86400:
        hrs = max(1, int(round(diff_sec / 3600)))
        return f"{hrs} hour{'' if hrs == 1 else 's'} ago"
    if diff_sec < 86400 * 14:
        days = max(1, int(round(diff_sec / 86400)))
        return f"{days} day{'' if days == 1 else 's'} ago"
    if diff_sec < 86400 * 60:
        wks = max(1, int(round(diff_sec / (86400 * 7))))
        return f"{wks} week{'' if wks == 1 else 's'} ago"
    mos = max(1, int(round(diff_sec / (86400 * 30))))
    return f"{mos} month{'' if mos == 1 else 's'} ago"


def parse_context_json(embed_html: str):
    if not embed_html:
        return None
    cm = re.search(r'"contextJSON"\s*:\s*"((?:\\.|[^"\\])*)"', embed_html)
    if not cm:
        return None
    try:
        ctx = json.loads(json.loads('"' + cm.group(1) + '"'))
        return (ctx.get("gql_data") or {}).get("shortcode_media") or None
    except Exception:
        return None


def extract_embed_fallback_image(embed_html: str) -> str:
    if not embed_html:
        return ""
    for tag in re.findall(r"<img[^>]+>", embed_html, re.I):
        if "EmbeddedMediaImage" in tag:
            sm = re.search(r'src=["\']([^"\']+)["\']', tag, re.I)
            if sm:
                return html.unescape(sm.group(1)).strip()
    return ""


def pick_best_node_image(node: dict) -> str:
    if not isinstance(node, dict):
        return ""
    resources = node.get("display_resources") or []
    for r in resources:
        if isinstance(r, dict) and r.get("config_width") == 640 and r.get("src"):
            return str(r["src"])
    if node.get("display_url"):
        return str(node["display_url"])
    if resources and isinstance(resources[-1], dict) and resources[-1].get("src"):
        return str(resources[-1]["src"])
    if node.get("thumbnail_src"):
        return str(node["thumbnail_src"])
    return ""


def resolve_instagram_post_summary(url: str) -> dict:
    return resolve_instagram_post(url, summary_only=True, persist_immediately=False)


def resolve_instagram_post(url: str, summary_only: bool = False, persist_immediately: bool = True) -> dict:
    url = (url or "").strip()
    m = re.search(
        r"instagram\.com/(?:share/)?(?:([^/?#]+)/)?(p|reel|reels|tv)/([^/?#]+)",
        url,
        re.I,
    )
    if not m:
        return {"url": url, "ok": False, "error": "Not a recognized Instagram post URL"}

    url_user, kind, shortcode = m.group(1) or "", m.group(2).lower(), m.group(3)
    if url_user.lower() == "share":
        url_user = ""

    def _get_valid_cached(sc: str):
        with CACHE_LOCK:
            by_sc = MEM_CACHE.get("by_shortcode", {})
            c = by_sc.get(sc) or by_sc.get(sc.lower())
            if c and c.get("fullResolved") and c.get("pictures"):
                pics = c.get("pictures") or []
                if all("/media/?size=" not in str(p.get("rawUrl") or p.get("imgUrl") or "") for p in pics):
                    for p in pics:
                        if is_video_cdn_url(str(p.get("rawUrl") or p.get("imgUrl") or "")):
                            p["isVideo"] = True
                    if any(p.get("isVideo") and not p.get("videoUrl") for p in pics):
                        if not summary_only:
                            return None
                    res = dict(c)
                    res["url"] = url
                    res["ok"] = True
                    return build_post_summary(res) if summary_only else res
            if summary_only:
                sum_by_sc = MEM_CACHE.get("summaries_by_shortcode", {})
                s = sum_by_sc.get(sc) or sum_by_sc.get(sc.lower())
                if isinstance(s, dict) and s.get("summaryResolved"):
                    res = dict(s)
                    res["url"] = url
                    res["ok"] = True
                    return res
        return None

    cached = _get_valid_cached(shortcode)
    if cached:
        return cached

    wait_event = None
    is_leader = False
    sc_key = f"sum:{shortcode.lower()}" if summary_only else shortcode.lower()
    with IN_FLIGHT_LOCK:
        if sc_key in IN_FLIGHT_EVENTS:
            wait_event = IN_FLIGHT_EVENTS[sc_key]
        else:
            wait_event = threading.Event()
            IN_FLIGHT_EVENTS[sc_key] = wait_event
            is_leader = True

    if not is_leader and wait_event is not None:
        wait_event.wait(timeout=12)
        cached = _get_valid_cached(shortcode)
        if cached:
            return cached

    try:
        f_embed = RESOLVE_POOL.submit(fetch_embed_html, shortcode)
        f_page = RESOLVE_POOL.submit(fetch_page_html, shortcode)
        f_sf = RESOLVE_POOL.submit(fetch_via_savefree, shortcode)
        f_ig_sess = (
            RESOLVE_POOL.submit(extract_photos_with_session, shortcode)
            if (not summary_only and kind not in ("reel", "reels", "tv"))
            else None
        )
        embed_html = f_embed.result()
        page_html = f_page.result()
        ig_sess_res = f_ig_sess.result() if f_ig_sess is not None else None

        og_title = extract_meta(page_html, "og:title")
        og_desc = extract_meta(page_html, "og:description") or extract_meta(page_html, "description")
        og_image = extract_meta(page_html, "og:image") or extract_meta(page_html, "twitter:image")
        og_url = extract_meta(page_html, "og:url") or extract_canonical_url(page_html)

        polaris_media = parse_polaris_from_html(page_html)
        media = parse_context_json(embed_html)
        has_sidecar = bool(media and (media.get("edge_sidecar_to_children") or {}).get("edges")) or bool(
            polaris_media and polaris_media.get("carousel_media")
        )
        if (not media and not polaris_media or not has_sidecar) and og_url:
            pm = re.search(r"instagram\.com/(?:[^/?#]+/)?(?:p|reel|reels|tv)/([^/?#]+)", og_url, re.I)
            if pm and pm.group(1).lower() != shortcode.lower():
                parent_sc = pm.group(1)
                parent_embed = fetch_embed_html(parent_sc)
                parent_media = parse_context_json(parent_embed)
                if parent_media:
                    media = parent_media
                    if parent_embed:
                        embed_html = parent_embed

        title = ""
        owner = url_user
        owner_full = ""
        likes = "—"
        date_str = "Recently"
        explicit_date = False
        saved_at_ms = int(datetime.datetime.now(datetime.timezone.utc).timestamp() * 1000)
        slides = []
        slide_origins = []

        def _sync_origins(default):
            while len(slide_origins) < len(slides):
                slide_origins.append(default)

        if page_html:
            dlg_m = re.search(
                r'"xig_logged_out_dynamic_dialog_info"\s*:\s*\{\s*"user"\s*:\s*\{([^}]+)\}',
                page_html,
            )
            if dlg_m:
                u_frag = dlg_m.group(1)
                un_m = re.search(r'"username"\s*:\s*"([^"]+)"', u_frag)
                fn_m = re.search(r'"full_name"\s*:\s*"((?:\\.|[^"\\])*)"', u_frag)
                if un_m and not owner:
                    owner = un_m.group(1).strip()
                if fn_m and not owner_full:
                    try:
                        owner_full = json.loads('"' + fn_m.group(1) + '"').strip()
                    except Exception:
                        owner_full = fn_m.group(1).strip()
            pk_m = re.search(r'"pk"\s*:\s*"(\d{15,22})"', page_html)
            if pk_m:
                try:
                    ts_pk = ((int(pk_m.group(1)) >> 23) + 1314220021721) / 1000.0
                    if ts_pk > 1200000000:
                        saved_at_ms = int(ts_pk * 1000)
                        date_str = format_relative_age(ts_pk)
                        explicit_date = True
                except Exception:
                    pass

        if og_title:
            tm = re.match(r'^(.*?)\s+on Instagram:\s*["“”](.*)["“”]$', og_title, re.S)
            if tm:
                if not owner_full:
                    owner_full = tm.group(1).strip()
                title = tm.group(2).strip()
            else:
                am = re.match(r"^(.*?)\s*\(@([A-Za-z0-9._]+)\)", og_title)
                if am:
                    if not owner_full:
                        owner_full = am.group(1).strip()
                    if not owner:
                        owner = am.group(2).strip()
                else:
                    on_ig = re.match(r"^(.*?)\s+on Instagram\s*$", og_title, re.I)
                    if on_ig and not owner_full:
                        owner_full = on_ig.group(1).strip()
                    title = og_title.strip()

        if og_desc:
            lm = re.search(r"([\d,.]+[kKmM]?)\s+likes?", og_desc, re.I)
            if lm:
                likes = lm.group(1).replace(",", "")
            um = re.search(
                r"-\s*([A-Za-z0-9._]+)\s+on\s+([A-Za-z]+\s+\d{1,2},\s+\d{4})(?::\s*[\"“”]([\s\S]*?)[\"“”]\.?\s*$)?",
                og_desc,
            )
            if um:
                if not owner:
                    owner = um.group(1)
                if date_str == "Recently":
                    date_str = um.group(2)
                    explicit_date = True
                if um.group(3) and (not title or title.endswith("on Instagram")):
                    title = um.group(3).strip()

        if not owner and embed_html:
            um_emb = re.search(r'class="UsernameText"[^>]*>([^<]+)<', embed_html)
            if um_emb:
                owner = html.unescape(um_emb.group(1)).strip()

        if polaris_media:
            p_user = polaris_media.get("user") or polaris_media.get("owner") or {}
            if p_user.get("username"):
                owner = str(p_user["username"])
            if p_user.get("full_name") and not owner_full:
                owner_full = str(p_user["full_name"])
            cap_obj = polaris_media.get("caption")
            if isinstance(cap_obj, dict) and cap_obj.get("text"):
                title = str(cap_obj["text"]).strip()
            like_cnt = polaris_media.get("like_count")
            if isinstance(like_cnt, int):
                likes = f"{like_cnt/1000:.1f}k".replace(".0k", "k") if like_cnt >= 1000 else str(like_cnt)
            ts = polaris_media.get("taken_at")
            if ts:
                saved_at_ms = int(float(ts) * 1000)
                date_str = format_relative_age(float(ts))
                explicit_date = True
            c_slides = polaris_media.get("carousel_media")
            p_items = c_slides if isinstance(c_slides, list) and c_slides else [polaris_media]
            for s_item in p_items:
                img_src = pick_best_polaris_image(s_item)
                v_vers = s_item.get("video_versions") if isinstance(s_item, dict) else None
                is_vid = bool(
                    isinstance(s_item, dict)
                    and (
                        s_item.get("media_type") == 2
                        or s_item.get("__typename") == "XIGPolarisVideoMedia"
                        or bool(v_vers)
                        or bool(s_item.get("is_video"))
                        or is_video_cdn_url(img_src)
                    )
                )
                vid_url = (
                    str(v_vers[0].get("url") or "")
                    if isinstance(v_vers, list) and v_vers and isinstance(v_vers[0], dict)
                    else ""
                )
                if img_src:
                    slides.append((img_src, is_vid, vid_url))

        if media and not slides:
            owner_obj = media.get("owner") or {}
            if owner_obj.get("username"):
                owner = owner_obj["username"]
            cap_edges = (media.get("edge_media_to_caption") or {}).get("edges") or []
            if cap_edges and cap_edges[0].get("node", {}).get("text"):
                title = cap_edges[0]["node"]["text"].strip()
            liked_by = (media.get("edge_liked_by") or {}).get("count")
            if isinstance(liked_by, int):
                likes = f"{liked_by/1000:.1f}k".replace(".0k", "k") if liked_by >= 1000 else str(liked_by)
            ts = media.get("taken_at_timestamp")
            if not ts and media.get("id"):
                try:
                    ts = ((int(media["id"]) >> 23) + 1314220021721) / 1000.0
                except Exception:
                    pass
            if ts:
                saved_at_ms = int(float(ts) * 1000)
                date_str = format_relative_age(float(ts))
                explicit_date = True

            sidecar = (media.get("edge_sidecar_to_children") or {}).get("edges") or []
            if sidecar:
                for edge in sidecar:
                    node = edge.get("node") or {}
                    img_src = pick_best_node_image(node)
                    if img_src:
                        slides.append((img_src, bool(node.get("is_video")) or is_video_cdn_url(img_src), str(node.get("video_url") or "")))
            else:
                img_src = pick_best_node_image(media)
                if img_src:
                    slides.append((img_src, bool(media.get("is_video")) or is_video_cdn_url(img_src), str(media.get("video_url") or "")))

        _sync_origins("direct")

        if ig_sess_res and isinstance(ig_sess_res, dict):
            if ig_sess_res.get("owner") and not owner:
                owner = str(ig_sess_res["owner"])
            if ig_sess_res.get("ownerFull") and not owner_full:
                owner_full = str(ig_sess_res["ownerFull"])
            if ig_sess_res.get("title") and not title:
                title = str(ig_sess_res["title"])
            if ig_sess_res.get("likes") and likes == "—":
                likes = str(ig_sess_res["likes"])
            sess_slides = ig_sess_res.get("slides") or []
            if sess_slides and not ig_sess_res.get("hasVideo") and len(sess_slides) >= len(slides):
                slides = sess_slides
                # authenticated scontent extraction is still Instagram's own data
                slide_origins = ["direct"] * len(slides)

        full_resolved = bool((polaris_media or media or (ig_sess_res and not ig_sess_res.get("hasVideo"))) and slides)
        needs_savefree = not full_resolved or any(is_v and not vid_u for _, is_v, vid_u in slides)
        sf_slides = f_sf.result() if needs_savefree else []
        if sf_slides:
            if not slides or len(sf_slides) > len(slides):
                slides = sf_slides
                # Instagram gave us nothing usable -> every slide came from save-free.net
                slide_origins = ["api"] * len(slides)
                full_resolved = True
            elif len(sf_slides) == len(slides):
                merged = []
                merged_origins = []
                for idx_s, (img_u, is_v, vid_u) in enumerate(slides):
                    sf_img, sf_is_v, sf_vid = sf_slides[idx_s]
                    merged.append((img_u or sf_img, is_v or sf_is_v, vid_u or sf_vid))
                    merged_origins.append("direct" if img_u else ("api" if sf_img else "direct"))
                slides = merged
                slide_origins = merged_origins
                full_resolved = True

        if not slides and kind not in ("reel", "reels", "tv"):
            direct_scontent = extract_scontent_photos_from_html((embed_html or "") + "\n" + (page_html or ""))
            if direct_scontent:
                slides = [(u, False, "") for u in direct_scontent]
                slide_origins = ["direct"] * len(slides)
                if len(direct_scontent) > 1:
                    full_resolved = True

        if not slides:
            emb_img = extract_embed_fallback_image(embed_html)
            is_vid_fallback = (
                kind in ("reel", "reels", "tv")
                or is_video_cdn_url(emb_img)
                or is_video_cdn_url(og_image)
                or is_video_html(embed_html)
                or is_video_html(page_html)
            )
            if emb_img:
                slides.append((emb_img, is_vid_fallback, ""))
            elif og_image:
                slides.append((og_image, is_vid_fallback, ""))
            _sync_origins("direct")

        if not slides:
            if summary_only:
                is_reel_k = kind in ("reel", "reels", "tv")
                return {
                    "ok": False,
                    "summaryOnly": True,
                    "summaryResolved": False,
                    "shortcode": shortcode,
                    "parentCode": shortcode,
                    "url": url,
                    "photoCount": 0 if is_reel_k else 1,
                    "videoCount": 1 if is_reel_k else 0,
                    "coverUrl": "",
                    "coverIsVideo": is_reel_k,
                }
            return {"url": url, "shortcode": shortcode, "ok": False, "fullResolved": False, "pictures": []}

        if date_str == "Recently":
            ts_sc = shortcode_to_timestamp(shortcode)
            if ts_sc:
                saved_at_ms = int(ts_sc * 1000)
                date_str = format_relative_age(ts_sc)

        is_reel_kind = kind in ("reel", "reels", "tv") or (len(slides) == 1 and bool(slides[0][1]))
        default_title = f"Instagram {'reel' if is_reel_kind else 'post'} · {shortcode}"
        full_caption = title.strip() if title else ""
        non_empty_lines = [ln.strip() for ln in full_caption.splitlines() if ln.strip()]
        first_line_title = non_empty_lines[0] if non_empty_lines else default_title
        loc_parts = []
        if owner:
            loc_parts.append(f"@{owner.lstrip('@')}")
        if owner_full and owner_full.lower() != owner.lower():
            loc_parts.append(owner_full)
        default_loc_kind = "reel" if is_reel_kind else "p"
        location = " · ".join(loc_parts) if loc_parts else f"instagram.com/{default_loc_kind}/{shortcode}"

        _sync_origins("direct")
        _has_direct = any(o == "direct" for o in slide_origins)
        _has_api = any(o == "api" for o in slide_origins)
        _photo_n = sum(1 for _, is_vid, _ in slides if not is_vid)
        _video_n = sum(1 for _, is_vid, _ in slides if is_vid)
        if _photo_n and _video_n:
            source_kind = "mixed"
        elif _has_direct and _has_api:
            source_kind = "mixed"
        elif _has_api:
            source_kind = "api"
        elif _has_direct:
            source_kind = "direct"
        else:
            source_kind = ""

        if summary_only:
            photo_count = _photo_n
            video_count = _video_n
            if photo_count == 0 and video_count == 0:
                photo_count = 1
            cover_url = str(slides[0][0] or "") if slides else ""
            cover_is_video = bool(slides[0][1]) if slides else is_reel_kind
            summary_obj = {
                "ok": True,
                "summaryOnly": True,
                "summaryResolved": True,
                "shortcode": shortcode,
                "parentCode": shortcode,
                "title": first_line_title,
                "caption": full_caption,
                "owner": owner,
                "ownerFull": owner_full,
                "location": location,
                "date": date_str,
                "dateStr": date_str,
                "likes": likes,
                "url": url,
                "savedAt": saved_at_ms,
                "hasExplicitDate": explicit_date,
                "photoCount": photo_count,
                "videoCount": video_count,
                "coverUrl": cover_url,
                "coverIsVideo": cover_is_video,
                "sourceKind": source_kind,
            }
            with CACHE_LOCK:
                MEM_CACHE.setdefault("summaries_by_shortcode", {})[shortcode] = summary_obj
                if owner or owner_full:
                    MEM_CACHE.setdefault("owners_by_shortcode", {})[shortcode.lower()] = {
                        "ok": True,
                        "shortcode": shortcode,
                        "owner": owner,
                        "ownerFull": owner_full,
                    }
                if persist_immediately:
                    persist_cache()
            return summary_obj

        pictures = []
        for pic_idx, (img_url, is_vid, vid_url) in enumerate(slides):
            pictures.append(
                {
                    "rawUrl": img_url,
                    "imgUrl": img_url,
                    "proxyUrl": "/api/image?url=" + urllib.parse.quote(img_url, safe=""),
                    "isVideo": is_vid,
                    "videoUrl": vid_url,
                    "origin": slide_origins[pic_idx] if pic_idx < len(slide_origins) else "direct",
                }
            )

        resolved = {
            "ok": True,
            "fullResolved": full_resolved,
            "confirmedSingleMedia": bool(full_resolved and len(pictures) == 1),
            "hasExplicitDate": explicit_date,
            "shortcode": shortcode,
            "title": first_line_title,
            "caption": full_caption,
            "owner": owner,
            "ownerFull": owner_full,
            "location": location,
            "date": date_str,
            "likes": likes,
            "url": url,
            "savedAt": saved_at_ms,
            "pictures": pictures,
            "sourceKind": source_kind,
        }

        if full_resolved:
            with CACHE_LOCK:
                MEM_CACHE.setdefault("by_shortcode", {})[shortcode] = resolved
                if persist_immediately:
                    persist_cache()

        return resolved
    finally:
        if is_leader:
            with IN_FLIGHT_LOCK:
                ev = IN_FLIGHT_EVENTS.pop(sc_key, None)
                if ev:
                    ev.set()


def fetch_owner_light(shortcode: str) -> tuple:
    """Cheapest possible way to learn WHO posted a post: 1 tiny request (embed HTML),
    optionally 1 more small page fetch. No image work, no 3rd-party API, no session."""
    owner = ""
    owner_full = ""
    embed = fetch_embed_html(shortcode)
    if embed:
        um = re.search(r'class="UsernameText"[^>]*>([^<]+)<', embed)
        if um:
            owner = html.unescape(um.group(1)).strip()
        fm = re.search(r'class="FullName"[^>]*>([^<]+)<', embed)
        if fm:
            owner_full = html.unescape(fm.group(1)).strip()
        if not owner:
            try:
                media = parse_context_json(embed) or {}
                o = media.get("owner") or {}
                if o.get("username"):
                    owner = str(o["username"]).strip()
                if o.get("full_name") and not owner_full:
                    owner_full = str(o["full_name"]).strip()
            except Exception:
                pass
    if not owner:
        page = fetch_text(
            f"https://www.instagram.com/p/{shortcode}/",
            ua=BOT_UAS[0],
            timeout=5,
            max_bytes=32768,
        )
        if page:
            um = re.search(r'"username"\s*:\s*"([^"]+)"', page)
            if um:
                owner = um.group(1).strip()
            if not owner:
                title = extract_meta(page, "og:title") or ""
                tm = re.search(r"^(.*?)\s+on Instagram", title)
                if tm:
                    cand = html.unescape(tm.group(1)).strip()
                    cm = re.match(r"^(.*?)\s*\(@([^)]+)\)$", cand)
                    if cm:
                        if not owner_full:
                            owner_full = cm.group(1).strip()
                        if not owner:
                            owner = cm.group(2).strip()
                    elif cand and re.fullmatch(r"[A-Za-z0-9._]+", cand):
                        owner = cand
    return owner, owner_full


def resolve_instagram_owner_only(url: str) -> dict:
    """Return ONLY the account (owner / ownerFull) that posted a URL.

    Used by the lightweight full-list search index: it costs ~1 small request per post
    instead of the 4-strategy fan-out of a full resolve.
    """
    url = (url or "").strip()
    m = re.search(
        r"instagram\.com/(?:share/)?(?:([^/?#]+)/)?(p|reel|reels|tv)/([^/?#]+)",
        url,
        re.I,
    )
    if not m:
        return {"url": url, "ok": False, "error": "Not a recognized Instagram post URL"}

    url_user, _kind, shortcode = m.group(1) or "", m.group(2).lower(), m.group(3)
    if url_user.lower() == "share":
        url_user = ""
    key = shortcode.lower()

    with CACHE_LOCK:
        owners = MEM_CACHE.setdefault("owners_by_shortcode", {})
        cached = owners.get(key)
        if cached and (cached.get("owner") or cached.get("ownerFull")):
            res = dict(cached)
            res["url"] = url
            res["ok"] = True
            return res
        full = MEM_CACHE.get("by_shortcode", {}).get(shortcode) or MEM_CACHE.get("by_shortcode", {}).get(key)
        if isinstance(full, dict) and (full.get("owner") or full.get("ownerFull")):
            res = {
                "ok": True,
                "shortcode": shortcode,
                "url": url,
                "owner": full.get("owner") or "",
                "ownerFull": full.get("ownerFull") or "",
                "sourceKind": full.get("sourceKind") or "",
            }
            owners[key] = dict(res)
            return res
        # no full resolve cached, but a summary may already know the verdict
        cached_sum = MEM_CACHE.get("summaries_by_shortcode", {}).get(shortcode) or MEM_CACHE.get("summaries_by_shortcode", {}).get(key)
        if isinstance(cached_sum, dict) and cached_sum.get("sourceKind"):
            return {
                "ok": True,
                "shortcode": shortcode,
                "url": url,
                "owner": cached_sum.get("owner") or "",
                "ownerFull": cached_sum.get("ownerFull") or "",
                "sourceKind": cached_sum.get("sourceKind") or "",
            }

    owner, owner_full = fetch_owner_light(shortcode)
    if not owner and url_user:
        owner = url_user

    res = {
        "ok": bool(owner or owner_full),
        "shortcode": shortcode,
        "url": url,
        "owner": owner,
        "ownerFull": owner_full,
    }
    if res["ok"]:
        with CACHE_LOCK:
            MEM_CACHE.setdefault("owners_by_shortcode", {})[key] = dict(res)
    return res


def update_index_tunnel_url(tunnel_url: str) -> None:
    if not tunnel_url or not INDEX_FILE.exists():
        return
    try:
        content = INDEX_FILE.read_text(encoding="utf-8")
        new_content, count = re.subn(
            r'const SERVER_API_BASE="[^"]*";',
            f'const SERVER_API_BASE="{tunnel_url}";',
            content,
            count=1,
        )
        if count > 0 and new_content != content:
            INDEX_FILE.write_text(new_content, encoding="utf-8")
    except Exception:
        pass


def start_tunnel_Loop(port: int) -> None:
    def _worker():
        while True:
            try:
                proc = subprocess.Popen(
                    [
                        "ssh",
                        "-o",
                        "StrictHostKeyChecking=no",
                        "-o",
                        "ServerAliveInterval=25",
                        "-R",
                        f"80:localhost:{port}",
                        "nokey@localhost.run",
                    ],
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                )
                for line in proc.stdout:
                    m = re.search(r"https://[a-zA-Z0-9.-]+\.lhr\.life", line)
                    if m:
                        url = m.group(0).strip()
                        print(f"Tunnel ready: {url}", flush=True)
                        update_index_tunnel_url(url)
                proc.wait()
            except Exception:
                pass
            time.sleep(2)

    t = threading.Thread(target=_worker, daemon=True)
    t.start()


class AppHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(WORKSPACE), **kwargs)

    def log_message(self, format, *args):
        pass

    def send_json(self, status: int, payload: dict) -> None:
        raw = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(raw)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "*")
        self.end_headers()

    def end_headers(self):
        parsed_p = urllib.parse.urlsplit(getattr(self, "path", "") or "").path
        if parsed_p in ("", "/", "/index.html") or parsed_p.endswith(".html"):
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.send_header("Pragma", "no-cache")
            self.send_header("Expires", "0")
        super().end_headers()

    def do_GET(self):
        parsed = urllib.parse.urlsplit(self.path)
        if parsed.path == "/api/health":
            self.send_json(200, {"ok": True, "app": "still-sane"})
            return
        if parsed.path in ("", "/", "/index.html"):
            try:
                raw = INDEX_FILE.read_bytes()
            except Exception:
                self.send_response(404)
                self.end_headers()
                return
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(raw)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, proxy-revalidate, max-age=0")
            self.send_header("Pragma", "no-cache")
            self.send_header("Expires", "0")
            self.end_headers()
            self.wfile.write(raw)
            return

        if parsed.path in ("/api/image", "/api/download-photo"):
            qs = urllib.parse.parse_qs(parsed.query)
            img_url = (qs.get("url") or [""])[0].strip()
            if not img_url:
                self.send_response(400)
                self.end_headers()
                return
            raw = fetch_image_bytes(img_url)
            if not raw:
                self.send_response(404)
                self.end_headers()
                return
            is_download = parsed.path == "/api/download-photo" or (qs.get("download") or [""])[0] == "1"
            filename = re.sub(r"[^A-Za-z0-9._-]+", "_", (qs.get("filename") or ["instagram_photo.jpg"])[0]).strip("_") or "instagram_photo.jpg"
            if not filename.lower().endswith((".jpg", ".jpeg", ".png", ".webp", ".svg")):
                filename += ".jpg"
            self.send_response(200)
            self.send_header("Content-Type", "image/jpeg")
            self.send_header("Content-Length", str(len(raw)))
            if is_download:
                self.send_header("Content-Disposition", f'attachment; filename="{filename}"')
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Cross-Origin-Resource-Policy", "cross-origin")
            self.send_header("Cache-Control", "public, max-age=86400")
            self.end_headers()
            self.wfile.write(raw)
            return

        if parsed.path in ("/api/resolve-post", "/api/post"):
            qs = urllib.parse.parse_qs(parsed.query)
            url = (qs.get("url") or [""])[0].strip()
            if not url:
                self.send_json(400, {"ok": False, "error": "Missing url"})
                return
            is_summary = (qs.get("summary") or [""])[0] == "1"
            result = resolve_instagram_post(url, summary_only=is_summary)
            if is_summary:
                result = build_post_summary(result)
            self.send_json(200, result)
            return

        if parsed.path in ("/api/resolve-batch", "/api/resolve-posts"):
            qs = urllib.parse.parse_qs(parsed.query)
            raw_urls = qs.get("urls") or []
            urls: list[str] = []
            for item in raw_urls:
                for part in str(item).split(","):
                    part = part.strip()
                    if part:
                        urls.append(part)
            urls = urls[:20]
            raw_results = list(RESOLVE_POOL.map(resolve_instagram_post_summary, urls)) if urls else []
            with CACHE_LOCK:
                persist_cache()
            results = [build_post_summary(r) for r in raw_results if isinstance(r, dict)]
            by_sc = {
                str(r.get("shortcode") or "").lower(): r
                for r in results
                if isinstance(r, dict) and r.get("shortcode")
            }
            self.send_json(200, {"ok": True, "posts": results, "byShortcode": by_sc})
            return

        if parsed.path in ("/api/resolve-owners", "/api/resolve-owner"):
            qs = urllib.parse.parse_qs(parsed.query)
            raw_urls = qs.get("urls") or []
            urls: list[str] = []
            for item in raw_urls:
                for part in str(item).split(","):
                    part = part.strip()
                    if part:
                        urls.append(part)
            urls = urls[:40]
            results = list(RESOLVE_POOL.map(resolve_instagram_owner_only, urls)) if urls else []
            with CACHE_LOCK:
                persist_cache()
            by_sc = {
                str(r.get("shortcode") or "").lower(): r
                for r in results
                if isinstance(r, dict) and r.get("shortcode")
            }
            self.send_json(200, {"ok": True, "posts": results, "byShortcode": by_sc})
            return

        if parsed.path in ("/api/cached-posts", "/api/cache"):
            with CACHE_LOCK:
                entries = list(MEM_CACHE.get("imported_entries") or [])
                raw_by_sc = dict(MEM_CACHE.get("by_shortcode") or {})
                raw_sum_by_sc = dict(MEM_CACHE.get("summaries_by_shortcode") or {})
                last_name = MEM_CACHE.get("last_file_name") or ""
            summary_by_sc = {
                k: build_post_summary(v)
                for k, v in raw_sum_by_sc.items()
                if isinstance(v, dict)
            }
            for k, v in raw_by_sc.items():
                if isinstance(v, dict):
                    summary_by_sc[k] = build_post_summary(v)
            self.send_json(
                200,
                {
                    "fileName": last_name,
                    "entries": entries,
                    "summaryByShortcode": summary_by_sc,
                    "resolvedByShortcode": summary_by_sc,
                    "by_shortcode": summary_by_sc,
                },
            )
            return

        if parsed.path in ("/api/browser/state", "/api/ig-session"):
            qs = urllib.parse.parse_qs(parsed.query)
            if (qs.get("fast") or [""])[0] == "1":
                sess = load_ig_session()
                self.send_json(
                    200,
                    {
                        "ok": True,
                        "authenticated": bool(sess.get("authenticated")),
                        "username": sess.get("username") or "",
                        "userId": sess.get("userId") or "",
                        "twoFactorPending": bool(sess.get("twoFactorPending")),
                        "statusMessage": sess.get("statusMessage") or "",
                        "currentUrl": sess.get("currentUrl") or "https://www.instagram.com/accounts/login/",
                    },
                )
                return
            state = BROWSER_MGR.run_action({"type": "state"})
            self.send_json(200, state)
            return

        # SECURITY: never fall back to serving arbitrary files from the workspace. The app is reachable through a
        # public tunnel, and the workspace holds secrets (.ssh/, ig_session.json, the resolved-posts cache, source).
        # The page needs nothing but index.html, which is served above.
        if parsed.path == "/favicon.ico":
            self.send_response(204)
            self.end_headers()
            return
        self.send_response(404)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.end_headers()
        self.wfile.write(b"Not found")

    def do_HEAD(self):
        parsed = urllib.parse.urlsplit(self.path)
        self.send_response(200 if parsed.path in ("", "/", "/index.html") else 404)
        self.end_headers()

    def do_POST(self):
        parsed = urllib.parse.urlsplit(self.path)
        length = int(self.headers.get("Content-Length") or 0)
        body_bytes = self.rfile.read(length) if length > 0 else b"{}"
        try:
            payload = json.loads(body_bytes.decode("utf-8"))
        except Exception:
            payload = {}

        if parsed.path in ("/api/browser/action", "/api/ig-browser"):
            state = BROWSER_MGR.run_action(payload if isinstance(payload, dict) else {"type": "state"})
            self.send_json(200, state)
            return

        if parsed.path == "/api/resolve-post":
            url = str(payload.get("url") or "").strip()
            if not url:
                self.send_json(400, {"ok": False, "error": "Missing url"})
                return
            is_summary = bool(payload.get("summary"))
            result = resolve_instagram_post(url, summary_only=is_summary)
            if is_summary:
                result = build_post_summary(result)
            self.send_json(200, result)
            return

        if parsed.path == "/api/save-imported-list":
            entries = payload.get("entries") or []
            file_name = str(payload.get("fileName") or "").strip()
            with CACHE_LOCK:
                MEM_CACHE["imported_entries"] = entries
                if file_name:
                    MEM_CACHE["last_file_name"] = file_name
                persist_cache()
            self.send_json(200, {"ok": True})
            return

        if parsed.path in ("/api/resolve-posts", "/api/resolve-batch"):
            urls = [str(u).strip() for u in (payload.get("urls") or []) if str(u).strip()][:20]
            raw_results = list(RESOLVE_POOL.map(resolve_instagram_post_summary, urls)) if urls else []
            with CACHE_LOCK:
                persist_cache()
            results = [build_post_summary(r) for r in raw_results if isinstance(r, dict)]
            by_sc = {
                str(r.get("shortcode") or "").lower(): r
                for r in results
                if isinstance(r, dict) and r.get("shortcode")
            }
            self.send_json(200, {"ok": True, "posts": results, "byShortcode": by_sc})
            return

        if parsed.path == "/api/download-selected":
            folder_raw = str(payload.get("folder") or "~/Pictures/Instagram").strip()
            if folder_raw.startswith("~/") or folder_raw == "~":
                target_dir = Path("/home/user") / folder_raw[2:]
            else:
                target_dir = Path(folder_raw)
                if not target_dir.is_absolute():
                    target_dir = Path("/home/user") / target_dir
            try:
                target_dir.mkdir(parents=True, exist_ok=True)
            except Exception:
                target_dir = Path("/home/user/Pictures/Instagram")
                target_dir.mkdir(parents=True, exist_ok=True)

            items = payload.get("items") or []
            saved_files: list[dict] = []
            resolved_post_cache: dict[str, list[dict]] = {}

            for idx, item in enumerate(items):
                if not isinstance(item, dict):
                    continue
                sc = re.sub(r"[^A-Za-z0-9._-]+", "_", str(item.get("shortcode") or f"post_{idx + 1}")).strip("_") or f"post_{idx + 1}"
                photo_idx = int(item.get("photoIndex") if isinstance(item.get("photoIndex"), int) else idx)
                filename = re.sub(r"[^A-Za-z0-9._-]+", "_", str(item.get("filename") or f"{sc}_photo_{photo_idx + 1}.jpg")).strip("_")
                raw_url = str(item.get("rawUrl") or "").strip()
                data_uri = str(item.get("dataUri") or "").strip()
                svg_markup = str(item.get("svgMarkup") or "").strip()
                post_url = str(item.get("postUrl") or "").strip()

                # If a closed post was selected via right-click or had its pictures unloaded, resolve its photo list on demand
                if not raw_url and not data_uri and not svg_markup and post_url:
                    cache_key = sc.lower()
                    if cache_key not in resolved_post_cache:
                        res = resolve_instagram_post(post_url, summary_only=False)
                        pics = (res.get("pictures") or []) if isinstance(res, dict) else []
                        photos_only = [
                            p for p in pics
                            if isinstance(p, dict) and not bool(p.get("isVideo") or p.get("videoUrl") or is_video_cdn_url(str(p.get("rawUrl") or p.get("imgUrl") or "")))
                        ]
                        resolved_post_cache[cache_key] = photos_only
                    photos_only = resolved_post_cache.get(cache_key) or []
                    if 0 <= photo_idx < len(photos_only):
                        raw_url = str(photos_only[photo_idx].get("rawUrl") or photos_only[photo_idx].get("imgUrl") or "").strip()

                raw_bytes = b""
                ext = ".jpg"
                if data_uri.startswith("data:image/"):
                    try:
                        head, b64_part = data_uri.split(",", 1)
                        if "png" in head:
                            ext = ".png"
                        elif "webp" in head:
                            ext = ".webp"
                        elif "svg" in head:
                            ext = ".svg"
                        raw_bytes = base64.b64decode(b64_part)
                    except Exception:
                        raw_bytes = b""
                elif svg_markup.startswith("<svg"):
                    ext = ".svg"
                    raw_bytes = svg_markup.encode("utf-8")
                elif raw_url:
                    raw_bytes = fetch_image_bytes(raw_url)

                if not filename.lower().endswith((".jpg", ".jpeg", ".png", ".webp", ".svg")):
                    filename += ext
                elif ext == ".svg" and filename.lower().endswith(".jpg"):
                    filename = filename[:-4] + ".svg"

                saved_path = ""
                if raw_bytes:
                    out_path = target_dir / filename
                    try:
                        out_path.write_bytes(raw_bytes)
                        saved_path = str(out_path)
                    except Exception:
                        saved_path = ""

                saved_files.append({
                    "shortcode": sc,
                    "photoIndex": photo_idx,
                    "filename": filename,
                    "rawUrl": raw_url,
                    "saved": bool(saved_path),
                    "savedPath": saved_path,
                })

            self.send_json(
                200,
                {
                    "ok": True,
                    "savedCount": sum(1 for f in saved_files if f.get("saved")),
                    "totalRequested": len(saved_files),
                    "folder": str(target_dir),
                    "files": saved_files,
                },
            )
            return

        self.send_json(404, {"error": "Not found"})


def fetch_image_bytes(img_url: str) -> bytes:
    """Fetch image bytes from local cache, Instagram CDN, or wsrv.nl fallback."""
    if not img_url:
        return b""
    key = hashlib.sha1(img_url.encode("utf-8")).hexdigest()
    cache_path = IMG_CACHE_DIR / f"{key}.jpg"
    if cache_path.exists():
        try:
            cached = cache_path.read_bytes()
            if cached:
                return cached
        except Exception:
            pass
    try:
        req = urllib.request.Request(
            img_url,
            headers={
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
                "Accept": "image/avif,image/webp,image/apng,image/*,*/*;q=0.8",
            },
        )
        with urllib.request.urlopen(req, timeout=10) as resp:
            raw = resp.read()
            if raw:
                cache_path.write_bytes(raw)
                return raw
    except Exception:
        if "/media/?size=" not in img_url:
            try:
                wsrv = "https://wsrv.nl/?url=" + urllib.parse.quote(img_url, safe="")
                req2 = urllib.request.Request(wsrv, headers={"User-Agent": "Mozilla/5.0"})
                with urllib.request.urlopen(req2, timeout=10) as resp2:
                    raw = resp2.read()
                    if raw:
                        cache_path.write_bytes(raw)
                        return raw
            except Exception:
                pass
    return b""


def build_post_summary(res: dict) -> dict:
    """Return lightweight card summary (photo/video counts + 1st cover image URL only, without holding all carousel photos/videos)."""
    if not isinstance(res, dict):
        return {"ok": False}
    if res.get("summaryOnly") and "pictures" not in res:
        return dict(res)
    pics = res.get("pictures") or []
    photo_count = 0
    video_count = 0
    cover_url = str(res.get("coverUrl") or "")
    cover_is_video = bool(res.get("coverIsVideo"))
    for idx, pic in enumerate(pics):
        if not isinstance(pic, dict):
            continue
        raw_u = str(pic.get("rawUrl") or pic.get("imgUrl") or "")
        is_vid = bool(pic.get("isVideo") or pic.get("videoUrl") or is_video_cdn_url(raw_u))
        if idx == 0:
            cover_url = raw_u
            cover_is_video = is_vid
        if is_vid:
            video_count += 1
        else:
            photo_count += 1
    if isinstance(res.get("photoCount"), int) and not pics:
        photo_count = int(res["photoCount"])
    if isinstance(res.get("videoCount"), int) and not pics:
        video_count = int(res["videoCount"])
    if photo_count == 0 and video_count == 0:
        photo_count = 1
    date_val = str(res.get("date") or res.get("dateStr") or "")
    return {
        "ok": bool(res.get("ok", True)),
        "summaryOnly": True,
        "summaryResolved": bool(res.get("fullResolved") or res.get("summaryResolved") or len(pics) > 0),
        "shortcode": res.get("shortcode") or "",
        "parentCode": res.get("parentCode") or res.get("shortcode") or "",
        "title": res.get("title") or "",
        "caption": res.get("caption") or res.get("title") or "",
        "owner": res.get("owner") or "",
        "ownerFull": res.get("ownerFull") or "",
        "sourceKind": res.get("sourceKind") or "",
        "location": res.get("location") or "",
        "likes": res.get("likes") or "—",
        "timestamp": res.get("timestamp"),
        "date": date_val,
        "dateStr": date_val,
        "savedAt": res.get("savedAt"),
        "hasExplicitDate": bool(res.get("hasExplicitDate")),
        "photoCount": photo_count,
        "videoCount": video_count,
        "coverUrl": cover_url,
        "coverIsVideo": cover_is_video,
    }


BROWSER_MGR.set_post_resolver(resolve_instagram_post)

if __name__ == "__main__":
    init_cache()
    port = int(os.environ.get("PORT", "8000"))
    if not os.environ.get("STILL_SANE_NO_TUNNEL"):   # set STILL_SANE_NO_TUNNEL=1 to keep the server local-only
        start_tunnel_Loop(port)
    server = ThreadingHTTPServer(("0.0.0.0", port), AppHandler)
    print(f"Serving Still Sane, Exile? on http://0.0.0.0:{port}", flush=True)
    server.serve_forever()
