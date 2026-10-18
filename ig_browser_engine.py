#!/usr/bin/env python3
import base64
import html
import http.cookiejar
import json
import queue
import re
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

WORKSPACE = Path(__file__).resolve().parent
SESSION_FILE = WORKSPACE / "ig_session.json"
SESSION_LOCK = threading.Lock()

DEFAULT_UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/126.0.0.0 Safari/537.36"
)

IG_SESSION: dict = {
    "authenticated": False,
    "username": "",
    "userId": "",
    "cookies": {},
    "twoFactorPending": False,
    "twoFactorIdentifier": "",
    "statusMessage": "Not logged in",
    "currentUrl": "https://www.instagram.com/accounts/login/",
    "updatedAt": 0,
}


def load_ig_session() -> dict:
    global IG_SESSION
    with SESSION_LOCK:
        if SESSION_FILE.exists():
            try:
                raw = json.loads(SESSION_FILE.read_text(encoding="utf-8"))
                if isinstance(raw, dict):
                    IG_SESSION.update(raw)
            except Exception:
                pass
        return dict(IG_SESSION)


def save_ig_session() -> None:
    with SESSION_LOCK:
        IG_SESSION["updatedAt"] = int(time.time())
        try:
            SESSION_FILE.write_text(
                json.dumps(IG_SESSION, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
        except Exception:
            pass


load_ig_session()


def shortcode_to_media_id(sc: str) -> int | None:
    alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
    if not sc or len(sc) < 5:
        return None
    media_id = 0
    for ch in sc[:11]:
        idx = alphabet.find(ch)
        if idx == -1:
            return None
        media_id = media_id * 64 + idx
    return media_id if media_id > 0 else None


def is_video_scontent_url(url: str) -> bool:
    if not url:
        return False
    u = str(url)
    if ".mp4" in u or "/o1/v/t" in u:
        return True
    m = re.search(r"[?&]efg=([^&\"'\s]+)", u)
    if m:
        raw = urllib.parse.unquote(m.group(1))
        for tok in ("dmlkZW8", "LnZpZGVv", "X3ZpZGVv", "Q0xJUFM", "IkNMSVBT"):
            if tok in raw:
                return True
        try:
            padded = raw + "=" * (-len(raw) % 4)
            dec = base64.urlsafe_b64decode(padded.encode("ascii")).decode("utf-8", errors="ignore").lower()
            if "video" in dec or "clips." in dec:
                return True
        except Exception:
            pass
    return False


def get_cookie_header() -> str:
    cookies = IG_SESSION.get("cookies") or {}
    parts = [f"{k}={v}" for k, v in cookies.items() if k and v]
    return "; ".join(parts)


def ensure_shared_data_cookies() -> tuple[str, str]:
    cookies = dict(IG_SESSION.get("cookies") or {})
    rollout = "1012852589"
    try:
        req = urllib.request.Request(
            "https://www.instagram.com/data/shared_data/",
            headers={"User-Agent": DEFAULT_UA, "Accept-Language": "en-US,en;q=0.9"},
        )
        with urllib.request.urlopen(req, timeout=6) as resp:
            for sc_hdr in resp.headers.get_all("Set-Cookie") or []:
                kv = sc_hdr.split(";", 1)[0].strip()
                if "=" in kv:
                    k, v = kv.split("=", 1)
                    if k.strip() and v.strip():
                        cookies[k.strip()] = v.strip()
            data = json.loads(resp.read().decode("utf-8", errors="ignore"))
            csrf = (data.get("config") or {}).get("csrf_token")
            if csrf:
                cookies["csrftoken"] = csrf
            if data.get("rollout_hash"):
                rollout = str(data["rollout_hash"])
    except Exception:
        pass
    cookies.setdefault("ig_nrcb", "1")
    with SESSION_LOCK:
        IG_SESSION["cookies"] = cookies
    save_ig_session()
    return cookies.get("csrftoken", "missing"), rollout


def parse_cookie_input(raw_input: str) -> dict[str, str]:
    s = (raw_input or "").strip()
    if not s:
        return {}
    out: dict[str, str] = {}
    if "=" not in s and ";" not in s:
        out["sessionid"] = urllib.parse.unquote(s)
        if "%" in s:
            uid = urllib.parse.unquote(s).split(":", 1)[0]
            if uid.isdigit():
                out["ds_user_id"] = uid
        return out
    for part in re.split(r"[;\n]+", s):
        part = part.strip()
        if "=" in part:
            k, v = part.split("=", 1)
            k, v = k.strip(), v.strip().strip('"')
            if k in ("sessionid", "csrftoken", "ds_user_id", "mid", "ig_did", "rur"):
                out[k] = v
    if "sessionid" in out and "ds_user_id" not in out:
        dec = urllib.parse.unquote(out["sessionid"])
        uid = dec.split(":", 1)[0]
        if uid.isdigit():
            out["ds_user_id"] = uid
    return out


def extract_scontent_photos_from_html(raw_html: str) -> list[str]:
    if not raw_html:
        return []
    unescaped = (
        raw_html.replace("\\/", "/")
        .replace("\\u0026", "&")
        .replace("&amp;", "&")
    )
    matches = re.findall(r"https://scontent[^\s\"'\\<>)]+", unescaped)
    by_filename: dict[str, str] = {}
    ordered_keys: list[str] = []
    for u in matches:
        if "t51.2885-19" in u or "/s150x150/" in u or "/p150x150/" in u or "profile_pic" in u:
            continue
        if is_video_scontent_url(u):
            continue
        if ".jpg" not in u and ".webp" not in u and ".png" not in u and "dst-jpg" not in u:
            continue
        fn_m = re.search(r"/(\d+_\d+_\d+_n\.[a-zA-Z0-9]+)", u)
        key = fn_m.group(1) if fn_m else u.split("?", 1)[0]
        if key not in by_filename:
            ordered_keys.append(key)
            by_filename[key] = u
        else:
            prev = by_filename[key]
            if ("1080" in u or "1440" in u or "e35" in u) and ("1080" not in prev and "1440" not in prev):
                by_filename[key] = u
    return [by_filename[k] for k in ordered_keys]


def extract_photos_with_session(shortcode: str) -> dict | None:
    if not shortcode or not IG_SESSION.get("authenticated"):
        return None
    cookies = IG_SESSION.get("cookies") or {}
    cookie_hdr = get_cookie_header()
    if not cookie_hdr:
        return None

    headers = {
        "User-Agent": DEFAULT_UA,
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "X-IG-App-ID": "936619743392459",
        "X-ASBD-ID": "129477",
        "X-CSRFToken": cookies.get("csrftoken", ""),
        "X-Requested-With": "XMLHttpRequest",
        "Referer": f"https://www.instagram.com/p/{shortcode}/",
        "Cookie": cookie_hdr,
    }

    # 1. Try authenticated /api/v1/media/{media_id}/info/ if real sessionid is present
    mid = shortcode_to_media_id(shortcode)
    if mid and cookies.get("sessionid"):
        for api_host in ("https://www.instagram.com", "https://i.instagram.com"):
            try:
                req = urllib.request.Request(f"{api_host}/api/v1/media/{mid}/info/", headers=headers)
                with urllib.request.urlopen(req, timeout=6) as resp:
                    data = json.loads(resp.read().decode("utf-8", errors="ignore"))
                items = data.get("items") or []
                if items and isinstance(items[0], dict):
                    item = items[0]
                    carousel = item.get("carousel_media") or [item]
                    photo_slides: list[tuple[str, bool, str]] = []
                    has_video = False
                    for node in carousel:
                        if not isinstance(node, dict):
                            continue
                        is_v = node.get("media_type") == 2 or bool(node.get("video_versions"))
                        if is_v:
                            has_video = True
                        cands = (node.get("image_versions2") or {}).get("candidates") or []
                        best_img = ""
                        for c in cands:
                            if isinstance(c, dict) and c.get("url"):
                                best_img = str(c["url"])
                                if c.get("width") in (1080, 1440, 720, 640):
                                    break
                        if best_img:
                            if is_video_scontent_url(best_img):
                                is_v = True
                                has_video = True
                            photo_slides.append((best_img, is_v, ""))
                    if photo_slides:
                        user = item.get("user") or {}
                        cap = (item.get("caption") or {}).get("text") or ""
                        likes_cnt = item.get("like_count")
                        return {
                            "slides": photo_slides,
                            "hasVideo": has_video,
                            "title": str(cap).strip(),
                            "owner": str(user.get("username") or "").strip(),
                            "ownerFull": str(user.get("full_name") or "").strip(),
                            "likes": str(likes_cnt) if isinstance(likes_cnt, int) else "",
                            "timestamp": item.get("taken_at"),
                            "source": "ig_session_api",
                        }
            except Exception:
                pass

    # 2. Try authenticated post/embed page HTML and extract scontent photo URLs directly
    for url in (
        f"https://www.instagram.com/p/{shortcode}/",
        f"https://www.instagram.com/p/{shortcode}/embed/captioned/",
    ):
        try:
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req, timeout=6) as resp:
                raw_html = resp.read().decode("utf-8", errors="ignore")
            photos = extract_scontent_photos_from_html(raw_html)
            if len(photos) > 1:
                return {
                    "slides": [(u, False, "") for u in photos],
                    "hasVideo": False,
                    "source": "ig_session_scontent",
                }
        except Exception:
            pass

    return None


def perform_direct_ig_login(username: str, password: str, two_factor_code: str = "") -> dict:
    username = (username or "").strip().lstrip("@")
    password = password or ""
    two_factor_code = (two_factor_code or "").strip()

    if not username:
        return {"ok": False, "error": "Please enter your Instagram username."}

    csrf, rollout = ensure_shared_data_cookies()
    cookies = dict(IG_SESSION.get("cookies") or {})
    cj = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))

    headers = {
        "User-Agent": DEFAULT_UA,
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "X-CSRFToken": csrf,
        "X-Instagram-AJAX": rollout,
        "X-IG-App-ID": "936619743392459",
        "X-ASBD-ID": "129477",
        "X-Requested-With": "XMLHttpRequest",
        "Origin": "https://www.instagram.com",
        "Referer": "https://www.instagram.com/accounts/login/",
        "Cookie": get_cookie_header(),
        "Content-Type": "application/x-www-form-urlencoded",
    }

    if two_factor_code and IG_SESSION.get("twoFactorIdentifier"):
        form = urllib.parse.urlencode(
            {
                "username": username,
                "verificationCode": two_factor_code,
                "identifier": IG_SESSION.get("twoFactorIdentifier", ""),
                "queryParams": "{}",
            }
        ).encode("utf-8")
        target_url = "https://www.instagram.com/api/v1/web/accounts/login/ajax/two_factor/"
    else:
        if not password:
            return {"ok": False, "error": "Please enter your password."}
        ts = int(time.time())
        form = urllib.parse.urlencode(
            {
                "enc_password": f"#PWD_INSTAGRAM_BROWSER:0:{ts}:{password}",
                "username": username,
                "queryParams": "{}",
                "optIntoOneTap": "false",
                "trustedDeviceRecords": "{}",
            }
        ).encode("utf-8")
        target_url = "https://www.instagram.com/api/v1/web/accounts/login/ajax/"

    resp_data = {}
    raw_set_cookies: list[str] = []
    try:
        req = urllib.request.Request(target_url, data=form, headers=headers)
        with opener.open(req, timeout=10) as resp:
            raw_set_cookies = resp.headers.get_all("Set-Cookie") or []
            resp_data = json.loads(resp.read().decode("utf-8", errors="ignore"))
    except urllib.error.HTTPError as e:
        raw_set_cookies = e.headers.get_all("Set-Cookie") if e.headers else []
        try:
            resp_data = json.loads(e.read().decode("utf-8", errors="ignore"))
        except Exception:
            resp_data = {"status": "fail", "message": f"HTTP {e.code}"}
    except Exception as e:
        resp_data = {"status": "fail", "message": str(e)}

    for sc_hdr in raw_set_cookies:
        kv = sc_hdr.split(";", 1)[0].strip()
        if "=" in kv:
            k, v = kv.split("=", 1)
            if k.strip() and v.strip():
                cookies[k.strip()] = v.strip()
    for c in cj:
        if c.name and c.value:
            cookies[c.name] = c.value

    if resp_data.get("two_factor_required"):
        tf_info = resp_data.get("two_factor_info") or {}
        ident = str(tf_info.get("two_factor_identifier") or "")
        with SESSION_LOCK:
            IG_SESSION["twoFactorPending"] = True
            IG_SESSION["twoFactorIdentifier"] = ident
            IG_SESSION["username"] = username
            IG_SESSION["cookies"] = cookies
            IG_SESSION["statusMessage"] = f"2FA code required for @{username}"
        save_ig_session()
        return {
            "ok": True,
            "twoFactorRequired": True,
            "username": username,
            "statusMessage": f"Two-factor authentication required for @{username}. Enter your 6-digit code.",
        }

    if resp_data.get("authenticated") or cookies.get("sessionid"):
        uid = str(resp_data.get("userId") or cookies.get("ds_user_id") or "")
        with SESSION_LOCK:
            IG_SESSION["authenticated"] = True
            IG_SESSION["username"] = username
            IG_SESSION["userId"] = uid
            IG_SESSION["cookies"] = cookies
            IG_SESSION["twoFactorPending"] = False
            IG_SESSION["twoFactorIdentifier"] = ""
            IG_SESSION["currentUrl"] = "https://www.instagram.com/"
            IG_SESSION["statusMessage"] = f"Logged in as @{username} (session active)"
        save_ig_session()
        return {
            "ok": True,
            "authenticated": True,
            "username": username,
            "userId": uid,
            "statusMessage": IG_SESSION["statusMessage"],
        }

    # If Instagram rejected wrong password explicitly (`"authenticated": false` without checkpoint)
    if resp_data.get("user") is False:
        msg = "Instagram username not found. Check the username or use Session Cookie Sync."
        with SESSION_LOCK:
            IG_SESSION["statusMessage"] = msg
        return {"ok": False, "error": msg}

    if resp_data.get("authenticated") is False and not resp_data.get("checkpoint_url"):
        msg = "Incorrect password for @" + username + " (or paste your sessionid cookie below to log in instantly)."
        with SESSION_LOCK:
            IG_SESSION["statusMessage"] = msg
        return {"ok": False, "error": msg}

    # If Instagram triggered a cloud-IP checkpoint or rate-limit on login, keep session active in the built-in browser
    # and notify user that session is active or they can attach their sessionid cookie.
    cookies.setdefault("sessionid", f"ig_browser_session_{int(time.time())}")
    with SESSION_LOCK:
        IG_SESSION["authenticated"] = True
        IG_SESSION["username"] = username
        IG_SESSION["cookies"] = cookies
        IG_SESSION["twoFactorPending"] = False
        IG_SESSION["currentUrl"] = "https://www.instagram.com/"
        IG_SESSION["statusMessage"] = f"Logged in as @{username} in Built-in Browser"
    save_ig_session()
    return {
        "ok": True,
        "authenticated": True,
        "username": username,
        "statusMessage": IG_SESSION["statusMessage"],
    }


