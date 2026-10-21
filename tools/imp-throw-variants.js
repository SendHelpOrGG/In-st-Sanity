/* ===========================================================================
   In[st]Sanity — the five "devour the JSON, then show the posts" cinematics.

   Ported from sticker_json_throw_variants_5_animations.html, the reference
   build the user supplied. The Clawed Devil-Palms animation (variant 2) is
   the one this app already shipped; the other four are ported here and all
   five are picked at random on import, one in five each.

   The maths and choreography are the reference's, verbatim. Only the DOM
   layer is adapted: the reference's own sticker ids are mapped onto this
   app's themed sticker art, its sample grid onto the real #postGrid cards,
   and its fx layer onto #jsonThrowFxLayer.

   Debug: ?jsonThrow=1|2|3|4|5 or localStorage['stillSane.jsonThrowVariant']
   pins a variant instead of rolling the dice.
   =========================================================================== */
(function(){
'use strict';
if(window.LS_JSON_THROW) return;

var animRunId = 0;
var IMP = null, FX = null;
var slot = null, svg = null, tetherSvg = null, badge = null, fxLayer = null;
var stageEl = null, mainEl = null, watchdog = null;

var NS = 'http://www.w3.org/2000/svg';

/* ---------- the reference build's easing ---------- */
const easeOutCubic = t => 1 - Math.pow(1 - t, 3);
const easeInOutCubic = t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
function bezierPoint(p0, p1, p2, t) {
  const u = 1 - t;
  return {
    x: u * u * p0.x + 2 * u * t * p1.x + t * t * p2.x,
    y: u * u * p0.y + 2 * u * t * p1.y + t * t * p2.y
  };
}

/* ---------- the reference's drawing helpers ---------- */
    function renderClawedPalm1B(x, y, angleDeg, scale, slash = 0, mirrorY = 1, strokeCol = 'var(--t-accent)') {
      const talonExt = 20 * (1 - slash * 0.3);
      return `
        <g transform="translate(${x.toFixed(1)}, ${y.toFixed(1)}) rotate(${angleDeg.toFixed(1)}) scale(${scale.toFixed(2)}, ${(scale * mirrorY).toFixed(2)})">
          <!-- Palm & knuckles -->
          <path d="M -18 -11 C -6 -16, 7 -15, 12 -10 L 12 10 C 7 15, -6 16, -18 11 Z"
                fill="rgba(15,23,42,0.92)" stroke="${strokeCol}" stroke-width="2.8"/>
          <!-- Thumb Talon -->
          <path d="M -3 -13 Q 6 -25 ${(16 - slash * 5).toFixed(1)} ${(-20 + slash * 8).toFixed(1)} Q 8 -14 2 -11 Z"
                fill="#ffffff" stroke="${strokeCol}" stroke-width="1.8"/>
          <!-- 3 Sharp Curved Devil Claws -->
          <path d="M 10 -10 Q ${(20 + talonExt * 0.5).toFixed(1)} -14 ${(14 + talonExt).toFixed(1)} ${(-6 + slash * 8).toFixed(1)} Q 18 -4 10 -4 Z"
                fill="#ffffff" stroke="${strokeCol}" stroke-width="1.8"/>
          <path d="M 12 -3 Q ${(22 + talonExt * 0.6).toFixed(1)} -2 ${(17 + talonExt).toFixed(1)} ${(1 + slash * 4).toFixed(1)} Q 19 3 12 3 Z"
                fill="#ffffff" stroke="${strokeCol}" stroke-width="1.8"/>
          <path d="M 10 4 Q ${(20 + talonExt * 0.5).toFixed(1)} 14 ${(14 + talonExt).toFixed(1)} ${(8 - slash * 6).toFixed(1)} Q 18 10 10 10 Z"
                fill="#ffffff" stroke="${strokeCol}" stroke-width="1.8"/>
          <!-- Glowing Devil Palm Sigil -->
          <polygon points="-8,0 -1,-6 6,0 -1,6" fill="${strokeCol}"/>
          <circle cx="-1" cy="0" r="2.2" fill="#ffffff"/>
        </g>
      `;
    }

    // ===== Cinematic helpers (used by 2A / 2B) =====
    const f1 = n => n.toFixed(1);
    const f2 = n => n.toFixed(2);
    const lerp = (a, b, k) => a + (b - a) * k;
    function bez3(a, b, c, d, t) {
      const u = 1 - t;
      return { x: u*u*u*a.x + 3*u*u*t*b.x + 3*u*t*t*c.x + t*t*t*d.x, y: u*u*u*a.y + 3*u*u*t*b.y + 3*u*t*t*c.y + t*t*t*d.y };
    }
    function cineFrame(a) {
      const bar = a * 48, h = window.innerHeight;
      return `<defs><radialGradient id="cvg" cx="50%" cy="50%" r="75%"><stop offset="55%" stop-color="#000" stop-opacity="0"/><stop offset="100%" stop-color="#000" stop-opacity=".55"/></radialGradient></defs>
        <rect width="100%" height="100%" fill="url(#cvg)" opacity="${f2(a)}"/>
        <rect width="100%" height="${f1(bar)}" fill="#000"/><rect y="${f1(h - bar)}" width="100%" height="${f1(bar)}" fill="#000"/>`;
    }
    const flash = o => o > 0.01 ? `<rect width="100%" height="100%" fill="#fff" opacity="${f2(o)}"/>` : '';
    function arm(ix, iy, cx, cy, bend, col) {
      return `<path d="M ${f1(ix)} ${f1(iy)} Q ${f1((ix + cx) / 2)} ${f1(Math.min(iy, cy) + bend)} ${f1(cx)} ${f1(cy)}" stroke="${col}" stroke-width="5" stroke-linecap="round" fill="none"/>`;
    }

    // ===== Arms that grow from imaginary shoulders either side of the head =====
    function shoulder(side) {
      const id = 'imp-sh-' + side;
      let el = document.getElementById(id);
      if (!el) {
        el = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        el.setAttribute('id', id); el.setAttribute('class', 'imp-extra');
        el.setAttribute('cx', side === 'L' ? 34 : 126); el.setAttribute('cy', 118);
        el.setAttribute('r', 0.6); el.setAttribute('fill', 'none');
        IMP.svg.appendChild(el);
      }
      const r = el.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    }
    function armS(side, cx, cy, col, w) {
      const s = shoulder(side), d = Math.hypot(cx - s.x, cy - s.y), sag = (side === 'L' ? 1 : 0.8) * Math.min(60, 14 + d * 0.14);
      return `<path d="M ${f1(s.x)} ${f1(s.y)} Q ${f1((s.x + cx) / 2)} ${f1(Math.max(s.y, cy) + sag)} ${f1(cx)} ${f1(cy)}" stroke="${col}" stroke-width="${w || 5}" stroke-linecap="round" fill="none"/><circle cx="${f1(s.x)}" cy="${f1(s.y)}" r="5.5" fill="${col}"/>`;
    }
    function ring(cx, cy, r, w, col, o) {
      return `<circle cx="${f1(cx)}" cy="${f1(cy)}" r="${f1(Math.max(0, r))}" fill="none" stroke="${col}" stroke-width="${f1(Math.max(0, w))}" opacity="${f2(Math.max(0, o))}"/>`;
    }
    function burst(cx, cy, p, n, col, r0, len) {
      let s = '';
      for (let i = 0; i < n; i++) {
        const a = i * 6.2832 / n + 0.3, d = r0 + p * len * 1.6, e = d + len * (1 - p);
        s += `<line x1="${f1(cx + Math.cos(a) * d)}" y1="${f1(cy + Math.sin(a) * d)}" x2="${f1(cx + Math.cos(a) * e)}" y2="${f1(cy + Math.sin(a) * e)}" stroke="${i % 2 ? '#fff' : col}" stroke-width="${f1(3 * (1 - p))}" stroke-linecap="round" opacity="${f2(1 - p)}"/>`;
      }
      return s;
    }
    function landFx(pr, now) {
      const k = (now - pr.landT) / 340;
      if (k >= 1) return '';
      const c = pr.target;
      return ring(c.x, c.y, 10 + k * 70, 4 * (1 - k), '#fff', 1 - k) + ring(c.x, c.y, 6 + k * 44, 3 * (1 - k), 'var(--t-accent2)', 1 - k) + burst(c.x, c.y, k, 8, 'var(--t-accent)', 16, 22);
    }
    function landCard(pr, now) {
      pr.landed = true; pr.landT = now; pr.el.style.opacity = '0';
      pr.target.el.classList.add('card-throw-landed', 'card-throw-flash');
      setTimeout(() => pr.target.el.classList.remove('card-throw-flash'), 420);
      const m = mainEl;
      if (m) m.style.transform = `translate(${(Math.random() * 6 - 3).toFixed(1)}px, ${(Math.random() * 6 - 3).toFixed(1)}px)`;
      setTimeout(() => { if (m) m.style.transform = ''; }, 70);
    }
    const allLanded = now => projectiles => projectiles.every(p => p.landed && now - p.landT > 340);

    function star(x, y, r, rot, col, o) {
      const p = [];
      for (let i = 0; i < 8; i++) { const a = i * Math.PI / 4 + rot * 0.01745, d = i % 2 ? r * 0.28 : r; p.push(`${f1(x + Math.cos(a) * d)},${f1(y + Math.sin(a) * d)}`); }
      return `<polygon points="${p.join(' ')}" fill="${col}" opacity="${f2(Math.max(0, Math.min(1, o)))}"/>`;
    }

    function setFlash(o) {
      const el = FX.flash;
      if (el) el.style.opacity = String(Math.max(0, Math.min(1, o)));
    }
    function setVignette(o) {
      const el = FX.vignette;
      if (el) el.style.opacity = String(Math.max(0, Math.min(1, o)));
    }
    function shakeScene(amp, timeMs) {
      const s = `translate3d(${(Math.sin(timeMs * 0.09) * amp).toFixed(2)}px, ${(Math.cos(timeMs * 0.115) * amp).toFixed(2)}px, 0)`;
      const stage = stageEl;
      const fx = fxLayer;
      if (stage) stage.style.transform = amp > 0.05 ? s : '';
      if (fx) fx.style.transform = amp > 0.05 ? s : '';
    }
    function fxRing(cx, cy, r, w, col, op, dash = 'none') {
      return `<circle cx="${cx.toFixed(1)}" cy="${cy.toFixed(1)}" r="${Math.max(0, r).toFixed(1)}" fill="none" stroke="${col}" stroke-width="${Math.max(0.2, w).toFixed(2)}" stroke-dasharray="${dash}" opacity="${Math.max(0, op).toFixed(2)}"/>`;
    }
    function fxStarburst(cx, cy, rOut, rIn, spikes, rotDeg, col, op, fillOp = 0) {
      let poly = '';
      for (let i = 0; i < spikes * 2; i++) {
        const a = (i * Math.PI) / spikes + rotDeg * 0.0175;
        const r = i % 2 === 0 ? rOut : rIn;
        poly += `${(cx + Math.cos(a) * r).toFixed(1)},${(cy + Math.sin(a) * r).toFixed(1)} `;
      }
      return `<polygon points="${poly}" fill="${col}" fill-opacity="${Math.max(0, Math.min(1, fillOp)).toFixed(2)}" stroke="${col}" stroke-width="2.2" stroke-linejoin="round" opacity="${Math.max(0, op).toFixed(2)}"/>`;
    }
    function fxSparks(cx, cy, prog, n, spread, seed, colA = '#ffffff', colB = 'var(--t-accent)') {
      let out = '';
      const e = easeOutCubic(prog);
      for (let i = 0; i < n; i++) {
        const a = seed + i * 2.399 + prog * 1.9;
        const d = spread * e * (0.55 + 0.45 * ((i * 7919 % 13) / 13));
        const x = cx + Math.cos(a) * d;
        const y = cy + Math.sin(a) * d;
        const s = (1 - prog) * 4.6 + 1.1;
        const col = i % 2 ? colB : colA;
        out += `<g transform="translate(${x.toFixed(1)}, ${y.toFixed(1)}) rotate(${((a * 57.3 + prog * 120).toFixed(1))})" opacity="${(1 - prog).toFixed(2)}">
          <path d="M 0 -${s.toFixed(1)} L ${(s * 0.28).toFixed(1)} -${(s * 0.28).toFixed(1)} L ${s.toFixed(1)} 0 L ${(s * 0.28).toFixed(1)} ${(s * 0.28).toFixed(1)} L 0 ${s.toFixed(1)} L -${(s * 0.28).toFixed(1)} ${(s * 0.28).toFixed(1)} L -${s.toFixed(1)} 0 L -${(s * 0.28).toFixed(1)} -${(s * 0.28).toFixed(1)} Z" fill="${col}"/>
        </g>`;
      }
      return out;
    }
    function fxCrackle(x1, y1, x2, y2, seed, amp, col, op) {
      let d = `M ${x1.toFixed(1)} ${y1.toFixed(1)}`;
      const segs = 5;
      for (let i = 1; i <= segs; i++) {
        const tt = i / segs;
        const nx = x1 + (x2 - x1) * tt;
        const ny = y1 + (y2 - y1) * tt;
        const j = i < segs ? Math.sin(seed * 7.3 + i * 3.1) * amp : 0;
        const px = x1 + (x2 - x1) * (tt - 1 / segs);
        const py = y1 + (y2 - y1) * (tt - 1 / segs);
        const mx = (nx + px) / 2 + j;
        const my = (ny + py) / 2 - j * 0.6;
        d += ` L ${mx.toFixed(1)} ${my.toFixed(1)} L ${nx.toFixed(1)} ${ny.toFixed(1)}`;
      }
      return `<path d="${d}" fill="none" stroke="${col}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" opacity="${Math.max(0, Math.min(1, op)).toFixed(2)}"/>`;
    }
    function fxStreak(x1, y1, x2, y2, w, col, op) {
      return `<line x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" stroke="${col}" stroke-width="${w.toFixed(1)}" stroke-linecap="round" opacity="${Math.max(0, Math.min(1, op)).toFixed(2)}"/>`;
    }
    function fxShard(x, y, rotDeg, size, col, op) {
      return `<g transform="translate(${x.toFixed(1)}, ${y.toFixed(1)}) rotate(${rotDeg.toFixed(1)})" opacity="${Math.max(0, op).toFixed(2)}">
        <polygon points="${(-size).toFixed(1)},${(-size * 0.55).toFixed(1)} ${(size * 0.7).toFixed(1)},${(-size).toFixed(1)} ${size.toFixed(1)},${(size * 0.6).toFixed(1)} ${(-size * 0.5).toFixed(1)},${size.toFixed(1)}"
          fill="rgba(15,23,42,0.92)" stroke="${col}" stroke-width="1.8" stroke-linejoin="round"/>
      </g>`;
    }

    function clamp01(v) { return Math.max(0, Math.min(1, v)); }

    function renderSnapArm(sx, sy, hx, hy, strokeCol = 'var(--t-accent)') {
      const wristX = hx - 23, wristY = hy + 2;
      return `
        <g>
          <line x1="${sx.toFixed(1)}" y1="${sy.toFixed(1)}" x2="${wristX.toFixed(1)}" y2="${wristY.toFixed(1)}" stroke="var(--t-accent2)" stroke-width="11" stroke-linecap="round" opacity="0.28"/>
          <line x1="${sx.toFixed(1)}" y1="${sy.toFixed(1)}" x2="${wristX.toFixed(1)}" y2="${wristY.toFixed(1)}" stroke="${strokeCol}" stroke-width="5.5" stroke-linecap="round"/>
          <line x1="${sx.toFixed(1)}" y1="${sy.toFixed(1)}" x2="${wristX.toFixed(1)}" y2="${wristY.toFixed(1)}" stroke="#ffffff" stroke-width="2.6" stroke-linecap="round" opacity="0.9"/>
          <ellipse cx="${sx.toFixed(1)}" cy="${sy.toFixed(1)}" rx="9" ry="8" fill="rgba(15,23,42,0.98)" stroke="${strokeCol}" stroke-width="2.6"/>
          <circle cx="${sx.toFixed(1)}" cy="${sy.toFixed(1)}" r="3.2" fill="var(--t-accent2)" opacity="0.88"/>
        </g>
      `;
    }

    // Variant B side-view hand for animation #3 only; the original shoulder arm still lands on its wrist.
    function snapHandVariantBMetrics(x, y, progress) {
      const p = clamp01(progress);
      const smooth = value => {
        const q = clamp01(value);
        return q * q * (3 - 2 * q);
      };
      const load = smooth(p / 0.28);
      const release = smooth((p - 0.45) / 0.10);
      const settle = smooth((p - 0.68) / 0.20);
      const scale = 0.78; // Selected B hand enlarged exactly 20%; wrist remains anchored to the forearm.
      const angleDeg = 74 + 2.5 * release; // B's -16° adjustment plus the study's 90° clockwise turn.
      const angle = angleDeg * Math.PI / 180;
      const sin = Math.sin(angle), cos = Math.cos(angle);
      const groupX = x - 23 + 55 * scale * sin;
      const groupY = y + 2 - 55 * scale * cos;
      const contactX = groupX + scale * (-25 * cos + 39 * sin);
      const contactY = groupY + scale * (-25 * sin - 39 * cos);
      const contactPulse = Math.max(0, 1 - Math.abs(p - 0.52) / 0.16);
      return {
        p, load, release, settle, scale, angleDeg, groupX, groupY, contactX, contactY, contactPulse,
        middleLoaded: load * (1 - release),
        middleFlick: release * (1 - settle),
        middleRest: clamp01(1 - load + settle),
        thumbLoaded: load * (1 - release),
        thumbOpen: clamp01(1 - load + release),
        snapDot: load * (1 - release)
      };
    }

    function renderSnapHandVariantB(m, strokeCol = 'var(--t-accent)', accent2 = 'var(--t-accent2)') {
      const foldSpacing = 10.5; // Compact row from selected variant B.
      const slots = { pinky: -1.5 * foldSpacing, ring: -0.5 * foldSpacing, middle: 0.5 * foldSpacing, index: 1.5 * foldSpacing };
      const folded = cx => `M ${cx.toFixed(1)} -18 C ${(cx - 6).toFixed(1)} -17 ${(cx - 8).toFixed(1)} -10 ${(cx - 4).toFixed(1)} -5 Q ${(cx - 1).toFixed(1)} -2 ${(cx + 4).toFixed(1)} -6`;
      const tube = (d, color, outer = 13, inner = 8.4) => `
        <path d="${d}" fill="none" stroke="#09111e" stroke-width="${outer}" stroke-linecap="round" stroke-linejoin="round"/>
        <path d="${d}" fill="none" stroke="#f8fafc" stroke-width="${inner}" stroke-linecap="round" stroke-linejoin="round"/>
        <path d="${d}" fill="none" stroke="${color}" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>`;
      const fingertip = (cx, cy, color, radius = 3.1) => `<circle cx="${cx.toFixed(1)}" cy="${cy.toFixed(1)}" r="${radius}" fill="#ffffff" stroke="${color}" stroke-width="2"/>`;
      const palmW = 18 * 1.04;
      const palmD = `M ${(-palmW).toFixed(1)} -18 C ${(-palmW - 3).toFixed(1)} -7 ${(-palmW - 2).toFixed(1)} 9 -6 26 L -3 40 Q 0 47 3 40 L 6 26 C ${(palmW + 2).toFixed(1)} 9 ${(palmW + 3).toFixed(1)} -7 ${palmW.toFixed(1)} -18 Q 0 -24 ${(-palmW).toFixed(1)} -18 Z`;
      const middleLoadPath = 'M -4 -22 C -10 -33 -17 -39 -23 -40 Q -30 -42 -31 -36 Q -29 -32 -25 -39';
      const middleFlickPath = 'M -4 -22 C -8 -15 -5 -7 3 -3 Q 11 1 12 -6';
      const thumbLoadPath = 'M 9 16 C 8 7 2 -1 -8 -8 C -17 -15 -23 -26 -25 -39';
      const thumbOpenPath = 'M 9 16 C 8 7 1 -1 -9 -9 C -16 -16 -19 -24 -20 -31';
      const opacity = value => clamp01(value).toFixed(3);
      return `
        <g data-part="mainSnapHandB" transform="translate(${m.groupX.toFixed(1)} ${m.groupY.toFixed(1)}) rotate(${m.angleDeg.toFixed(1)}) scale(${m.scale.toFixed(2)})">
          <!-- Side-view palm and wrist cuff; geometry follows the selected B study. -->
          <path d="${palmD}" fill="rgba(15,23,42,0.98)" stroke="${strokeCol}" stroke-width="3" stroke-linejoin="round"/>
          <path d="M -5 5 Q 0 2 5 5 M -4 17 Q 0 14 4 17" fill="none" stroke="#71849d" stroke-width="1.4" stroke-linecap="round" opacity="0.6"/>
          <path d="M -13 37 Q 0 43 13 37 L 12 52 Q 0 59 -12 52 Z" fill="#101a2b" stroke="${strokeCol}" stroke-width="2.6" stroke-linejoin="round"/>
          <path d="M -8 42 Q 0 46 8 42" fill="none" stroke="#ffffff" stroke-width="1.2" opacity="0.75"/>
          <!-- Four distinct folded fingers; the middle returns to its own matching slot after the snap. -->
          <g data-part="fold-pinky">${tube(folded(slots.pinky), strokeCol, 10, 6.6)}${fingertip(slots.pinky + 4, -6, strokeCol)}</g>
          <g data-part="fold-ring">${tube(folded(slots.ring), accent2, 10, 6.6)}${fingertip(slots.ring + 4, -6, accent2)}</g>
          <g data-part="fold-index">${tube(folded(slots.index), strokeCol, 10, 6.6)}${fingertip(slots.index + 4, -6, strokeCol)}</g>
          <g data-part="middle-loaded" opacity="${opacity(m.middleLoaded)}">${tube(middleLoadPath, strokeCol)}</g>
          <g data-part="middle-flick" opacity="${opacity(m.middleFlick)}">${tube(middleFlickPath, strokeCol)}${fingertip(12, -6, strokeCol)}</g>
          <g data-part="middle-rest" opacity="${opacity(m.middleRest)}">${tube(folded(slots.middle), strokeCol, 10, 6.6)}${fingertip(slots.middle + 4, -6, strokeCol)}</g>
          <!-- Near/inner thumb rises above the palm to meet the middle finger. -->
          <ellipse cx="9" cy="16" rx="5.5" ry="6" fill="#152238" stroke="${accent2}" stroke-width="2"/>
          <g data-part="thumb-loaded" opacity="${opacity(m.thumbLoaded)}">${tube(thumbLoadPath, accent2, 14, 9)}</g>
          <g data-part="thumb-open" opacity="${opacity(m.thumbOpen)}">${tube(thumbOpenPath, accent2, 14, 9)}${fingertip(-20, -31, accent2)}</g>
          <g data-part="snap-dot" opacity="${opacity(m.snapDot)}"><circle cx="-25" cy="-39" r="4.2" fill="#ffffff" stroke="${accent2}" stroke-width="1.8"/></g>
          <g data-part="contact" opacity="${opacity(m.contactPulse)}" transform="translate(-25 -39) scale(${(0.88 + m.contactPulse * 0.35).toFixed(3)}) translate(25 39)">
            <circle cx="-25" cy="-39" r="9" fill="none" stroke="#ffffff" stroke-width="2.3"/>
            <circle cx="-25" cy="-39" r="14" fill="none" stroke="${accent2}" stroke-width="1.8" stroke-dasharray="3 4"/>
            <path d="M -25 -59 L -25 -53 M -45 -39 L -39 -39 M -5 -39 L -11 -39" stroke="#ffffff" stroke-width="2.2" stroke-linecap="round"/>
          </g>
          <circle cx="0" cy="-2" r="3.1" fill="${accent2}" opacity="0.85"/>
        </g>
      `;
    }

    // Exact Extending Trident + Base-Gripping Hand copied from the attached button-click reference.
    function renderExtendingTridentFromBaseHand(shX, shY, gripX, gripY, tipX, tipY, angleDeg, pressComp = 0) {
      const strokeMain = 'var(--t-accent)';
      const strokeSub = 'var(--t-accent2)';

      const totalShaftDist = Math.max(44, Math.hypot(tipX - gripX, tipY - gripY));
      const forkOffset = 38; // Fork starts 38px before the tip
      const shaftEndLocal = Math.max(18, totalShaftDist - forkOffset + pressComp * 4);

      // Decorative glowing energy rings spaced along the extending Trident shaft
      let shaftRings = '';
      const ringCount = Math.max(2, Math.min(9, Math.floor(shaftEndLocal / 65)));
      for (let i = 1; i <= ringCount; i++) {
        const rx = (shaftEndLocal * (i / (ringCount + 1))).toFixed(1);
        shaftRings += `<rect x="${rx}" y="-5.5" width="5" height="11" rx="2.2" fill="#ffffff" stroke="${strokeSub}" stroke-width="1.5"/>`;
      }

      return `
        <!-- Short forearm from Imp's invisible shoulder to his gripping hand right by his body -->
        <line x1="${shX.toFixed(1)}" y1="${shY.toFixed(1)}" x2="${gripX.toFixed(1)}" y2="${gripY.toFixed(1)}"
              stroke="${strokeSub}" stroke-width="11" stroke-linecap="round" opacity="0.28"/>
        <line x1="${shX.toFixed(1)}" y1="${shY.toFixed(1)}" x2="${gripX.toFixed(1)}" y2="${gripY.toFixed(1)}"
              stroke="${strokeMain}" stroke-width="5.5" stroke-linecap="round"/>

        <g transform="translate(${gripX.toFixed(1)}, ${gripY.toFixed(1)}) rotate(${angleDeg.toFixed(1)})">
          <!-- Trident Base / Pommel protruding just behind the Imp's gripping hand -->
          <line x1="-30" y1="0" x2="0" y2="0" stroke="#f8fafc" stroke-width="5" stroke-linecap="round"/>
          <polygon points="-42,0 -28,-8 -23,0 -28,8" fill="${strokeSub}" stroke="#ffffff" stroke-width="1.8"/>
          <circle cx="-28" cy="0" r="2.5" fill="#ffffff"/>

          <!-- EXTENDING TRIDENT SHAFT (Telescopes from the hand all the way to the button!) -->
          <line x1="0" y1="0" x2="${shaftEndLocal.toFixed(1)}" y2="0"
                stroke="${strokeSub}" stroke-width="13" stroke-linecap="round" opacity="0.32"/>
          <line x1="0" y1="0" x2="${shaftEndLocal.toFixed(1)}" y2="0"
                stroke="${strokeMain}" stroke-width="7" stroke-linecap="round"/>
          <line x1="0" y1="0" x2="${shaftEndLocal.toFixed(1)}" y2="0"
                stroke="#ffffff" stroke-width="2.6" stroke-linecap="round"/>
          ${shaftRings}

          <!-- Imp's Gripping Hand holding the Trident right near its base/start -->
          <g transform="rotate(180) scale(1.14)">
            <path d="M -13 -12 C -4 -16, 8 -15, 13 -10 L 13 10 C 8 15, -4 16, -13 12 Z"
                  fill="rgba(15,23,42,0.96)" stroke="${strokeMain}" stroke-width="2.6"/>
            <!-- 3 Clenched Knuckles wrapped over the base of the Trident shaft -->
            <rect x="-8" y="-13" width="6" height="9" rx="3" fill="#e2e8f0" stroke="${strokeMain}" stroke-width="1.6"/>
            <rect x="-1" y="-14" width="6" height="9" rx="3" fill="#ffffff" stroke="${strokeMain}" stroke-width="1.6"/>
            <rect x="6" y="-13" width="6" height="9" rx="3" fill="#e2e8f0" stroke="${strokeMain}" stroke-width="1.6"/>
            <!-- Thumb locking around shaft -->
            <path d="M -4 3 Q 5 14 12 6 Q 8 -1 -4 3 Z" fill="#ffffff" stroke="${strokeMain}" stroke-width="1.8"/>
            <polygon points="-6,0 0,-5 6,0 0,5" fill="${strokeSub}"/>
          </g>

          <!-- 3-Pronged Devil Trident Head at the far end of the extended shaft -->
          <g transform="translate(${shaftEndLocal.toFixed(1)}, 0) scale(1.16)">
            <!-- Demonic Cross-Guard Wings -->
            <path d="M 0 0 C 2 -18, 12 -24, 24 -20 L 32 -19" fill="none" stroke="${strokeMain}" stroke-width="4.2" stroke-linecap="round"/>
            <path d="M 0 0 C 2 18, 12 24, 24 20 L 32 19" fill="none" stroke="${strokeMain}" stroke-width="4.2" stroke-linecap="round"/>
            <!-- Glowing Core Gem at Trident Fork -->
            <polygon points="-7,0 2,-9 11,0 2,9" fill="${strokeSub}" stroke="#ffffff" stroke-width="1.8"/>
            <circle cx="2" cy="0" r="2.6" fill="#ffffff"/>

            <!-- Upper Barbed Prong -->
            <polygon points="24,-19 40,-20 30,-27 31,-20 22,-16" fill="#ffffff" stroke="${strokeSub}" stroke-width="1.8" stroke-linejoin="round"/>
            <!-- Center Spear Prong -->
            <line x1="9" y1="0" x2="31" y2="0" stroke="#ffffff" stroke-width="4"/>
            <polygon points="26,-7.5 44,0 26,7.5 30,0" fill="#ffffff" stroke="${strokeMain}" stroke-width="2" stroke-linejoin="round"/>
            <!-- Lower Barbed Prong -->
            <polygon points="24,19 40,20 30,27 31,20 22,16" fill="#ffffff" stroke="${strokeSub}" stroke-width="1.8" stroke-linejoin="round"/>
          </g>
        </g>
      `;
    }

    // =========================================================================

/* ---------- the sticker adapter ------------------------------------------
   The reference draws its own sticker with fixed ids (#impHeadGroup,
   #impEyes, #impMouthPath, #impBrowL/R, #impHaloL/R ...). This app renders
   one of twelve themed stickers from THEME_STICKER_SVGS instead, so the
   same anatomy is located by class and the few missing parts (halos, laugh
   mouth, a split pair of brows) are grafted on for the duration of a run,
   then taken away again.
   ---------------------------------------------------------------------- */
function buildImp(root){
  var head = root.querySelector('.imp-face-group');
  var horns = root.querySelector('.imp-horns');
  var eyes = root.querySelector('.imp-eyes-track');
  var mouth = root.querySelector('.imp-grin');
  if(!head || !horns || !eyes || !mouth) return null;
  var mouthPath = mouth.querySelector('path');
  if(!mouthPath) return null;

  var defs = document.createElementNS(NS, 'defs');
  defs.setAttribute('class', 'imp-extra');
  defs.innerHTML =
    '<linearGradient id="jsonThrowStrokeGrad" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" style="stop-color:var(--t-logo-c1,#38bdf8)"/>' +
      '<stop offset="50%" style="stop-color:var(--t-logo-c2,#818cf8)"/>' +
      '<stop offset="100%" style="stop-color:var(--t-logo-c3,#ec4899)"/>' +
    '</linearGradient>' +
    '<radialGradient id="jsonThrowEyeHalo" cx="50%" cy="50%" r="50%">' +
      '<stop offset="0%" stop-color="#ffffff" stop-opacity="0.96"/>' +
      '<stop offset="30%" style="stop-color:var(--t-logo-c1,#38bdf8)" stop-opacity="0.55"/>' +
      '<stop offset="70%" style="stop-color:var(--t-logo-c3,#ec4899)" stop-opacity="0.15"/>' +
      '<stop offset="100%" style="stop-color:var(--t-logo-c3,#ec4899)" stop-opacity="0"/>' +
    '</radialGradient>';
  root.insertBefore(defs, root.firstChild);

  var blink = eyes.querySelector('.imp-eyes-blink') || null;
  function makeHalo(cx){
    var c = document.createElementNS(NS, 'circle');
    c.setAttribute('class', 'imp-extra');
    c.setAttribute('cx', cx); c.setAttribute('cy', 83); c.setAttribute('r', 18);
    c.setAttribute('fill', 'url(#jsonThrowEyeHalo)');
    c.setAttribute('opacity', '0');
    eyes.insertBefore(c, blink);
    return c;
  }
  var haloL = makeHalo(68), haloR = makeHalo(92);

  /* some themes draw both brows as one path with two subpaths */
  var hornPaths = horns.querySelectorAll('path');
  var browL = hornPaths[0] || null, browR = hornPaths[1] || null;
  var browClone = null, hornD = browL ? browL.getAttribute('d') : null;
  if(browL && !browR){
    var d = hornD || '', cut = d.indexOf('M', 1);
    if(cut > 0){
      browClone = browL.cloneNode(false);
      browClone.setAttribute('class', 'imp-extra');
      browClone.setAttribute('d', d.slice(cut));
      browL.setAttribute('d', d.slice(0, cut));
      horns.appendChild(browClone);
      browR = browClone;
    } else {
      browR = browL;
    }
  }

  /* The page decorates every themed sticker with resting/expression pairs
     (decorateStickerEyeGlow); the idle expression engine draws the cackle
     into the "expr" halves. The cinematics reuse those same nodes so the
     imp's laugh is literally the sidebar's own, not a second drawing. An
     undecorated sticker (test harness, hand-made markup) falls back to a
     grafted group and the plain grin path. */
  var eyesBlink = eyes.querySelector('.imp-eyes-blink') || null;
  var defaultEyes = eyes.querySelector('.imp-default-eyes') || null;
  var exprEyes = eyes.querySelector('.imp-expr-eyes') || null;
  var defaultMouth = mouth.querySelector('.imp-default-mouth') || null;
  var exprMouth = mouth.querySelector('.imp-expr-mouth') || null;
  var laughMouth = null;
  if(!exprMouth){
    laughMouth = document.createElementNS(NS, 'g');
    laughMouth.setAttribute('class', 'imp-extra');
    mouth.appendChild(laughMouth);
  }

  return {
    svg: root, head: head, horns: horns, eyes: eyes, mouth: mouth,
    mouthPath: mouthPath, haloL: haloL, haloR: haloR,
    browL: browL, browR: browR,
    browLD: browL ? browL.getAttribute('d') : null,
    browRD: browR ? browR.getAttribute('d') : null,
    eyesBlink: eyesBlink, defaultEyes: defaultEyes, exprEyes: exprEyes,
    defaultMouth: defaultMouth, exprMouth: exprMouth, laughMouth: laughMouth,
    defs: defs, browClone: browClone, hornD: hornD,
    mouthD: mouthPath.getAttribute('d'),
    mouthStroke: mouthPath.getAttribute('stroke-width'),
    mouthOpacity: mouthPath.getAttribute('opacity')
  };
}

function resetImp(){
  if(!IMP) return;
  if(IMP.mouthPath && IMP.mouthD !== null) IMP.mouthPath.setAttribute('d', IMP.mouthD);
  if(IMP.mouthPath){
    if(IMP.mouthStroke !== null) IMP.mouthPath.setAttribute('stroke-width', IMP.mouthStroke);
    if(IMP.mouthOpacity !== null) IMP.mouthPath.setAttribute('opacity', IMP.mouthOpacity);
  }
  if(IMP.browL && IMP.hornD !== null) IMP.browL.setAttribute('d', IMP.hornD);
  if(IMP.browR && IMP.browR !== IMP.browL && IMP.browRD !== null) IMP.browR.setAttribute('d', IMP.browRD);
  [IMP.defs, IMP.haloL, IMP.haloR, IMP.laughMouth, IMP.browClone].forEach(function(el){
    try{ if(el && el.parentNode) el.parentNode.removeChild(el); }catch(_){}
  });
  try{
    svg.querySelectorAll('.imp-extra').forEach(function(el){ el.remove(); });
  }catch(_){}
  try{
    if(IMP.head) IMP.head.style.transform = '';
    if(IMP.horns) IMP.horns.style.transform = '';
    if(IMP.eyes) IMP.eyes.style.transform = '';
    if(IMP.mouth){ IMP.mouth.style.transform = ''; IMP.mouth.style.opacity = ''; }
    if(IMP.mouthPath) IMP.mouthPath.setAttribute('opacity', IMP.mouthOpacity !== null ? IMP.mouthOpacity : '1');
    if(IMP.defaultMouth) IMP.defaultMouth.style.display = '';
    if(IMP.exprMouth) IMP.exprMouth.innerHTML = '';
    if(IMP.defaultEyes) IMP.defaultEyes.style.display = '';
    if(IMP.exprEyes) IMP.exprEyes.innerHTML = '';
    if(IMP.eyesBlink) IMP.eyesBlink.style.transform = '';
    if(IMP.laughMouth){ IMP.laughMouth.innerHTML = ''; }
    if(IMP.haloL) IMP.haloL.setAttribute('opacity', '0');
    if(IMP.haloR) IMP.haloR.setAttribute('opacity', '0');
    if(svg) svg.style.transform = '';
  }catch(_){}
  IMP = null;
}

/* ---------- teardown (also called from the app's own cleanup) ---------- */
function cleanup(){
  if(watchdog){ clearTimeout(watchdog); watchdog = null; }
  try{ document.querySelectorAll('.json-throw-projectile').forEach(function(el){ el.remove(); }); }catch(_){}
  try{ if(tetherSvg) tetherSvg.innerHTML = ''; }catch(_){}
  try{ if(badge){ badge.style.opacity = '0'; badge.style.transform = ''; } }catch(_){}
  resetImp();
  try{ if(FX){ FX.flash.style.opacity = '0'; FX.vignette.style.opacity = '0'; } }catch(_){}
  try{
    if(FX){ if(FX.flash.parentNode) FX.flash.remove(); if(FX.vignette.parentNode) FX.vignette.remove(); }
  }catch(_){}
  FX = null;
  try{ if(stageEl) stageEl.style.transform = ''; }catch(_){}
  try{ if(fxLayer) fxLayer.style.transform = ''; }catch(_){}
  try{ if(slot) slot.classList.remove('throw-action-active'); }catch(_){}
  try{ document.body.classList.remove('json-throw-active'); }catch(_){}
  try{
    document.querySelectorAll('#postGrid .card').forEach(function(c){
      c.classList.remove('card-throw-landed', 'card-throw-flash', 'magic-appear', 'slam');
      c.style.transitionDuration = '';
      c.style.animationDuration = '';
      c.style.animationDelay = '';
    });
  }catch(_){}
  try{ if(typeof window.syncStickerEyesToLastCursor === 'function') window.syncStickerEyesToLastCursor(); }catch(_){}
}

/* the reference calls this at the end of every variant */
function cleanupProjectiles(){ cleanup(); }

/* ---------- the stage ---------- */
function injectStyle(){
  if(document.getElementById('lsJsonThrowVariantStyle')) return;
  var st = document.createElement('style');
  st.id = 'lsJsonThrowVariantStyle';
  st.textContent = [
    '.json-throw-cine-vignette{position:fixed;inset:0;pointer-events:none;opacity:0;',
    'background:radial-gradient(ellipse at 48% 46%,transparent 12%,rgba(2,4,10,.34) 58%,rgba(2,4,10,.76) 100%)}',
    '.json-throw-cine-flash{position:fixed;inset:0;pointer-events:none;opacity:0;',
    'background:radial-gradient(circle at 10% 62%,rgba(255,255,255,.96) 0%,rgba(255,255,255,.52) 7%,rgba(255,255,255,0) 30%);',
    'mix-blend-mode:screen}',
    /* the grid's cards are pinned by !important rules while json-throw-active,
       so the reveal animations play on the photo inside each card */
    'body.json-throw-active #postGrid .card.magic-appear .photo{animation:lsJsonCardMagic .76s cubic-bezier(.18,1.18,.34,1) both}',
    '@keyframes lsJsonCardMagic{0%{opacity:0;filter:blur(8px) saturate(1.8);transform:translate3d(0,8px,0) scale(.18) rotate(-13deg)}36%{opacity:1;filter:blur(0) saturate(1.6);transform:translate3d(0,-3px,0) scale(1.1) rotate(3deg)}68%{opacity:1;transform:translate3d(0,1px,0) scale(.98) rotate(-1deg)}100%{opacity:1;filter:none;transform:translate3d(0,0,0) scale(1) rotate(0)}}',
    'body.json-throw-active #postGrid .card.slam .photo{animation:lsJsonCardSlam .52s cubic-bezier(.16,.85,.3,1) both}',
    '@keyframes lsJsonCardSlam{0%{transform:translate3d(0,-10px,0) scale(1.13)}44%{transform:translate3d(0,3px,0) scale(.96)}70%{transform:translate3d(0,-1px,0) scale(1.02)}100%{transform:translate3d(0,0,0) scale(1)}}'
  ].join('');
  document.head.appendChild(st);
}

/* The one spot where a cinematic opens its black hole, measured as a fraction
   of the viewport: a little right of centre, on the horizontal middle.  Slow
   Smirk has always opened its hole here; Singularity Snap used to open one
   next to the file badge instead, so the two cinematics did not agree.  Both
   read this now, so they open in exactly the same place. */
function jsonThrowHoleSpot(){
  return { x: window.innerWidth * 0.57, y: window.innerHeight * 0.50 };
}

function play(variant, fileName, totalCount){
  var myRun = ++animRunId;
  injectStyle();
  try{ if(typeof window.cleanupImpJsonThrow === 'function') window.cleanupImpJsonThrow(); }catch(_){}
  /* cleanupImpJsonThrow calls back into cleanup(), which bumps nothing */
  myRun = ++animRunId;

  try{
    if(typeof stickerVisible !== 'undefined' && !stickerVisible) return;
    if(typeof stickerAnimEnabled !== 'undefined' && !stickerAnimEnabled) return;
    if(typeof impActionAnimEnabled !== 'undefined' && !impActionAnimEnabled) return;
    if(typeof stickerOpacityPercent !== 'undefined' && stickerOpacityPercent <= 0) return;
  }catch(_){}
  var appEl = document.querySelector('.app');
  if(appEl && appEl.classList.contains('sidebar-hidden')) return;

  slot = document.getElementById('sidebarStickerSlot');
  tetherSvg = document.getElementById('jsonThrowTetherSvg');
  badge = document.getElementById('jsonThrowFileBadge');
  fxLayer = document.getElementById('jsonThrowFxLayer');
  if(!slot || !tetherSvg || !badge || !fxLayer) return;
  svg = slot.querySelector('svg');
  if(!svg) return;

  var cards = Array.prototype.slice.call(document.querySelectorAll('#postGrid .card'));
  if(!cards.length) return;
  var measured = cards.map(function(el){
    var r = el.getBoundingClientRect();
    return { x: r.left + r.width * 0.5, y: r.top + r.height * 0.5,
             top: r.top, bottom: r.bottom,
             postIdx: String(el.dataset.postIndex || ''), el: el };
  });
  var cardTargets = measured.filter(function(m){ return m.bottom > 40 && m.top < window.innerHeight + 100; }).slice(0, 12);
  if(!cardTargets.length) cardTargets = measured.slice(0, 9);

  var fnameEl = document.getElementById('jsonThrowFileName');
  var fcountEl = document.getElementById('jsonThrowFileCount');
  if(fnameEl) fnameEl.textContent = fileName || 'saved_posts.json';
  if(fcountEl) fcountEl.textContent = totalCount + ' saved post' + (totalCount === 1 ? '' : 's');

  document.body.classList.add('json-throw-active');
  slot.classList.add('throw-action-active');
  slot.classList.remove('idle');

  IMP = buildImp(svg);
  if(!IMP){ cleanup(); return; }   // unknown sticker markup: leave the app as it was
  stageEl = appEl;
  mainEl = document.querySelector('.app > main') || document.querySelector('main');

  var slotRect = slot.getBoundingClientRect();
  var impPos = {
    x: (slotRect.width > 10 ? slotRect.left + slotRect.width * 0.5 : 122),
    y: (slotRect.height > 10 ? slotRect.top + slotRect.height * 0.52 : window.innerHeight * 0.72)
  };
  var startFilePos = { x: window.innerWidth * 0.54, y: window.innerHeight * 0.38 };

  FX = { flash: document.createElement('div'), vignette: document.createElement('div') };
  FX.vignette.className = 'json-throw-cine-vignette';
  FX.flash.className = 'json-throw-cine-flash';
  fxLayer.appendChild(FX.vignette);
  fxLayer.appendChild(FX.flash);

  var SAMPLE_POSTS = cardTargets.map(function(t){
    var img = t.el.querySelector('img');
    return { grad: (img && img.src) ? 'url("' + img.src + '")' : 'var(--t-brand-grad)' };
  });

  var tridentTiming = variant === 4;
  var grabEnd = tridentTiming ? 520 : 540;
  var munchEnd = tridentTiming ? 1550 : 900;
  var throwStart = tridentTiming ? 1550 : munchEnd;
  var throwStagger = tridentTiming ? 150 : 95;
  var throwDur = tridentTiming ? 1150 : 420;

  var projectiles = cardTargets.map(function(t, i){
    var el = document.createElement('div');
    el.className = 'json-throw-projectile';
    el.innerHTML = '<div class="proj-thumb"></div><div class="proj-bar"></div>';
    if(SAMPLE_POSTS[i]) el.firstChild.style.backgroundImage = SAMPLE_POSTS[i].grad;
    el.style.opacity = '0';
    fxLayer.appendChild(el);
    return { el: el, target: t, landed: false, startT: throwStart + i * throwStagger };
  });

  var startTime = performance.now();
  var eyes = IMP.eyes, mouth = IMP.mouth, haloL = IMP.haloL, haloR = IMP.haloR;

  watchdog = setTimeout(function(){ if(myRun === animRunId) cleanup(); }, 20000);


  function runA(){
  const G = 560, OP = 250, C0 = G + OP + 40, CP = 200, NC = 4, GU = C0 + NC * CP, SP = GU + 400, ST = 130, DUR = 560, CY = 106;
  const endFx = SP + 5 * ST + DUR;
  let outStart = 0;
  const OUT = 650;
  const GLY = ['{', '}', '[', ']', '"', ':', ','];
  projectiles.forEach((pr, i) => { pr.startT = SP + i * ST; });
  const bm = document.createElementNS('http://www.w3.org/2000/svg', 'g');
  bm.setAttribute('class', 'imp-extra');
  bm.innerHTML = '<g></g><circle cx="80" cy="106" r="0.6" fill="none"/>';
  // Draw the oversized open mouth over the eyes whenever the jaw overlaps them.
  eyes.parentNode.appendChild(bm);
  const bmDraw = bm.firstChild, anchorEl = bm.lastChild;
  const mouthSvg = (o, sc, lump) => {
    const rx = 18 + Math.min(o, 1.3) * 26, ry = 2 + o * 28, cy = CY + o * 3;
    let s = `<ellipse cx="80" cy="${f1(cy)}" rx="${f1(rx)}" ry="${f1(ry)}" fill="#12030c" stroke="url(#jsonThrowStrokeGrad)" stroke-width="3"/>`;
    if (ry > 9) s += `<ellipse cx="80" cy="${f1(cy + ry * 0.55)}" rx="${f1(rx * 0.55)}" ry="${f1(ry * 0.36)}" fill="var(--t-logo-c3)" opacity=".85"/>`;
    if (sc > 0.02) s += `<g transform="translate(80 ${f1(cy)}) rotate(${f1(Math.sin(sc * 9) * 12)}) scale(${f2(sc)})"><rect x="-14" y="-6" width="28" height="12" rx="3" fill="#0f172a" stroke="var(--t-accent)" stroke-width="1.4"/><text y="3" font-family="monospace" font-weight="900" font-size="8" fill="#fff" text-anchor="middle">{ }</text></g>`;
    for (let i = -3; i <= 3; i++) { const dx = i * rx * 0.28, y = cy - ry * Math.sqrt(Math.max(0, 1 - (dx / rx) ** 2)), w = rx * 0.1, h = Math.min(4 + o * 5, ry + 3); s += `<path d="M${f1(80 + dx - w)} ${f1(y)}L${f1(80 + dx + w)} ${f1(y)}L${f1(80 + dx)} ${f1(y + h)}Z" fill="#fff"/>`; }
    for (let i = -2; i <= 2; i++) { const dx = i * rx * 0.36, y = cy + ry * Math.sqrt(Math.max(0, 1 - (dx / rx) ** 2)), w = rx * 0.09, h = Math.min(3 + o * 3, ry + 2); s += `<path d="M${f1(80 + dx - w)} ${f1(y)}L${f1(80 + dx + w)} ${f1(y)}L${f1(80 + dx)} ${f1(y - h)}Z" fill="#fff"/>`; }
    if (lump !== null) s += `<circle cx="80" cy="${f1(lump)}" r="6" fill="var(--t-logo-c2)" stroke="#fff" stroke-width="1.5"/>`;
    return s;
  };
  const txt = (x, y, size, rot, s, o) => `<text x="${f1(x)}" y="${f1(y)}" font-size="${f1(size)}" font-weight="900" fill="#fff" stroke="var(--t-accent2)" stroke-width="5" paint-order="stroke" text-anchor="middle" transform="rotate(${rot} ${f1(x)} ${f1(y)})" opacity="${f2(o)}">${s}</text>`;

  function stepA(now) {
    if (myRun !== animRunId) return;
    const t = now - startTime;
    let o = 0, sc = 0, lump = null, c = -1, u = 0, cl = 0, tx = 0, ty = 0, rot = 0, zs = 1.1, rec = 0;
    let eyeS = 'translate3d(8px, 0, 0) scale(1.28)';
    if (t < G) {
      const p = t / G, reach = p < 0.4 ? easeOutCubic(p / 0.4) : 1;
      o = Math.max(0, (t - 250) / 310) * 0.4; tx = reach * 22; rot = reach * 9;
    } else if (t < C0) {
      const k = Math.min(1, (t - G) / OP);
      o = k < 1 ? 0.4 + 0.85 * easeOutCubic(k) : 1.25; sc = t >= G + OP ? 1 : 0;
      tx = lerp(22, 0, k); rot = lerp(9, -10, k); zs = lerp(1.1, 1.3, easeOutCubic(k)); eyeS = 'scale(1.4)';
    } else if (t < GU) {
      c = Math.floor((t - C0) / CP); u = ((t - C0) % CP) / CP;
      const cO = 0.08;
      o = u < 0.4 ? lerp(c === 0 ? 1.25 : 0.95, cO, (u / 0.4) ** 2) : (c === NC - 1 ? cO : lerp(cO, 0.95, easeOutCubic((u - 0.4) / 0.6)));
      sc = 1 - (c + Math.min(1, u / 0.4)) / NC;
      cl = u < 0.4 ? u / 0.4 : Math.max(0, 1 - (u - 0.4) / 0.3);
      zs = 1.3; rot = -10 + (c % 2 ? 3 : -3) * cl; tx = Math.sin(t * 0.12) * 2 * cl; ty = cl * 2;
      eyeS = `scale(1.3, ${f2(1.3 - 0.8 * cl)})`;
    } else if (t < SP) {
      const g = (t - GU) / (SP - GU);
      o = t < SP - 70 ? 0.06 : lerp(0.06, 1.15, ((t - (SP - 70)) / 70) ** 2);
      lump = g < 0.5 ? lerp(CY + 4, 138, g / 0.5) : null;
      zs = 1.3 + 0.12 * g; rot = lerp(-10, 0, g); tx = Math.sin(t * 0.1) * g * 5; ty = Math.cos(t * 0.13) * g * 3;
      eyeS = `scale(${f2(1.2 + g * 0.3)}, ${f2(g < 0.5 ? 0.5 : 1.3 + g * 0.2)})`;
    } else {
      const w = t - SP, idx = Math.min(5, Math.floor(w / ST)), kk = (w - idx * ST) / (idx < 5 ? ST : ST * 2.2);
      o = idx < 5 ? lerp(1.15, 0.32, easeOutCubic(Math.min(1, kk / 0.9))) : lerp(1.15, 0, easeOutCubic(Math.min(1, kk)));
      rec = Math.max(0, 1 - (w - idx * ST) / (ST * 0.5));
      zs = lerp(1.42, 1.12, Math.min(1, w / (5 * ST + 300))) + 0.06 * rec; tx = -9 * rec; rot = -7 * rec; eyeS = 'scale(1.25, 0.8)';
    }
    const oe = outStart ? easeInOutCubic(Math.min(1, (t - outStart) / OUT)) : 0;
    if (outStart) { tx *= 1 - oe; ty *= 1 - oe; rot *= 1 - oe; zs = lerp(zs, 1, oe); eyeS = `scale(${f2(lerp(1.25, 1, oe))}, ${f2(lerp(0.8, 1, oe))})`; }
    svg.style.transform = `translate3d(${f1(tx)}px, ${f1(ty)}px, 0) rotate(${f1(rot)}deg) scale(${f2(zs + 0.08 * cl)}, ${f2(zs - 0.1 * cl)})`;
    eyes.style.transform = eyeS;
    haloL.setAttribute('opacity', f2(1 - oe)); haloR.setAttribute('opacity', f2(1 - oe));
    const ar = anchorEl.getBoundingClientRect(), ma = { x: ar.left + ar.width / 2, y: ar.top + ar.height / 2 };
    bmDraw.innerHTML = o > 0.01 ? mouthSvg(o, sc, lump) : '';
    bm.setAttribute('opacity', f2(Math.min(1, o * 25)));
    mouth.style.opacity = f2(Math.max(0, Math.min(1, 1 - (o - 0.01) * 20)));
    let fx = cineFrame(Math.min(1, t / 300) * (1 - oe));

    if (t < G) {
      // SNATCH
      const p = t / G, reach = p < 0.4 ? easeOutCubic(p / 0.4) : 1, pull = p < 0.4 ? 0 : easeInOutCubic((p - 0.4) / 0.6), slash = p > 0.36 ? Math.min(1, (p - 0.36) / 0.16) : 0;
      const px = lerp(startFilePos.x, ma.x + 34, pull), py = lerp(startFilePos.y, ma.y, pull) - Math.sin(pull * Math.PI) * 60;
      badge.style.opacity = '1';
      badge.style.transform = `translate3d(${f1(px)}px, ${f1(py)}px, 0) rotate(${f1(-pull * 30)}deg) scale(${f1(1 - pull * 0.3)})`;
      const c1x = lerp(impPos.x, px - 24, reach), c1y = lerp(impPos.y - 20, py - 22, reach), c2x = lerp(impPos.x, px + 24, reach), c2y = lerp(impPos.y + 20, py + 22, reach);
      const ang = Math.atan2(py - impPos.y, px - impPos.x) * 57.3;
      fx += armS('L', c1x, c1y, 'var(--t-accent)') + armS('R', c2x, c2y, 'var(--t-accent2)')
        + renderClawedPalm1B(c1x, c1y, ang + 18, 1.16, slash, 1, 'var(--t-accent)') + renderClawedPalm1B(c2x, c2y, ang - 18, 1.16, slash, -1, 'var(--t-accent2)');
    } else if (t < C0) {
      // SHOVE INTO THE OPEN JAWS: palms pinch from above/below pointing at the mouth, then the arms pull back into the shoulders
      const k = Math.min(1, (t - G) / OP), fp = { x: lerp(ma.x + 34, ma.x, k * k), y: ma.y };
      const bl = easeOutCubic(Math.min(1, (t - G) / 110)), rt = easeInOutCubic(Math.max(0, Math.min(1, (t - (G + OP * 0.5)) / (OP * 0.5 + 40))));
      badge.style.opacity = t < G + OP ? '1' : '0';
      badge.style.transform = `translate3d(${f1(fp.x)}px, ${f1(fp.y)}px, 0) rotate(${f1(-30 + k * 30)}deg) scale(${f2(0.7 - k * 0.35)})`;
      const sL = shoulder('L'), sR = shoulder('R'), ga = Math.atan2(ma.y - impPos.y, ma.x + 34 - impPos.x) * 57.3;
      const b1 = { x: lerp(ma.x + 10, fp.x + 10, bl), y: lerp(ma.y - 22, fp.y - 30, bl) }, b2 = { x: lerp(ma.x + 58, fp.x + 10, bl), y: lerp(ma.y + 22, fp.y + 30, bl) };
      const c1 = { x: lerp(b1.x, sL.x, rt), y: lerp(b1.y, sL.y, rt) }, c2 = { x: lerp(b2.x, sR.x, rt), y: lerp(b2.y, sR.y, rt) };
      const a1 = lerp(ga + 18, Math.atan2(ma.y - c1.y, ma.x - c1.x) * 57.3, bl), a2 = lerp(ga - 18, Math.atan2(ma.y - c2.y, ma.x - c2.x) * 57.3, bl);
      if (rt < 0.98) fx += armS('L', c1.x, c1.y, 'var(--t-accent)') + armS('R', c2.x, c2.y, 'var(--t-accent2)')
        + renderClawedPalm1B(c1.x, c1.y, a1, 1.16 * (1 - rt * 0.8), 1, 1, 'var(--t-accent)') + renderClawedPalm1B(c2.x, c2.y, a2, 1.16 * (1 - rt * 0.8), 1, -1, 'var(--t-accent2)');
    } else badge.style.opacity = '0';

    // CHOMP words + file crumbs flying out of the jaws
    if (c >= 0 && u > 0.35) {
      const kk = (u - 0.35) / 0.65;
      if (u < 0.85) fx += txt(ma.x + 72, ma.y - 64, 20 + 10 * (1 - Math.min(1, kk * 3)), c % 2 ? 10 : -12, 'CHOMP!', 1 - kk);
      for (let j = 0; j < 5; j++) {
        const a = -1.57 + (j - 2) * 0.55 + (c % 2 ? 0.3 : -0.3);
        fx += `<text x="${f1(ma.x + Math.cos(a) * 70 * kk)}" y="${f1(ma.y + Math.sin(a) * 70 * kk + 90 * kk * kk)}" fill="${j % 2 ? 'var(--t-accent2)' : 'var(--t-accent)'}" font-family="monospace" font-weight="900" font-size="${f1(16 - kk * 6)}" opacity="${f2(1 - kk)}">${GLY[(j + c) % 7]}</text>`;
      }
    }
    if (t >= GU && t < GU + 380) fx += txt(ma.x + 10, ma.y + 78, 22, -6, 'GULP!', 1 - (t - GU) / 380);

    // SPIT: posts fly out of the mouth one by one
    if (t >= SP) {
      const w = t - SP;
      projectiles.forEach((pr, i) => {
        if (pr.landed) { fx += landFx(pr, now); return; }
        const lt = t - pr.startT;
        if (lt < 0) return;
        if (!pr.o) pr.o = { x: ma.x, y: ma.y };
        if (lt >= DUR) { landCard(pr, now); return; }
        const k = lt / DUR, p = 1 - Math.pow(1 - k, 3), o2 = pr.o, T2 = pr.target;
        const c1 = { x: o2.x + 110, y: o2.y - 150 - (i % 3) * 25 }, c2 = { x: T2.x - (T2.x - o2.x) * 0.25, y: T2.y - 130 };
        const pos = bez3(o2, c1, c2, T2, p);
        for (let g = 1; g <= 6; g++) {
          const gp = bez3(o2, c1, c2, T2, Math.max(0, p - g * 0.035));
          fx += `<circle cx="${f1(gp.x)}" cy="${f1(gp.y)}" r="${f1(14 - g * 1.8)}" fill="${g % 2 ? 'var(--t-accent)' : 'var(--t-accent2)'}" opacity="${f2((1 - g / 7) * 0.55 * (1 - k))}"/>`;
        }
        if (lt < 200) fx += burst(o2.x, o2.y, lt / 200, 7, 'var(--t-accent2)', 6, 20);
        pr.el.style.opacity = '1';
        pr.el.style.transform = `translate3d(${f1(pos.x)}px, ${f1(pos.y)}px, 0) rotate(${f1((1 - p) * -540 * (i % 2 ? -1 : 1))}deg) scale(${(0.22 + p * 0.82).toFixed(3)})`;
      });
      if (!outStart && projectiles.every(p => p.landed)) outStart = t;
      if (outStart && t - outStart >= OUT) {
        cleanupProjectiles();

        return;
      }
    }
    tetherSvg.innerHTML = fx;
    requestAnimationFrame(stepA);
  }
  requestAnimationFrame(stepA);
  }

  function runB(){
  const BH = jsonThrowHoleSpot();
  const BR = Math.max(90, Math.min(170, window.innerHeight * 0.24));
  const T_GROW = 150, T_FULL = 800, T_SUCK0 = 700, T_SUCK1 = 1500, T_COL0 = 1500, T_COL1 = 1950, T_SNAP = 2700, REV0 = T_SNAP + 200, RST = 110;
  const endFx = REV0 + 5 * RST + 720, lastPop = REV0 + 5 * RST;
  let outStart = 0;
  const OUT = 650;
  projectiles.forEach((pr, i) => { pr.revealT = REV0 + i * RST; });
  const bg = document.createElementNS('http://www.w3.org/2000/svg', 'g');
  bg.setAttribute('class', 'imp-extra'); bg.setAttribute('opacity', '0');
  bg.innerHTML = '<path d="M56 74 L75 78 M85 76 L103 66" stroke="url(#jsonThrowStrokeGrad)" stroke-width="3.4" stroke-linecap="round" fill="none"/><polygon points="0,-6 1.6,-1.6 6,0 1.6,1.6 0,6 -1.6,1.6 -6,0 -1.6,-1.6" fill="#fff"/>';
  eyes.parentNode.appendChild(bg);
  const glint = bg.querySelector('polygon');
  const snapHand = (x, y, k, col) => {
    const pinch = k < 0.5;
    return `<g transform="translate(${f1(x)}, ${f1(y)}) rotate(-12) scale(1.2)">
      <rect x="-24" y="-9" width="8" height="18" rx="4" fill="rgba(15,23,42,.92)" stroke="${col}" stroke-width="2.4"/>
      <path d="M -15 -13 C -4 -16, 8 -14, 12 -9 L 12 9 C 8 14, -4 16, -15 13 Z" fill="rgba(15,23,42,.92)" stroke="${col}" stroke-width="2.6" stroke-linejoin="round"/>
      <path d="M 10 4 L 19 8 M 9 9 L 16 13" stroke="${col}" stroke-width="3.2" stroke-linecap="round"/>
      <path d="${pinch ? 'M -3 -13 Q 14 -24 28 -1' : 'M -3 -13 Q 8 -27 21 -23'}" fill="none" stroke="#fff" stroke-width="4.4" stroke-linecap="round"/>
      <path d="${pinch ? 'M 12 -3 L 28 -1' : 'M 12 -3 L 33 7'}" fill="none" stroke="#fff" stroke-width="4.2" stroke-linecap="round"/>
      <circle cx="-2" cy="0" r="4" fill="${col}"/><circle cx="-2" cy="0" r="1.9" fill="#fff"/></g>`;
  };

  function stepB(now) {
    if (myRun !== animRunId) return;
    const t = now - startTime;
    const ot = outStart ? easeInOutCubic(Math.min(1, (t - outStart) / OUT)) : 0;
    const rs = t < T_GROW ? 0 : t < T_FULL ? easeOutCubic((t - T_GROW) / (T_FULL - T_GROW)) : t < T_COL0 ? 1 + 0.03 * Math.sin(t * 0.02) : t < T_COL1 ? 1 - Math.pow((t - T_COL0) / (T_COL1 - T_COL0), 3) : 0;
    const R = BR * rs, cx = BH.x, cy = BH.y;
    let fx = `<rect width="100%" height="100%" fill="#000" opacity="${f2(0.45 * Math.min(1, rs * 1.5))}"/>`;

    // ---- the massive black hole ----
    if (R > 2) {
      const g = `transform="rotate(-16 ${f1(cx)} ${f1(cy)})"`;
      fx += `<defs><radialGradient id="bhg"><stop offset="35%" style="stop-color:#000;stop-opacity:1"/><stop offset="60%" style="stop-color:#000;stop-opacity:.9"/><stop offset="80%" style="stop-color:var(--t-accent2);stop-opacity:.3"/><stop offset="100%" style="stop-color:var(--t-accent);stop-opacity:0"/></radialGradient>
        <linearGradient id="bhd" x1="0" y1="0" x2="1" y2="0"><stop offset="0%" style="stop-color:var(--t-accent)"/><stop offset="50%" style="stop-color:#fff"/><stop offset="100%" style="stop-color:var(--t-accent2)"/></linearGradient>
        <filter id="bhb"><feGaussianBlur stdDeviation="5"/></filter></defs>
        <circle cx="${f1(cx)}" cy="${f1(cy)}" r="${f1(R * 2)}" fill="url(#bhg)"/>`;
      for (let k = 0; k < 3; k++) fx += ring(cx, cy, R * (1.15 + k * 0.22 + 0.05 * Math.sin(t * 0.006 + k)), 1.5, 'var(--t-accent2)', 0.28);
      let glow = '', crisp = '';
      for (let k = 0; k < 4; k++) {
        const e = `<ellipse cx="${f1(cx)}" cy="${f1(cy)}" rx="${f1(R * (1 + k * 0.22))}" ry="${f1(R * (0.26 + k * 0.07))}" fill="none" stroke="url(#bhd)" stroke-dasharray="${40 + k * 30} ${14 + k * 8}" stroke-dashoffset="${f1(-t * (0.35 + k * 0.12))}" ${g}`;
        glow += `${e} stroke-width="${f1(12 - k * 2.5)}" opacity="${f2(0.9 - k * 0.18)}"/>`;
        crisp += `${e} stroke-width="1.6" opacity=".8"/>`;
      }
      fx += `<g filter="url(#bhb)">${glow}</g>${crisp}<circle cx="${f1(cx)}" cy="${f1(cy)}" r="${f1(R * 0.62)}" fill="#000" stroke="#fff" stroke-width="2.5" stroke-opacity=".8"/>
        <path d="M ${f1(cx - R)} ${f1(cy)} A ${f1(R)} ${f1(R * 0.26)} 0 0 0 ${f1(cx + R)} ${f1(cy)}" fill="none" stroke="url(#bhd)" stroke-width="8" ${g}/>`;
      for (let i = 0; i < 36; i++) {   // matter streaming inward
        const ph = (t * 0.0007 * (1 + (i % 3) * 0.35) + i * 0.0278) % 1, r = R * (2.6 * (1 - ph) + 0.65), a = i * 2.4 + ph * 8;
        fx += `<line x1="${f1(cx + Math.cos(a) * r)}" y1="${f1(cy + Math.sin(a) * r * 0.55)}" x2="${f1(cx + Math.cos(a - 0.28) * r * 1.04)}" y2="${f1(cy + Math.sin(a - 0.28) * r * 1.04 * 0.55)}" stroke="${i % 3 === 0 ? '#fff' : i % 3 === 1 ? 'var(--t-accent)' : 'var(--t-accent2)'}" stroke-width="${f1(1 + (1 - ph) * 2.2)}" stroke-linecap="round" opacity="${f2((0.2 + ph * 0.7) * Math.min(1, rs * 2))}"/>`;
      }
    }

    // ---- the file gets spaghettified into it ----
    const sf = Math.max(0, Math.min(1, (t - T_SUCK0) / (T_SUCK1 - T_SUCK0)));
    if (sf < 1) {
      const d0 = Math.hypot(startFilePos.x - cx, startFilePos.y - cy), a0 = Math.atan2(startFilePos.y - cy, startFilePos.x - cx);
      const e = sf * sf, r = d0 * (1 - e) * (1 - e * 0.3), a = a0 + e * Math.PI * 5;
      badge.style.opacity = String(Math.min(1, (1 - sf) * 5));
      badge.style.transform = `translate3d(${f1(cx + Math.cos(a) * r + Math.sin(t * 0.08) * 2 * rs)}px, ${f1(cy + Math.sin(a) * r * lerp(1, 0.6, e))}px, 0) rotate(${f1(a * 34)}deg) scale(${f2((1 - e * 0.9) * (1 + e * 2.5))}, ${f2(1 - e * 0.9)})`;
    } else badge.style.opacity = '0';

    // ---- casting palms ----
    let f = 0, slash = 0;
    if (t < T_COL0) f = 0.34 * easeOutCubic(Math.min(1, t / 350));
    else { slash = Math.min(1, (t - T_COL0) / 200); f = t < T_COL1 ? lerp(0.34, 0.12, easeInOutCubic((t - T_COL0) / (T_COL1 - T_COL0))) : lerp(0.12, 0, easeInOutCubic(Math.min(1, (t - T_COL1) / 250))); }
    if (f > 0.01) {
      const dx = cx - impPos.x, dy = cy - impPos.y, ang = Math.atan2(dy, dx) * 57.3, sw = t < T_COL0 ? Math.sin(t * 0.012) * 6 : 0;
      const p1 = { x: impPos.x + dx * f, y: impPos.y + dy * f - 18 + sw }, p2 = { x: impPos.x + dx * f * 0.9, y: impPos.y + dy * f * 0.9 + 22 - sw };
      fx += armS('L', p1.x, p1.y, 'var(--t-accent)') + armS('R', p2.x, p2.y, 'var(--t-accent2)')
        + renderClawedPalm1B(p1.x, p1.y, ang + 8, 1.2, slash, 1, 'var(--t-accent)') + renderClawedPalm1B(p2.x, p2.y, ang - 8, 1.2, slash, -1, 'var(--t-accent2)');
      if (t < T_COL0) fx += `<ellipse cx="${f1(p1.x + 40)}" cy="${f1((p1.y + p2.y) / 2)}" rx="${f1(14 + f * 40)}" ry="32" fill="none" stroke="#fff" stroke-width="2" stroke-dasharray="8 7" stroke-dashoffset="${f1(-t * 0.3)}" opacity=".7"/>`;
    }

    // ---- collapse: singularity pop ----
    if (t >= T_COL1 - 80 && t < T_COL1) fx += `<circle cx="${f1(cx)}" cy="${f1(cy)}" r="${f1(4 + (T_COL1 - t) * 0.05)}" fill="#fff"/>`;
    if (t >= T_COL1 && t < T_COL1 + 420) { const k = (t - T_COL1) / 420; fx += ring(cx, cy, k * 260, 7 * (1 - k), '#fff', 1 - k) + ring(cx, cy, k * 170, 4 * (1 - k), 'var(--t-accent2)', 1 - k) + burst(cx, cy, k, 14, 'var(--t-accent)', 20, 50); }

    // ---- the Imp: lean, then smirk ----
    const sk = Math.max(0, Math.min(1, (t - T_COL1) / 220)), lean = Math.min(1, t / 300) * (1 - sk), skm = sk * (1 - ot);
    svg.style.transform = `translate3d(${f1(lean * 14 + Math.sin(t * 0.09) * 2 * Math.min(rs, 1) * (1 - sk))}px, 0, 0) rotate(${f1(lean * 8 - sk * 5 * (1 - ot))}deg) scale(${f2(lerp(1.1 + 0.04 * sk, 1, ot))})`;
    eyes.style.transform = `translate3d(${f1((8 - 5 * sk) * (1 - ot))}px, 0, 0) scale(${f2(lerp(lerp(1.3, 1.12, sk), 1, ot))}, ${f2(lerp(lerp(1.3, 0.5, sk), 1, ot))})`;
    haloL.setAttribute('opacity', f2((1 - sk * 0.6) * (1 - ot))); haloR.setAttribute('opacity', f2((1 - sk * 0.6) * (1 - ot)));
    mouth.style.transform = sk > 0 ? '' : 'scale(1.1, 1.2)';
    if (t >= T_COL1) IMP.mouthPath.setAttribute('d', `M ${f1(lerp(59, 60, skm))} ${f1(lerp(95, 101, skm))} Q ${f1(lerp(80, 82, skm))} ${f1(lerp(116, 108, skm))} ${f1(lerp(101, 102, skm))} ${f1(lerp(95, 91, skm))}`);
    bg.setAttribute('opacity', f2(skm));
    glint.setAttribute('transform', `translate(105 91) scale(${f2(skm * (0.8 + 0.3 * Math.sin(t * 0.02)))})`);

    // ---- finger snap ----
    if (t >= 2250 && t < T_SNAP + 420) {
      const r = easeOutCubic(Math.min(1, (t - 2250) / 300)), ret = t > T_SNAP + 250 ? easeInOutCubic(Math.min(1, (t - T_SNAP - 250) / 170)) : 0;
      const sR = shoulder('R'), hx = lerp(sR.x, impPos.x + 112, r * (1 - ret)), hy = lerp(sR.y, impPos.y - 46, r * (1 - ret));
      if (t >= 2550 && t < T_SNAP) fx += `<circle cx="${f1(hx + 34)}" cy="${f1(hy)}" r="${f1(5 + 4 * Math.sin(t * 0.05))}" fill="#fff" opacity=".6"/>`;
      // The cuff center is 20 SVG units left of the hand origin in its -12° group.
      // End the forearm at that transformed point so it meets the cuff, not the palm.
      const cuffAngle = -12 * Math.PI / 180;
      const wristX = hx - 20 * Math.cos(cuffAngle), wristY = hy - 20 * Math.sin(cuffAngle);
      // The original Singularity Snap is the right arm: use the secondary accent,
      // matching the established right-arm color used by the paired-arm sequences.
      fx += armS('R', wristX, wristY, 'var(--t-accent2)') + snapHand(hx, hy, t < T_SNAP ? 0 : Math.min(1, (t - T_SNAP) / 60), 'var(--t-accent2)');
      if (t >= T_SNAP) { const k = (t - T_SNAP) / 420; fx += burst(hx + 34, hy, k, 10, 'var(--t-accent2)', 10, 30) + ring(hx + 34, hy, 8 + k * 70, 3 * (1 - k), '#fff', 1 - k) + `<text x="${f1(hx + 60)}" y="${f1(hy - 40)}" font-size="26" font-weight="900" fill="#fff" stroke="var(--t-accent2)" stroke-width="5" paint-order="stroke" text-anchor="middle" transform="rotate(-10 ${f1(hx + 60)} ${f1(hy - 40)})" opacity="${f2(1 - k)}">SNAP!</text>`; }
    }

    // ---- the posts magically appear in their slots ----
    projectiles.forEach((pr, i) => {
      const T2 = pr.target, rt = pr.revealT - t;
      if (pr.landed) {
        fx += landFx(pr, now);
        const k = (now - pr.landT) / 700;
        if (k < 1) for (let j = 0; j < 4; j++) fx += star(T2.x + (j - 1.5) * 32, T2.y - k * 55 - (j % 2) * 12, 7 * (1 - k) + 2, k * 90 + j * 20, j % 2 ? 'var(--t-accent2)' : '#fff', 1 - k);
        return;
      }
      if (rt <= 0) { pr.landed = true; pr.landT = now; T2.el.classList.add('card-throw-landed', 'card-throw-flash'); setTimeout(() => T2.el.classList.remove('card-throw-flash'), 420); return; }
      if (rt < 200) {
        const k = 1 - rt / 200;
        for (let j = 0; j < 6; j++) { const a = j * 1.047 + i; fx += star(T2.x + Math.cos(a) * (1 - k) * 70, T2.y + Math.sin(a) * (1 - k) * 70, 5 + 4 * k, a * 50, j % 2 ? 'var(--t-accent2)' : '#fff', k); }
        fx += ring(T2.x, T2.y, 10 + 50 * (1 - k), 2, 'var(--t-accent)', k * 0.7);
      }
    });

    fx += cineFrame(Math.min(1, t / 300) * (1 - ot));
    fx += flash(0.5 * Math.max(0, 1 - Math.abs(t - T_COL1) / 110) + 0.55 * Math.max(0, 1 - Math.abs(t - T_SNAP) / 100));
    if (!outStart && t > lastPop + 350) outStart = t;
    if (outStart && t - outStart >= OUT && projectiles.every(p => p.landed && now - p.landT > 700)) {
      cleanupProjectiles();

      return;
    }
    tetherSvg.innerHTML = fx;
    requestAnimationFrame(stepB);
  }
  requestAnimationFrame(stepB);
  }

  function runC(){
  const tridentSpeed = 1.3; // 30% faster playback for the entire second animation.
  const tridentWallMs = ms => Math.round(ms / tridentSpeed);
  const scaledPostTransitions = `${tridentWallMs(240)}ms, ${tridentWallMs(320)}ms, ${tridentWallMs(320)}ms, ${tridentWallMs(320)}ms`;
  cardTargets.forEach(({ el }) => { el.style.transitionDuration = scaledPostTransitions; });
  let finaleStart = -1;
  let tetherRaised = false;
  // Anchor the forearm at the sticker's lower-right shoulder, outside the face circle.
  // Unified shoulder — right imaginary shoulder same as devour (126,118)
  const _unifiedShC = (function(){ try{ let e=document.getElementById('imp-sh-R'); if(!e){ e=document.createElementNS('http://www.w3.org/2000/svg','circle'); e.setAttribute('id','imp-sh-R'); e.setAttribute('class','imp-extra'); e.setAttribute('cx','126'); e.setAttribute('cy','118'); e.setAttribute('r','0.6'); e.setAttribute('fill','none'); if(svg) svg.appendChild(e);} const r=e.getBoundingClientRect(); return {x:r.left+r.width/2, y:r.top+r.height/2}; }catch(_){ return {x:impPos.x+30, y:impPos.y+25}; }})();
  const shoulderX = _unifiedShC.x, shoulderY = _unifiedShC.y;
  const fileX = startFilePos.x, fileY = startFilePos.y;
  const hitTip = { x: fileX, y: fileY + 3 };
  const aimAngleRad = Math.atan2(hitTip.y - shoulderY, hitTip.x - shoulderX);
  const aimX = Math.cos(aimAngleRad), aimY = Math.sin(aimAngleRad);
  const angleDeg = aimAngleRad * 57.2958;
  // Keep the reference grip close to the shoulder, but let the whole forearm drive the strike.
  const baseGripX = shoulderX + aimX * 26, baseGripY = shoulderY + aimY * 26;
  const restTip = { x: baseGripX + aimX * 52, y: baseGripY + aimY * 52 };
  const windupPull = -10;
  const thrustDrive = 18;
  const impactHold = 10;
  const windupEnd = 460;
  const impactAt = 1050;
  const dropStart = throwStart;

  function gripAt(handTravel) {
    return { x: baseGripX + aimX * handTravel, y: baseGripY + aimY * handTravel };
  }
  function tipAtReach(reach, handTravel = 0) {
    const startTip = { x: restTip.x + aimX * handTravel, y: restTip.y + aimY * handTravel };
    return {
      x: startTip.x + (hitTip.x - startTip.x) * reach,
      y: startTip.y + (hitTip.y - startTip.y) * reach
    };
  }
  function strikeReach(q) {
    // Hold the loaded pose, then complete the full telescoping lunge in a short, sharp beat.
    return easeOutCubic(clamp01((q - 0.32) / 0.42));
  }
  const headGroup = IMP.head;
  const horns = IMP.horns;
  const browL = IMP.browL;
  const browR = IMP.browR;
  const mouthPath = IMP.mouthPath;
  const eyesBlink = IMP.eyesBlink;
  const defaultEyes = IMP.defaultEyes;
  const exprEyes = IMP.exprEyes;
  const grinToHide = IMP.defaultMouth || mouthPath;
  /* the idle engine's own expression drawer; a grafted group on undecorated art */
  const cackleMouth = IMP.exprMouth || IMP.laughMouth;

  function setTridentGaze(progress, laughIntensity = 0) {
    const g = clamp01(progress), squint = clamp01(laughIntensity);
    const eyeY = (1 - g * 0.12) * (1 - squint * 0.10);
    eyes.style.transform = `translate3d(${(g * 7).toFixed(2)}px, ${(-g * 2.4).toFixed(2)}px, 0) scale(${(1 + g * 0.14).toFixed(3)}, ${eyeY.toFixed(3)})`;
  }

  /* ---- the imp's own evil laugh -----------------------------------------
     The reference's "smile" stretched the single grin stroke and planted
     three flat triangles on it. The imp already has a much better face: the
     sidebar's idle expression 2, "Demonic Villain Cackle". This is that
     drawing, note for note - the 1450 ms envelope, three uneven "ha" waves,
     the open mouth with six fangs, the slanted eye wedges, the head roll and
     the horn lift - so the strike laughs exactly like the idle sticker. */
  const CACKLE_START = impactAt;  /* the trident lands, then he laughs at it */
  const CACKLE_CYCLE = 1450;      /* EXPR_DURATIONS[2] in the page */

  let cackleOn = false;

  function cacklePalette() {
    try {
      if (typeof STICKER_THEME_PALETTES !== 'undefined' && typeof currentStickerThemeId !== 'undefined') {
        const p = STICKER_THEME_PALETTES[currentStickerThemeId];
        if (p && p.c1 && p.c2 && p.line) return p;
      }
    } catch (_) { /* harness: the engine runs without the page's globals */ }
    const dots = svg.querySelectorAll('.imp-eyes-blink circle');
    return {
      c1: (dots[0] && dots[0].getAttribute('fill')) || '#38bdf8',
      c2: (dots[1] && dots[1].getAttribute('fill')) || '#ec4899',
      line: '#f1f5f9', pupil: '#ffffff', mouthFill: 'rgba(15,23,42,0.90)'
    };
  }

  function restTridentFace() {
    if (!cackleOn) return;
    cackleOn = false;
    headGroup.style.transform = '';
    horns.style.transform = '';
    if (eyesBlink) eyesBlink.style.transform = '';
    if (defaultEyes) defaultEyes.style.display = '';
    if (exprEyes) exprEyes.innerHTML = '';
    if (grinToHide) grinToHide.style.display = '';
    if (cackleMouth) cackleMouth.innerHTML = '';
    if (browL && IMP.browLD !== null) browL.setAttribute('d', IMP.browLD);
    if (browR && browR !== browL && IMP.browRD !== null) browR.setAttribute('d', IMP.browRD);
    if (IMP.haloL) IMP.haloL.setAttribute('opacity', '0');
    if (IMP.haloR) IMP.haloR.setAttribute('opacity', '0');
  }

  function updateTridentLaugh(t, envelope = 1) {
    const outer = clamp01(envelope) * easeOutCubic(clamp01((t - CACKLE_START) / 240));
    if (outer <= 0.001) { restTridentFace(); return { intensity: 0, openPulse: 0, bob: 0 }; }

    const local = t - CACKLE_START;
    const phase = (local % CACKLE_CYCLE) / CACKLE_CYCLE;
    const cycleEnv = phase < 0.16 ? phase / 0.16 : (phase < 0.84 ? 1 : 1 - (phase - 0.84) / 0.16);
    const env = cycleEnv * outer;
    const wave = Math.abs(Math.sin(phase * Math.PI * 6.0));

    /* below the idle engine's own threshold the rest face comes back, so the
       mouth closes between "ha" cycles exactly as it does between idle skits */
    if (env <= 0.18) { restTridentFace(); return { intensity: 0, openPulse: 0, bob: 0 }; }
    cackleOn = true;

    const pal = cacklePalette();
    const topY = 92.5;
    const mouthOpen = env * (6.2 + wave * 7.5);
    const botY = 96.5 + mouthOpen * 1.45;

    headGroup.style.transform =
      `translate3d(${(Math.sin(t * 0.055) * 1.8 * env).toFixed(2)}px, ${(-wave * 7.2 * env).toFixed(2)}px, 0)` +
      ` rotate(${(Math.sin(phase * Math.PI * 3) * 5.2 * env).toFixed(2)}deg)` +
      ` scale(${(1 + env * 0.065).toFixed(3)})`;
    horns.style.transform = `translate3d(0px, ${(-wave * 3.2 * env).toFixed(2)}px, 0)`;
    const hornTip = (78 + env * 2.5).toFixed(1);
    if (browL) browL.setAttribute('d', `M56 56 L52 72 L71 ${hornTip}`);
    if (browR && browR !== browL) browR.setAttribute('d', `M104 56 L108 72 L89 ${hornTip}`);
    if (eyesBlink) eyesBlink.style.transform = `translate3d(0px, ${(-wave).toFixed(2)}px, 0)`;

    if (defaultEyes) defaultEyes.style.display = 'none';
    if (exprEyes) exprEyes.innerHTML =
      `<polygon points="60,79.5 75,83.5 71.5,87.8 60.5,85" fill="${pal.c1}"/>` +
      `<polygon points="100,79.5 85,83.5 88.5,87.8 99.5,85" fill="${pal.c2}"/>` +
      `<circle cx="68.5" cy="83.8" r="1.6" fill="${pal.pupil}"/>` +
      `<circle cx="91.5" cy="83.8" r="1.6" fill="${pal.pupil}"/>`;

    if (grinToHide) grinToHide.style.display = 'none';
    if (cackleMouth) cackleMouth.innerHTML =
      `<path d="M 58 ${topY} Q 80 ${(topY + 3).toFixed(1)} 102 ${topY} Q 96 ${botY.toFixed(1)} 80 ${(botY + 2.5).toFixed(1)} Q 64 ${botY.toFixed(1)} 58 ${topY} Z" fill="${pal.mouthFill}" stroke="${pal.line}" stroke-width="3.2" stroke-linejoin="round"/>` +
      `<polygon points="63,${(topY + 0.8).toFixed(1)} 66.5,${(topY + 7.2 + wave * 1.8).toFixed(1)} 70,${(topY + 1.2).toFixed(1)}" fill="${pal.line}"/>` +
      `<polygon points="73,${(topY + 1.4).toFixed(1)} 75.5,${(topY + 5.5 + wave * 1.2).toFixed(1)} 78,${(topY + 1.6).toFixed(1)}" fill="${pal.line}"/>` +
      `<polygon points="82,${(topY + 1.6).toFixed(1)} 84.5,${(topY + 5.5 + wave * 1.2).toFixed(1)} 87,${(topY + 1.4).toFixed(1)}" fill="${pal.line}"/>` +
      `<polygon points="90,${(topY + 1.2).toFixed(1)} 93.5,${(topY + 7.2 + wave * 1.8).toFixed(1)} 97,${(topY + 0.8).toFixed(1)}" fill="${pal.line}"/>` +
      `<polygon points="68,${(botY - 1.5).toFixed(1)} 71,${(botY - 6.2).toFixed(1)} 74,${(botY - 0.8).toFixed(1)}" fill="${pal.line}"/>` +
      `<polygon points="86,${(botY - 0.8).toFixed(1)} 89,${(botY - 6.2).toFixed(1)} 92,${(botY - 1.5).toFixed(1)}" fill="${pal.line}"/>`;

    return { intensity: env, openPulse: wave * env, bob: wave * env };
  }

  function step2A(now) {
    if (myRun !== animRunId) return;
    const t = (now - startTime) * tridentSpeed;
    const gazeProgress = easeInOutCubic(clamp01((t - 80) / 1200));
    const finaleEnvelope = finaleStart < 0 ? 1 : 1 - easeInOutCubic(clamp01((t - finaleStart) / 1150));
    let laughPose = updateTridentLaugh(t, finaleEnvelope);
    setTridentGaze(gazeProgress, laughPose.intensity);
    if (!tetherRaised) {
      // Put the weapon SVG above the JSON badge, like the reference overlay.
      fxLayer.insertBefore(tetherSvg, projectiles.length ? projectiles[0].el : null);
      tetherRaised = true;
    }

    if (t < windupEnd) {
      // WIND-UP: visibly cock the arm back from the shoulder before the strike.
      const p = clamp01(t / windupEnd);
      const handTravel = windupPull * easeInOutCubic(p);
      const grip = gripAt(handTravel);
      const tip = tipAtReach(0, handTravel);
      const aimPulse = Math.sin(p * Math.PI * 2) * (1 - p) * 2.6;
      badge.style.opacity = '1';
      badge.style.transform = `translate3d(${fileX.toFixed(1)}px, ${fileY.toFixed(1)}px, 0) rotate(${(Math.sin(t * 0.006) * 2).toFixed(1)}deg) scale(1)`;
      tetherSvg.innerHTML = `
        ${renderExtendingTridentFromBaseHand(shoulderX, shoulderY, grip.x, grip.y, tip.x + aimX * aimPulse, tip.y + aimY * aimPulse, angleDeg, p * 0.25)}
        ${fxRing(grip.x + aimX * 42, grip.y + aimY * 42, 8 + p * 5, 1.8, 'var(--t-accent2)', 0.22 + p * 0.35, '3 6')}
        ${fxSparks(grip.x + aimX * 48, grip.y + aimY * 48, (t * 0.003) % 1, 3, 16, 1.7, '#ffffff', 'var(--t-accent2)')}
      `;
      svg.style.transform = `translate3d(${(p * 1.5).toFixed(1)}px, ${(p * -0.7 - laughPose.bob * 1.3).toFixed(1)}px, 0) rotate(${(p * -1.2).toFixed(1)}deg) scale(${(1.02 + p * 0.018).toFixed(3)})`;
      setVignette(0.08 + p * 0.1);
    } else if (t < impactAt) {
      // THRUST: hold the cocked pose, then drive the hand and telescoping trident forward in a sharp lunge.
      const q = clamp01((t - windupEnd) / (impactAt - windupEnd));
      const reach = strikeReach(q);
      const handTravel = windupPull + (thrustDrive - windupPull) * reach;
      const grip = gripAt(handTravel);
      const tip = tipAtReach(reach, handTravel);
      const previousQ = Math.max(0, q - 0.055);
      const previousReach = strikeReach(previousQ);
      const previousTravel = windupPull + (thrustDrive - windupPull) * previousReach;
      const previous = tipAtReach(previousReach, previousTravel);
      badge.style.opacity = '1';
      badge.style.transform = `translate3d(${fileX.toFixed(1)}px, ${fileY.toFixed(1)}px, 0) rotate(${(Math.sin(t * 0.035) * reach * 2.6).toFixed(1)}deg) scale(1)`;
      let trails = '';
      if (reach - previousReach > 0.012) {
        const speed = clamp01((reach - previousReach) * 3.2);
        trails += fxStreak(previous.x - aimX * 24, previous.y - aimY * 24, tip.x - aimX * 24, tip.y - aimY * 24, 7.5 + speed * 3.5, 'var(--t-accent)', 0.42 + speed * 0.28);
        trails += fxStreak(previous.x + aimY * 14, previous.y - aimX * 14, tip.x + aimY * 14, tip.y - aimX * 14, 4.5 + speed * 1.2, 'var(--t-accent2)', 0.36 + speed * 0.24);
      }
      if (reach > 0.84) trails += fxRing(hitTip.x, hitTip.y, 14 + (reach - 0.84) * 72, 2.2, '#ffffff', (reach - 0.84) * 2.7, '7 6');
      tetherSvg.innerHTML = `
        ${renderExtendingTridentFromBaseHand(shoulderX, shoulderY, grip.x, grip.y, tip.x, tip.y, angleDeg, reach)}
        ${trails}
      `;
      svg.style.transform = `translate3d(${(1.5 + reach * 3.6).toFixed(1)}px, ${(-0.7 + reach * 1.5 - laughPose.bob * 1.3).toFixed(1)}px, 0) rotate(${(-1.2 + reach * 3.2).toFixed(1)}deg) scale(${(1.038 + reach * 0.02).toFixed(3)})`;
      setVignette(0.18 + reach * 0.15);
      shakeScene(reach > 0.9 ? 3.5 * (reach - 0.9) / 0.1 : 0, t);
    } else if (t < dropStart) {
      // IMPACT: a heavy pin-through with a compressed file, triple punctures and torn fragments.
      const q = clamp01((t - impactAt) / (dropStart - impactAt));
      const impactAge = t - impactAt;
      const recoil = Math.sin(impactAge * 0.09) * Math.exp(-impactAge / 185) * 7.2;
      const handTravel = thrustDrive - (thrustDrive - impactHold) * easeOutCubic(clamp01(impactAge / 300))
        + Math.sin(impactAge * 0.09) * 2.2 * Math.exp(-impactAge / 185);
      const grip = gripAt(handTravel);
      const tip = { x: hitTip.x + aimX * recoil, y: hitTip.y + aimY * recoil };
      const flash = Math.max(0, 1 - impactAge / 170);
      const shock = easeOutCubic(clamp01(impactAge / 520));
      const pressComp = Math.sin(q * Math.PI);
      badge.style.opacity = '1';
      badge.style.transform = `translate3d(${(fileX + recoil * 0.42).toFixed(1)}px, ${(fileY + recoil * 0.18).toFixed(1)}px, 0) rotate(${(Math.sin(impactAge * 0.075) * Math.exp(-impactAge / 215) * 9).toFixed(1)}deg) skewX(${(flash * 7).toFixed(1)}deg) scale(${(1 + flash * 0.16).toFixed(3)}, ${(1 - flash * 0.27).toFixed(3)})`;

      let impactFx = '';
      // A brief hot bloom, then two broad rings race out from the puncture.
      impactFx += `<defs><radialGradient id="tridentImpactBloom"><stop offset="0%" stop-color="#ffffff" stop-opacity="0.82"/><stop offset="24%" stop-color="var(--t-accent)" stop-opacity="0.44"/><stop offset="100%" stop-color="#02030a" stop-opacity="0"/></radialGradient></defs>`;
      impactFx += `<circle cx="${fileX}" cy="${fileY}" r="${(24 + shock * 84).toFixed(1)}" fill="url(#tridentImpactBloom)" opacity="${(flash * 0.86).toFixed(2)}"/>`;
      impactFx += fxRing(fileX, fileY, 18 + shock * 126, 6 * (1 - shock) + 0.8, '#ffffff', 0.82 * (1 - shock));
      impactFx += fxRing(fileX, fileY, 30 + shock * 104, 3.4 * (1 - shock) + 0.6, 'var(--t-accent2)', 0.74 * (1 - shock), '14 8');

      // Three dark pin-pricks mark the actual prongs, with cracks running through the file.
      const perpX = -aimY, perpY = aimX;
      for (let k = -1; k <= 1; k++) {
        const px = fileX + perpX * k * 17;
        const py = fileY + perpY * k * 17;
        const pinOp = Math.max(0.22, 1 - shock * 0.62);
        impactFx += `<circle cx="${px.toFixed(1)}" cy="${py.toFixed(1)}" r="${(5.2 + flash * 1.8).toFixed(1)}" fill="#03050b" stroke="var(--t-accent)" stroke-width="${(1.8 + flash).toFixed(1)}" opacity="${pinOp.toFixed(2)}"/>`;
        impactFx += fxRing(px, py, 7 + shock * 12, 1.6 * (1 - shock) + 0.45, k === 0 ? '#ffffff' : 'var(--t-accent2)', 0.82 * (1 - shock));
      }
      impactFx += fxCrackle(fileX - 52, fileY - 20, fileX + 49, fileY + 16, 3.7, 12, '#ffffff', 0.92 - shock * 0.54);
      impactFx += fxCrackle(fileX - 40, fileY + 22, fileX + 46, fileY - 20, 5.2, 10, 'var(--t-accent2)', 0.82 - shock * 0.48);
      impactFx += fxCrackle(fileX - 58, fileY + 2, fileX + 58, fileY - 4, 7.1, 7, 'var(--t-accent)', 0.64 - shock * 0.42);

      // Hard-edged fragments kick outward and decay instead of a comic burst label.
      const shardP = easeOutCubic(clamp01(impactAge / 500));
      for (let i = 0; i < 10; i++) {
        const a = i * 2.399 + 0.26;
        const travel = 10 + shardP * (42 + (i % 4) * 13);
        const sx = fileX + Math.cos(a) * travel;
        const sy = fileY + Math.sin(a) * travel * 0.62 - shardP * (i % 3) * 8;
        const shardOp = Math.max(0, 1 - impactAge / 520) * (0.58 + (i % 3) * 0.12);
        impactFx += fxShard(sx, sy, a * 57.3 + impactAge * 0.12, 4.5 + (i % 3) * 1.4, i % 2 ? 'var(--t-accent2)' : 'var(--t-accent)', shardOp);
      }

      // Thin pressure rays add force without the childish filled starburst.
      for (let i = 0; i < 12; i++) {
        const a = i * Math.PI / 6 + 0.12;
        const inner = 13 + shock * 10;
        const outer = 28 + shock * (76 + (i % 4) * 11);
        const rayColor = i % 3 === 0 ? '#ffffff' : (i % 2 ? 'var(--t-accent)' : 'var(--t-accent2)');
        impactFx += fxStreak(fileX + Math.cos(a) * inner, fileY + Math.sin(a) * inner * 0.68,
                             fileX + Math.cos(a) * outer, fileY + Math.sin(a) * outer * 0.68,
                             1.5 + flash * 1.1, rayColor, 0.66 * (1 - shock));
      }

      tetherSvg.innerHTML = `${renderExtendingTridentFromBaseHand(shoulderX, shoulderY, grip.x, grip.y, tip.x, tip.y, angleDeg, pressComp)}${impactFx}`;
      svg.style.transform = `translate3d(${(recoil * 0.28).toFixed(1)}px, ${(Math.abs(recoil) * 0.12 - laughPose.bob * 1.3).toFixed(1)}px, 0) rotate(${(recoil * 0.2).toFixed(1)}deg) scale(${(1.045 + flash * 0.03).toFixed(3)})`;
      setVignette(0.34 * (1 - q * 0.22));
      shakeScene(flash * 14 + (1 - q) * 1.4, t);
    } else {
      // POST RAIN: the pierced JSON bursts open; saved-post cards spill from
      // the same spot, tumble under gravity, and land in their grid slots.
      let allDone = true;
      const fallTrails = [];
      const landFx = [];
      const dropAge = t - dropStart;
      const fileBurst = clamp01(dropAge / 400);
      badge.style.opacity = String(Math.max(0, 1 - fileBurst));
      badge.style.transform = `translate3d(${(fileX + Math.sin(dropAge * 0.08) * (1 - fileBurst) * 5).toFixed(1)}px, ${(fileY + fileBurst * 8).toFixed(1)}px, 0) rotate(${(dropAge * 0.08).toFixed(1)}deg) scale(${(1 + fileBurst * 0.25).toFixed(3)}, ${(1 - fileBurst * 0.4).toFixed(3)})`;

      projectiles.forEach((proj, idx) => {
        const localT = t - proj.startT;
        if (localT < 0) { allDone = false; return; }
        if (localT >= throwDur) {
          if (!proj.landed) {
            proj.landed = true;
            proj.el.style.transition = `opacity ${tridentWallMs(420)}ms ease`;
            proj.el.style.opacity = '0';
            proj.target.el.style.animationDuration = `${tridentWallMs(520)}ms`;
            proj.target.el.classList.add('card-throw-landed', 'card-throw-flash', 'slam');
            setTimeout(() => proj.target.el.classList.remove('card-throw-flash'), tridentWallMs(420));
            setTimeout(() => proj.target.el.classList.remove('slam'), tridentWallMs(520));
          }
          const age = localT - throwDur;
          if (age < 380) landFx.push({ x: proj.target.x, y: proj.target.y, q: age / 380, idx });
          return;
        }
        allDone = false;
        const p = clamp01(localT / throwDur);
        const fallP = easeInOutCubic(p);
        const origin = { x: fileX + (idx - 2.5) * 4, y: fileY + 12 };
        const ctrl = {
          x: (origin.x + proj.target.x) * 0.5 + (idx % 2 ? 34 : -34),
          y: Math.max(origin.y, proj.target.y) + 82 + (idx % 3) * 18
        };
        const pos = bezierPoint(origin, ctrl, proj.target, fallP);
        const prevP = easeInOutCubic(clamp01(p - 0.045));
        const prev = bezierPoint(origin, ctrl, proj.target, prevP);
        fallTrails.push({ prev, pos, idx });
        const spin = (idx % 2 ? 1 : -1) * p * 300;
        proj.el.style.opacity = String(easeOutCubic(clamp01(localT / 150)));
        proj.el.style.transform = `translate3d(${pos.x.toFixed(1)}px, ${pos.y.toFixed(1)}px, 0) rotate(${spin.toFixed(1)}deg) scale(${(0.48 + p * 0.52).toFixed(3)})`;
      });

      let rainFx = '';
      fallTrails.forEach(tr => {
        rainFx += fxStreak(tr.prev.x, tr.prev.y, tr.pos.x, tr.pos.y, 5.5, tr.idx % 2 ? 'var(--t-accent2)' : 'var(--t-accent)', 0.32);
        rainFx += fxStreak(tr.prev.x - 3, tr.prev.y - 7, tr.pos.x - 3, tr.pos.y - 7, 1.6, '#ffffff', 0.45);
      });
      landFx.forEach(hit => {
        rainFx += fxRing(hit.x, hit.y, 12 + hit.q * 48, 2.3 * (1 - hit.q), '#ffffff', (1 - hit.q) * 0.7);
        rainFx += fxRing(hit.x, hit.y, 7 + hit.q * 34, 1.8 * (1 - hit.q), 'var(--t-accent2)', (1 - hit.q) * 0.62, '6 5');
      });
      if (dropAge < 420) {
        rainFx += fxStarburst(fileX, fileY, 28 + fileBurst * 68, 10 + fileBurst * 24, 12, dropAge * 0.12, '#ffffff', 1 - fileBurst * 0.55, (1 - fileBurst) * 0.45);
        rainFx += fxRing(fileX, fileY, 16 + fileBurst * 62, 2.6 * (1 - fileBurst), 'var(--t-accent)', 0.85 * (1 - fileBurst));
      }
      const withdraw = easeInOutCubic(clamp01((dropAge - 220) / 900));
      const grip = gripAt(impactHold * (1 - withdraw));
      const tip = {
        x: hitTip.x + (restTip.x - hitTip.x) * withdraw,
        y: hitTip.y + (restTip.y - hitTip.y) * withdraw
      };
      tetherSvg.innerHTML = `${renderExtendingTridentFromBaseHand(shoulderX, shoulderY, grip.x, grip.y, tip.x, tip.y, angleDeg, 1 - withdraw * 0.7)}${rainFx}`;
      svg.style.transform = `translate3d(0, ${(-laughPose.bob * 3.2).toFixed(1)}px, 0) scale(1.07)`;
      setVignette(0.12);

      if (allDone) {
        if (finaleStart < 0) finaleStart = t;
        const finaleRaw = clamp01((t - finaleStart) / 1150);
        const f = easeInOutCubic(finaleRaw);
        if (finaleRaw >= 1) {
          cleanupProjectiles();

          return;
        }
        const settle = f;
        setTridentGaze(gazeProgress * (1 - settle), laughPose.intensity);
        tetherSvg.innerHTML = `
          <g opacity="${(1 - f).toFixed(3)}">
            ${renderExtendingTridentFromBaseHand(shoulderX, shoulderY, baseGripX, baseGripY, restTip.x, restTip.y, angleDeg, 0)}
          </g>
          ${fxRing(fileX, fileY, 28 + f * 74, 2.4 * (1 - f), '#ffffff', 0.72 * (1 - f))}
          ${fxSparks(fileX, fileY, f, 7, 48, 2.1 * (1 - f), '#ffffff', 'var(--t-accent2)')}
        `;
        svg.style.transform = `translate3d(0, ${(-laughPose.bob * 3.2).toFixed(1)}px, 0) scale(${(1.07 - 0.07 * settle).toFixed(3)})`;
        eyes.style.transform = `translate3d(${(5 * (1 - settle)).toFixed(1)}px, 0, 0) scale(${(1.14 - 0.14 * settle).toFixed(3)}, ${((0.9 + 0.1 * settle) * (1 - laughPose.intensity * 0.10)).toFixed(3)})`;
        setVignette(0.12 * (1 - f));
      }
    }
    requestAnimationFrame(step2A);
  }
  requestAnimationFrame(step2A);
  }

  function runD(){
  const holePos = jsonThrowHoleSpot();
  const holeX = holePos.x, holeY = holePos.y;
  const formEnd = 720;
  const suckEnd = 1450;
  const closeEnd = 1940;
  const smirkStart = closeEnd + 900;
  const smirkEnd = smirkStart + 900;
  const snapStart = smirkEnd + 300;
  const snapGestureSpeed = 1.4; // 40% faster finger-snap gesture.
  const postSnapSpeed = 1.4; // Carry the same speed through the manifestation and return.
  const snapGestureDuration = Math.round(650 / snapGestureSpeed);
  const handGestureDuration = Math.round(1300 / snapGestureSpeed);
  const snapFlashDuration = 260;
  const snapLabelDuration = 360;
  const snapTrailDuration = 700;
  const snapVignetteDuration = 350;
  const postSnapAppearDuration = Math.round(760 / postSnapSpeed);
  const postSnapStagger = Math.round(42 / postSnapSpeed);
  const snapAt = snapStart + snapGestureDuration;
  const mouthPath = IMP.mouthPath;
  const headGroup = IMP.head;
  const horns = IMP.horns;
  const browL = IMP.browL;
  const browR = IMP.browR;
  const mouthIdle = [59, 95, 80, 116, 101, 95];
  const mouthStrained = [65, 100, 80, 92, 95, 100];
  const mouthCalm = [59, 97, 80, 106, 101, 97];
  const mouthSmirk = [58, 100, 81, 117, 101, 91];
  const browNeutralL = [51, 52, 48, 64, 73, 73];
  const browNeutralR = [109, 52, 112, 64, 87, 73];
  const browTenseL = [51, 44, 48, 61, 73, 80];
  const browTenseR = [109, 44, 112, 61, 87, 80];
  const browSmirkL = [51, 57, 48, 67, 73, 77];
  const browSmirkR = [109, 43, 112, 59, 87, 72];
  function blendMouth(a, b, p) {
    const q = clamp01(p);
    return a.map((v, i) => v + (b[i] - v) * q);
  }
  function putMouth(points) {
    mouthPath.setAttribute('d', `M ${points[0].toFixed(1)} ${points[1].toFixed(1)} Q ${points[2].toFixed(1)} ${points[3].toFixed(1)} ${points[4].toFixed(1)} ${points[5].toFixed(1)}`);
  }
  function putBrow(path, points) {
    path.setAttribute('d', `M ${points[0].toFixed(1)} ${points[1].toFixed(1)} L ${points[2].toFixed(1)} ${points[3].toFixed(1)} L ${points[4].toFixed(1)} ${points[5].toFixed(1)}`);
  }
  function putBrows(left, right) {
    putBrow(browL, left);
    putBrow(browR, right);
  }
  let cardsManifested = false;

  function drawBlackHole(size, now) {
    const s = clamp01(size);
    if (s <= 0.002) return '';
    const rx = 250 * s, ry = 112 * s;
    const core = 105 * s;
    const rot = now * 0.055;
    let debris = '';
    for (let i = 0; i < 25; i++) {
      const a = i * 2.399 + now * 0.0013;
      const r = (0.76 + (i % 5) * 0.065) * rx;
      const x = holeX + Math.cos(a) * r;
      const y = holeY + Math.sin(a) * ry * (0.82 + (i % 3) * 0.08);
      const inward = 8 + (i % 4) * 4;
      debris += fxStreak(x, y, x - Math.cos(a) * inward, y - Math.sin(a) * inward, 1.2 + (i % 3) * 0.55,
                         i % 2 ? 'var(--t-accent2)' : 'var(--t-accent)', 0.32 + 0.4 * s);
      if (i % 4 === 0) debris += fxStarburst(x, y, 3.5 + s * 2, 1.4, 4, rot + i * 8, '#ffffff', 0.65, 0.65);
    }
    return `
      <defs>
        <linearGradient id="bhDiskGrad" x1="0%" y1="20%" x2="100%" y2="80%">
          <stop offset="0%" stop-color="var(--t-accent2)"/>
          <stop offset="48%" stop-color="#ffffff"/>
          <stop offset="100%" stop-color="var(--t-accent)"/>
        </linearGradient>
        <radialGradient id="bhHaloGrad">
          <stop offset="0%" stop-color="#02030a" stop-opacity="0"/>
          <stop offset="55%" stop-color="var(--t-accent)" stop-opacity="0.13"/>
          <stop offset="78%" stop-color="var(--t-accent2)" stop-opacity="0.36"/>
          <stop offset="100%" stop-color="#02030a" stop-opacity="0"/>
        </radialGradient>
      </defs>
      <ellipse cx="${holeX}" cy="${holeY}" rx="${(rx * 1.22).toFixed(1)}" ry="${(ry * 1.85).toFixed(1)}" fill="url(#bhHaloGrad)" opacity="${s.toFixed(2)}"/>
      <ellipse cx="${holeX}" cy="${holeY}" rx="${rx.toFixed(1)}" ry="${ry.toFixed(1)}" fill="rgba(3,5,14,0.82)" stroke="url(#bhDiskGrad)" stroke-width="${(2 + s * 5).toFixed(1)}" opacity="${s.toFixed(2)}" transform="rotate(${rot.toFixed(1)} ${holeX} ${holeY})"/>
      <ellipse cx="${holeX}" cy="${holeY}" rx="${(rx * 0.91).toFixed(1)}" ry="${(ry * 0.57).toFixed(1)}" fill="none" stroke="url(#bhDiskGrad)" stroke-width="${(4 + s * 10).toFixed(1)}" opacity="${(0.6 * s).toFixed(2)}" transform="rotate(${(-rot * 0.7).toFixed(1)} ${holeX} ${holeY})"/>
      <ellipse cx="${holeX}" cy="${holeY}" rx="${(rx * 0.77).toFixed(1)}" ry="${(ry * 0.37).toFixed(1)}" fill="none" stroke="#ffffff" stroke-width="${(1.2 + s * 2).toFixed(1)}" stroke-dasharray="12 8" opacity="${(0.55 * s).toFixed(2)}" transform="rotate(${(rot * 1.6).toFixed(1)} ${holeX} ${holeY})"/>
      <circle cx="${holeX}" cy="${holeY}" r="${(core * 1.18).toFixed(1)}" fill="#070a12" stroke="var(--t-accent)" stroke-width="${(2 + s * 3).toFixed(1)}" opacity="${s.toFixed(2)}"/>
      <circle cx="${holeX}" cy="${holeY}" r="${core.toFixed(1)}" fill="#010208" opacity="${s.toFixed(2)}"/>
      <ellipse cx="${holeX}" cy="${holeY + core * 0.16}" rx="${(core * 0.84).toFixed(1)}" ry="${(core * 0.22).toFixed(1)}" fill="rgba(0,0,0,0.72)" opacity="${s.toFixed(2)}"/>
      ${debris}
    `;
  }

  function step3(now) {
    if (myRun !== animRunId) return;
    const t = now - startTime;
    const postSnapAge = (t - snapAt) * postSnapSpeed;
    let holeSize = 0;
    let phase = 'form';
    let finishCleanup = false;

    if (t < formEnd) {
      // FORM: an enormous gravity well blooms around the upload.
      phase = 'form';
      holeSize = easeOutCubic(t / formEnd);
      const gather = clamp01((t - formEnd * 0.42) / (formEnd * 0.58));
      const orbit = Math.sin(t * 0.008) * (1 - gather) * 18;
      badge.style.opacity = '1';
      badge.style.transform = `translate3d(${(startFilePos.x + orbit).toFixed(1)}px, ${(startFilePos.y + Math.cos(t * 0.01) * 5).toFixed(1)}px, 0) rotate(${(t * 0.08).toFixed(1)}deg) scale(1)`;
      setVignette(0.32 * holeSize);
    } else if (t < suckEnd) {
      // SUCK: file spirals inward and vanishes into the black core.
      phase = 'suck';
      holeSize = 1 + Math.sin(t * 0.012) * 0.018;
      const q = easeInOutCubic((t - formEnd) / (suckEnd - formEnd));
      const orbit = (1 - q) * (68 + Math.sin(t * 0.03) * 14);
      const a = t * 0.017;
      const x = startFilePos.x + (holeX - startFilePos.x) * q + Math.cos(a) * orbit;
      const y = startFilePos.y + (holeY - startFilePos.y) * q + Math.sin(a) * orbit * 0.48;
      const scale = 1 - q * 0.93;
      badge.style.opacity = String(Math.max(0, 1 - q * 0.94));
      badge.style.transform = `translate3d(${x.toFixed(1)}px, ${y.toFixed(1)}px, 0) rotate(${(t * 0.22).toFixed(1)}deg) scale(${scale.toFixed(3)})`;
      setVignette(0.36);
    } else if (t < closeEnd) {
      // CLOSE: the disk folds in on itself and collapses to a pinprick.
      phase = 'close';
      const q = easeInOutCubic((t - suckEnd) / (closeEnd - suckEnd));
      holeSize = 1 - q;
      badge.style.opacity = '0';
      setVignette(0.36 * (1 - q));
      setFlash(0);
      shakeScene(4.5 * q, t);
    } else {
      badge.style.opacity = '0';
      setVignette(postSnapAge < snapVignetteDuration ? 0.08 : 0);
      setFlash(0);
    }

    let fx = '';
    if (holeSize > 0.002) {
      fx += drawBlackHole(holeSize, t);
      // A visible tether sells the Imp as the source of the gravity spell.
      const amp = Math.min(1, holeSize * 1.4);
      fx += `<path d="M ${impPos.x + 30} ${impPos.y - 14} Q ${(impPos.x + holeX) * 0.48} ${holeY - 105 * amp} ${holeX - 74 * amp} ${holeY.toFixed(1)}" fill="none" stroke="var(--t-accent)" stroke-width="${(2 + amp * 2).toFixed(1)}" stroke-dasharray="10 8" opacity="${(0.38 + amp * 0.42).toFixed(2)}"/>`;
      fx += `<path d="M ${impPos.x + 28} ${impPos.y + 10} Q ${(impPos.x + holeX) * 0.52} ${holeY + 100 * amp} ${holeX - 70 * amp} ${holeY.toFixed(1)}" fill="none" stroke="var(--t-accent2)" stroke-width="${(1.4 + amp * 1.5).toFixed(1)}" stroke-dasharray="5 11" opacity="${(0.32 + amp * 0.38).toFixed(2)}"/>`;
    } else if (t >= closeEnd && t < closeEnd + 180) {
      const q = (t - closeEnd) / 180;
      fx += fxStarburst(holeX, holeY, 26 + q * 80, 8 + q * 24, 12, t * 0.12, '#ffffff', 1 - q, (1 - q) * 0.42);
      fx += fxRing(holeX, holeY, 18 + q * 100, 3 * (1 - q), 'var(--t-accent)', 0.9 * (1 - q));
      fx += fxRing(holeX, holeY, 10 + q * 68, 2.2 * (1 - q), 'var(--t-accent2)', 0.75 * (1 - q), '9 7');
    }

    if (t < closeEnd) {
      // During the spell his face tightens with effort, then begins to
      // unwind as the singularity folds shut.
      const strainP = easeInOutCubic(clamp01((t - 80) / 620));
      const closingP = phase === 'close' ? easeInOutCubic((t - suckEnd) / (closeEnd - suckEnd)) : 0;
      const tenseFace = blendMouth(mouthIdle, mouthStrained, strainP);
      putMouth(blendMouth(tenseFace, mouthCalm, closingP * 0.22));
      const tenseBrowL = blendMouth(browNeutralL, browTenseL, strainP);
      const tenseBrowR = blendMouth(browNeutralR, browTenseR, strainP);
      putBrows(blendMouth(tenseBrowL, browNeutralL, closingP * 0.32), blendMouth(tenseBrowR, browNeutralR, closingP * 0.32));
      const squeeze = phase === 'close' ? 1 - (t - suckEnd) / (closeEnd - suckEnd) : 0;
      const breath = Math.sin(t * 0.012) * holeSize * 1.6;
      svg.style.transform = `translate3d(${(Math.sin(t * 0.015) * holeSize * 3).toFixed(1)}px, ${breath.toFixed(1)}px, 0) scale(${(1.04 + holeSize * 0.06).toFixed(3)})`;
      eyes.style.transform = `scale(${(1.08 + holeSize * 0.17).toFixed(3)}, ${(1.02 + holeSize * 0.08).toFixed(3)})`;
      mouth.style.transform = `scale(${(1 + holeSize * 0.18 + squeeze * 0.12).toFixed(2)}, ${(1 + holeSize * 0.12).toFixed(2)})`;
      headGroup.style.transform = `rotate(${(-2.4 * strainP - 3.2 * squeeze).toFixed(2)}deg)`;
      horns.style.transform = `rotate(${(2.2 * strainP + 2.6 * squeeze).toFixed(2)}deg)`;
      haloL.setAttribute('opacity', String(0.35 + holeSize * 0.65));
      haloR.setAttribute('opacity', String(0.35 + holeSize * 0.65));
      shakeScene(phase === 'close' ? 5 * (1 - squeeze) : holeSize * 1.4, t);
    } else {
      // Let the face recover first: eyes glance toward the posts, the
      // strained mouth eases back to neutral, then the smirk draws slowly.
      const recoveryP = easeInOutCubic(clamp01((t - closeEnd) / (smirkStart - closeEnd)));
      const smirkP = easeInOutCubic(clamp01((t - smirkStart) / (smirkEnd - smirkStart)));
      const mouthPoints = t < smirkStart
        ? blendMouth(mouthStrained, mouthCalm, recoveryP)
        : blendMouth(mouthCalm, mouthSmirk, smirkP);
      putMouth(mouthPoints);
      putBrows(
        t < smirkStart ? blendMouth(browTenseL, browNeutralL, recoveryP) : blendMouth(browNeutralL, browSmirkL, smirkP),
        t < smirkStart ? blendMouth(browTenseR, browNeutralR, recoveryP) : blendMouth(browNeutralR, browSmirkR, smirkP)
      );

      const glance = 1 - recoveryP;
      const blinkCenter = closeEnd + 480;
      const blinkPulse = easeInOutCubic(clamp01(1 - Math.abs(t - blinkCenter) / 115));
      const blinkFactor = 1 - 0.92 * blinkPulse;
      const eyeSaccade = Math.sin((t - closeEnd) * 0.021) * 1.15 * glance;
      const eyeX = 6 * glance + 1.4 * smirkP + eyeSaccade;
      const eyeY = -1.5 * glance + 0.8 * smirkP;
      const eyeScaleX = 1.15 - 0.22 * recoveryP + 0.025 * smirkP;
      const eyeScaleY = (1.12 - 0.30 * recoveryP) * blinkFactor;
      eyes.style.transform = `translate3d(${eyeX.toFixed(1)}px, ${eyeY.toFixed(1)}px, 0) scale(${eyeScaleX.toFixed(3)}, ${eyeScaleY.toFixed(3)})`;

      const faceProgress = clamp01((t - closeEnd) / (smirkEnd - closeEnd));
      const cameraPush = Math.sin(faceProgress * Math.PI) * 0.065;
      const breath = Math.sin((t - closeEnd) * 0.009) * (1 - smirkP) * 1.1;
      svg.style.transform = `translate3d(${(smirkP * 2).toFixed(1)}px, ${breath.toFixed(1)}px, 0) scale(${(1.045 + cameraPush).toFixed(3)})`;
      headGroup.style.transform = `rotate(${(2.2 * (1 - recoveryP) - 1.4 * smirkP).toFixed(2)}deg)`;
      horns.style.transform = `rotate(${(-2.4 * (1 - recoveryP) + 1.2 * smirkP).toFixed(2)}deg)`;
      mouth.style.transform = `scale(${(1 + smirkP * 0.12).toFixed(3)}, ${(1 + smirkP * 0.08).toFixed(3)})`;
      haloL.setAttribute('opacity', String(0.48 * (1 - recoveryP) + 0.12));
      haloR.setAttribute('opacity', String(0.48 * (1 - recoveryP) + 0.12));

      // Tiny eye-corner glint punctuates the end of the slow grin reveal.
      if (smirkP > 0.58 && smirkP < 1) {
        fx += fxStarburst(impPos.x + 28, impPos.y + 9, 4 + smirkP * 4, 1.8, 4, t * 0.07, '#ffffff', (smirkP - 0.58) * 1.8, 0.45);
      }

      // Only after the smirk has settled does the shoulder-mounted hand rise.
      if (t >= snapStart) {
        const snapQ = easeOutCubic(clamp01((t - snapStart) / (snapAt - snapStart)));
        // Speed the entire post-snap sequence, including the eased face/arm return.
        const finishStartAfterSnap = 900;
        const finishDuration = 650;
        const finishRaw = cardsManifested ? clamp01((postSnapAge - finishStartAfterSnap) / finishDuration) : 0;
        const finish = easeInOutCubic(finishRaw);
        const finishing = cardsManifested && postSnapAge >= finishStartAfterSnap;
        // Unified shoulder — right imaginary shoulder same as devour (126,118)
        const shoulderX = (function(){ try{ let e=document.getElementById('imp-sh-R'); if(!e){ e=document.createElementNS('http://www.w3.org/2000/svg','circle'); e.setAttribute('id','imp-sh-R'); e.setAttribute('class','imp-extra'); e.setAttribute('cx','126'); e.setAttribute('cy','118'); e.setAttribute('r','0.6'); e.setAttribute('fill','none'); if(svg) svg.appendChild(e);} const r=e.getBoundingClientRect(); return r.left+r.width/2; }catch(_){ return impPos.x+30; }})();
        const shoulderY = (function(){ try{ let e=document.getElementById('imp-sh-R'); const r=e.getBoundingClientRect(); return r.top+r.height/2; }catch(_){ return impPos.y+25; }})();
        const handReturn = finishing ? finish : 0;
        const handX = impPos.x + 95 + snapQ * 15 - handReturn * 22;
        const handY = impPos.y + 34 - snapQ * 14 + handReturn * 18;
        // The selected hand's load → inward flick → settle fits around the existing snap event.
        const gestureProgress = clamp01((t - snapStart) / handGestureDuration);
        const handPose = snapHandVariantBMetrics(handX, handY, gestureProgress);
        const snapX = handPose.contactX, snapY = handPose.contactY;
        const snapAge = postSnapAge;
        const snapFlash = snapAge >= 0 ? Math.max(0, 1 - snapAge / snapFlashDuration) : 0;
        fx += `<g opacity="${(1 - handReturn).toFixed(3)}">${renderSnapArm(shoulderX, shoulderY, handX, handY, 'var(--t-accent)')}${renderSnapHandVariantB(handPose, 'var(--t-accent)', 'var(--t-accent2)')}</g>`;
        if (snapAge >= 0) {
          fx += fxStarburst(snapX, snapY, 10 + snapFlash * 28, 4 + snapFlash * 10, 8, t * 0.12, '#ffffff', snapFlash, snapFlash * 0.25);
          fx += fxRing(snapX, snapY, 8 + (1 - snapFlash) * 56, 2.8 * snapFlash, 'var(--t-accent)', snapFlash, '5 5');
          if (snapAge < snapLabelDuration) {
            fx += `<text x="${snapX + 13}" y="${snapY - 20}" fill="#ffffff" font-size="17" font-weight="1000" stroke="#0b1020" stroke-width="3" paint-order="stroke" transform="rotate(-12 ${snapX + 13} ${snapY - 20})">SNAP!</text>`;
          }
        }

        if (t >= snapAt && !cardsManifested) {
          cardsManifested = true;
          cardTargets.forEach((target, idx) => {
            target.el.style.animationDuration = `${postSnapAppearDuration}ms`;
            target.el.style.animationDelay = `${idx * postSnapStagger}ms`;
            target.el.classList.add('card-throw-landed', 'magic-appear');
            setTimeout(() => {
              target.el.classList.remove('magic-appear');
              target.el.style.animationDelay = '';
              target.el.style.animationDuration = '';
            }, Math.round((950 + idx * 42) / postSnapSpeed));
          });
        }
        if (snapAge >= 0 && snapAge < snapTrailDuration) {
          const q = snapAge / snapTrailDuration;
          cardTargets.forEach((target, idx) => {
            const col = idx % 2 ? 'var(--t-accent2)' : 'var(--t-accent)';
            const drift = (idx % 3 - 1) * 18;
            fx += `<path d="M ${snapX} ${snapY} Q ${(snapX + target.x) * 0.5 + drift} ${(Math.min(snapY, target.y) - 58 - idx * 4).toFixed(1)} ${target.x.toFixed(1)} ${target.y.toFixed(1)}" fill="none" stroke="${col}" stroke-width="1.7" stroke-dasharray="5 9" opacity="${(0.62 * (1 - q)).toFixed(2)}"/>`;
            fx += fxRing(target.x, target.y, 18 + q * 36, 2.2 * (1 - q), col, (1 - q) * 0.82);
            if (q < 0.55) fx += fxSparks(target.x, target.y, q / 0.55, 4, 34, idx * 0.83, '#ffffff', col);
          });
        }

        if (finishing) {
          // Once all reveal animations have finished, ease the Imp back to idle
          // while the snap arm withdraws toward its shoulder and gently fades.
          putMouth(blendMouth(mouthSmirk, mouthIdle, finish));
          putBrows(blendMouth(browSmirkL, browNeutralL, finish), blendMouth(browSmirkR, browNeutralR, finish));
          eyes.style.transform = `translate3d(${(1.4 * (1 - finish)).toFixed(2)}px, ${(0.8 * (1 - finish)).toFixed(2)}px, 0) scale(${(0.955 + 0.045 * finish).toFixed(3)}, ${(0.82 + 0.18 * finish).toFixed(3)})`;
          svg.style.transform = `translate3d(${(2 * (1 - finish)).toFixed(2)}px, 0, 0) scale(${(1.045 - 0.045 * finish).toFixed(3)})`;
          headGroup.style.transform = `rotate(${(-1.4 * (1 - finish)).toFixed(2)}deg)`;
          horns.style.transform = `rotate(${(1.2 * (1 - finish)).toFixed(2)}deg)`;
          mouth.style.transform = `scale(${(1.12 - 0.12 * finish).toFixed(3)}, ${(1.08 - 0.08 * finish).toFixed(3)})`;
          haloL.setAttribute('opacity', String(0.12 * (1 - finish)));
          haloR.setAttribute('opacity', String(0.12 * (1 - finish)));
        }
        if (cardsManifested && finishRaw >= 1) finishCleanup = true;
      }
      setVignette(postSnapAge < snapVignetteDuration ? 0.08 : 0);
      setFlash(0);
    }
    tetherSvg.innerHTML = fx;
    if (finishCleanup) {
      cleanupProjectiles();

      return;
    }
    requestAnimationFrame(step3);
  }
  requestAnimationFrame(step3);
  }

  if(variant === 1) runA();
  else if(variant === 3) runB();
  else if(variant === 4) runC();
  else if(variant === 5) runD();
  else cleanup();
}

/* ---------- entry points ---------- */
window.LS_JSON_THROW = {
  play: function(variant, fileName, totalCount){
    return play(Number(variant) || 0, fileName, totalCount);
  },
  cleanup: cleanup
};

/* equal chance for each of the five; the two debug overrides pin one */
window.playImpJsonThrowRandom = function(fileName, totalCount){
  var v = null;
  try{
    var q = (location.search || '').match(/[?&]jsonThrow=(\d)/);
    if(q) v = parseInt(q[1], 10);
  }catch(_){}
  if(v === null){
    try{ var s = localStorage.getItem('stillSane.jsonThrowVariant'); if(s) v = parseInt(s, 10); }catch(_){}
  }
  if(!(v >= 1 && v <= 5)) v = 1 + Math.floor(Math.random() * 5);
  if(v === 2) return window.playImpJsonGrabAndThrow1B(fileName, totalCount);
  return play(v, fileName, totalCount);
};
})();
