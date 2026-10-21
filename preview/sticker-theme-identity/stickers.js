/* ===========================================================================
   In[st]Sanity — sticker identity pass, rev 2 (proposal only).

   Same anatomy contract as the app (frame / decor / face with imp-horns,
   imp-eyes-track > imp-eyes-blink, imp-grin), all gradient ids keep {uid}.

   rev 2 notes:
     00 reworked into a frosted glass lens with aurora caught inside it
     01 corner shards replaced by four matching neon chevrons, the bottom
        bars are now a real equaliser (imp-eq-bar), fangs follow the palette
     02 new plate kept, horns reverted to the original thin stroke
     03 head back to a circle, horns reverted to the original shape
     04 keeps the new arch/drapes but wears the ORIGINAL crown
     05 keeps the new shield/orbit but wears the OLD crown
     08 new bubbles + lure + head, old scalloped border restored
     09 old neon borders/background restored, new head treatment kept
     12 improved: forged hammers, anvil with horn + hot billet, embers
     13/14/15 built again from scratch as different concepts
   =========================================================================== */
(function (root) {
  'use strict';

  var HORNS = 'M56 56 L52 72 L71 78 M104 56 L108 72 L89 78';

  function assemble(defs, frame, decor, face) {
    return '<svg viewBox="0 0 160 160" fill="none">' +
      (defs ? '<defs>' + defs + '</defs>' : '') +
      '<g class="imp-outer-frame">' + frame + '</g>' +
      '<g class="imp-decor">' + decor + '</g>' +
      face + '</svg>';
  }

  function eyes(c1, c2, r) {
    r = r || 4.2;
    return '<circle cx="68" cy="83" r="' + r + '" fill="' + c1 + '"/>' +
      '<circle cx="92" cy="83" r="' + r + '" fill="' + c2 + '"/>' +
      '<circle cx="69.5" cy="81.4" r="1.25" fill="#ffffff" opacity=".92"/>' +
      '<circle cx="93.5" cy="81.4" r="1.25" fill="#ffffff" opacity=".92"/>';
  }
  function grin(c, w) {
    return '<path d="M62 94 Q80 109 98 94" stroke="' + c + '" stroke-width="' + (w || 3.4) +
      '" stroke-linecap="round" fill="none"/>';
  }
  function fangs(c, size) {
    var s = size || 1;
    return '<polygon points="69,97 73,' + (104 * s) + ' 76,99" fill="' + c + '"/>' +
      '<polygon points="91,97 87,' + (104 * s) + ' 84,99" fill="' + c + '"/>';
  }
  /* the stock zig-zag horns, used verbatim by the themes that keep them */
  function stockHorns(color, w) {
    return '<path d="' + HORNS + '" stroke="' + color + '" stroke-width="' + (w || 3.4) +
      '" stroke-linecap="round" stroke-linejoin="round" fill="none"/>';
  }
  function face(o) {
    var head = o.head || ('<circle class="imp-head-circle" cx="80" cy="80" r="' + (o.r || 41) +
      '" fill="' + o.fill + '" stroke="' + o.stroke + '" stroke-width="' + (o.sw || 2.6) + '"/>');
    return '<g class="imp-face-group">' +
      (o.under || '') + head + (o.over || '') +
      '<g class="imp-horns">' + (o.horns || HORNS) + '</g>' +
      '<g class="imp-eyes-track"><g class="imp-eyes-blink">' + (o.eyes || eyes('#38bdf8', '#ec4899')) + '</g></g>' +
      '<g class="imp-grin">' + (o.grin || grin('#ffffff')) + '</g>' +
      (o.extra || '') +
      '</g>';
  }

  /* =========================================================================
     00 — DEFAULT · Frosted Glass & Aurora   (original design, improved)
     The shipping sticker kept exactly as it is — rounded plate, aurora wash,
     diamond, bottom ribbons, four sparkles — with the details cleaned up: the
     ribbons are now layered light instead of two flat lines, the diamond gets
     a faint inner facet, and the whole plate catches one soft highlight.
     ====================================================================== */
  var vDefault = assemble(
    '<linearGradient id="st_defEdge_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#38bdf8"/><stop offset="50%" stop-color="#818cf8"/><stop offset="100%" stop-color="#ec4899"/></linearGradient>' +
    '<radialGradient id="st_defGlow_{uid}" cx="50%" cy="50%" r="48%">' +
      '<stop offset="0%" stop-color="#38bdf8" stop-opacity=".38"/><stop offset="55%" stop-color="#ec4899" stop-opacity=".22"/><stop offset="100%" stop-color="#0f172a" stop-opacity="0"/></radialGradient>',
    '<rect x="14" y="14" width="132" height="132" rx="30" fill="rgba(15,23,42,.42)" stroke="url(#st_defEdge_{uid})" stroke-width="3.4"/>' +
    '<rect x="20" y="20" width="120" height="120" rx="24" fill="url(#st_defGlow_{uid})" stroke="rgba(236,72,153,.45)" stroke-width="1.5"/>' +
    '<polygon points="80,20 132,80 80,140 28,80" fill="rgba(30,41,59,.36)" stroke="url(#st_defEdge_{uid})" stroke-width="2.4" stroke-linejoin="round"/>' +
    '<polygon points="80,36 116,80 80,124 44,80" fill="none" stroke="rgba(165,180,252,.14)" stroke-width="1.1" stroke-linejoin="round"/>' +
    '<path d="M31 46 C 36 32 46 23 60 19" fill="none" stroke="#e8f4ff" stroke-opacity=".2" stroke-width="2" stroke-linecap="round"/>',
    /* layered aurora ribbons along the bottom + the four sparkles */
    '<g fill="none" stroke-linecap="round">' +
      '<path d="M22 129 C 52 111, 82 139, 138 117" stroke="#38bdf8" stroke-width="5" stroke-opacity=".14"/>' +
      '<path d="M22 129 C 52 111, 82 139, 138 117" stroke="#38bdf8" stroke-width="2.1" stroke-opacity=".6"/>' +
      '<path d="M26 137 C 58 120, 88 143, 136 126" stroke="#ec4899" stroke-width="4" stroke-opacity=".13"/>' +
      '<path d="M26 137 C 58 120, 88 143, 136 126" stroke="#ec4899" stroke-width="1.7" stroke-opacity=".5"/>' +
      '<path d="M30 121 C 56 106, 86 131, 132 110" stroke="#818cf8" stroke-width="1.2" stroke-opacity=".32"/>' +
    '</g>' +
    '<path d="M34 30 L36.5 37 L43.5 39.5 L36.5 42 L34 49 L31.5 42 L24.5 39.5 L31.5 37 Z" fill="#38bdf8"/>' +
    '<path d="M126 30 L128.5 37 L135.5 39.5 L128.5 42 L126 49 L123.5 42 L116.5 39.5 L123.5 37 Z" fill="#ec4899"/>' +
    '<path d="M34 112 L36 117.5 L41.5 119.5 L36 121.5 L34 127 L32 121.5 L26.5 119.5 L32 117.5 Z" fill="#818cf8"/>' +
    '<path d="M126 112 L128 117.5 L133.5 119.5 L128 121.5 L126 127 L124 121.5 L118.5 119.5 L124 117.5 Z" fill="#38bdf8"/>' +
    '<g fill="#dff3ff"><circle cx="46" cy="24" r="1.2" opacity=".7"/><circle cx="114" cy="24" r="1" opacity=".55"/>' +
      '<circle cx="22" cy="78" r="1" opacity=".45"/><circle cx="138" cy="78" r="1.2" opacity=".5"/></g>',
    face({
      fill: 'rgba(15,23,42,.52)', stroke: 'url(#st_defEdge_{uid})', sw: 2.6,
      over: '<path d="M50 62 C 58 52 68 47 80 46" stroke="#ffffff" stroke-opacity=".16" stroke-width="2.2" stroke-linecap="round" fill="none"/>',
      horns: stockHorns('#ffffff'),
      eyes: eyes('#38bdf8', '#ec4899'),
      grin: grin('#ffffff')
    })
  );

  /* =========================================================================
     01 — NEON OBSIDIAN
     Shard triangles gone: all four corners wear the same neon chevron.
     The bottom strips are a real equaliser (imp-eq-bar) and the fangs pick
     up the theme gradient instead of white.
     ====================================================================== */
  var EQ_H = [9, 16, 12, 24, 15, 20, 10];
  var eqBars = EQ_H.map(function (h, i) {
    var x = (80 + (i - 3) * 7.6 - 2.3).toFixed(1);
    return '<rect class="imp-eq-bar" x="' + x + '" y="' + (137 - h) + '" width="4.6" height="' + h +
      '" rx="2.3" fill="' + (i % 2 ? '#ee2a7b' : '#f8b250') + '" style="animation-delay:' + (i * 0.12).toFixed(2) + 's"/>';
  }).join('');
  var v1 = assemble(
    '<linearGradient id="st_v1Edge_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#f8b250"/><stop offset="35%" stop-color="#f46856"/><stop offset="70%" stop-color="#ee2a7b"/><stop offset="100%" stop-color="#9d4edd"/></linearGradient>' +
    '<linearGradient id="st_v1Tube_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#f8b250"/><stop offset="55%" stop-color="#ee2a7b"/><stop offset="100%" stop-color="#a855f7"/></linearGradient>',
    '<polygon points="48,13 112,13 147,48 147,112 112,147 48,147 13,112 13,48" fill="rgba(18,13,26,.66)" stroke="url(#st_v1Edge_{uid})" stroke-width="3.6" stroke-linejoin="round"/>' +
    '<polygon points="52,22 108,22 138,52 138,108 108,138 52,138 22,108 22,52" fill="none" stroke="rgba(248,178,80,.28)" stroke-width="1.2" stroke-linejoin="round"/>' +
    '<path d="M22 52 L52 22 L60 60 Z" fill="rgba(157,78,221,.16)"/>' +
    '<path d="M138 108 L108 138 L100 100 Z" fill="rgba(238,42,123,.14)"/>',
    '<g stroke-linecap="round" stroke-linejoin="round" fill="none">' +
      '<path d="M28 48 V28 H48" stroke="#f8b250" stroke-opacity=".17" stroke-width="8"/>' +
      '<path d="M132 48 V28 H112" stroke="#ee2a7b" stroke-opacity=".17" stroke-width="8"/>' +
      '<path d="M132 112 V132 H112" stroke="#ee2a7b" stroke-opacity=".17" stroke-width="8"/>' +
      '<path d="M28 112 V132 H48" stroke="#f8b250" stroke-opacity=".17" stroke-width="8"/>' +
      '<path d="M28 48 V28 H48" stroke="#f8b250" stroke-width="2.6"/>' +
      '<path d="M132 48 V28 H112" stroke="#ee2a7b" stroke-width="2.6"/>' +
      '<path d="M132 112 V132 H112" stroke="#ee2a7b" stroke-width="2.6"/>' +
      '<path d="M28 112 V132 H48" stroke="#f8b250" stroke-width="2.6"/>' +
    '</g>' +
    eqBars +
    '<path d="M47 140 H113" stroke="rgba(248,178,80,.5)" stroke-width="1.3" stroke-linecap="round"/>' +
    '<ellipse cx="80" cy="138" rx="34" ry="7" fill="#f8b250" opacity=".1"/>' +
    '<circle cx="80" cy="17" r="1.8" fill="#f8b250" opacity=".8"/>',
    face({
      fill: 'rgba(24,17,36,.74)', stroke: 'url(#st_v1Edge_{uid})', sw: 2.8,
      over: '<path d="M80 39 L104 56 L96 80 Z" fill="rgba(238,42,123,.12)"/>' +
        '<path d="M80 39 L56 56 L64 80 Z" fill="rgba(248,178,80,.12)"/>' +
        '<path d="M56 104 L80 121 L104 104" fill="none" stroke="rgba(157,78,221,.24)" stroke-width="1.4"/>',
      horns: '<path d="' + HORNS + '" stroke="url(#st_v1Tube_{uid})" stroke-width="7" stroke-opacity=".22" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="' + HORNS + '" stroke="url(#st_v1Tube_{uid})" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#f8b250', '#ee2a7b'),
      grin: grin('url(#st_v1Edge_{uid})') + fangs('url(#st_v1Edge_{uid})', 1)
    })
  );

  /* =========================================================================
     02 — TITANIUM SLATE & MERCURY-MINT
     Bigger plate, heavier border: the hex now runs to the frame edge and the
     stroke is the thickest of the set, with the bolts pushed out to match.
     ====================================================================== */
  function bolt(x, y, r) {
    r = r || 5.4;
    return '<g transform="translate(' + x + ' ' + y + ')">' +
      '<circle r="' + r + '" fill="#1b2630" stroke="#99f6e4" stroke-width="1.3"/>' +
      '<path d="M-' + (r * 0.5) + ' 0 H' + (r * 0.5) + ' M0 -' + (r * 0.5) + ' V' + (r * 0.5) +
      '" stroke="#8aa3b2" stroke-width="1.2" stroke-linecap="round"/></g>';
  }
  var mintLeaf = function (x, y, rot, sc) {
    return '<g transform="translate(' + x + ' ' + y + ') rotate(' + rot + ') scale(' + sc + ')">' +
      '<path d="M0 0 C 3 -5 9 -6 12 0 C 9 6 3 5 0 0 Z" fill="#99f6e4" stroke="#2dd4bf" stroke-width=".9" stroke-linejoin="round"/>' +
      '<path d="M1.5 0 H9.5" stroke="#0f766e" stroke-width=".7" stroke-linecap="round" opacity=".55"/>' +
      '</g>';
  };
  var mintSprig = function () {
    return '<g>' +
      '<path d="M25 134 C 29 126 34 119 42 111" fill="none" stroke="#2dd4bf" stroke-width="1.7" stroke-linecap="round"/>' +
      mintLeaf(28, 129, -118, 1.15) +
      mintLeaf(34, 121, -46, 1.05) +
      mintLeaf(40, 114, -96, .95) +
      mintLeaf(42, 110, -142, .78) +
      '</g>';
  };
  var v2 = assemble(
    '<linearGradient id="st_v2Metal_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#f8fafc"/><stop offset="50%" stop-color="#99f6e4"/><stop offset="100%" stop-color="#2dd4bf"/></linearGradient>',
    '<polygon points="80,5 144,42 144,118 80,155 16,118 16,42" fill="rgba(22,31,40,.62)" stroke="url(#st_v2Metal_{uid})" stroke-width="5" stroke-linejoin="round"/>' +
    '<polygon points="80,16 134,47 134,113 80,144 26,113 26,47" fill="none" stroke="#3d5060" stroke-width="1.6" stroke-linejoin="round"/>' +
    '<path d="M30 50 C 40 32 58 22 76 18" stroke="#cbd5e1" stroke-opacity=".22" stroke-width="6" stroke-linecap="round" fill="none"/>' +
    '<path d="M32 60 C 42 44 56 34 70 30" stroke="#cbd5e1" stroke-opacity=".12" stroke-width="2.6" stroke-linecap="round" fill="none"/>',
    bolt(45, 34) + bolt(115, 34) + bolt(115, 126) + bolt(45, 126) +
    '<path d="M80 20 V28 M80 132 V140" stroke="#5c7382" stroke-width="1.3" stroke-linecap="round" opacity=".7"/>' +
    /* two mint sprigs growing up the lower corners */
    mintSprig() + '<g transform="translate(160 0) scale(-1 1)">' + mintSprig() + '</g>' +
    '<path d="M60 138 C 56 145 57 150 60 150 C 63 150 64 145 60 138 Z" fill="#99f6e4"/>' +
    '<path d="M80 144 C 77 150 78 154 80 154 C 82 154 83 150 80 144 Z" fill="#2dd4bf"/>' +
    '<path d="M100 138 C 96 145 97 150 100 150 C 103 150 104 145 100 138 Z" fill="#99f6e4"/>' +
    '<circle cx="60" cy="145.5" r="1.1" fill="#ffffff" opacity=".8"/><circle cx="100" cy="145.5" r="1.1" fill="#ffffff" opacity=".8"/>',
    face({
      fill: 'rgba(15,21,27,.66)', stroke: '#2dd4bf', sw: 2.4,
      over: '<circle cx="80" cy="80" r="35.5" fill="none" stroke="rgba(226,232,240,.3)" stroke-width="1.2"/>' +
        '<path d="M52 62 C 60 51 70 45 82 43" stroke="#f8fafc" stroke-opacity=".3" stroke-width="2.6" stroke-linecap="round" fill="none"/>',
      horns: stockHorns('url(#st_v2Metal_{uid})'),
      eyes: eyes('#99f6e4', '#2dd4bf'),
      grin: grin('#e2e8f0')
    })
  );

  /* =========================================================================
     03 — MINIMALIST STUDIO
     Round head restored, stock horns restored; the selection frame, handles
     and ruler ticks stay, the pointer cursor is gone.
     ====================================================================== */
  var v3 = assemble(
    '',
    '<rect x="14" y="14" width="132" height="132" rx="22" fill="rgba(17,19,24,.52)" stroke="#f4f4f6" stroke-width="3.2"/>' +
    '<rect x="21.5" y="21.5" width="117" height="117" rx="16" fill="none" stroke="#2e3442" stroke-width="1.2"/>' +
    '<path d="M22 38 H138" stroke="#2e3442" stroke-width="1.2"/>' +
    '<circle cx="30" cy="27" r="2.2" fill="#6b7280"/><circle cx="39" cy="27" r="2.2" fill="#6b7280"/><circle cx="48" cy="27" r="2.2" fill="#f4f4f6"/>' +
    '<g fill="#090a0c" stroke="#f4f4f6" stroke-width="1.6">' +
      '<rect x="16.5" y="16.5" width="7" height="7" rx="1"/><rect x="136.5" y="16.5" width="7" height="7" rx="1"/>' +
      '<rect x="16.5" y="136.5" width="7" height="7" rx="1"/><rect x="136.5" y="136.5" width="7" height="7" rx="1"/>' +
    '</g>',
    '<g stroke="#a1a1aa" stroke-width="1.4" stroke-linecap="round" opacity=".8">' +
      '<path d="M28 80 H42 M118 80 H132 M80 28 V42 M80 118 V132"/>' +
      '<path d="M26 74 V70 M26 90 V86 M134 74 V70 M134 90 V86"/>' +
    '</g>',
    face({
      fill: 'rgba(12,13,16,.6)', stroke: '#f4f4f6', sw: 2.4,
      over: '<circle cx="80" cy="80" r="35.5" fill="none" stroke="rgba(244,244,246,.16)" stroke-width="1"/>',
      horns: stockHorns('#f4f4f6', 3),
      eyes: '<circle cx="68" cy="83" r="3.7" fill="#f4f4f6"/><circle cx="92" cy="83" r="3.7" fill="#d4d4d8"/>',
      grin: grin('#f4f4f6', 3)
    })
  );

  /* =========================================================================
     04 — CRIMSON VELVET & GOLD (gothic 'Exile')
     New arch + drapes + tracery + fleur-de-lis, but the ORIGINAL crown.
     ====================================================================== */
  var v4 = assemble(
    '<linearGradient id="st_v4Gold_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#fde68a"/><stop offset="50%" stop-color="#fbbf24"/><stop offset="100%" stop-color="#b45309"/></linearGradient>' +
    '<linearGradient id="st_v4Velvet_{uid}" x1="0%" y1="0%" x2="0%" y2="100%">' +
      '<stop offset="0%" stop-color="#9f1239"/><stop offset="60%" stop-color="#6b0f24"/><stop offset="100%" stop-color="#3b0a18"/></linearGradient>',
    '<path d="M80 10 C 118 26 142 52 142 90 L142 122 C 142 138 118 150 80 152 C 42 150 18 138 18 122 L18 90 C 18 52 42 26 80 10 Z" fill="rgba(59,10,24,.6)" stroke="url(#st_v4Gold_{uid})" stroke-width="3.4"/>' +
    '<path d="M80 19 C 114 33 134 56 134 90 L134 120 C 134 133 112 143 80 145 C 48 143 26 133 26 120 L26 90 C 26 56 46 33 80 19 Z" fill="none" stroke="rgba(251,191,36,.36)" stroke-width="1.3"/>' +
    /* one clean velvet panel each side, a single fold highlight, no swags */
    '<path d="M28 46 C 33 66 30 104 35 136 L27 118 Z" fill="url(#st_v4Velvet_{uid})" stroke="#fbbf24" stroke-opacity=".38" stroke-width=".9" stroke-linejoin="round"/>' +
    '<path d="M132 46 C 127 66 130 104 125 136 L133 118 Z" fill="url(#st_v4Velvet_{uid})" stroke="#fbbf24" stroke-opacity=".38" stroke-width=".9" stroke-linejoin="round"/>' +
    '<path d="M31 60 C 34 80 31 112 33 126" stroke="#fbbf24" stroke-opacity=".28" stroke-width="1" fill="none"/>' +
    '<path d="M129 60 C 126 80 129 112 127 126" stroke="#fbbf24" stroke-opacity=".28" stroke-width="1" fill="none"/>',
    /* the original crown, kept verbatim */
    '<path d="M61 36 Q80 29 99 36 V38 H61Z" fill="url(#st_v4Gold_{uid})" stroke="#fde68a" stroke-width="1.1"/>' +
    '<path d="M64 34 L66 25 L73 31 L80 20 L87 31 L94 25 L96 34" fill="none" stroke="#fde68a" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"/>' +
    '<circle cx="66" cy="25" r="2" fill="#e11d48"/>' +
    '<circle cx="80" cy="20" r="2.5" fill="#fde68a"/>' +
    '<circle cx="94" cy="25" r="2" fill="#e11d48"/>' +
    '<path d="M80 131 C 84 135 87 139 80 146 C 73 139 76 135 80 131 Z" fill="url(#st_v4Gold_{uid})"/>' +
    '<path d="M75 137 C 68 133 66 128 71 126 C 75 129 76 133 77 136 Z" fill="url(#st_v4Gold_{uid})"/>' +
    '<path d="M85 137 C 92 133 94 128 89 126 C 85 129 84 133 83 136 Z" fill="url(#st_v4Gold_{uid})"/>' +
    '<path d="M80 121 V127" stroke="url(#st_v4Gold_{uid})" stroke-width="1.4"/>',
    face({
      fill: 'rgba(105,12,36,.55)', stroke: 'url(#st_v4Gold_{uid})', sw: 2.6,
      over: '<path d="M48 62 C 58 50 70 45 82 44" stroke="#fde68a" stroke-opacity=".2" stroke-width="2.2" stroke-linecap="round" fill="none"/>',
      horns: '<path d="' + HORNS + '" stroke="url(#st_v4Gold_{uid})" stroke-width="3.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#fde68a', '#e11d48'),
      grin: grin('url(#st_v4Gold_{uid})') + fangs('#fde68a', 1)
    })
  );

  /* =========================================================================
     05 — ROYAL AMETHYST & SILVER
     New shield/orbit/drips/chrome horns, but the OLD crown.
     ====================================================================== */
  var v5 = assemble(
    '<linearGradient id="st_v5Chrome_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#ffffff"/><stop offset="38%" stop-color="#cbd5e1"/><stop offset="66%" stop-color="#94a3b8"/><stop offset="100%" stop-color="#e2e8f0"/></linearGradient>' +
    '<linearGradient id="st_v5Violet_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#c084fc"/><stop offset="100%" stop-color="#7c3aed"/></linearGradient>',
    '<path d="M80 11 L139 34 V86 C 139 116 111 138 80 149 C 49 138 21 116 21 86 V34 Z" fill="rgba(30,18,56,.66)" stroke="url(#st_v5Chrome_{uid})" stroke-width="3.2"/>' +
    '<path d="M80 21 L129 40 V84 C 129 108 107 126 80 138 C 53 126 31 108 31 84 V40 Z" fill="none" stroke="#c084fc" stroke-opacity=".55" stroke-width="1.4"/>' +
    '<ellipse cx="80" cy="80" rx="69" ry="27" transform="rotate(-18 80 80)" fill="none" stroke="url(#st_v5Chrome_{uid})" stroke-opacity=".5" stroke-width="1.8"/>' +
    '<path d="M22 96 C 15 104 17 110 21 110 C 25 110 27 104 22 96 Z" fill="url(#st_v5Chrome_{uid})"/>' +
    '<path d="M138 84 C 131 92 133 98 137 98 C 141 98 143 92 138 84 Z" fill="url(#st_v5Chrome_{uid})"/>',
    /* the original crown, kept verbatim */
    '<path d="M69 29 L73 21 L80 26 L87 21 L91 29" fill="none" stroke="#e2e8f0" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>' +
    '<path d="M80 22 L84 27 L80 32 L76 27Z" fill="#c084fc" stroke="#f8fafc" stroke-width=".8"/>' +
    '<polygon points="80,140 86,146 80,152 74,146" fill="url(#st_v5Violet_{uid})" stroke="#e2e8f0" stroke-width=".9" stroke-linejoin="round"/>' +
    '<circle cx="80" cy="146" r="1.3" fill="#f8fafc"/>',
    face({
      fill: 'rgba(30,18,56,.6)', stroke: 'url(#st_v5Chrome_{uid})', sw: 2.6,
      over: '<path d="M80 39 C 100 44 112 58 116 78 C 106 62 96 52 80 47 Z" fill="rgba(192,132,252,.22)"/>' +
        '<path d="M46 66 C 52 52 62 45 74 43" stroke="#f8fafc" stroke-opacity=".4" stroke-width="2" stroke-linecap="round" fill="none"/>',
      horns: '<path d="' + HORNS + '" stroke="#f8fafc" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="' + HORNS + '" stroke="url(#st_v5Violet_{uid})" stroke-width="5.6" stroke-linecap="round" stroke-linejoin="round" fill="none" opacity=".32"/>',
      eyes: eyes('#f8fafc', '#c084fc'),
      grin: grin('#e2e8f0')
    })
  );

  /* =========================================================================
     06 — CYBER YELLOW & LIME (kept)
     ====================================================================== */
  var v6 = assemble(
    '',
    '<rect x="14" y="14" width="132" height="132" rx="24" fill="rgba(17,21,6,.5)" stroke="#facc15" stroke-width="3.6"/>' +
    '<g stroke="#a3e635" stroke-width="4" stroke-linecap="round" opacity=".85">' +
      '<path d="M19 43 L43 19"/><path d="M19 55 L55 19"/><path d="M21 65 L65 21"/>' +
      '<path d="M141 117 L117 141"/><path d="M141 105 L105 141"/><path d="M139 95 L95 139"/>' +
    '</g>',
    /* three strike spots — one is picked at random, at a random 1–3 s beat */
    '<g class="imp-strike">' +
      '<g class="imp-thunder" opacity="0">' +
        '<path d="M134 16 L112 37 L124 41 L116 60" fill="none" stroke="#111506" stroke-width="8.6" stroke-linejoin="round" stroke-linecap="round" opacity=".5"/>' +
        '<path d="M134 16 L112 37 L124 41 L116 60" fill="none" stroke="#facc15" stroke-width="4.8" stroke-linejoin="round" stroke-linecap="round"/>' +
        '<path d="M134 16 L112 37 L124 41 L116 60" fill="none" stroke="#ffffff" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round" opacity=".9"/>' +
        '<path d="M120 30 L130 27 L122 36" fill="none" stroke="#a3e635" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round"/>' +
      '</g>' +
      '<g class="imp-thunder-flash" opacity="0">' +
        '<circle class="imp-thunder-ring" cx="116" cy="62" r="13" fill="none" stroke="#facc15" stroke-width="2.4" opacity="0"/>' +
        '<path d="M116 49 L119.4 58 L128.4 61.4 L119.4 64.8 L116 73.8 L112.6 64.8 L103.6 61.4 L112.6 58 Z" fill="#fff8c0" stroke="#facc15" stroke-width=".7"/>' +
        '<g stroke="#facc15" stroke-width="2" stroke-linecap="round">' +
          '<path d="M132 47 L137 42"/><path d="M100 76 L95 81"/><path d="M130 76 L136 82"/><path d="M102 46 L97 41"/>' +
        '</g>' +
      '</g>' +
    '</g>' +
    '<g class="imp-strike">' +
      '<g class="imp-thunder" opacity="0">' +
        '<path d="M26 16 L48 37 L36 41 L44 60" fill="none" stroke="#111506" stroke-width="8.6" stroke-linejoin="round" stroke-linecap="round" opacity=".5"/>' +
        '<path d="M26 16 L48 37 L36 41 L44 60" fill="none" stroke="#facc15" stroke-width="4.8" stroke-linejoin="round" stroke-linecap="round"/>' +
        '<path d="M26 16 L48 37 L36 41 L44 60" fill="none" stroke="#ffffff" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round" opacity=".9"/>' +
        '<path d="M40 30 L30 27 L38 36" fill="none" stroke="#a3e635" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round"/>' +
      '</g>' +
      '<g class="imp-thunder-flash" opacity="0">' +
        '<circle class="imp-thunder-ring" cx="44" cy="62" r="13" fill="none" stroke="#facc15" stroke-width="2.4" opacity="0"/>' +
        '<path d="M44 49 L47.4 58 L56.4 61.4 L47.4 64.8 L44 73.8 L40.6 64.8 L31.6 61.4 L40.6 58 Z" fill="#fff8c0" stroke="#facc15" stroke-width=".7"/>' +
        '<g stroke="#facc15" stroke-width="2" stroke-linecap="round">' +
          '<path d="M28 47 L23 42"/><path d="M60 76 L65 81"/><path d="M30 76 L24 82"/><path d="M58 46 L63 41"/>' +
        '</g>' +
      '</g>' +
    '</g>' +
    '<g class="imp-strike">' +
      '<g class="imp-thunder" opacity="0">' +
        '<path d="M80 16 L68 30 L78 32 L76 42" fill="none" stroke="#111506" stroke-width="8.6" stroke-linejoin="round" stroke-linecap="round" opacity=".5"/>' +
        '<path d="M80 16 L68 30 L78 32 L76 42" fill="none" stroke="#facc15" stroke-width="4.8" stroke-linejoin="round" stroke-linecap="round"/>' +
        '<path d="M80 16 L68 30 L78 32 L76 42" fill="none" stroke="#ffffff" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round" opacity=".9"/>' +
        '<path d="M70 24 L62 22 L68 30" fill="none" stroke="#a3e635" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round"/>' +
      '</g>' +
      '<g class="imp-thunder-flash" opacity="0">' +
        '<circle class="imp-thunder-ring" cx="78" cy="46" r="12" fill="none" stroke="#facc15" stroke-width="2.4" opacity="0"/>' +
        '<path d="M78 34 L81.2 42.6 L89.8 45.8 L81.2 49 L78 57.6 L74.8 49 L66.2 45.8 L74.8 42.6 Z" fill="#fff8c0" stroke="#facc15" stroke-width=".7"/>' +
        '<g stroke="#facc15" stroke-width="2" stroke-linecap="round">' +
          '<path d="M93 33 L98 28"/><path d="M64 57 L59 62"/><path d="M92 57 L98 63"/><path d="M64 33 L58 27"/>' +
        '</g>' +
      '</g>' +
    '</g>' +
    '<rect x="30" y="125" width="100" height="12" rx="4" fill="#facc15" stroke="#84cc16" stroke-width="1.3"/>' +
    '<path d="M38 137 L45 125 M54 137 L61 125 M70 137 L77 125 M86 137 L93 125 M102 137 L109 125 M118 137 L125 125" stroke="#111506" stroke-width="5" fill="none"/>' +
    '<g fill="#a3e635"><circle cx="30" cy="118" r="1.6"/><circle cx="130" cy="118" r="1.6"/><circle cx="24" cy="86" r="1.4"/><circle cx="136" cy="86" r="1.4"/></g>',
    face({
      fill: 'rgba(25,32,9,.52)', stroke: '#a3e635', sw: 2.8,
      horns: stockHorns('#facc15'),
      eyes: '<path d="M64 79 L72 87 M72 79 L64 87" stroke="#facc15" stroke-width="3" stroke-linecap="round" fill="none"/>' +
        '<path d="M88 79 L96 87 M96 79 L88 87" stroke="#a3e635" stroke-width="3" stroke-linecap="round" fill="none"/>',
      grin: grin('#a3e635')
    })
  );

  /* =========================================================================
     07 — MOLTEN AMBER & COPPER (kept)
     ====================================================================== */
  var v7 = assemble(
    '<linearGradient id="st_v7Lava_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#fde047"/><stop offset="55%" stop-color="#f97316"/><stop offset="100%" stop-color="#b91c1c"/></linearGradient>' +
    '<clipPath id="st_v7Head_{uid}"><circle cx="80" cy="80" r="41"/></clipPath>',
    '<polygon points="48,13 112,13 147,48 147,112 112,147 48,147 13,112 13,48" fill="rgba(22,14,8,.7)" stroke="url(#st_v7Lava_{uid})" stroke-width="3.4" stroke-linejoin="round"/>' +
    '<polygon points="52,22 108,22 138,52 138,108 108,138 52,138 22,108 22,52" fill="none" stroke="rgba(253,224,71,.3)" stroke-width="1.2" stroke-linejoin="round"/>' +
    '<path d="M20 60 L34 56 L41 68 M140 98 L126 102 L119 90" stroke="rgba(253,224,71,.55)" stroke-width="1.4" fill="none" stroke-linecap="round"/>',
    '<path d="M24 146 L44 132 L60 140 L80 130 L100 140 L116 132 L136 146 Z" fill="#170f08" stroke="#7c2d12" stroke-width="1.4" stroke-linejoin="round"/>' +
    '<path d="M44 132 L60 140 L80 130 L100 140 L116 132" stroke="url(#st_v7Lava_{uid})" stroke-width="2.6" fill="none" stroke-linejoin="round"/>' +
    '<path d="M60 140 C 58 144 62 145 61 148 M100 140 C 102 144 98 145 99 148" stroke="#f97316" stroke-width="2" stroke-linecap="round" fill="none" opacity=".9"/>' +
    '<g fill="#ff7a18"><circle cx="72" cy="136" r="1.3"/><circle cx="90" cy="135" r="1.1"/><circle cx="80" cy="133" r="1"/></g>' +
    '<g>' +
      '<path d="M24 146 V112 L26 106 H44 L46 112 V146 Z" fill="#1c120a" stroke="#7c2d12" stroke-width="1.2" stroke-linejoin="round"/>' +
      '<g stroke="#d6b46a" stroke-opacity=".55" stroke-width=".9"><path d="M24 122 H46 M24 132 H46 M24 141 H46"/></g>' +
      '<rect x="22" y="103" width="26" height="5" rx="1.6" fill="#b45309" stroke="#3d331a" stroke-width=".8"/>' +
      '<path d="M28 103 V98 M35 103 V96 M42 103 V99" stroke="#7c2d12" stroke-width="1.6" stroke-linecap="round"/>' +
      '<g fill="none" stroke="#cbd5e1" stroke-width="1.5" stroke-linecap="round" opacity=".4">' +
        '<path d="M32 96 C 26 90 34 86 30 80"/><path d="M40 94 C 46 89 38 84 43 78"/>' +
      '</g>' +
      '<g fill="#fbbf24"><circle cx="30" cy="118" r="1.3"/><circle cx="38" cy="127" r="1.1"/><circle cx="32" cy="136" r="1.2"/></g>' +
    '</g>' +
    '<g fill="url(#st_v7Lava_{uid})">' +
      '<path d="M56 14 C 56 22 53 24 56 27 C 59 30 62 26 60 22 C 59 19 58 16 58 14 Z"/>' +
      '<path d="M80 14 C 80 24 76 27 80 31 C 84 34 87 29 85 24 C 84 20 82 17 82 14 Z"/>' +
      '<path d="M104 14 C 104 21 101 23 104 26 C 107 29 110 25 108 21 C 107 18 106 16 106 14 Z"/>' +
    '</g>' +
    '<g transform="translate(126 30)"><circle r="6" fill="url(#st_v7Lava_{uid})"/><circle r="2.6" fill="#fef3c7"/>' +
      '<g stroke="#fbbf24" stroke-width="1.4" stroke-linecap="round"><path d="M0 -9 V-12 M0 9 V12 M-9 0 H-12 M9 0 H12 M-6.4 -6.4 L-8.5 -8.5 M6.4 -6.4 L8.5 -8.5 M-6.4 6.4 L-8.5 8.5 M6.4 6.4 L8.5 8.5"/></g></g>' +
    '<g fill="#fbbf24"><circle cx="52" cy="104" r="1.5"/><circle cx="112" cy="96" r="1.4"/><circle cx="100" cy="116" r="1.2"/></g>',
    face({
      fill: 'rgba(28,18,9,.86)', stroke: 'url(#st_v7Lava_{uid})', sw: 2.6,
      over: '<g clip-path="url(#st_v7Head_{uid})">' +
        '<path d="M46 64 L60 72 L55 90 L70 100 M114 58 L100 70 L106 84 L94 98" stroke="url(#st_v7Lava_{uid})" stroke-width="2" stroke-linejoin="round" fill="none" opacity=".85"/>' +
        '<path d="M44 52 L58 58 L52 70 M118 104 L106 98 L112 88" stroke="#3f2a16" stroke-width="1.6" fill="none"/>' +
        '</g>',
      horns: '<path d="' + HORNS + '" stroke="#3b1d0a" stroke-width="5.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="' + HORNS + '" stroke="url(#st_v7Lava_{uid})" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#fde047', '#ff7a18'),
      grin: grin('url(#st_v7Lava_{uid})')
    })
  );

  /* =========================================================================
     08 — ABYSSAL COBALT
     Old scalloped shell border + old wave decor restored; keeps the new
     lure, bubbles and head treatment.
     ====================================================================== */
  var v8 = assemble(
    '<radialGradient id="st_v8Lure_{uid}" cx="50%" cy="50%" r="50%">' +
      '<stop offset="0%" stop-color="#e0f2fe" stop-opacity=".95"/><stop offset="45%" stop-color="#60a5fa" stop-opacity=".55"/><stop offset="100%" stop-color="#2563eb" stop-opacity="0"/></radialGradient>' +
    '<radialGradient id="st_v8Eye_{uid}" cx="50%" cy="50%" r="50%">' +
      '<stop offset="0%" stop-color="#93c5fd" stop-opacity=".85"/><stop offset="100%" stop-color="#3b82f6" stop-opacity="0"/></radialGradient>',
    /* original frame, verbatim */
    '<path d="M80 10 C99 30 132 39 142 68 C155 105 120 138 80 151 C40 138 5 105 18 68 C28 39 61 30 80 10Z" fill="rgba(6,15,32,.58)" stroke="#3b82f6" stroke-width="3.5" stroke-linejoin="round"/>' +
    '<path d="M80 22 C98 39 123 46 132 70 C143 99 115 124 80 140 C45 124 17 99 28 70 C37 46 62 39 80 22Z" fill="none" stroke="#60a5fa" stroke-width="1.6" stroke-opacity=".72"/>',
    /* original wave decor ... */
    '<path d="M34 111 Q56 124 80 113 Q104 124 126 111" fill="none" stroke="#93c5fd" stroke-width="2.2" stroke-linecap="round" opacity=".75"/>' +
    '<circle cx="80" cy="18" r="3" fill="#93c5fd"/>' +
    '<circle cx="80" cy="143" r="2.5" fill="#60a5fa"/>' +
    /* ... plus the new lure, bubbles and current lines */
    '<path d="M71 47 C 64 45 57 41 52 35" stroke="#3b82f6" stroke-width="4.4" stroke-linecap="round" fill="none" opacity=".4"/>' +
    '<path d="M71 47 C 64 45 57 41 52 35" stroke="#7dd3fc" stroke-width="2.2" stroke-linecap="round" fill="none"/>' +
    '<circle cx="50" cy="31" r="11" fill="url(#st_v8Lure_{uid})"/>' +
    '<circle cx="50" cy="31" r="5.6" fill="#dbeafe" stroke="#60a5fa" stroke-width="1.1"/>' +
    '<circle cx="50" cy="31" r="2.8" fill="#f8fdff"/><circle cx="48.8" cy="29.8" r="1" fill="#ffffff"/>' +
    '<g stroke="#93c5fd" stroke-width="1.2" stroke-linecap="round" opacity=".75">' +
      '<path d="M50 19 V16"/><path d="M50 43 V46"/><path d="M38 31 H35"/><path d="M62 31 H65"/>' +
      '<path d="M41.6 22.6 L39.4 20.4"/><path d="M58.4 39.4 L60.6 41.6"/><path d="M58.4 22.6 L60.6 20.4"/><path d="M41.6 39.4 L39.4 41.6"/>' +
    '</g>' +
    '<g fill="rgba(147,197,253,.22)" stroke="#93c5fd" stroke-width="1.1">' +
      '<circle cx="38" cy="124" r="4.6"/><circle cx="44" cy="112" r="3"/><circle cx="37" cy="102" r="1.9"/>' +
      '<circle cx="122" cy="118" r="3.4"/><circle cx="127" cy="108" r="2.2"/>' +
    '</g>' +
    '<g fill="none" stroke="rgba(147,197,253,.35)" stroke-width="1.1" stroke-linecap="round">' +
      '<path d="M112 74 C 116 78 116 84 112 88"/><path d="M118 70 C 124 77 124 85 118 92"/>' +
    '</g>',
    face({
      fill: 'rgba(6,15,32,.72)', stroke: '#60a5fa', sw: 2.4,
      over: '<circle cx="68" cy="83" r="10" fill="url(#st_v8Eye_{uid})" opacity=".55"/>' +
        '<circle cx="92" cy="83" r="10" fill="url(#st_v8Eye_{uid})" opacity=".55"/>' +
        '<path d="M80 39 C 98 44 110 58 114 76" stroke="rgba(147,197,253,.4)" stroke-width="2.4" stroke-linecap="round" fill="none"/>',
      horns: '<path d="' + HORNS + '" stroke="#60a5fa" stroke-width="6" stroke-opacity=".2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="' + HORNS + '" stroke="#7dd3fc" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#93c5fd', '#60a5fa'),
      grin: grin('#93c5fd')
    })
  );

  /* =========================================================================
     09 — CYBER CHERRY & CYAN   (glitch pass)
     The original neon rings, crosshair ticks and dots are untouched. Signal
     damage is added around them: two dark tears with chroma edges, stray
     pixels, a displaced copy of the rings that flashes in, and a stepped
     jitter that snaps the whole glitch layer sideways for a few frames.
     ====================================================================== */
  var v9 = assemble(
    '',
    /* original frame, verbatim */
    '<circle cx="80" cy="80" r="67" fill="rgba(13,17,26,.38)" stroke="#ff2a5f" stroke-width="2.7" stroke-dasharray="72 10 36 10" transform="rotate(-12 80 80)"/>' +
    '<circle cx="80" cy="80" r="59" fill="none" stroke="#00f0ff" stroke-width="2.4" stroke-dasharray="54 12" transform="rotate(18 80 80)"/>' +
    '<circle cx="80" cy="80" r="49" fill="none" stroke="#ff2a5f" stroke-opacity=".68" stroke-width="1.4" stroke-dasharray="2 5"/>',
    /* original decor, verbatim */
    '<path d="M80 10 V24 M80 136 V150 M10 80 H24 M136 80 H150 M31 31 L41 41 M119 119 L129 129 M129 31 L119 41 M41 119 L31 129" stroke="#00f0ff" stroke-width="2.2" stroke-linecap="round"/>' +
    '<circle cx="80" cy="13" r="3" fill="#ff2a5f"/>' +
    '<circle cx="147" cy="80" r="3" fill="#00f0ff"/>' +
    '<circle cx="80" cy="147" r="3" fill="#ff2a5f"/>' +
    '<circle cx="13" cy="80" r="3" fill="#00f0ff"/>' +
    /* glitch: two tears, stray pixels, and a chroma ghost of the rings */
    '<g class="imp-glitch">' +
      '<rect x="16" y="46.6" width="62" height="4.2" fill="rgba(8,11,18,.94)"/>' +
      '<path d="M16 46.6 H78" stroke="#00f0ff" stroke-width="1" stroke-opacity=".6"/>' +
      '<path d="M16 50.8 H78" stroke="#ff2a5f" stroke-width="1" stroke-opacity=".55"/>' +
      '<rect x="24" y="47.4" width="15" height="1.7" fill="#ff2a5f" fill-opacity=".8"/>' +
      '<rect x="47" y="48.6" width="10" height="1.4" fill="#00f0ff" fill-opacity=".75"/>' +
      '<rect x="64" y="47.8" width="6" height="1.2" fill="#00f0ff" fill-opacity=".5"/>' +
      '<rect x="84" y="111" width="62" height="4.2" fill="rgba(8,11,18,.94)"/>' +
      '<path d="M84 111 H146" stroke="#ff2a5f" stroke-width="1" stroke-opacity=".6"/>' +
      '<path d="M84 115.2 H146" stroke="#00f0ff" stroke-width="1" stroke-opacity=".55"/>' +
      '<rect x="98" y="111.8" width="16" height="1.7" fill="#00f0ff" fill-opacity=".75"/>' +
      '<rect x="122" y="113" width="9" height="1.4" fill="#ff2a5f" fill-opacity=".8"/>' +
      '<g fill="#00f0ff"><rect x="20" y="55.4" width="3" height="3"/><rect x="135" y="103.4" width="2.6" height="2.6"/><rect x="59.5" y="44.5" width="2" height="2" opacity=".8"/></g>' +
      '<g fill="#ff2a5f"><rect x="132.5" y="57" width="3" height="3"/><rect x="23" y="101" width="2.6" height="2.6"/><rect x="91" y="117.4" width="2" height="2" opacity=".8"/></g>' +
    '</g>' +
    '<g class="imp-glitch-ghost" opacity="0">' +
      '<circle cx="77.4" cy="80" r="67" fill="none" stroke="#00f0ff" stroke-width="1.5" stroke-dasharray="16 7 5 13" stroke-opacity=".7"/>' +
      '<circle cx="82.6" cy="80" r="59" fill="none" stroke="#ff2a5f" stroke-width="1.5" stroke-dasharray="10 9 18 6" stroke-opacity=".7"/>' +
      '<circle cx="77.4" cy="80" r="49" fill="none" stroke="#00f0ff" stroke-width="1" stroke-dasharray="3 9" stroke-opacity=".5"/>' +
    '</g>',
    face({
      fill: 'rgba(20,26,41,.66)', stroke: '#00f0ff', sw: 2.4,
      over: '<circle cx="80" cy="80" r="35" fill="none" stroke="rgba(255,42,95,.45)" stroke-width="1.2"/>' +
        '<path d="M48 60 C 56 49 68 44 80 43" stroke="#ffffff" stroke-opacity=".16" stroke-width="3" stroke-linecap="round" fill="none"/>',
      horns: '<path d="' + HORNS + '" stroke="#ff2a5f" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="' + HORNS + '" stroke="#00f0ff" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" fill="none" opacity=".55"/>',
      eyes: eyes('#ff2a5f', '#00f0ff'),
      grin: grin('#ffffff') + '<polygon points="69,97 73,104 76,99" fill="#00f0ff"/><polygon points="91,97 87,104 84,99" fill="#ff2a5f"/>'
    })
  );

  /* =========================================================================
     10 — NORDIC TWILIGHT (kept)
     ====================================================================== */
  var v10 = assemble(
    '<linearGradient id="st_v10Aur_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#a78bfa"/><stop offset="55%" stop-color="#5eead4"/><stop offset="100%" stop-color="#2dd4bf"/></linearGradient>',
    '<ellipse cx="80" cy="80" rx="67" ry="68" fill="rgba(24,29,46,.42)" stroke="#5eead4" stroke-width="2.8" stroke-dasharray="140 18 110 22" transform="rotate(-18 80 80)"/>' +
    '<ellipse cx="80" cy="80" rx="59" ry="61" fill="none" stroke="#a78bfa" stroke-width="1.8" stroke-dasharray="122 20 100 20" transform="rotate(18 80 80)"/>',
    '<path d="M26 62 C 42 45 55 43 68 50 C 82 58 91 57 104 45 C 115 35 126 38 136 47" fill="none" stroke="url(#st_v10Aur_{uid})" stroke-width="2.8" stroke-linecap="round" opacity=".85"/>' +
    '<path d="M21 103 C 37 91 50 91 62 99 C 77 109 89 108 103 96 C 117 84 129 87 140 97" fill="none" stroke="#38bdf8" stroke-width="2.2" stroke-linecap="round" opacity=".7"/>' +
    '<path d="M34 55 C 49 42 58 43 68 49 M92 105 C 107 93 119 90 130 98" fill="none" stroke="#a78bfa" stroke-width="1.6" stroke-linecap="round" opacity=".7"/>' +
    '<g fill="#a78bfa"><path d="M31 28 L32.6 33 L37.6 34.6 L32.6 36.2 L31 41 L29.4 36.2 L24.4 34.6 L29.4 33 Z"/>' +
      '<path d="M128 34 L129.4 38 L133.4 39.4 L129.4 40.8 L128 45 L126.6 40.8 L122.6 39.4 L126.6 38 Z" fill="#5eead4"/>' +
      '<path d="M120 108 L121.2 111.4 L124.6 112.6 L121.2 113.8 L120 117 L118.8 113.8 L115.4 112.6 L118.8 111.4 Z" fill="#c7d2fe"/></g>' +
    '<path d="M20 138 L46 104 L62 122 L80 98 L100 126 L118 106 L140 138 Z" fill="#0f1524" stroke="#2dd4bf" stroke-width="1.1" stroke-linejoin="round"/>' +
    '<path d="M46 104 L53 113 L59 109 L64 115 L57 118 L50 114 L43 116 Z" fill="#e9f7ff" opacity=".8"/>' +
    '<path d="M80 98 L87 108 L94 105 L100 112 L92 116 L84 111 L76 114 Z" fill="#e9f7ff" opacity=".85"/>' +
    '<path d="M118 106 L124 113 L130 110 L136 118 L128 120 L121 115 L114 118 Z" fill="#e9f7ff" opacity=".7"/>' +
    '<g stroke="#2dd4bf" stroke-width="1.2" stroke-linejoin="round" fill="#123240">' +
      '<g transform="translate(35 141)">' +
        '<rect x="-1.7" y="-5" width="3.4" height="5"/>' +
        '<path d="M0 -27 L9 -13 H-9 Z"/><path d="M0 -18 L12 -2 H-12 Z"/>' +
      '</g>' +
      '<g transform="translate(124 141)">' +
        '<rect x="-1.7" y="-5" width="3.4" height="5"/>' +
        '<path d="M0 -27 L9 -13 H-9 Z"/><path d="M0 -18 L12 -2 H-12 Z"/>' +
      '</g>' +
    '</g>' +
    '<g stroke="#a8ffd1" stroke-width=".9" opacity=".5" stroke-linecap="round">' +
      '<path d="M30 126 H40 M29 135 H41 M119 126 H129 M118 135 H130"/>' +
    '</g>' +
    '<g fill="#e9f7ff" opacity=".8"><path d="M35 114 L38.4 120 L31.6 120 Z"/><path d="M124 114 L127.4 120 L120.6 120 Z"/></g>' +
    '<path d="M22 141 H138" stroke="#5eead4" stroke-width="1.2" opacity=".5"/>' +
    '<path d="M26 145 H40 M48 145 H58 M66 145 H74 M88 145 H96 M104 145 H114 M120 145 H134" stroke="#5eead4" stroke-width="1" opacity=".35"/>',
    face({
      fill: 'rgba(16,22,36,.93)', stroke: '#5eead4', sw: 2.4,
      horns:
        '<path d="M62 60 C 53 51 48 40 45 27 M53 51 C 46 47 42 40 40 33 M57 55 C 55 49 51 44 47 38 M98 60 C 107 51 112 40 115 27 M107 51 C 114 47 118 40 120 33 M103 55 C 105 49 109 44 113 38" stroke="#0c111d" stroke-width="6.4" stroke-linecap="round" fill="none" opacity=".85"/>' +
        '<path d="M62 60 C 53 51 48 40 45 27 M53 51 C 46 47 42 40 40 33 M57 55 C 55 49 51 44 47 38 M98 60 C 107 51 112 40 115 27 M107 51 C 114 47 118 40 120 33 M103 55 C 105 49 109 44 113 38" stroke="url(#st_v10Aur_{uid})" stroke-width="3.4" stroke-linecap="round" fill="none"/>' +
        '<g fill="#e6ecff"><circle cx="45" cy="27" r="1.6"/><circle cx="115" cy="27" r="1.6"/><circle cx="40" cy="33" r="1.2"/><circle cx="120" cy="33" r="1.2"/></g>',
      eyes: eyes('#5eead4', '#a78bfa'),
      grin: grin('#c7d2fe')
    })
  );

  /* =========================================================================
     11 — MOSS & BRASS (kept)
     ====================================================================== */
  var v11 = assemble(
    '<linearGradient id="st_v11Brass_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#E8D995"/><stop offset="100%" stop-color="#A98D45"/></linearGradient>',
    '<circle cx="80" cy="80" r="67" fill="rgba(31,38,20,.66)" stroke="#A98D45" stroke-width="3.4"/>' +
    '<circle cx="80" cy="80" r="59.5" fill="none" stroke="#D6C16D" stroke-opacity=".62" stroke-width="1.5"/>' +
    '<circle cx="80" cy="80" r="53" fill="none" stroke="#8BA64E" stroke-opacity=".55" stroke-width="1.2" stroke-dasharray="1 4"/>',
    '<path d="M34 112 C 19 94 22 63 43 42 M126 112 C 141 94 138 63 117 42" fill="none" stroke="#A98D45" stroke-width="2.3" stroke-linecap="round"/>' +
    '<g fill="#8BA64E" stroke="#D6C16D" stroke-width="0.8">' +
      '<path d="M32 101 C 20 96 19 88 20 83 C 31 85 37 91 32 101Z"/>' +
      '<path d="M28 84 C 17 77 18 69 20 64 C 30 69 34 76 28 84Z"/>' +
      '<path d="M31 66 C 23 56 26 49 30 45 C 38 54 39 61 31 66Z"/>' +
      '<path d="M128 101 C 140 96 141 88 140 83 C 129 85 123 91 128 101Z"/>' +
      '<path d="M132 84 C 143 77 142 69 140 64 C 130 69 126 76 132 84Z"/>' +
      '<path d="M129 66 C 137 56 134 49 130 45 C 122 54 121 61 129 66Z"/>' +
    '</g>' +
    '<g fill="#8BA64E" stroke="#D6C16D" stroke-width=".7">' +
      '<path d="M49 39 C 45 36 44 32 46 29 C 50 31 52 35 49 39 Z"/>' +
      '<path d="M45 31 C 42 27 43 23 46 21 C 49 25 49 28 45 31 Z"/>' +
      '<path d="M53 45 C 49 43 47 39 48 36 C 52 38 54 42 53 45 Z"/>' +
      '<path d="M111 39 C 115 36 116 32 114 29 C 110 31 108 35 111 39 Z"/>' +
      '<path d="M115 31 C 118 27 117 23 114 21 C 111 25 111 28 115 31 Z"/>' +
      '<path d="M107 45 C 111 43 113 39 112 36 C 108 38 106 42 107 45 Z"/>' +
    '</g>' +
    '<g transform="translate(80 24)">' +
      '<path d="M0 12 C -2 4 2 -2 0 -8" stroke="#8BA64E" stroke-width="1.8" fill="none" stroke-linecap="round"/>' +
      '<path d="M0 4 C -7 2 -10 5 -11 9 C -5 10 -1 8 0 4 Z" fill="#8BA64E" stroke="#D6C16D" stroke-width=".8"/>' +
      '<path d="M0 -1 C 7 -3 10 0 11 4 C 5 5 1 3 0 -1 Z" fill="#A9C46A" stroke="#D6C16D" stroke-width=".8"/>' +
    '</g>' +
    '<g stroke="#D6C16D" stroke-width=".8">' +
      '<g transform="translate(63 139)">' +
        '<path d="M0 -8 V-5" stroke="#A98D45" stroke-width="1.4"/><path d="M-1 -8 H1" stroke="#A98D45" stroke-width="1.4"/>' +
        '<path d="M-3.4 -4.4 C -3.4 -5.6 3.4 -5.6 3.4 -4.4 C 3.4 -3.4 -3.4 -3.4 -3.4 -4.4 Z" fill="#A98D45"/>' +
        '<path d="M-3 -4 C -3 1 0 4 0 6 C 0 4 3 1 3 -4 Z" fill="#8BA64E"/>' +
        '<path d="M-2.4 -3.6 Q 0 -2 2.4 -3.6" fill="none" stroke="#D6C16D" stroke-width=".6" opacity=".8"/>' +
      '</g>' +
      '<g transform="translate(97 139)">' +
        '<path d="M0 -8 V-5" stroke="#A98D45" stroke-width="1.4"/><path d="M-1 -8 H1" stroke="#A98D45" stroke-width="1.4"/>' +
        '<path d="M-3.4 -4.4 C -3.4 -5.6 3.4 -5.6 3.4 -4.4 C 3.4 -3.4 -3.4 -3.4 -3.4 -4.4 Z" fill="#A98D45"/>' +
        '<path d="M-3 -4 C -3 1 0 4 0 6 C 0 4 3 1 3 -4 Z" fill="#8BA64E"/>' +
        '<path d="M-2.4 -3.6 Q 0 -2 2.4 -3.6" fill="none" stroke="#D6C16D" stroke-width=".6" opacity=".8"/>' +
      '</g>' +
    '</g>' +
    '<circle cx="80" cy="14.5" r="2.6" fill="#E8D995"/><circle cx="80" cy="146" r="2.6" fill="#E8D995"/>',
    face({
      fill: 'rgba(139,166,78,.18)', stroke: '#E8D995', sw: 2.6,
      over: '<path d="M80 39 C 96 44 106 54 111 68" stroke="rgba(232,217,149,.3)" stroke-width="2.2" stroke-linecap="round" fill="none"/>' +
        '<path d="M56 104 C 68 112 92 112 104 104" stroke="rgba(139,166,78,.35)" stroke-width="1.6" fill="none" stroke-linecap="round"/>',
      horns: '<path d="M62 58 C 54 50 50 40 46 28 M54 50 C 48 46 44 40 42 34 M58 54 C 56 48 52 44 48 38 M98 58 C 106 50 110 40 114 28 M106 50 C 112 46 116 40 118 34 M102 54 C 104 48 108 44 112 38" stroke="url(#st_v11Brass_{uid})" stroke-width="3.2" stroke-linecap="round" fill="none"/>',
      eyes: eyes('#D6C16D', '#A9C46A'),
      grin: grin('#E8D995')
    })
  );

  /* =========================================================================
     12 — CINDERFORGE KILN   (improved)
     Banded hammer heads with grip wraps, an anvil with a horn and a hot
     billet, a coal glow under it and embers drifting up (imp-ember).
     ====================================================================== */
  function rivet(x, y) {
    return '<circle cx="' + x + '" cy="' + y + '" r="1.6" fill="#2b1d0a" stroke="#d6b46a" stroke-width=".8"/>';
  }
  function hammer(flip) {
    var t = flip
      ? 'translate(122 30) rotate(32)'
      : 'translate(38 30) rotate(-32)';
    var cls = flip ? 'imp-hammer-r' : 'imp-hammer-l';
    return '<g transform="' + t + '"><g class="' + cls + '">' +
      '<path d="M-10 -5.5 H8 L10 0 L8 5.5 H-10 Z" fill="#d6b46a" stroke="#3d331a" stroke-width="1.2" stroke-linejoin="round"/>' +
      '<path d="M-10 -5.5 H-5 V5.5 H-10 Z" fill="#8a6a26"/>' +
      '<path d="M-5.2 -5.5 V5.5" stroke="#3d331a" stroke-width=".9"/>' +
      '<path d="M6 -5.5 L10 0 L6 5.5 Z" fill="#a9822f" opacity=".65"/>' +
      '<rect x="-8.4" y="-4" width="11" height="1.7" rx=".5" fill="#ffffff" opacity=".3"/>' +
      '<rect x="-8.4" y="2.2" width="9" height="1.3" rx=".5" fill="#3d331a" opacity=".45"/>' +
      '<circle cx="0.5" cy="0" r="1.3" fill="#3d331a"/>' +
      '<line x1="0" y1="5.7" x2="0" y2="33" stroke="#b85a16" stroke-width="6.2" stroke-linecap="round"/>' +
      '<line x1="0" y1="5.7" x2="0" y2="33" stroke="#ff7a18" stroke-width="4" stroke-linecap="round"/>' +
      '<g stroke="#2b1d0a" stroke-width="1.3" stroke-linecap="round" opacity=".65">' +
        '<path d="M-2 13 L2 11"/><path d="M-2 17 L2 15"/><path d="M-2 21 L2 19"/><path d="M-2 25 L2 23"/>' +
      '</g>' +
      '<rect x="-4" y="26" width="8" height="6.5" rx="1.1" fill="#2b1d0a" stroke="#3d331a" stroke-width=".7"/>' +
      '<rect x="-1.4" y="27" width="2.8" height="4.5" rx=".9" fill="#d6b46a" opacity=".55"/>' +
    '</g></g>';
  }
  var v12 = assemble(
    '<radialGradient id="st_v12Heat_{uid}" cx="50%" cy="50%" r="50%">' +
      '<stop offset="0%" stop-color="#ff7a18" stop-opacity=".55"/><stop offset="55%" stop-color="#ff3d00" stop-opacity=".22"/><stop offset="100%" stop-color="#ff3d00" stop-opacity="0"/></radialGradient>' +
    '<linearGradient id="st_v12Ember_{uid}" x1="0%" y1="0%" x2="0%" y2="100%">' +
      '<stop offset="0%" stop-color="#ffe9c0"/><stop offset="100%" stop-color="#ff7a18"/></linearGradient>',
    '<circle cx="80" cy="80" r="67" fill="rgba(15,15,15,.72)" stroke="#d6b46a" stroke-width="3.4"/>' +
    '<circle cx="80" cy="80" r="58" fill="none" stroke="#3d331a" stroke-width="1.4"/>' +
    '<ellipse cx="80" cy="126" rx="42" ry="18" fill="url(#st_v12Heat_{uid})"/>' +
    '<g opacity=".85">' + rivet(80, 16.5) + rivet(80, 143.5) + rivet(16.5, 80) + rivet(143.5, 80) +
      rivet(35, 35) + rivet(125, 35) + rivet(35, 125) + rivet(125, 125) + '</g>',
    hammer(false) + hammer(true) +
    '<g class="imp-clash" opacity="0">' +
      '<path d="M80 23 L82.4 29.6 L89 32 L82.4 34.4 L80 41 L77.6 34.4 L71 32 L77.6 29.6 Z" fill="#ffe9c0" stroke="#ff7a18" stroke-width=".7"/>' +
      '<g stroke="#ffd9a8" stroke-width="1.8" stroke-linecap="round">' +
        '<path d="M92 22 L97 17"/><path d="M68 22 L63 17"/><path d="M92 42 L97 47"/><path d="M68 42 L63 47"/>' +
      '</g>' +
    '</g>' +
    /* coal bed + the original anvil silhouette, plus a billet on the face */
    '<ellipse cx="80" cy="134" rx="36" ry="13" fill="url(#st_v12Heat_{uid})"/>' +
    '<g fill="#ffb066" opacity=".55">' +
      '<circle cx="62" cy="133" r="1.6"/><circle cx="72" cy="137" r="1.2"/><circle cx="90" cy="136" r="1.4"/><circle cx="99" cy="132" r="1.1"/>' +
    '</g>' +
    '<rect x="58" y="116" width="44" height="4" rx="1" fill="#d6b46a"/>' +
    '<path d="M62 126 L62 118 L98 118 L98 126 L104 130 L56 130 Z" fill="#3d331a" stroke="#d6b46a" stroke-width="1.4" stroke-linejoin="round"/>' +
    '<path d="M56 130 L104 130 L100 136 L60 136 Z" fill="#241a0c" stroke="#3d331a" stroke-width=".9" stroke-linejoin="round"/>' +
    '<path d="M64 120 H96" stroke="#ffe9c0" stroke-opacity=".3" stroke-width="1"/>' +
    /* hot billet resting on the face */
    '<rect x="70" y="109.6" width="22" height="6.4" rx="2.2" fill="#ff7a18" stroke="#ffd9a8" stroke-width=".8"/>' +
    '<rect x="70" y="109.6" width="22" height="2.3" rx="1.2" fill="#ffd9a8" opacity=".85"/>' +
    '<g fill="#ffd9a8"><circle cx="66" cy="107.6" r=".9"/><circle cx="96" cy="107" r=".7"/><circle cx="88" cy="104.6" r=".6"/></g>' +
    /* heat shimmer off the billet */
    '<g fill="none" stroke="#ffb066" stroke-width="1.2" stroke-linecap="round" opacity=".45">' +
      '<path d="M74 106 C 71 101 77 98 74 93"/><path d="M88 106 C 91 101 85 98 88 93"/>' +
    '</g>' +
    /* drifting embers (imp-ember animates) */
    '<g fill="url(#st_v12Ember_{uid})">' +
      '<circle class="imp-ember" cx="72" cy="104" r="1.6" style="animation-delay:0s"/>' +
      '<circle class="imp-ember" cx="88" cy="100" r="1.3" style="animation-delay:.7s"/>' +
      '<circle class="imp-ember" cx="98" cy="96" r="1.1" style="animation-delay:1.3s"/>' +
      '<circle class="imp-ember" cx="62" cy="98" r="1" style="animation-delay:1.9s"/>' +
      '<circle class="imp-ember" cx="80" cy="94" r="1.2" style="animation-delay:2.4s"/>' +
    '</g>' +
    '<g fill="#ffd9a8" opacity=".85">' +
      '<path d="M46 22 L47.2 26.4 L51.6 27.6 L47.2 28.8 L46 33.2 L44.8 28.8 L40.4 27.6 L44.8 26.4 Z"/>' +
      '<path d="M114 25 L114.9 28.3 L118.2 29.2 L114.9 30.1 L114 33.4 L113.1 30.1 L109.8 29.2 L113.1 28.3 Z" opacity=".6"/>' +
    '</g>',
    face({
      fill: 'rgba(28,22,12,.56)', stroke: '#d6b46a', sw: 2.6,
      over: '<path d="M48 64 C 58 52 70 47 82 46" stroke="#ffe9c0" stroke-opacity=".18" stroke-width="2.2" stroke-linecap="round" fill="none"/>' +
        '<path d="M58 104 C 68 111 92 111 102 104" stroke="#3d331a" stroke-width="1.6" fill="none" stroke-linecap="round" opacity=".6"/>',
      horns: '<path d="' + HORNS + '" stroke="#b85a16" stroke-width="5.4" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="' + HORNS + '" stroke="#ff7a18" stroke-width="3.4" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#d6b46a', '#ff7a18'),
      grin: grin('#ff7a18')
    })
  );

  /* =========================================================================
     13 — KELP CATHEDRAL   (original design, improved + demonic horns)
     The shipping layout kept — long-dash outer ring, dotted inner ring, three
     faceted crystals joined by a sigil line, soft violet bloom. The crystals
     move down out of the horns' way. The horns are the point: two long demon
     horns, thick at the root, curved out and back, sharp at the tip, with a
     mint-lit edge.
     ====================================================================== */
  var hornPath = function (p0, c, p1) {
    var L = [], R = [], i, t, x, y, dx, dy, len, w, nx, ny;
    for (i = 0; i <= 12; i++) {
      t = i / 12;
      x = (1 - t) * (1 - t) * p0[0] + 2 * (1 - t) * t * c[0] + t * t * p1[0];
      y = (1 - t) * (1 - t) * p0[1] + 2 * (1 - t) * t * c[1] + t * t * p1[1];
      dx = 2 * (1 - t) * (c[0] - p0[0]) + 2 * t * (p1[0] - c[0]);
      dy = 2 * (1 - t) * (c[1] - p0[1]) + 2 * t * (p1[1] - c[1]);
      len = Math.sqrt(dx * dx + dy * dy) || 1;
      nx = dy / len; ny = -dx / len;
      w = 8.6 * Math.pow(1 - t, 0.9);
      L.push((x + nx * w).toFixed(1) + ' ' + (y + ny * w).toFixed(1));
      R.push((x - nx * w).toFixed(1) + ' ' + (y - ny * w).toFixed(1));
    }
    return 'M' + L.join(' L ') + ' L ' + R.reverse().join(' L ') + ' Z';
  };
  var HORN_L = hornPath([62, 61], [30, 39], [36, 13]);
  var HORN_R = hornPath([98, 61], [130, 39], [124, 13]);
  var v13 = assemble(
    '<linearGradient id="st_v13Horn_{uid}" x1="0%" y1="100%" x2="55%" y2="0%">' +
      '<stop offset="0%" stop-color="#2f2158"/><stop offset="48%" stop-color="#6d4fe0"/><stop offset="100%" stop-color="#c4b5fd"/></linearGradient>' +
    '<radialGradient id="st_v13Bloom_{uid}" cx="50%" cy="50%" r="50%">' +
      '<stop offset="0%" stop-color="#a78bfa" stop-opacity=".16"/><stop offset="100%" stop-color="#a78bfa" stop-opacity="0"/></radialGradient>',
    '<circle cx="80" cy="80" r="67" fill="rgba(22,22,38,.42)" stroke="#a78bfa" stroke-width="2.6" stroke-dasharray="60 12 30 12"/>' +
    '<circle cx="80" cy="80" r="58" fill="none" stroke="#6df0c8" stroke-width="1.4" stroke-dasharray="4 6"/>' +
    '<circle cx="80" cy="80" r="50" fill="url(#st_v13Bloom_{uid})"/>' +
    '<path d="M38 94 L80 120 M122 94 L80 120" fill="none" stroke="#6df0c8" stroke-width="1.2" stroke-linecap="round" opacity=".5"/>' +
    '<g stroke="#a78bfa" stroke-width="1.4" stroke-linejoin="round" fill="rgba(167,139,250,.2)">' +
      '<path d="M38 90 L46 102 L38 114 L30 102 Z"/>' +
      '<path d="M122 90 L130 102 L122 114 L114 102 Z"/>' +
      '<path d="M80 120 L88 132 L80 144 L72 132 Z"/>' +
    '</g>' +
    '<g stroke="rgba(223,247,255,.5)" stroke-width="1" stroke-linecap="round" fill="none">' +
      '<path d="M38 90 V114 M30 102 H46"/>' +
      '<path d="M122 90 V114 M114 102 H130"/>' +
      '<path d="M80 120 V144 M72 132 H88"/>' +
    '</g>',
    '',
    face({
      fill: 'rgba(22,22,38,.62)', stroke: '#a78bfa', sw: 2.5,
      horns:
        '<path d="' + HORN_L + '" fill="url(#st_v13Horn_{uid})" stroke="#1a1230" stroke-width="1.4" stroke-linejoin="round"/>' +
        '<path d="' + HORN_R + '" fill="url(#st_v13Horn_{uid})" stroke="#1a1230" stroke-width="1.4" stroke-linejoin="round"/>' +
        '<g fill="none" stroke-linecap="round">' +
          '<path d="M56 55 C 42 41 36 25 37 16" stroke="#6df0c8" stroke-width="1.2" stroke-opacity=".55"/>' +
          '<path d="M104 55 C 118 41 124 25 123 16" stroke="#6df0c8" stroke-width="1.2" stroke-opacity=".55"/>' +
          '<path d="M60 58 C 49 49 44 38 43 29" stroke="#241a45" stroke-width="2" stroke-opacity=".6"/>' +
          '<path d="M100 58 C 111 49 116 38 117 29" stroke="#241a45" stroke-width="2" stroke-opacity=".6"/>' +
        '</g>' +
        '<g fill="#c4b5fd"><circle cx="36.6" cy="14" r="1.6"/><circle cx="123.4" cy="14" r="1.6"/></g>',
      eyes: eyes('#a78bfa', '#6df0c8'),
      grin: grin('#a78bfa'),
      extra: '<g fill="rgba(109,240,200,.45)"><circle cx="80" cy="152" r="1.2"/><circle cx="22" cy="80" r="1"/><circle cx="138" cy="80" r="1"/></g>'
    })
  );

  /* =========================================================================
     14 — WASTELAND CARNIVAL   (new concept)
     A striped big-top with a scalloped valance and a pennant, a high-striker
     and popcorn in the foreground, a harlequin-motley head with a bow tie,
     and pennant-flag horns.
     ====================================================================== */
  var valance = (function () {
    var d = 'M14 66 ';
    for (var x = 14; x < 146; x += 12) d += 'q 6 11 12 0 ';
    return d + 'V 74 H14 Z';
  })();
  var v14 = assemble(
    '<linearGradient id="st_v14Top_{uid}" x1="0%" y1="0%" x2="0%" y2="100%">' +
      '<stop offset="0%" stop-color="#fbbf24"/><stop offset="100%" stop-color="#b45309"/></linearGradient>',
    /* roof */
    '<path d="M80 10 L146 66 H14 Z" fill="rgba(10,15,30,.8)" stroke="#f59e0b" stroke-width="3" stroke-linejoin="round"/>' +
    '<path d="M80 10 L96 66 H64 Z" fill="rgba(245,158,11,.18)"/>' +
    '<path d="M80 10 L124 66 H96 Z" fill="rgba(245,158,11,.07)"/>' +
    '<path d="M80 10 L36 66 H64 Z" fill="rgba(245,158,11,.07)"/>' +
    /* pennant on the apex */
    '<path d="M80 10 V 2" stroke="#d6b46a" stroke-width="2" stroke-linecap="round"/>' +
    '<path d="M80 2 L94 6.5 L80 11 Z" fill="#f59e0b" stroke="#4a2a06" stroke-width=".8" stroke-linejoin="round"/>' +
    /* tent body: dark interior, striped walls, floor */
    '<path d="M14 66 H146 V140 H14 Z" fill="rgba(6,10,20,.55)"/>' +
    '<path d="M14 66 H50 V140 H14 Z" fill="rgba(245,158,11,.12)" stroke="rgba(245,158,11,.35)" stroke-width="1"/>' +
    '<path d="M110 66 H146 V140 H110 Z" fill="rgba(245,158,11,.12)" stroke="rgba(245,158,11,.35)" stroke-width="1"/>' +
    '<g stroke="rgba(245,158,11,.32)" stroke-width="1">' +
      '<path d="M26 66 V140 M38 66 V140 M122 66 V140 M134 66 V140"/>' +
    '</g>' +
    /* valance */
    '<path d="' + valance + '" fill="url(#st_v14Top_{uid})" stroke="#4a2a06" stroke-width=".9"/>' +
    /* guy ropes + floor */
    '<path d="M14 66 L3 140 M146 66 L157 140" stroke="#f59e0b" stroke-width="1.4" stroke-dasharray="4 4" opacity=".5"/>' +
    '<path d="M14 140 H146" stroke="#f59e0b" stroke-width="2"/>' +
    /* bunting strung under the roof line */
    '<path d="M16 78 Q 80 94 144 78" fill="none" stroke="#d6b46a" stroke-width="1.1" opacity=".8"/>' +
    '<g stroke="#4a2a06" stroke-width=".6">' +
      '<path d="M28 81 L34 81 L31 87 Z" fill="#fde68a"/>' +
      '<path d="M44 85 L50 85 L47 91 Z" fill="#ff8fab"/>' +
      '<path d="M110 85 L116 85 L113 91 Z" fill="#fde68a"/>' +
      '<path d="M126 81 L132 81 L129 87 Z" fill="#ff8fab"/>' +
    '</g>',
    /* high striker */
    '<g>' +
      '<rect x="19" y="100" width="13" height="40" rx="2" fill="rgba(10,15,30,.92)" stroke="#f59e0b" stroke-width="1.4"/>' +
      '<path d="M19 108 H32 M19 116 H32 M19 124 H32 M19 132 H32" stroke="#f59e0b" stroke-opacity=".32" stroke-width="1"/>' +
      '<path d="M25.5 100 V 101" stroke="#f59e0b" stroke-width="2"/>' +
      '<circle cx="25.5" cy="95.5" r="5" fill="#fbbf24" stroke="#4a2a06" stroke-width="1"/>' +
      '<path d="M25.5 100.5 V 103" stroke="#f59e0b" stroke-width="1.6"/>' +
      '<rect x="21.5" y="111" width="8" height="5" rx="1.2" fill="#ff8fab" stroke="#4a2a06" stroke-width=".8"/>' +
      '<path d="M13 140 L19 129" stroke="#d6b46a" stroke-width="2.4" stroke-linecap="round"/>' +
      '<rect x="8" y="135" width="9" height="7" rx="1.5" fill="#d6b46a" stroke="#4a2a06" stroke-width=".9"/>' +
    '</g>' +
    /* popcorn */
    '<g transform="translate(128 124)">' +
      '<path d="M-7 0 H7 L5 16 H-5 Z" fill="rgba(10,15,30,.92)" stroke="#f59e0b" stroke-width="1.2" stroke-linejoin="round"/>' +
      '<path d="M-7 0 H7 L6 4.5 H-6 Z" fill="#fef3c7" opacity=".9"/>' +
      '<path d="M-2 4.5 L-1.4 16 M2 4.5 L1.4 16" stroke="#f59e0b" stroke-width=".9" opacity=".55"/>' +
      '<g fill="#fef3c7" stroke="#f0d9a0" stroke-width=".5">' +
        '<circle cx="-4.4" cy="-2.8" r="2.5"/><circle cx="0.6" cy="-4.4" r="2.7"/><circle cx="5" cy="-2" r="2.3"/>' +
      '</g>' +
    '</g>' +
    /* confetti */
    '<g fill="#fde68a"><rect x="46" y="96" width="3" height="3" rx=".6" transform="rotate(18 47.5 97.5)" opacity=".85"/>' +
      '<rect x="104" y="54" width="3" height="3" rx=".6" transform="rotate(-24 105.5 55.5)" opacity=".7"/></g>' +
    '<g fill="#ff8fab"><rect x="60" y="132" width="3" height="3" rx=".6" transform="rotate(32 61.5 133.5)" opacity=".7"/>' +
      '<rect x="96" y="88" width="2.6" height="2.6" rx=".6" transform="rotate(-14 97.3 89.3)" opacity=".6"/></g>',
    face({
      fill: '#121824', stroke: '#f59e0b', sw: 2.6,
      /* harlequin motley */
      over: '<path d="M80 39 A41 41 0 0 1 121 80 L 80 80 Z" fill="rgba(245,158,11,.07)"/>' +
        '<g fill="rgba(245,158,11,.26)" stroke="rgba(245,158,11,.5)" stroke-width=".7">' +
          '<path d="M62 62 L66 67 L62 72 L58 67 Z"/>' +
          '<path d="M80 56 L84 61 L80 66 L76 61 Z"/>' +
          '<path d="M98 62 L102 67 L98 72 L94 67 Z"/>' +
        '</g>',
      horns: stockHorns('#fef3c7'),
      eyes: eyes('#f59e0b', '#ff8fab'),
      grin: grin('#f59e0b'),
      extra:
        /* bow tie under the chin */
        '<g class="imp-bowtie">' +
          '<path d="M80 126 L66 119 L66 133 Z" fill="url(#st_v14Top_{uid})" stroke="#4a2a06" stroke-width="1" stroke-linejoin="round"/>' +
          '<path d="M80 126 L94 119 L94 133 Z" fill="url(#st_v14Top_{uid})" stroke="#4a2a06" stroke-width="1" stroke-linejoin="round"/>' +
          '<circle cx="80" cy="126" r="3.4" fill="#fef3c7" stroke="#4a2a06" stroke-width="1"/>' +
        '</g>'
    })
  );

  /* =========================================================================
     15 — FROSTBOUND SANCTUARY   (runes, original head)
     The shipping shrine again — arched frost frame, stepped jambs, mint
     hairline arch, dashed inner arch, the four columns and the finial dot,
     with the original head fill and the original navy + mint eyes. What's new
     is carved into it: a band of mint runes under the arch and two more down
     each jamb, lit from behind, plus a frost bloom off the floor.
     ====================================================================== */
  var RUNES = [
    'M2 0 V8 L5 12 L8 8 V0',            /* uruz    ᚢ */
    'M5 0 V12 M1 3 L5 0 L9 3',          /* tiwaz   ᛏ */
    'M8 0 L3 5 L8 8 L3 13',             /* sowilo  ᛊ */
    'M3 12 V0 L8 4 M3 6 L8 10',         /* fehu    ᚠ */
    'M3 12 V0 H7 L7 5 H3 M5 5 L8 11'    /* raidho  ᚱ */
  ];
  var rune = function (x, y, sc, i) {
    return '<g transform="translate(' + x + ' ' + y + ') scale(' + sc + ')">' +
      '<path d="' + RUNES[i] + '" fill="none" stroke="#a8ffd1" stroke-width="3.2" stroke-opacity=".16" stroke-linecap="round" stroke-linejoin="round"/>' +
      '<path d="' + RUNES[i] + '" fill="none" stroke="#a8ffd1" stroke-width="1.4" stroke-opacity=".9" stroke-linecap="round" stroke-linejoin="round"/>' +
      '</g>';
  };
  var v15 = assemble(
    '<radialGradient id="st_v15Glow_{uid}" cx="50%" cy="100%" r="62%">' +
      '<stop offset="0%" stop-color="#a8ffd1" stop-opacity=".1"/><stop offset="100%" stop-color="#a8ffd1" stop-opacity="0"/></radialGradient>',
    '<path d="M26 78 C26 38 46 16 80 14 C114 16 134 38 134 78" fill="rgba(14,20,32,.62)" stroke="#1e3a5f" stroke-width="3.2" stroke-linejoin="round"/>' +
    '<path d="M26 78 L26 108 L34 114 L44 110 L44 78 M46 108 L54 114 M106 108 L114 114 L116 108 L116 78 M134 78 L134 108 L126 114 L116 110" fill="none" stroke="#1e3a5f" stroke-width="2.2" stroke-linecap="round"/>' +
    '<path d="M26 78 C26 38 46 16 80 14 C114 16 134 38 134 78" fill="none" stroke="#a8ffd1" stroke-width="1.6" stroke-opacity=".7"/>' +
    '<path d="M34 78 C34 48 52 24 80 20 C108 24 126 48 126 78" fill="none" stroke="#a8ffd1" stroke-width="1.2" stroke-dasharray="2 4"/>' +
    '<circle cx="80" cy="14" r="3" fill="#a8ffd1" stroke="#1e3a5f" stroke-width="1.2"/>' +
    '<ellipse cx="80" cy="122" rx="46" ry="15" fill="url(#st_v15Glow_{uid})"/>',
    '<path d="M40 78 V110 M120 78 V110" stroke="#a8ffd1" stroke-width="1.6" stroke-linecap="round" opacity=".7"/>' +
    '<path d="M28 108 H52 M108 108 H132" stroke="#1e3a5f" stroke-width="1.4"/>' +
    /* rune band under the arch + two per jamb */
    rune(59, 27, .8, 0) + rune(75, 26, .9, 1) + rune(92, 27, .8, 2) +
    rune(28.5, 83, .72, 3) + rune(28.5, 97, .72, 4) +
    rune(124.5, 83, .72, 4) + rune(124.5, 97, .72, 3) +
    '<g fill="rgba(168,255,209,.45)"><circle cx="46" cy="126" r="1.1"/><circle cx="80" cy="132" r="1.3"/><circle cx="114" cy="126" r="1.1"/></g>',
    face({
      fill: 'rgba(14,20,32,.48)', stroke: '#1e3a5f', sw: 2.5,
      horns: stockHorns('#a8ffd1'),
      eyes: eyes('#1e3a5f', '#a8ffd1'),
      grin: grin('#a8ffd1')
    })
  );

  /* =========================================================================
     metadata for the preview page
     ====================================================================== */
  var THEMES = [
    {
      id: 'default', tag: 'improved', index: 0, badge: '00', name: 'Default', subtitle: 'Frosted Glass & Aurora',
      palette: { c1: '#38bdf8', c2: '#ec4899', bg: '#0a0f18', side: '#141e30', ink: '#f1f5f9', muted: '#9fb1c1' },
      cues: ['shipping design kept', 'layered aurora ribbons', 'soft plate highlight'],
      note: 'No new concept — this is the sticker shipping today. The two flat ribbons along the bottom are now built from three layers of light, the diamond gets a faint inner facet, the plate catches one soft highlight and the sparkles are rebalanced. Everything else is untouched.',
      art: vDefault
    },
    {
      id: 'v1', tag: 'revised', index: 1, badge: '01', name: 'Neon Obsidian', subtitle: 'In[st]Sanity Gradient Unified',
      palette: { c1: '#f8b250', c2: '#ee2a7b', bg: '#0b0910', side: '#14101d', ink: '#f6efe9', muted: '#a99aa8' },
      cues: ['obsidian shard', '4 matching chevrons', 'equaliser bars (live)', 'gradient fangs'],
      note: 'The two stray shard triangles are gone — all four corners now carry the same neon chevron. The bottom strips are wired as a real equaliser that moves with the sticker, and the fangs take the theme gradient instead of white.',
      art: v1
    },
    {
      id: 'v2', tag: 'revised', index: 2, badge: '02', name: 'Titanium Slate & Mercury-Mint', subtitle: 'Brushed Gunmetal + Ice-Mint',
      palette: { c1: '#99f6e4', c2: '#2dd4bf', bg: '#0b0f14', side: '#1c252e', ink: '#e6f2f2', muted: '#8fa8ac' },
      cues: ['hex machine plate', 'mint sprigs', 'socket bolts + mercury drips'],
      note: 'The machined hex plate stays — heavy mint border, socket bolts, mercury drips — and two mint sprigs now grow up the lower corners, four leaves each. The horns are the original thin stroke, carrying the mercury-mint gradient.',
      art: v2
    },
    {
      id: 'v3', tag: 'revised', index: 3, badge: '03', name: 'Minimalist Studio', subtitle: 'Linear / Raycast Matte',
      palette: { c1: '#f4f4f6', c2: '#a1a1aa', bg: '#090a0c', side: '#0c0d10', ink: '#f4f4f6', muted: '#7c8496' },
      cues: ['round head restored', 'selection handles', 'ruler ticks', 'original horns'],
      note: 'Round head and original horn shape kept. The pointer cursor is removed; the selection frame, corner handles, ruler ticks and toolbar dots stay.',
      art: v3
    },
    {
      id: 'v4', tag: 'revised', index: 4, badge: '04', name: 'Crimson Velvet & Gold', subtitle: "Gothic 'Exile' Luxury",
      palette: { c1: '#fbbf24', c2: '#e11d48', bg: '#0d080a', side: '#170e12', ink: '#fbe9d0', muted: '#b08b90' },
      cues: ['lancet arch', 'clean velvet panels', 'gilt tracery', 'original crown'],
      note: 'Drapes reworked: where there were four overlapping ribbon shapes and swag tails there is now one clean velvet panel each side with a single fold highlight. The lancet arch, tracery, fleur-de-lis and the original crown stay.',
      art: v4
    },
    {
      id: 'v5', tag: 'revised', index: 5, badge: '05', name: 'Royal Amethyst & Silver', subtitle: 'Ultraviolet + Liquid Chrome',
      palette: { c1: '#c084fc', c2: '#e2e8f0', bg: '#090611', side: '#140d24', ink: '#ece7fb', muted: '#9c8fb8' },
      cues: ['original crown', 'chrome shield', 'tilted orbit', 'liquid chrome'],
      note: 'The old crown is back (chevron + diamond gem) while the chrome shield, orbit, drips and chrome horns are all from the new version.',
      art: v5
    },
    {
      id: 'v6', tag: 'revised', index: 6, badge: '06', name: 'Cyber Yellow & Lime', subtitle: 'Electric Yellow + Acid Green',
      palette: { c1: '#facc15', c2: '#a3e635', bg: '#090b05', side: '#131709', ink: '#f7f3c9', muted: '#a3ae7a' },
      cues: ['random spot + timing', 'only during the hit', 'hazard corners'],
      note: 'Hazard stripes and caution tape kept. The thunder now picks its own spot: three strike points (right shoulder, left shoulder, crown) and one is chosen at random every 1–3 s. Nothing sits on the sticker at rest — the bolt, the impact flash, the spark star and the ring exist only for the half-second hit. The old static bolt is gone, since the strike supplies the lightning now.',
      art: v6
    },
    {
      id: 'v7', tag: 'kept', index: 7, badge: '07', name: 'Molten Amber & Copper', subtitle: 'Volcanic Basalt + Solar Gold',
      palette: { c1: '#f97316', c2: '#fde047', bg: '#0d0906', side: '#19100a', ink: '#fde7c4', muted: '#b08a6a' },
      cues: ['basalt octagon', 'erupting caldera', 'lava drips', 'brick kiln'],
      note: 'Unchanged from rev 1 — glowing basalt seams, caldera, lava beads and the brick kiln chimney.',
      art: v7
    },
    {
      id: 'v8', tag: 'revised', index: 8, badge: '08', name: 'Abyssal Cobalt', subtitle: 'Midnight Navy + Sapphire',
      palette: { c1: '#60a5fa', c2: '#93c5fd', bg: '#040812', side: '#091224', ink: '#dce9ff', muted: '#7f95b8' },
      cues: ['old scalloped shell', 'angler lure', 'bubbles', 'wake lines'],
      note: 'The original scalloped shell border and wakes are back; the angler lure, bubbles and the new head treatment (rim light + eye glow) stay.',
      art: v8
    },
    {
      id: 'v9', tag: 'revised', index: 9, badge: '09', name: 'Cyber Cherry & Cyan', subtitle: 'Gunmetal Arcade Dual-Neon',
      palette: { c1: '#ff2a5f', c2: '#00f0ff', bg: '#090b10', side: '#111520', ink: '#ececf2', muted: '#8890a3' },
      cues: ['dashed rings + crosshair', 'signal tears + stray pixels', 'chroma ghost (live)'],
      note: 'The original dashed rings, crosshair ticks and dots stay exactly as they ship. Added: two dark signal tears with cyan/magenta chroma edges, stray pixels, a displaced chroma copy of the rings that flashes in, and a stepped sideways jitter every 4.4 s — all CSS, no JS.',
      art: v9
    },
    {
      id: 'v10', tag: 'kept', index: 10, badge: '10', name: 'Nordic Twilight', subtitle: 'Dusk Periwinkle + Mint-Iris',
      palette: { c1: '#5eead4', c2: '#a78bfa', bg: '#161a28', side: '#1e2436', ink: '#e6ecff', muted: '#96a0bd' },
      cues: ['aurora ribbons', 'fjord + pines', 'branching antlers'],
      note: 'Unchanged from rev 1 — aurora over a snow-capped fjord, layered firs and branching antlers.',
      art: v10
    },
    {
      id: 'v11', tag: 'kept', index: 11, badge: '11', name: 'Moss & Brass', subtitle: 'Botanical Green & Antique Gold',
      palette: { c1: '#A98D45', c2: '#8BA64E', bg: '#171b11', side: '#1d2416', ink: '#F0EFDF', muted: '#a7ad8c' },
      cues: ['laurel wreath', 'acorns', 'leaf-antlers', 'brass rings'],
      note: 'Unchanged from rev 1 — laurel wreath, leaf-antlers, acorns and brass rings.',
      art: v11
    },
    {
      id: 'v12', tag: 'improved', index: 12, badge: '12', name: 'Cinderforge Kiln', subtitle: 'Brass Soot & Ember',
      palette: { c1: '#d6b46a', c2: '#ff7a18', bg: '#0f0f0f', side: '#1a160f', ink: '#FDF0D6', muted: '#a89472' },
      cues: ['original hammers + clash 1-3 s', 'anvil + hot billet', 'coal glow', 'drifting embers (live)'],
      note: 'The hammers sit exactly where they ship — top corners, heads pointing out — and from there swing at each other and clash every 1–3 s. Heads are banded with rivets and wrapped grips, the anvil has a horn, hardy hole and a glowing billet, a coal glow sits underneath and embers drift up.',
      art: v12
    },
    {
      id: 'v13', tag: 'improved', index: 13, badge: '13', name: 'Kelp Cathedral', subtitle: 'Brine Violet & Bloom',
      palette: { c1: '#6df0c8', c2: '#a78bfa', bg: '#0f0f1a', side: '#151422', ink: '#E6E0F5', muted: '#9a92b5' },
      cues: ['shipping circle kept', 'demon horns', 'faceted crystals + sigil lines'],
      note: 'Back to the sticker that ships: the long-dash outer ring, the dotted inner ring, three crystals and the lines that join them, now faceted and lit by a soft violet bloom. The crystals drop to the lower half to clear the horns: two long demon horns, thick at the root, curved out and back, sharp at the tip, mint-lit, and now rooted lower on the head so they sit closer to the eyes.',
      art: v13
    },
    {
      id: 'v14', tag: 'revised', index: 14, badge: '14', name: 'Wasteland Carnival', subtitle: 'Fairground Gold & Midnight',
      palette: { c1: '#f59e0b', c2: '#fef3c7', bg: '#0a0f1e', side: '#12182e', ink: '#fdf6e3', muted: '#a99a86' },
      cues: ['striped big top', 'high striker', 'popcorn', 'original head colour + horns'],
      note: 'Big top kept — striped roof, scalloped valance, pennant, bunting, high striker, popcorn and the harlequin head with a bow tie. The head now uses the original fill colour (#121824) at full opacity, so the tent frame never shows through it, and the horns are the original zig-zag.',
      art: v14
    },
    {
      id: 'v15', tag: 'improved', index: 15, badge: '15', name: 'Frostbound Sanctuary', subtitle: 'Deep Frost & Runes',
      palette: { c1: '#a8ffd1', c2: '#93c5fd', bg: '#0a0f14', side: '#0e141e', ink: '#d6e2f5', muted: '#8ba0b6' },
      cues: ['shipping shrine kept', 'carved runes', 'original head + eyes'],
      note: 'The shipping shrine, unchanged at its core: arched frost frame, stepped jambs, mint hairline arch, dashed inner arch, the four columns, the floor bars and the finial dot. Carved into it now: three runes under the arch and two down each jamb, lit from behind, plus a frost bloom off the floor. The head fill and both eyes are the original colours.',
      art: v15
    }
  ];

  root.STICKER_THEMES = THEMES;
})(typeof window !== 'undefined' ? window : globalThis);