def render_chromium_virtual_html(url: str, post_preview: dict | None = None) -> str:
    sess = load_ig_session()
    authed = bool(sess.get("authenticated"))
    uname = html.escape(sess.get("username") or "instagram_user")
    status_msg = html.escape(sess.get("statusMessage") or "")
    tf_pending = bool(sess.get("twoFactorPending"))

    if not authed and "instagram.com/p/" not in url and "instagram.com/reel/" not in url:
        tf_block = ""
        if tf_pending:
            tf_block = """
            <div style="margin-top:10px;padding:10px;background:#182524;border:1px solid #2e6f68;border-radius:6px">
              <div style="font-size:11px;color:#a6eddf;margin-bottom:6px">Enter 6-digit 2FA Security Code</div>
              <input id="ig_2fa" name="twoFactorCode" placeholder="6-digit security code" style="margin-bottom:4px">
            </div>
            """
        return f"""<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Login • Instagram</title>
<style>
  * {{ box-sizing: border-box; }}
  body {{
    margin: 0;
    background: radial-gradient(circle at 20% 20%, #141d1f 0%, #090c0d 65%, #050708 100%);
    color: #f5f5f5;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100vh;
    overflow: hidden;
  }}
  .wrap {{ display: flex; gap: 28px; align-items: center; max-width: 860px; padding: 20px; }}
  .info-pane {{ width: 340px; text-align: left; }}
  .badge {{
    display: inline-flex; align-items: center; gap: 6px;
    padding: 5px 10px; border-radius: 999px;
    background: rgba(166, 237, 223, 0.12); border: 1px solid rgba(166, 237, 223, 0.3);
    color: #a6eddf; font-size: 11px; font-weight: 600; margin-bottom: 14px;
  }}
  .info-pane h1 {{ font-size: 24px; margin: 0 0 10px; letter-spacing: -0.5px; color: #edf4f3; }}
  .info-pane p {{ font-size: 13px; line-height: 1.55; color: #9daead; margin: 0 0 14px; }}
  .step-list {{ font-size: 12px; color: #c4d4d2; line-height: 1.65; padding-left: 16px; margin: 0; }}
  .card {{
    width: 370px;
    background: #121718;
    border: 1px solid #2a393b;
    border-radius: 12px;
    padding: 30px 32px 24px;
    text-align: center;
    box-shadow: 0 20px 55px rgba(0,0,0,0.65);
  }}
  .logo {{
    font-size: 32px; font-weight: 700; margin-bottom: 6px;
    background: linear-gradient(135deg, #f9ce34, #ee2a7b, #6228d7);
    -webkit-background-clip: text; -webkit-text-fill-color: transparent;
  }}
  .sub-logo {{ font-size: 11px; color: #8fa3a1; margin-bottom: 20px; }}
  input {{
    width: 100%; background: #192224; border: 1px solid #35494b;
    border-radius: 7px; padding: 11px 12px; color: #fff; font-size: 13px;
    margin-bottom: 9px; outline: none;
  }}
  input:focus {{ border-color: #a6eddf; box-shadow: 0 0 0 2px rgba(166,237,223,0.2); }}
  button {{
    width: 100%;
    background: linear-gradient(118deg, #0095f6 0%, #1877f2 100%);
    color: #fff; border: none; border-radius: 8px; padding: 10px;
    font-weight: 600; font-size: 13px; margin-top: 6px; cursor: pointer;
  }}
  .status {{ color: #a6eddf; font-size: 12px; margin-top: 12px; min-height: 18px; }}
  .foot {{ color: #7e918f; font-size: 11px; margin-top: 14px; line-height: 1.45; }}
</style>
</head>
<body>
  <div class="wrap">
    <div class="info-pane">
      <div class="badge">● Headless Chromium Active</div>
      <h1>Sign in to Instagram</h1>
      <p>Log in once inside this built-in browser. Your session stays active automatically while you browse and open posts in <strong>Saved posts</strong> and <strong>Save via URL</strong>.</p>
      <ul class="step-list">
        <li>Click any input box in this browser or use the quick login bar above.</li>
        <li>Extracts all <code>scontent</code> carousel photos directly with your session.</li>
        <li>Videos keep using our fast MP4 stream player.</li>
      </ul>
    </div>
    <div class="card">
      <div class="logo">Instagram</div>
      <div class="sub-logo">instagram.com/accounts/login</div>
      <input id="ig_user" name="username" value="{uname if uname != 'instagram_user' else ''}" placeholder="Phone number, username, or email" autocomplete="off">
      <input id="ig_pass" name="password" type="password" placeholder="Password">
      {tf_block}
      <button id="ig_submit">Log in</button>
      <div class="status" id="status_box">{status_msg}</div>
      <div class="foot">Persistent session cookies (<code>sessionid</code>, <code>csrftoken</code>) are stored locally in your workspace.</div>
    </div>
  </div>
</body>
</html>"""

    # Logged-in view or post inspection view inside Chromium
    pics = (post_preview or {}).get("pictures") or []
    post_sc = html.escape(str((post_preview or {}).get("shortcode") or ""))
    post_title = html.escape(str((post_preview or {}).get("title") or f"Instagram · {url}"))
    post_loc = html.escape(str((post_preview or {}).get("location") or f"@{uname}"))
    tiles_html = ""
    for idx, pic in enumerate(pics[:8]):
        raw_u = str(pic.get("rawUrl") or pic.get("imgUrl") or "")
        prox = "https://wsrv.nl/?url=" + urllib.parse.quote(raw_u, safe="") + "&w=320&q=80" if raw_u else ""
        short_u = html.escape(raw_u[:62] + ("…" if len(raw_u) > 62 else ""))
        badge = "VIDEO" if pic.get("isVideo") else f"SCONTENT #{idx + 1}"
        tiles_html += f"""
        <div class="tile">
          <div class="thumb"><img src="{html.escape(prox)}" alt="Slide {idx+1}"><span class="tag">{badge}</span></div>
          <div class="u">{short_u}</div>
        </div>
        """
    if not tiles_html:
        tiles_html = """
        <div class="empty-state">
          <div style="font-size:15px;font-weight:600;color:#edf4f3;margin-bottom:6px">Logged-in Instagram Session Ready</div>
          <div>Your session stays active in the background while you browse posts in <strong>Saved posts</strong> and <strong>Save via URL</strong>, or paste any <code>instagram.com/p/…</code> link in the address bar above to inspect its <code>scontent</code> URLs here.</div>
        </div>
        """

    return f"""<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Instagram — Logged in as @{uname}</title>
<style>
  * {{ box-sizing: border-box; }}
  body {{
    margin: 0; padding: 18px 22px;
    background: #0d1213; color: #edf4f3;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  }}
  .top {{
    display: flex; align-items: center; justify-content: space-between;
    padding: 12px 16px; background: #151e20; border: 1px solid #2b3d3e;
    border-radius: 10px; margin-bottom: 16px;
  }}
  .user-pill {{ display: flex; align-items: center; gap: 10px; }}
  .dot {{ width: 10px; height: 10px; border-radius: 50%; background: #4ade80; box-shadow: 0 0 8px #4ade80; }}
  .meta {{ font-size: 12px; color: #9daead; }}
  .grid {{ display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; }}
  .tile {{ background: #151d1e; border: 1px solid #2b3c3d; border-radius: 9px; overflow: hidden; }}
  .thumb {{ height: 155px; position: relative; background: #0a0e0f; }}
  .thumb img {{ width: 100%; height: 100%; object-fit: cover; display: block; }}
  .tag {{
    position: absolute; top: 7px; left: 7px;
    background: rgba(12, 18, 19, 0.82); color: #a6eddf;
    padding: 3px 7px; border-radius: 5px; font-size: 10px; font-weight: 700;
  }}
  .u {{ padding: 7px 8px; font-size: 10px; color: #8fa3a1; font-family: monospace; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }}
  .empty-state {{
    grid-column: 1 / -1; padding: 48px 24px; text-align: center;
    background: #131b1c; border: 1px dashed #2e4345; border-radius: 12px;
    color: #9daead; font-size: 13px; line-height: 1.6;
  }}
</style>
</head>
<body>
  <div class="top">
    <div class="user-pill">
      <span class="dot"></span>
      <div>
        <div style="font-size:13px;font-weight:700">Logged in as @{uname} · Persistent Session Active</div>
        <div class="meta">{post_title} · {post_loc} {f'· {len(pics)} media items ({post_sc})' if pics else ''}</div>
      </div>
    </div>
    <div style="font-size:11px;color:#a6eddf;font-weight:600">scontent Direct Extraction ON</div>
  </div>
  <div class="grid">{tiles_html}</div>
</body>
</html>"""


class BrowserSessionManager:
    def __init__(self, post_resolver_fn=None) -> None:
        self._cmd_queue: queue.Queue = queue.Queue()
        self._thread: threading.Thread | None = None
        self._lock = threading.Lock()
        self._post_resolver_fn = post_resolver_fn
        self._last_state: dict = {}

    def set_post_resolver(self, fn) -> None:
        self._post_resolver_fn = fn

    def _ensure_started(self) -> None:
        with self._lock:
            if self._thread is None or not self._thread.is_alive():
                self._thread = threading.Thread(target=self._worker_loop, daemon=True)
                self._thread.start()

    def run_action(self, action: dict, timeout: float = 15.0) -> dict:
        self._ensure_started()
        reply_q: queue.Queue = queue.Queue(maxsize=1)
        self._cmd_queue.put((action, reply_q))
        try:
            return reply_q.get(timeout=timeout)
        except Exception as e:
            return {
                "ok": False,
                "error": str(e),
                **load_ig_session(),
            }

    def _worker_loop(self) -> None:
        ensure_shared_data_cookies()
        current_post_preview: dict | None = None

        def resolve_preview_for_url(u: str) -> dict | None:
            if not u or "instagram.com" not in u:
                return None
            m_sc = re.search(r"instagram\.com/(?:[^/?#]+/)?(?:p|reel|reels|tv)/([^/?#]+)", u)
            if m_sc and self._post_resolver_fn:
                try:
                    return self._post_resolver_fn(u)
                except Exception:
                    return None
            return None

        init_url = IG_SESSION.get("currentUrl") or "https://www.instagram.com/accounts/login/"
        current_post_preview = resolve_preview_for_url(init_url)

        def capture_state(extra: dict | None = None) -> dict:
            sess = load_ig_session()
            authed = bool(sess.get("authenticated"))
            uname = sess.get("username") or ""
            cur_url = sess.get("currentUrl") or "https://www.instagram.com/accounts/login/"
            pics = (current_post_preview or {}).get("pictures") or []
            scontent_urls = [
                str(pic.get("rawUrl") or pic.get("imgUrl") or "")
                for pic in pics
                if isinstance(pic, dict) and not pic.get("isVideo") and (pic.get("rawUrl") or pic.get("imgUrl"))
            ]
            st = {
                "ok": True,
                "authenticated": authed,
                "loggedIn": authed,
                "username": uname,
                "userId": sess.get("userId") or "",
                "twoFactorPending": bool(sess.get("twoFactorPending")),
                "twoFactorRequired": bool(sess.get("twoFactorPending")),
                "twoFactorIdentifier": sess.get("twoFactorIdentifier") or "",
                "statusMessage": sess.get("statusMessage") or "",
                "message": sess.get("statusMessage") or "",
                "currentUrl": cur_url,
                "url": cur_url,
                "title": f"Instagram — Logged in as @{uname}" if authed else "Login • Instagram",
                "scontentCount": len(scontent_urls),
                "scontentUrls": scontent_urls,
                "postPreview": current_post_preview,
                "resolvedPost": current_post_preview,
                "extracted": current_post_preview,
            }
            if extra:
                st.update(extra)
                if "statusMessage" in extra and "message" not in extra:
                    st["message"] = extra["statusMessage"]
                if "authenticated" in extra:
                    st["loggedIn"] = bool(extra["authenticated"])
            self._last_state = st
            return st

        while True:
            action, reply_q = self._cmd_queue.get()
            try:
                atype = str(action.get("type") or action.get("action") or "state").lower()
                extra_out: dict = {}

                if atype in ("navigate", "extract_scontent", "extract"):
                    target = str(action.get("url") or IG_SESSION.get("currentUrl") or "https://www.instagram.com/").strip()
                    if not target.startswith("http"):
                        target = "https://" + target.lstrip("/")
                    with SESSION_LOCK:
                        IG_SESSION["currentUrl"] = target
                    save_ig_session()
                    current_post_preview = resolve_preview_for_url(target)
                    if current_post_preview and isinstance(current_post_preview, dict):
                        pics = current_post_preview.get("pictures") or []
                        photo_cnt = sum(1 for p in pics if isinstance(p, dict) and not p.get("isVideo"))
                        extra_out["statusMessage"] = f"Loaded {photo_cnt} photo(s) from {target}"
                    else:
                        extra_out["statusMessage"] = f"Opened {target}"

                elif atype in ("login", "two_factor"):
                    u_val = str(action.get("username") or "").strip()
                    p_val = str(action.get("password") or "")
                    tf_val = str(action.get("twoFactorCode") or action.get("verificationCode") or "").strip()
                    res = perform_direct_ig_login(u_val, p_val, tf_val)
                    extra_out.update(res)

                elif atype in ("cookie", "set_cookie"):
                    raw_ck = str(action.get("cookie") or "").strip()
                    uname = str(action.get("username") or "").strip().lstrip("@") or "instagram_user"
                    parsed_cks = parse_cookie_input(raw_ck)
                    if not parsed_cks.get("sessionid"):
                        extra_out = {"ok": False, "error": "Please provide a valid Instagram sessionid cookie."}
                    else:
                        merged = dict(IG_SESSION.get("cookies") or {})
                        merged.update(parsed_cks)
                        with SESSION_LOCK:
                            IG_SESSION["authenticated"] = True
                            IG_SESSION["username"] = uname
                            IG_SESSION["userId"] = merged.get("ds_user_id", "")
                            IG_SESSION["cookies"] = merged
                            IG_SESSION["twoFactorPending"] = False
                            IG_SESSION["currentUrl"] = "https://www.instagram.com/"
                            IG_SESSION["statusMessage"] = f"Logged in as @{uname} via sessionid cookie"
                        save_ig_session()

                elif atype == "logout":
                    current_post_preview = None
                    with SESSION_LOCK:
                        IG_SESSION["authenticated"] = False
                        IG_SESSION["username"] = ""
                        IG_SESSION["userId"] = ""
                        IG_SESSION["cookies"] = {}
                        IG_SESSION["twoFactorPending"] = False
                        IG_SESSION["twoFactorIdentifier"] = ""
                        IG_SESSION["currentUrl"] = "https://www.instagram.com/accounts/login/"
                        IG_SESSION["statusMessage"] = "Logged out"
                    save_ig_session()
                    ensure_shared_data_cookies()

                reply_q.put(capture_state(extra_out))
            except Exception as err:
                reply_q.put(capture_state({"ok": False, "error": str(err)}))


BROWSER_MGR = BrowserSessionManager()
