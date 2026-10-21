/* ===========================================================================
   In[st]Sanity — sticker identity pass (proposal only, nothing wired in).

   Sixteen badge designs: the shipping imp anatomy (frame / decor / face with
   imp-horns, imp-eyes-track > imp-eyes-blink, imp-grin) but every theme now
   carries the thing its theme is named after, and several themes swap the
   head silhouette or the horns so the badges read apart at 96 px.

   Every gradient id keeps the app's {uid} placeholder, so the same markup can
   be dropped into THEME_STICKER_SVGS without id collisions.
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

  /* -------------------------------------------------------------------------
     00 — DEFAULT · Frosted Glass & Aurora
     identity: a frosted glass slab, two aurora ribbons and the diamond pane
     ---------------------------------------------------------------------- */
  var vDefault = assemble(
    '<linearGradient id="st_defEdge_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#38bdf8"/><stop offset="50%" stop-color="#818cf8"/><stop offset="100%" stop-color="#ec4899"/></linearGradient>' +
    '<radialGradient id="st_defPane_{uid}" cx="50%" cy="50%" r="54%">' +
      '<stop offset="0%" stop-color="#38bdf8" stop-opacity=".32"/><stop offset="58%" stop-color="#ec4899" stop-opacity=".16"/><stop offset="100%" stop-color="#0f172a" stop-opacity="0"/></radialGradient>' +
    '<linearGradient id="st_defAur_{uid}" x1="0%" y1="100%" x2="100%" y2="0%">' +
      '<stop offset="0%" stop-color="#38bdf8"/><stop offset="52%" stop-color="#a5b4fc"/><stop offset="100%" stop-color="#f472b6"/></linearGradient>',
    '<rect x="14" y="14" width="132" height="132" rx="30" fill="rgba(15,23,42,.42)" stroke="url(#st_defEdge_{uid})" stroke-width="3.6"/>' +
    '<rect x="20.5" y="20.5" width="119" height="119" rx="24" fill="url(#st_defPane_{uid})" stroke="rgba(236,72,153,.4)" stroke-width="1.4"/>' +
    '<polygon points="80,22 138,80 80,138 22,80" fill="rgba(30,41,59,.3)" stroke="url(#st_defEdge_{uid})" stroke-width="2" stroke-linejoin="round"/>' +
    '<path d="M40 58 Q 46 44 60 38" stroke="#ffffff" stroke-opacity=".2" stroke-width="3.4" stroke-linecap="round" fill="none"/>' +
    '<path d="M34 72 Q 36 63 42 58" stroke="#ffffff" stroke-opacity=".13" stroke-width="2.4" stroke-linecap="round" fill="none"/>',
    '<path d="M22 50 C 44 26 58 54 80 38 C 100 24 118 46 138 30" stroke="url(#st_defAur_{uid})" stroke-width="3.2" stroke-linecap="round" fill="none" opacity=".85"/>' +
    '<path d="M26 40 C 46 22 62 46 84 30" stroke="url(#st_defAur_{uid})" stroke-width="1.5" stroke-linecap="round" fill="none" opacity=".5"/>' +
    '<path d="M24 126 C 52 110 82 136 136 116" stroke="#38bdf8" stroke-width="2.2" stroke-linecap="round" fill="none" opacity=".62"/>' +
    '<path d="M26 134 C 58 118 88 140 134 124" stroke="#ec4899" stroke-width="1.8" stroke-linecap="round" fill="none" opacity=".5"/>' +
    '<path d="M34 29 L36.5 36 L43.5 38.5 L36.5 41 L34 48 L31.5 41 L24.5 38.5 L31.5 36 Z" fill="#38bdf8"/>' +
    '<path d="M126 29 L128.5 36 L135.5 38.5 L128.5 41 L126 48 L123.5 41 L116.5 38.5 L123.5 36 Z" fill="#ec4899"/>' +
    '<path d="M34 112 L36 117 L41 119 L36 121 L34 126 L32 121 L27 119 L32 117 Z" fill="#818cf8"/>' +
    '<path d="M126 112 L128 117 L133 119 L128 121 L126 126 L124 121 L119 119 L124 117 Z" fill="#38bdf8"/>',
    face({
      fill: 'rgba(15,23,42,.52)', stroke: 'url(#st_defEdge_{uid})',
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#ffffff" stroke-width="3.4" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#38bdf8', '#ec4899'),
      grin: grin('#ffffff')
    })
  );

  /* -------------------------------------------------------------------------
     01 — NEON OBSIDIAN
     identity: a cut obsidian shard ringed by neon tubes
     ---------------------------------------------------------------------- */
  var v1 = assemble(
    '<linearGradient id="st_v1Edge_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#f8b250"/><stop offset="35%" stop-color="#f46856"/><stop offset="70%" stop-color="#ee2a7b"/><stop offset="100%" stop-color="#9d4edd"/></linearGradient>' +
    '<linearGradient id="st_v1Tube_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#f8b250"/><stop offset="55%" stop-color="#ee2a7b"/><stop offset="100%" stop-color="#a855f7"/></linearGradient>',
    '<polygon points="48,13 112,13 147,48 147,112 112,147 48,147 13,112 13,48" fill="rgba(18,13,26,.66)" stroke="url(#st_v1Edge_{uid})" stroke-width="3.6" stroke-linejoin="round"/>' +
    '<polygon points="52,22 108,22 138,52 138,108 108,138 52,138 22,108 22,52" fill="none" stroke="rgba(248,178,80,.28)" stroke-width="1.2" stroke-linejoin="round"/>' +
    '<path d="M22 52 L52 22 L60 60 Z" fill="rgba(157,78,221,.16)"/>' +
    '<path d="M138 108 L108 138 L100 100 Z" fill="rgba(238,42,123,.14)"/>' +
    '<path d="M118 18 L131 26 L122 33 Z" fill="rgba(157,78,221,.42)" stroke="url(#st_v1Edge_{uid})" stroke-width="1.1" stroke-linejoin="round"/>' +
    '<path d="M28 116 L40 120 L34 132 Z" fill="rgba(238,42,123,.32)" stroke="url(#st_v1Edge_{uid})" stroke-width="1.1" stroke-linejoin="round"/>',
    '<g stroke-linecap="round" fill="none">' +
      '<path d="M28 48 V28 H48" stroke="#f8b250" stroke-opacity=".17" stroke-width="8"/>' +
      '<path d="M132 112 V132 H112" stroke="#ee2a7b" stroke-opacity=".17" stroke-width="8"/>' +
      '<path d="M28 48 V28 H48" stroke="#f8b250" stroke-width="2.6"/>' +
      '<path d="M132 112 V132 H112" stroke="#ee2a7b" stroke-width="2.6"/>' +
    '</g>' +
    '<g stroke-linecap="round" stroke-width="4">' +
      '<line x1="58" y1="134" x2="58" y2="128" stroke="#f8b250"/><line x1="66" y1="134" x2="66" y2="125" stroke="#ee2a7b"/>' +
      '<line x1="74" y1="134" x2="74" y2="129" stroke="#f8b250"/><line x1="82" y1="134" x2="82" y2="124" stroke="#ee2a7b"/>' +
      '<line x1="90" y1="134" x2="90" y2="128" stroke="#f8b250"/><line x1="98" y1="134" x2="98" y2="125" stroke="#ee2a7b"/>' +
    '</g>',
    face({
      fill: 'rgba(24,17,36,.74)', stroke: 'url(#st_v1Edge_{uid})', sw: 2.8,
      over: '<path d="M80 39 L104 56 L96 80 Z" fill="rgba(238,42,123,.12)"/>' +
        '<path d="M80 39 L56 56 L64 80 Z" fill="rgba(248,178,80,.12)"/>' +
        '<path d="M56 104 L80 121 L104 104" fill="none" stroke="rgba(157,78,221,.24)" stroke-width="1.4"/>',
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="url(#st_v1Tube_{uid})" stroke-width="7" stroke-opacity=".22" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="url(#st_v1Tube_{uid})" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#f8b250', '#ee2a7b'),
      grin: grin('url(#st_v1Edge_{uid})') + fangs('#ffffff', 1)
    })
  );

  /* -------------------------------------------------------------------------
     02 — TITANIUM SLATE & MERCURY-MINT
     identity: hex machine plate with socket bolts and mercury drips
     ---------------------------------------------------------------------- */
  function bolt(x, y) {
    return '<g transform="translate(' + x + ' ' + y + ')">' +
      '<circle r="4.6" fill="#1b2630" stroke="#99f6e4" stroke-width="1.1"/>' +
      '<path d="M-2.4 0 H2.4 M0 -2.4 V2.4" stroke="#7f97a6" stroke-width="1.1" stroke-linecap="round"/></g>';
  }
  var v2 = assemble(
    '<linearGradient id="st_v2Metal_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#f8fafc"/><stop offset="50%" stop-color="#99f6e4"/><stop offset="100%" stop-color="#2dd4bf"/></linearGradient>',
    '<polygon points="80,12 134,45 134,115 80,148 26,115 26,45" fill="rgba(22,31,40,.58)" stroke="url(#st_v2Metal_{uid})" stroke-width="3.4" stroke-linejoin="round"/>' +
    '<polygon points="80,21 126,50 126,110 80,139 34,110 34,50" fill="none" stroke="#3d5060" stroke-width="1.3" stroke-linejoin="round"/>' +
    '<path d="M34 62 C 42 46 58 36 76 32" stroke="#cbd5e1" stroke-opacity=".2" stroke-width="5" stroke-linecap="round" fill="none"/>' +
    '<path d="M36 70 C 44 56 56 46 70 42" stroke="#cbd5e1" stroke-opacity=".12" stroke-width="2.4" stroke-linecap="round" fill="none"/>',
    bolt(45, 41) + bolt(115, 41) + bolt(115, 119) + bolt(45, 119) +
    '<path d="M80 26 V33 M80 127 V134" stroke="#5c7382" stroke-width="1.2" stroke-linecap="round" opacity=".7"/>' +
    '<path d="M60 132 C 56 139 57 144 60 144 C 63 144 64 139 60 132 Z" fill="#99f6e4"/>' +
    '<path d="M80 138 C 77 144 78 148 80 148 C 82 148 83 144 80 138 Z" fill="#2dd4bf"/>' +
    '<path d="M100 132 C 96 139 97 144 100 144 C 103 144 104 139 100 132 Z" fill="#99f6e4"/>' +
    '<circle cx="60" cy="139.5" r="1" fill="#ffffff" opacity=".8"/><circle cx="100" cy="139.5" r="1" fill="#ffffff" opacity=".8"/>',
    face({
      fill: 'rgba(15,21,27,.62)', stroke: '#2dd4bf', sw: 2.4,
      over: '<circle cx="80" cy="80" r="35.5" fill="none" stroke="rgba(226,232,240,.3)" stroke-width="1.2"/>' +
        '<path d="M52 62 C 60 51 70 45 82 43" stroke="#f8fafc" stroke-opacity=".3" stroke-width="2.6" stroke-linecap="round" fill="none"/>',
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#e2e8f0" stroke-width="5" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#99f6e4" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#99f6e4', '#2dd4bf'),
      grin: grin('#e2e8f0')
    })
  );

  /* -------------------------------------------------------------------------
     03 — MINIMALIST STUDIO
     identity: squircle head inside a design-tool selection, cursor and all
     ---------------------------------------------------------------------- */
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
    '</g>' +
    '<path d="M112 106 L112 128 L117.4 122.6 L121.6 131.4 L125.4 129.6 L121.2 120.8 L128.6 120.4 Z" fill="#0b0c0f" stroke="#f4f4f6" stroke-width="1.6" stroke-linejoin="round"/>',
    face({
      head: '<rect class="imp-head-circle" x="40" y="40" width="80" height="80" rx="27" fill="rgba(12,13,16,.6)" stroke="#f4f4f6" stroke-width="2.4"/>',
      over: '<path d="M58 72 H102" stroke="#2e3442" stroke-width="1"/>',
      horns: '<path d="M52 58 V70 L66 78 M108 58 V70 L94 78" stroke="#f4f4f6" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: '<circle cx="68" cy="83" r="3.7" fill="#f4f4f6"/><circle cx="92" cy="83" r="3.7" fill="#d4d4d8"/>',
      grin: grin('#f4f4f6', 3)
    })
  );

  /* -------------------------------------------------------------------------
     04 — CRIMSON VELVET & GOLD (gothic 'Exile')
     identity: lancet arch, velvet drapes and gilt tracery
     ---------------------------------------------------------------------- */
  var v4 = assemble(
    '<linearGradient id="st_v4Gold_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#fde68a"/><stop offset="50%" stop-color="#fbbf24"/><stop offset="100%" stop-color="#b45309"/></linearGradient>' +
    '<linearGradient id="st_v4Velvet_{uid}" x1="0%" y1="0%" x2="0%" y2="100%">' +
      '<stop offset="0%" stop-color="#9f1239"/><stop offset="60%" stop-color="#6b0f24"/><stop offset="100%" stop-color="#3b0a18"/></linearGradient>',
    '<path d="M80 10 C 118 26 142 52 142 90 L142 122 C 142 138 118 150 80 152 C 42 150 18 138 18 122 L18 90 C 18 52 42 26 80 10 Z" fill="rgba(59,10,24,.6)" stroke="url(#st_v4Gold_{uid})" stroke-width="3.4"/>' +
    '<path d="M80 19 C 114 33 134 56 134 90 L134 120 C 134 133 112 143 80 145 C 48 143 26 133 26 120 L26 90 C 26 56 46 33 80 19 Z" fill="none" stroke="rgba(251,191,36,.36)" stroke-width="1.3"/>' +
    '<path d="M27 48 C 34 64 29 96 36 138 C 32 132 28 124 27 116 Z" fill="url(#st_v4Velvet_{uid})" stroke="#fbbf24" stroke-opacity=".4" stroke-width=".9"/>' +
    '<path d="M133 48 C 126 64 131 96 124 138 C 128 132 132 124 133 116 Z" fill="url(#st_v4Velvet_{uid})" stroke="#fbbf24" stroke-opacity=".4" stroke-width=".9"/>' +
    '<g stroke="#fbbf24" stroke-opacity=".34" stroke-width="1" fill="none">' +
      '<path d="M31 54 C 36 72 31 104 35 128"/><path d="M34 52 C 38 74 34 106 38 124"/>' +
      '<path d="M129 54 C 124 72 129 104 125 128"/><path d="M126 52 C 122 74 126 106 122 124"/>' +
    '</g>' +
    '<path d="M24 44 C 38 50 36 62 44 68 C 36 66 30 60 26 52 Z" fill="#fbbf24" fill-opacity=".16" stroke="#fbbf24" stroke-opacity=".45" stroke-width=".9"/>' +
    '<path d="M136 44 C 122 50 124 62 116 68 C 124 66 130 60 134 52 Z" fill="#fbbf24" fill-opacity=".16" stroke="#fbbf24" stroke-opacity=".45" stroke-width=".9"/>',
    '<g stroke="url(#st_v4Gold_{uid})" stroke-width="1.4" fill="rgba(251,191,36,.1)">' +
      '<circle cx="70" cy="31" r="5"/><circle cx="90" cy="31" r="5"/><circle cx="80" cy="21" r="4"/>' +
    '</g>' +
    '<path d="M64 40 Q 80 18 96 40" fill="none" stroke="url(#st_v4Gold_{uid})" stroke-width="1.3"/>' +
    '<path d="M80 131 C 84 135 87 139 80 146 C 73 139 76 135 80 131 Z" fill="url(#st_v4Gold_{uid})"/>' +
    '<path d="M75 137 C 68 133 66 128 71 126 C 75 129 76 133 77 136 Z" fill="url(#st_v4Gold_{uid})"/>' +
    '<path d="M85 137 C 92 133 94 128 89 126 C 85 129 84 133 83 136 Z" fill="url(#st_v4Gold_{uid})"/>' +
    '<path d="M80 121 V127" stroke="url(#st_v4Gold_{uid})" stroke-width="1.4"/>',
    face({
      fill: 'rgba(105,12,36,.55)', stroke: 'url(#st_v4Gold_{uid})', sw: 2.6,
      over: '<path d="M48 62 C 58 50 70 45 82 44" stroke="#fde68a" stroke-opacity=".2" stroke-width="2.2" stroke-linecap="round" fill="none"/>',
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="url(#st_v4Gold_{uid})" stroke-width="3.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#fde68a', '#e11d48'),
      grin: grin('url(#st_v4Gold_{uid})') + fangs('#fde68a', 1)
    })
  );

  /* -------------------------------------------------------------------------
     05 — ROYAL AMETHYST & SILVER
     identity: a jewelled crown over a chrome shield on a tilted orbit
     ---------------------------------------------------------------------- */
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
    '<g stroke-linejoin="round">' +
      '<path d="M52 40 L52 22 L64 31 L72 15 L80 26 L88 15 L96 31 L108 22 L108 40 Z" fill="url(#st_v5Chrome_{uid})" stroke="#e2e8f0" stroke-width="1.1"/>' +
      '<path d="M52 36 H108 V41 H52 Z" fill="url(#st_v5Violet_{uid})" stroke="#e2e8f0" stroke-width=".9"/>' +
    '</g>' +
    '<circle cx="80" cy="38.5" r="1.9" fill="#f8fafc"/>' +
    '<circle cx="64" cy="38.5" r="1.3" fill="#e2e8f0" opacity=".85"/><circle cx="96" cy="38.5" r="1.3" fill="#e2e8f0" opacity=".85"/>' +
    '<circle cx="52" cy="22" r="2.4" fill="url(#st_v5Violet_{uid})" stroke="#f8fafc" stroke-width=".8"/>' +
    '<circle cx="108" cy="22" r="2.4" fill="url(#st_v5Violet_{uid})" stroke="#f8fafc" stroke-width=".8"/>' +
    '<circle cx="80" cy="14.5" r="2.6" fill="#f8fafc"/>' +
    '<polygon points="80,140 86,146 80,152 74,146" fill="url(#st_v5Violet_{uid})" stroke="#e2e8f0" stroke-width=".9" stroke-linejoin="round"/>' +
    '<circle cx="80" cy="146" r="1.3" fill="#f8fafc"/>',
    face({
      fill: 'rgba(30,18,56,.6)', stroke: 'url(#st_v5Chrome_{uid})', sw: 2.6,
      over: '<path d="M80 39 C 100 44 112 58 116 78 C 106 62 96 52 80 47 Z" fill="rgba(192,132,252,.22)"/>' +
        '<path d="M46 66 C 52 52 62 45 74 43" stroke="#f8fafc" stroke-opacity=".4" stroke-width="2" stroke-linecap="round" fill="none"/>',
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#f8fafc" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="url(#st_v5Violet_{uid})" stroke-width="5.6" stroke-linecap="round" stroke-linejoin="round" fill="none" opacity=".32"/>',
      eyes: eyes('#f8fafc', '#c084fc'),
      grin: grin('#e2e8f0')
    })
  );

  /* -------------------------------------------------------------------------
     06 — CYBER YELLOW & LIME
     identity: hazard corners, bolt, live plug and caution tape
     ---------------------------------------------------------------------- */
  var v6 = assemble(
    '',
    '<rect x="14" y="14" width="132" height="132" rx="24" fill="rgba(17,21,6,.5)" stroke="#facc15" stroke-width="3.6"/>' +
    '<g stroke="#a3e635" stroke-width="4" stroke-linecap="round" opacity=".85">' +
      '<path d="M19 43 L43 19"/><path d="M19 55 L55 19"/><path d="M21 65 L65 21"/>' +
      '<path d="M141 117 L117 141"/><path d="M141 105 L105 141"/><path d="M139 95 L95 139"/>' +
    '</g>',
    '<polygon points="83,17 71,30 79,30 74,40 89,25 81,25" fill="#facc15" stroke="#111506" stroke-width=".8" stroke-linejoin="round"/>' +
    '<g transform="translate(121 27)" stroke="#a3e635" stroke-width="1.8" stroke-linecap="round" fill="none">' +
      '<path d="M-5 -7 V-2 M5 -7 V-2"/>' +
      '<path d="M-8 -2 H8 V2 A8 8 0 0 1 -8 2 Z" fill="rgba(163,230,53,.12)"/>' +
    '</g>' +
    '<rect x="30" y="125" width="100" height="12" rx="4" fill="#facc15" stroke="#84cc16" stroke-width="1.3"/>' +
    '<path d="M38 137 L45 125 M54 137 L61 125 M70 137 L77 125 M86 137 L93 125 M102 137 L109 125 M118 137 L125 125" stroke="#111506" stroke-width="5" fill="none"/>' +
    '<g fill="#a3e635"><circle cx="30" cy="118" r="1.6"/><circle cx="130" cy="118" r="1.6"/><circle cx="24" cy="86" r="1.4"/><circle cx="136" cy="86" r="1.4"/></g>',
    face({
      fill: 'rgba(25,32,9,.52)', stroke: '#a3e635', sw: 2.8,
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#facc15" stroke-width="3.4" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: '<path d="M64 79 L72 87 M72 79 L64 87" stroke="#facc15" stroke-width="3" stroke-linecap="round" fill="none"/>' +
        '<path d="M88 79 L96 87 M96 79 L88 87" stroke="#a3e635" stroke-width="3" stroke-linecap="round" fill="none"/>',
      grin: grin('#a3e635')
    })
  );

  /* -------------------------------------------------------------------------
     07 — MOLTEN AMBER & COPPER
     identity: basalt octagon, an erupting caldera and lava drips
     ---------------------------------------------------------------------- */
  var v7 = assemble(
    '<linearGradient id="st_v7Lava_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#fde047"/><stop offset="55%" stop-color="#f97316"/><stop offset="100%" stop-color="#b91c1c"/></linearGradient>' +
    '<clipPath id="st_v7Head_{uid}"><circle cx="80" cy="80" r="41"/></clipPath>',
    '<polygon points="48,13 112,13 147,48 147,112 112,147 48,147 13,112 13,48" fill="rgba(22,14,8,.7)" stroke="url(#st_v7Lava_{uid})" stroke-width="3.4" stroke-linejoin="round"/>' +
    '<polygon points="52,22 108,22 138,52 138,108 108,138 52,138 22,108 22,52" fill="none" stroke="rgba(253,224,71,.3)" stroke-width="1.2" stroke-linejoin="round"/>' +
    '<path d="M20 60 L34 56 L41 68 M140 98 L126 102 L119 90" stroke="rgba(253,224,71,.55)" stroke-width="1.4" fill="none" stroke-linecap="round"/>',
    /* low basalt ridge with a glowing fissure, kept mostly behind the chin */
    '<path d="M24 146 L44 132 L60 140 L80 130 L100 140 L116 132 L136 146 Z" fill="#170f08" stroke="#7c2d12" stroke-width="1.4" stroke-linejoin="round"/>' +
    '<path d="M44 132 L60 140 L80 130 L100 140 L116 132" stroke="url(#st_v7Lava_{uid})" stroke-width="2.6" fill="none" stroke-linejoin="round"/>' +
    '<path d="M60 140 C 58 144 62 145 61 148 M100 140 C 102 144 98 145 99 148" stroke="#f97316" stroke-width="2" stroke-linecap="round" fill="none" opacity=".9"/>' +
    '<g fill="#ff7a18"><circle cx="72" cy="136" r="1.3"/><circle cx="90" cy="135" r="1.1"/><circle cx="80" cy="133" r="1"/></g>' +
    '<g fill="url(#st_v7Lava_{uid})">' +
      '<path d="M56 14 C 56 22 53 24 56 27 C 59 30 62 26 60 22 C 59 19 58 16 58 14 Z"/>' +
      '<path d="M80 14 C 80 24 76 27 80 31 C 84 34 87 29 85 24 C 84 20 82 17 82 14 Z"/>' +
      '<path d="M104 14 C 104 21 101 23 104 26 C 107 29 110 25 108 21 C 107 18 106 16 106 14 Z"/>' +
    '</g>' +
    /* brick kiln chimney, left of the caldera */
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
    '<g transform="translate(126 30)"><circle r="6" fill="url(#st_v7Lava_{uid})"/><circle r="2.6" fill="#fef3c7"/>' +
      '<g stroke="#fbbf24" stroke-width="1.4" stroke-linecap="round"><path d="M0 -9 V-12 M0 9 V12 M-9 0 H-12 M9 0 H12 M-6.4 -6.4 L-8.5 -8.5 M6.4 -6.4 L8.5 -8.5 M-6.4 6.4 L-8.5 8.5 M6.4 6.4 L8.5 8.5"/></g></g>' +
    '<g fill="#fbbf24"><circle cx="52" cy="104" r="1.5"/><circle cx="112" cy="96" r="1.4"/><circle cx="100" cy="116" r="1.2"/></g>',
    face({
      fill: 'rgba(28,18,9,.86)', stroke: 'url(#st_v7Lava_{uid})', sw: 2.6,
      over: '<g clip-path="url(#st_v7Head_{uid})">' +
        '<path d="M46 64 L60 72 L55 90 L70 100 M114 58 L100 70 L106 84 L94 98" stroke="url(#st_v7Lava_{uid})" stroke-width="2" stroke-linejoin="round" fill="none" opacity=".85"/>' +
        '<path d="M44 52 L58 58 L52 70 M118 104 L106 98 L112 88" stroke="#3f2a16" stroke-width="1.6" fill="none"/>' +
        '</g>',
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#3b1d0a" stroke-width="5.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="url(#st_v7Lava_{uid})" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#fde047', '#ff7a18'),
      grin: grin('url(#st_v7Lava_{uid})')
    })
  );

  /* -------------------------------------------------------------------------
     08 — ABYSSAL COBALT
     identity: deep-sea porthole, an angler lure off the head, sonar and bubbles
     ---------------------------------------------------------------------- */
  var v8 = assemble(
    '<radialGradient id="st_v8Lure_{uid}" cx="50%" cy="50%" r="50%">' +
      '<stop offset="0%" stop-color="#e0f2fe" stop-opacity=".95"/><stop offset="45%" stop-color="#60a5fa" stop-opacity=".55"/><stop offset="100%" stop-color="#2563eb" stop-opacity="0"/></radialGradient>' +
    '<radialGradient id="st_v8Eye_{uid}" cx="50%" cy="50%" r="50%">' +
      '<stop offset="0%" stop-color="#93c5fd" stop-opacity=".85"/><stop offset="100%" stop-color="#3b82f6" stop-opacity="0"/></radialGradient>',
    '<circle cx="80" cy="80" r="67" fill="rgba(6,15,32,.66)" stroke="#3b82f6" stroke-width="3.6"/>' +
    '<circle cx="80" cy="80" r="59" fill="none" stroke="rgba(147,197,253,.42)" stroke-width="1.5"/>' +
    '<circle cx="80" cy="80" r="51" fill="none" stroke="rgba(147,197,253,.16)" stroke-width="1"/>',
    '<g fill="#12233c" stroke="#60a5fa" stroke-width="1">' +
      '<circle cx="80" cy="16.5" r="2.3"/><circle cx="112" cy="27" r="2.3"/><circle cx="126" cy="49" r="2.3"/>' +
      '<circle cx="134" cy="80" r="2.3"/><circle cx="126" cy="111" r="2.3"/><circle cx="112" cy="133" r="2.3"/>' +
      '<circle cx="80" cy="143.5" r="2.3"/><circle cx="48" cy="133" r="2.3"/><circle cx="34" cy="111" r="2.3"/>' +
      '<circle cx="26" cy="80" r="2.3"/><circle cx="34" cy="49" r="2.3"/><circle cx="48" cy="27" r="2.3"/>' +
    '</g>' +
    /* anglerfish lure: stalk off the crown, swollen esca, light rays */
    '<path d="M71 47 C 64 45 57 41 52 35" stroke="#3b82f6" stroke-width="4.4" stroke-linecap="round" fill="none" opacity=".4"/>' +
    '<path d="M71 47 C 64 45 57 41 52 35" stroke="#7dd3fc" stroke-width="2.2" stroke-linecap="round" fill="none"/>' +
    '<circle cx="50" cy="31" r="11" fill="url(#st_v8Lure_{uid})"/>' +
    '<circle cx="50" cy="31" r="5.6" fill="#dbeafe" stroke="#60a5fa" stroke-width="1.1"/>' +
    '<circle cx="50" cy="31" r="2.8" fill="#f8fdff"/><circle cx="48.8" cy="29.8" r="1" fill="#ffffff"/>' +
    '<g stroke="#93c5fd" stroke-width="1.2" stroke-linecap="round" opacity=".75">' +
      '<path d="M50 19 V16"/><path d="M50 43 V46"/><path d="M38 31 H35"/><path d="M62 31 H65"/>' +
      '<path d="M41.6 22.6 L39.4 20.4"/><path d="M58.4 39.4 L60.6 41.6"/><path d="M58.4 22.6 L60.6 20.4"/><path d="M41.6 39.4 L39.4 41.6"/>' +
    '</g>' +
    '<g fill="none" stroke="rgba(147,197,253,.5)" stroke-width="1.4">' +
      '<circle cx="118" cy="30" r="2.6"/><circle cx="127" cy="44" r="2"/><circle cx="108" cy="20" r="1.8"/>' +
      '<circle cx="46" cy="118" r="2.2"/><circle cx="58" cy="130" r="1.6"/>' +
    '</g>' +
    /* bubbles rising from the trench + a slow current past the cheek */
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
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#60a5fa" stroke-width="6" stroke-opacity=".2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#7dd3fc" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>',
      eyes: eyes('#93c5fd', '#60a5fa'),
      grin: grin('#93c5fd')
    })
  );

  /* -------------------------------------------------------------------------
     09 — CYBER CHERRY & CYAN (gunmetal arcade)
     identity: CRT glass with scanlines, a joystick, buttons and a coin slot
     ---------------------------------------------------------------------- */
  var v9 = assemble(
    '',
    '<rect x="14" y="14" width="132" height="132" rx="26" fill="rgba(13,17,26,.62)" stroke="#ff2a5f" stroke-width="3.2"/>' +
    '<rect x="21.5" y="21.5" width="117" height="117" rx="19" fill="none" stroke="#00f0ff" stroke-width="1.7"/>' +
    '<g stroke="#00f0ff" stroke-opacity=".12" stroke-width="1"><path d="M24 34 H136 M24 44 H136 M24 54 H136 M24 64 H136 M24 74 H136 M24 84 H136 M24 94 H136 M24 104 H136 M24 114 H136 M24 124 H136"/></g>' +
    '<g fill="#0b0e15" stroke="#3c4552" stroke-width="1.2">' +
      '<circle cx="27" cy="27" r="3.4"/><circle cx="133" cy="27" r="3.4"/><circle cx="27" cy="133" r="3.4"/><circle cx="133" cy="133" r="3.4"/>' +
    '</g>' +
    '<g stroke="#8794a5" stroke-width="1"><path d="M25 27 H29 M27 25 V29 M131 27 H135 M133 25 V29 M25 133 H29 M27 131 V135 M131 133 H135 M133 131 V135"/></g>' +
    '<g fill="#ff2a5f"><rect x="36" y="24" width="5" height="5"/><rect x="43" y="24" width="5" height="5" opacity=".55"/></g>' +
    '<g fill="#00f0ff"><rect x="36" y="31" width="5" height="5" opacity=".55"/><rect x="43" y="31" width="5" height="5"/></g>',
    '<ellipse cx="45" cy="141" rx="14" ry="4.6" fill="#0b0e15" stroke="#ff2a5f" stroke-width="1.2"/>' +
    '<path d="M45 139 V125" stroke="#cbd5e1" stroke-width="4" stroke-linecap="round"/>' +
    '<circle cx="45" cy="120" r="6.4" fill="#ff2a5f" stroke="#ffd1dc" stroke-width="1"/>' +
    '<circle cx="43" cy="118" r="1.6" fill="#ffffff" opacity=".85"/>' +
    '<circle cx="112" cy="131" r="6.6" fill="#00f0ff" stroke="#0b3a40" stroke-width="1.2"/>' +
    '<circle cx="110.4" cy="129.4" r="1.7" fill="#ffffff" opacity=".85"/>' +
    '<circle cx="128" cy="136" r="5.4" fill="#ff2a5f" stroke="#4a0f1e" stroke-width="1.2"/>' +
    '<circle cx="126.6" cy="134.6" r="1.4" fill="#ffffff" opacity=".85"/>' +
    '<rect x="70" y="136" width="20" height="9" rx="2" fill="#0b0e15" stroke="#00f0ff" stroke-width="1"/>' +
    '<path d="M80 138.5 V142.5" stroke="#ff2a5f" stroke-width="2" stroke-linecap="round"/>' +
    '<g transform="translate(126 48)" stroke="#00f0ff" stroke-width="1.6" fill="#0b0e15"><path d="M-4 -8 H4 V-4 H8 V4 H4 V8 H-4 V4 H-8 V-4 H-4 Z"/></g>',
    face({
      fill: 'rgba(20,26,41,.66)', stroke: '#00f0ff', sw: 2.4,
      over: '<circle cx="80" cy="80" r="35" fill="none" stroke="rgba(255,42,95,.45)" stroke-width="1.2"/>' +
        '<path d="M48 60 C 56 49 68 44 80 43" stroke="#ffffff" stroke-opacity=".14" stroke-width="3" stroke-linecap="round" fill="none"/>',
      horns: '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#ff2a5f" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round" fill="none"/>' +
        '<path d="M56 56 L52 72 L71 78 M104 56 L108 72 L89 78" stroke="#00f0ff" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" fill="none" opacity=".55"/>',
      eyes: eyes('#ff2a5f', '#00f0ff'),
      grin: grin('#ffffff') + '<polygon points="69,97 73,104 76,99" fill="#00f0ff"/><polygon points="91,97 87,104 84,99" fill="#ff2a5f"/>'
    })
  );

  /* -------------------------------------------------------------------------
     10 — NORDIC TWILIGHT
     identity: aurora ribbons over a fjord, pines and branching antlers
     ---------------------------------------------------------------------- */
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
    /* fjord: three snow-capped peaks on the horizon, water below */
    '<path d="M20 138 L46 104 L62 122 L80 98 L100 126 L118 106 L140 138 Z" fill="#0f1524" stroke="#2dd4bf" stroke-width="1.1" stroke-linejoin="round"/>' +
    '<path d="M46 104 L53 113 L59 109 L64 115 L57 118 L50 114 L43 116 Z" fill="#e9f7ff" opacity=".8"/>' +
    '<path d="M80 98 L87 108 L94 105 L100 112 L92 116 L84 111 L76 114 Z" fill="#e9f7ff" opacity=".85"/>' +
    '<path d="M118 106 L124 113 L130 110 L136 118 L128 120 L121 115 L114 118 Z" fill="#e9f7ff" opacity=".7"/>' +
    /* two tannenbaums, layered */
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
    /* water */
    '<path d="M22 141 H138" stroke="#5eead4" stroke-width="1.2" opacity=".5"/>' +
    '<path d="M26 145 H40 M48 145 H58 M66 145 H74 M88 145 H96 M104 145 H114 M120 145 H134" stroke="#5eead4" stroke-width="1" opacity=".35"/>' +
    '<path d="M56 141 V145 M104 141 V145" stroke="#2dd4bf" stroke-width="1" opacity=".3"/>',
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

  /* -------------------------------------------------------------------------
     11 — MOSS & BRASS
     identity: brass rings, a laurel wreath, acorns and leaf-antlers
     ---------------------------------------------------------------------- */
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
    /* acorns: scalloped cap over a pointed nut */
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

  /* -------------------------------------------------------------------------
     12 — CINDERFORGE KILN · kept verbatim (the benchmark)
     ---------------------------------------------------------------------- */
  var v12 = (root.STICKER_CURRENT && root.STICKER_CURRENT.v12) || '';

  /* -------------------------------------------------------------------------
     13 — KELP CATHEDRAL
     identity: lancet window with a rose, kelp fronds, coral and a fish
     ---------------------------------------------------------------------- */
  var v13 = assemble(
    '<linearGradient id="st_v13Glass_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#c4b5fd"/><stop offset="100%" stop-color="#6df0c8"/></linearGradient>',
    '<path d="M80 11 C 114 28 136 54 136 92 V146 H24 V92 C 24 54 46 28 80 11 Z" fill="rgba(22,22,38,.62)" stroke="#a78bfa" stroke-width="3"/>' +
    '<path d="M80 20 C 109 35 128 58 128 92 V138 H32 V92 C 32 58 51 35 80 20 Z" fill="none" stroke="rgba(109,240,200,.4)" stroke-width="1.3"/>',
    /* leaded rose window above the crown */
    '<g stroke="url(#st_v13Glass_{uid})" stroke-width="1.2" fill="none" opacity=".92">' +
      '<circle cx="80" cy="30" r="13.5"/>' +
      '<circle cx="80" cy="30" r="7.5"/>' +
      '<path d="M80 16.5 V43.5 M66.5 30 H93.5 M70.5 20.5 L89.5 39.5 M89.5 20.5 L70.5 39.5"/>' +
      '<g fill="rgba(109,240,200,.22)" stroke-width=".9">' +
        '<circle cx="80" cy="22" r="2.6"/><circle cx="86" cy="26" r="2.6"/><circle cx="88.5" cy="33" r="2.6"/>' +
        '<circle cx="83" cy="38.5" r="2.6"/><circle cx="75" cy="37" r="2.6"/><circle cx="71" cy="30" r="2.6"/><circle cx="74" cy="23" r="2.6"/>' +
      '</g>' +
      '<circle cx="80" cy="30" r="2.4" fill="rgba(167,139,250,.55)"/>' +
    '</g>' +
    '<g fill="none" stroke-linecap="round">' +
      '<path d="M30 146 C 22 122 34 104 27 80 C 22 62 31 50 29 38" stroke="#34d399" stroke-width="3"/>' +
      '<path d="M130 146 C 138 122 126 104 133 80 C 138 62 129 50 131 38" stroke="#34d399" stroke-width="3"/>' +
      '<path d="M33 128 C 25 124 22 118 22 114 M31 108 C 23 104 21 98 22 94 M29 86 C 22 82 21 76 22 72 M31 62 C 25 58 24 54 25 50" stroke="#6df0c8" stroke-width="1.8"/>' +
      '<path d="M127 128 C 135 124 138 118 138 114 M129 108 C 137 104 139 98 138 94 M131 86 C 138 82 139 76 138 72" stroke="#6df0c8" stroke-width="1.8"/>' +
    '</g>' +
    /* kelp blades: wavy ribbons, not leaves */
    '<g fill="none" stroke-linecap="round" stroke="#34d399" stroke-width="3.4" opacity=".95">' +
      '<path d="M30 124 C 22 116 26 108 20 100 C 14 92 20 84 18 76 C 16 68 22 60 26 54"/>' +
      '<path d="M130 124 C 138 116 134 108 140 100 C 146 92 140 84 142 76 C 144 68 138 60 134 54"/>' +
    '</g>' +
    '<g fill="none" stroke-linecap="round" stroke="#6df0c8" stroke-width="1.4" opacity=".65">' +
      '<path d="M32 118 C 26 112 30 104 24 96"/><path d="M128 118 C 134 112 130 104 136 96"/>' +
    '</g>' +
    '<g fill="#34d399" stroke="#6df0c8" stroke-width=".6" opacity=".9">' +
      '<path d="M20 104 C 14 100 12 94 13 88 C 19 92 22 98 20 104 Z"/>' +
      '<path d="M18 84 C 12 79 11 73 13 67 C 18 72 20 78 18 84 Z"/>' +
      '<path d="M22 64 C 18 58 19 52 23 48 C 26 54 26 60 22 64 Z"/>' +
      '<path d="M140 104 C 146 100 148 94 147 88 C 141 92 138 98 140 104 Z"/>' +
      '<path d="M142 84 C 148 79 149 73 147 67 C 142 72 140 78 142 84 Z"/>' +
      '<path d="M138 64 C 142 58 141 52 137 48 C 134 54 134 60 138 64 Z"/>' +
    '</g>' +
    /* coral shelf + sea fans at the sill */
    '<path d="M80 146 C 76 138 68 134 62 136 C 66 142 72 146 76 147 C 78 148 80 147 80 146 Z" fill="#6df0c8" opacity=".85"/>' +
    '<path d="M80 146 C 84 138 92 134 98 136 C 94 142 88 146 84 147 C 82 148 80 147 80 146 Z" fill="#6df0c8" opacity=".85"/>' +
    '<g stroke="#a78bfa" stroke-width="1.3" fill="none" stroke-linecap="round" opacity=".85">' +
      '<path d="M40 146 C 40 138 34 134 30 130"/><path d="M40 140 C 35 138 33 134 33 131"/>' +
      '<path d="M120 146 C 120 138 126 134 130 130"/><path d="M120 140 C 125 138 127 134 127 131"/>' +
    '</g>' +
    '<g fill="#6df0c8"><circle cx="34" cy="140" r="1.6"/><circle cx="126" cy="140" r="1.6"/></g>' +
    '<path d="M106 56 q 7 -6 14 0 q -7 6 -14 0 Z" fill="#6df0c8" opacity=".75"/><path d="M120 56 L126 52 L126 60 Z" fill="#6df0c8" opacity=".75"/>' +
    '<path d="M40 76 q 6 -5 12 0 q -6 5 -12 0 Z" fill="#a78bfa" opacity=".65"/><path d="M52 76 L57 73 L57 79 Z" fill="#a78bfa" opacity=".65"/>' +
    '<g fill="none" stroke="rgba(196,181,253,.55)" stroke-width="1.2"><circle cx="112" cy="94" r="2.6"/><circle cx="120" cy="104" r="1.8"/><circle cx="52" cy="96" r="2"/></g>',
    face({
      fill: 'rgba(20,20,36,.9)', stroke: '#a78bfa', sw: 2.5,
      over: '<path d="M80 39 C 96 44 108 56 113 72" stroke="rgba(109,240,200,.32)" stroke-width="2" stroke-linecap="round" fill="none"/>' +
        '<path d="M52 100 C 62 110 76 114 88 112" stroke="rgba(167,139,250,.3)" stroke-width="1.6" fill="none" stroke-linecap="round"/>',
      horns: '<path d="M58 62 C 50 50 54 40 46 30 M102 62 C 110 50 106 40 114 30" stroke="url(#st_v13Glass_{uid})" stroke-width="3.2" stroke-linecap="round" fill="none"/>',
      eyes: eyes('#a78bfa', '#6df0c8'),
      grin: grin('#6df0c8')
    })
  );

  /* -------------------------------------------------------------------------
     14 — WASTELAND CARNIVAL
     identity: a bulb ring, a jester cap, bunting, a ferris wheel, ticket
     ---------------------------------------------------------------------- */
  var bulbs = (function () {
    var out = '';
    for (var i = 0; i < 16; i++) {
      var a = (i / 16) * Math.PI * 2 - Math.PI / 2;
      var x = (80 + Math.cos(a) * 61).toFixed(1);
      var y = (80 + Math.sin(a) * 61).toFixed(1);
      var lit = i % 2 === 0;
      out += '<circle cx="' + x + '" cy="' + y + '" r="3.1" fill="' + (lit ? '#fde68a' : '#4a3410') + '"' +
        (lit ? ' stroke="#fef3c7" stroke-opacity=".6" stroke-width=".8"' : '') + '/>';
    }
    return out;
  })();
  var v14 = assemble(
    '<linearGradient id="st_v14Cap_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#fbbf24"/><stop offset="55%" stop-color="#f59e0b"/><stop offset="100%" stop-color="#b45309"/></linearGradient>',
    '<circle cx="80" cy="80" r="67" fill="rgba(10,15,30,.66)" stroke="#f59e0b" stroke-width="3.2"/>' +
    '<circle cx="80" cy="80" r="66" fill="none" stroke="#0a0f1e" stroke-width="1"/>' +
    bulbs,
    '<g transform="translate(119 116)" stroke="#f59e0b" stroke-width="1.3" fill="none">' +
      '<circle r="14"/><circle r="9"/><circle r="4"/>' +
      '<path d="M0 -14 V14 M-14 0 H14 M-9.9 -9.9 L9.9 9.9 M-9.9 9.9 L9.9 -9.9"/>' +
      '<g fill="#0a0f1e"><circle cx="0" cy="-9" r="1.8"/><circle cx="9" cy="0" r="1.8"/><circle cx="0" cy="9" r="1.8"/><circle cx="-9" cy="0" r="1.8"/></g>' +
      '<path d="M-6 20 L0 14 L6 20" stroke="#f59e0b" stroke-width="1.4"/>' +
    '</g>' +
    '<path d="M28 126 Q 80 142 132 126" stroke="#fbbf24" stroke-width="1.4" fill="none" stroke-opacity=".8"/>' +
    '<g stroke="#0a0f1e" stroke-width=".7">' +
      '<path d="M34 127 L44 127 L39 136 Z" fill="#fef3c7"/>' +
      '<path d="M48 131 L58 131 L53 139 Z" fill="#f59e0b"/>' +
      '<path d="M62 133 L72 133 L67 141 Z" fill="#fef3c7"/>' +
      '<path d="M88 133 L98 133 L93 141 Z" fill="#fef3c7"/>' +
      '<path d="M102 131 L112 131 L107 139 Z" fill="#f59e0b"/>' +
      '<path d="M116 127 L126 127 L121 136 Z" fill="#fef3c7"/>' +
    '</g>' +
    '<g transform="rotate(-12 36 116)">' +
      '<rect x="22" y="109" width="28" height="14" rx="2" fill="#0a0f1e" stroke="#f59e0b" stroke-width="1.1"/>' +
      '<path d="M30 109 V123" stroke="#f59e0b" stroke-width="1" stroke-dasharray="1.6 1.6"/>' +
      '<path d="M35 113 H46 M35 117 H44" stroke="#fef3c7" stroke-width="1.2" stroke-linecap="round" opacity=".7"/>' +
    '</g>' +
    '<g fill="#fbbf24"><circle cx="80" cy="17" r="2"/><circle cx="80" cy="143" r="2" transform="translate(0 -4)"/></g>',
    face({
      fill: 'rgba(18,24,36,.66)', stroke: '#f59e0b', sw: 2.6,
      over: '<path d="M48 62 C 58 50 70 45 80 44" stroke="#fef3c7" stroke-opacity=".2" stroke-width="2" stroke-linecap="round" fill="none"/>',
      horns:
        /* the hat IS the horns group, so it inherits the horn wiggle */
        '<g class="imp-cap">' +
          /* three floppy points that hang over the band */
          '<path d="M58 52 C 46 47 36 45 28 49 C 22 52 24 58 31 56 C 40 54 50 57 58 61 Z" fill="url(#st_v14Cap_{uid})" stroke="#4a2a06" stroke-width="1" stroke-linejoin="round"/>' +
          '<path d="M102 52 C 114 47 124 45 132 49 C 138 52 136 58 129 56 C 120 54 110 57 102 61 Z" fill="url(#st_v14Cap_{uid})" stroke="#4a2a06" stroke-width="1" stroke-linejoin="round"/>' +
          '<path d="M72 52 C 70 40 74 28 84 22 C 94 17 104 21 102 28 C 101 34 93 34 88 38 C 82 43 88 50 88 55 Z" fill="url(#st_v14Cap_{uid})" stroke="#4a2a06" stroke-width="1" stroke-linejoin="round"/>' +
          /* band across the forehead, scalloped bottom */
          '<path d="M50 58 C 56 46 104 46 110 58 C 104 51 56 51 50 58 Z" fill="#fef3c7" fill-opacity=".22"/>' +
          '<path d="M50 58 C 56 46 104 46 110 58 L 105 59.5 L 101 56.5 L 96 60 L 91 56.5 L 86 60 L 81 56.5 L 76 60 L 71 56.5 L 66 60 L 61 56.5 L 56 59.5 Z" fill="url(#st_v14Cap_{uid})" stroke="#4a2a06" stroke-width="1" stroke-linejoin="round"/>' +
          '<path d="M55 53 C 62 47 98 47 105 53" stroke="#fef3c7" stroke-width="1.1" fill="none" stroke-opacity=".7"/>' +
          '<g fill="#b45309" opacity=".8"><circle cx="68" cy="54.5" r="1"/><circle cx="80" cy="53.4" r="1"/><circle cx="92" cy="54.5" r="1"/></g>' +
          /* bells */
          '<g fill="#fef3c7" stroke="#4a2a06" stroke-width=".9">' +
            '<circle cx="26" cy="53" r="4.2"/><circle cx="134" cy="53" r="4.2"/><circle cx="104" cy="26" r="4.4"/>' +
          '</g>' +
          '<g fill="#b45309"><circle cx="26" cy="53" r="1.3"/><circle cx="134" cy="53" r="1.3"/><circle cx="104" cy="26" r="1.4"/></g>' +
          '<g stroke="#4a2a06" stroke-width=".8" stroke-linecap="round" opacity=".5">' +
            '<path d="M30 56 L33 58 M130 56 L127 58 M101 30 L98 32"/>' +
          '</g>' +
        '</g>',
      eyes: eyes('#f59e0b', '#ff8fab'),
      grin: grin('#f59e0b')
    })
  );

  /* -------------------------------------------------------------------------
     15 — FROSTBOUND SANCTUARY
     identity: a snow-domed shrine with icicles under the rim and an aurora
     ---------------------------------------------------------------------- */
  var v15 = assemble(
    '<linearGradient id="st_v15Aur_{uid}" x1="0%" y1="0%" x2="100%" y2="100%">' +
      '<stop offset="0%" stop-color="#a8ffd1"/><stop offset="100%" stop-color="#60a5fa"/></linearGradient>' +
    '<clipPath id="st_v15Head_{uid}"><circle cx="80" cy="80" r="41"/></clipPath>',
    /* dome: a shallow ice arch standing on a plinth */
    '<path d="M20 122 C 20 62 44 26 80 24 C 116 26 140 62 140 122 Z" fill="rgba(12,18,30,.82)" stroke="#5b90c9" stroke-width="3.2" stroke-linejoin="round"/>' +
    '<path d="M28 116 C 28 64 50 34 80 32 C 110 34 132 64 132 116" fill="none" stroke="#a8ffd1" stroke-width="1.5" stroke-opacity=".6"/>' +
    '<path d="M36 110 C 36 68 54 42 80 40 C 106 42 124 68 124 110" fill="none" stroke="#a8ffd1" stroke-width="1" stroke-dasharray="2 4" stroke-opacity=".45"/>' +
    /* plinth the dome stands on */
    '<path d="M13 122 H147" stroke="#5b90c9" stroke-width="3"/>' +
    '<path d="M20 122 V132 H140 V122" fill="rgba(12,18,30,.92)" stroke="#5b90c9" stroke-width="2"/>' +
    '<path d="M24 136 H136" stroke="#5b90c9" stroke-width="2" stroke-linecap="round" stroke-opacity=".7"/>' +
    /* snow on the crown */
    '<path d="M62 30 C 66 24 74 22 80 26 C 86 21 95 23 98 29 C 92 27 86 30 80 27 C 74 30 68 27 62 30 Z" fill="#eaf6ff" opacity=".92"/>' +
    '<path d="M48 44 C 52 39 58 38 61 41 C 57 42 53 45 51 49 Z" fill="#eaf6ff" opacity=".7"/>' +
    '<path d="M99 42 C 103 39 108 40 110 43 C 106 43 102 45 100 48 Z" fill="#eaf6ff" opacity=".6"/>',
    /* aurora slowly rolling behind the crown */
    '<path d="M24 58 C 40 36 58 28 80 28 C 102 28 120 36 136 58" stroke="url(#st_v15Aur_{uid})" stroke-width="3.4" fill="none" stroke-linecap="round" opacity=".6"/>' +
    '<path d="M32 52 C 46 34 62 26 80 26 C 98 26 114 34 128 52" stroke="url(#st_v15Aur_{uid})" stroke-width="1.6" fill="none" stroke-linecap="round" opacity=".4"/>' +
    /* icicles hanging off the dome, both sides, well clear of the face */
    '<g fill="#bfe9f7" stroke="#7dd3fc" stroke-width=".8" opacity=".92">' +
      '<path d="M31 82 L34 104 L37 82 Z"/>' +
      '<path d="M40 64 L42.5 80 L45 64 Z"/>' +
      '<path d="M115 64 L117.5 80 L120 64 Z"/>' +
      '<path d="M123 82 L126 104 L129 82 Z"/>' +
      '<path d="M48 52 L50 61 L52 52 Z"/>' +
      '<path d="M108 52 L110 61 L112 52 Z"/>' +
    '</g>' +
    /* snow drifts along the plinth */
    '<path d="M22 122 C 30 118 38 121 46 119 C 54 117 60 121 66 120 L66 122 Z" fill="#eaf6ff" opacity=".8"/>' +
    '<path d="M138 122 C 130 118 122 121 114 119 C 106 117 100 121 94 120 L94 122 Z" fill="#eaf6ff" opacity=".8"/>' +
    /* snowflakes */
    '<g stroke="#d6f2ff" stroke-width="1.3" stroke-linecap="round" opacity=".85">' +
      '<g transform="translate(28 46)"><path d="M0 -6 V6 M-5.2 -3 L5.2 3 M-5.2 3 L5.2 -3"/></g>' +
      '<g transform="translate(132 46)"><path d="M0 -5 V5 M-4.3 -2.5 L4.3 2.5 M-4.3 2.5 L4.3 -2.5"/></g>' +
      '<g transform="translate(132 96)"><path d="M0 -4 V4 M-3.5 -2 L3.5 2 M-3.5 2 L3.5 -2"/></g>' +
      '<g transform="translate(28 96)"><path d="M0 -4 V4 M-3.5 -2 L3.5 2 M-3.5 2 L3.5 -2"/></g>' +
    '</g>',
    face({
      fill: 'rgba(13,19,31,.9)', stroke: '#a8ffd1', sw: 2.5,
      over: '<g clip-path="url(#st_v15Head_{uid})">' +
        '<path d="M46 58 L60 66 L54 80 L68 88 M114 62 L100 70 L108 84 L96 94" stroke="rgba(191,233,247,.26)" stroke-width="1.6" fill="none" stroke-linejoin="round"/>' +
        '<path d="M80 39 C 92 44 102 54 108 68" stroke="rgba(168,255,209,.24)" stroke-width="2" stroke-linecap="round" fill="none"/>' +
        '</g>',
      horns: '<path d="M58 62 L51 32 L67 54 Z M102 62 L109 32 L93 54 Z" fill="#9ff5e2" fill-opacity=".5" stroke="#a8ffd1" stroke-width="1.4" stroke-linejoin="round"/>' +
        '<path d="M56 52 L58 60 M104 52 L102 60" stroke="#eaf6ff" stroke-width="1.2" stroke-linecap="round" opacity=".9"/>' +
        '<path d="M52 34 L56 48 M108 34 L104 48" stroke="#eaf6ff" stroke-width="1" stroke-linecap="round" opacity=".7"/>',
      eyes: eyes('#a8ffd1', '#93c5fd'),
      grin: grin('#a8ffd1')
    })
  );

  /* -------------------------------------------------------------------------
     metadata for the preview page
     ---------------------------------------------------------------------- */
  var THEMES = [
    {
      id: 'default', index: 0, badge: '00', name: 'Default', subtitle: 'Frosted Glass & Aurora',
      palette: { c1: '#38bdf8', c2: '#ec4899', bg: '#0a0f18', side: '#141e30', ink: '#f1f5f9', muted: '#9fb1c1' },
      cues: ['frosted slab', 'aurora ribbons', 'diamond pane'],
      note: 'Same aurora language, tightened: the frost slab now has a glass shine, two ribbons sweep the top and the diamond pane is a real pane instead of a decoration.',
      art: vDefault
    },
    {
      id: 'v1', index: 1, badge: '01', name: 'Neon Obsidian', subtitle: 'In[st]Sanity Gradient Unified',
      palette: { c1: '#f8b250', c2: '#ee2a7b', bg: '#0b0910', side: '#14101d', ink: '#f6efe9', muted: '#a99aa8' },
      cues: ['obsidian shard', 'neon tube horns', 'gradient brackets'],
      note: 'A cut obsidian octagon with glass facets and two shard splinters; the horns are now lit neon tubes with an under-glow. Reads "obsidian" before it reads "badge".',
      art: v1
    },
    {
      id: 'v2', index: 2, badge: '02', name: 'Titanium Slate & Mercury-Mint', subtitle: 'Brushed Gunmetal + Ice-Mint',
      palette: { c1: '#99f6e4', c2: '#2dd4bf', bg: '#0b0f14', side: '#1c252e', ink: '#e6f2f2', muted: '#8fa8ac' },
      cues: ['hex machine plate', 'socket bolts', 'mercury drips', 'chrome horns'],
      note: 'The theme is metal, so the badge became a machined hex plate: six socket bolts, brushed highlights and mercury beading off the bottom edge.',
      art: v2
    },
    {
      id: 'v3', index: 3, badge: '03', name: 'Minimalist Studio', subtitle: 'Linear / Raycast Matte',
      palette: { c1: '#f4f4f6', c2: '#a1a1aa', bg: '#090a0c', side: '#0c0d10', ink: '#f4f4f6', muted: '#7c8496' },
      cues: ['squircle head', 'selection handles', 'pointer cursor', 'toolbar dots'],
      note: 'The head is a squircle now, and the whole badge sits inside a design-tool selection with handles, ruler ticks and a pointer. Studio identity without adding colour.',
      art: v3
    },
    {
      id: 'v4', index: 4, badge: '04', name: 'Crimson Velvet & Gold', subtitle: "Gothic 'Exile' Luxury",
      palette: { c1: '#fbbf24', c2: '#e11d48', bg: '#0d080a', side: '#170e12', ink: '#fbe9d0', muted: '#b08b90' },
      cues: ['lancet arch', 'velvet drapes', 'gilt tracery', 'fleur-de-lis'],
      note: 'A real gothic lancet with trefoil tracery, velvet drapes down both inner edges and a gilt fleur-de-lis under the chin. Gold fangs for the Exile grin.',
      art: v4
    },
    {
      id: 'v5', index: 5, badge: '05', name: 'Royal Amethyst & Silver', subtitle: 'Ultraviolet + Liquid Chrome',
      palette: { c1: '#c084fc', c2: '#e2e8f0', bg: '#090611', side: '#140d24', ink: '#ece7fb', muted: '#9c8fb8' },
      cues: ['jewelled crown', 'chrome shield', 'tilted orbit', 'liquid chrome'],
      note: 'The shield gets a five-point jewelled crown, a chrome orbit cutting behind the head and two liquid-chrome drips on the edges. Purple only where it matters.',
      art: v5
    },
    {
      id: 'v6', index: 6, badge: '06', name: 'Cyber Yellow & Lime', subtitle: 'Electric Yellow + Acid Green',
      palette: { c1: '#facc15', c2: '#a3e635', bg: '#090b05', side: '#131709', ink: '#f7f3c9', muted: '#a3ae7a' },
      cues: ['hazard corners', 'lightning bolt', 'live plug', 'caution tape'],
      note: 'Hazard-striped corners, the bolt promoted to the top, a live power plug and the caution tape kept. X eyes already fit; now the frame agrees with them.',
      art: v6
    },
    {
      id: 'v7', index: 7, badge: '07', name: 'Molten Amber & Copper', subtitle: 'Volcanic Basalt + Solar Gold',
      palette: { c1: '#f97316', c2: '#fde047', bg: '#0d0906', side: '#19100a', ink: '#fde7c4', muted: '#b08a6a' },
      cues: ['basalt octagon', 'erupting caldera', 'lava drips', 'copper pipe'],
      note: 'The amber theme now actually erupts: a basalt octagon with glowing seams, a caldera behind the chin, lava beading from the top edge and a copper elbow pipe.',
      art: v7
    },
    {
      id: 'v8', index: 8, badge: '08', name: 'Abyssal Cobalt', subtitle: 'Midnight Navy + Sapphire',
      palette: { c1: '#60a5fa', c2: '#93c5fd', bg: '#040812', side: '#091224', ink: '#dce9ff', muted: '#7f95b8' },
      cues: ['angler lure', 'porthole rivets', 'bubbles', 'sonar ping'],
      note: 'The abyss gets an anglerfish lure curving off the head with a lit bulb, a riveted porthole ring, rising bubbles and a sonar sweep in the corner.',
      art: v8
    },
    {
      id: 'v9', index: 9, badge: '09', name: 'Cyber Cherry & Cyan', subtitle: 'Gunmetal Arcade Dual-Neon',
      palette: { c1: '#ff2a5f', c2: '#00f0ff', bg: '#090b10', side: '#111520', ink: '#ececf2', muted: '#8890a3' },
      cues: ['CRT scanlines', 'arcade stick', 'coin slot', 'corner screws'],
      note: 'A CRT bezel with scanlines and screws, plus the cabinet controls: a ball-top stick, two buttons and a coin slot. The most literal of the set, on purpose.',
      art: v9
    },
    {
      id: 'v10', index: 10, badge: '10', name: 'Nordic Twilight', subtitle: 'Dusk Periwinkle + Mint-Iris',
      palette: { c1: '#5eead4', c2: '#a78bfa', bg: '#161a28', side: '#1e2436', ink: '#e6ecff', muted: '#96a0bd' },
      cues: ['aurora ribbons', 'fjord + pines', 'branching antlers'],
      note: 'Nordic reads through landscape: aurora ribbons above a fjord with snow caps, small pines in front, and the horns rewritten as branching antlers.',
      art: v10
    },
    {
      id: 'v11', index: 11, badge: '11', name: 'Moss & Brass', subtitle: 'Botanical Green & Antique Gold',
      palette: { c1: '#A98D45', c2: '#8BA64E', bg: '#171b11', side: '#1d2416', ink: '#F0EFDF', muted: '#a7ad8c' },
      cues: ['laurel wreath', 'acorns', 'leaf-antlers', 'brass rings'],
      note: 'Botanical without losing the brass: laurel wreath on both sides, acorns at the base and the horns regrown as leaf-antlers. A sprout crowns the ring.',
      art: v11
    },
    {
      id: 'v12', index: 12, badge: '12', name: 'Cinderforge Kiln', subtitle: 'Brass Soot & Ember',
      palette: { c1: '#d6b46a', c2: '#ff7a18', bg: '#0f0f0f', side: '#1a160f', ink: '#FDF0D6', muted: '#a89472' },
      cues: ['crossed hammers', 'anvil', 'ember sparks'],
      note: 'Kept exactly as it ships — this one already does the job, so it is the benchmark the other fifteen are measured against. No changes.',
      art: v12, status: 'keep'
    },
    {
      id: 'v13', index: 13, badge: '13', name: 'Kelp Cathedral', subtitle: 'Brine Violet & Bloom',
      palette: { c1: '#6df0c8', c2: '#a78bfa', bg: '#0f0f1a', side: '#151422', ink: '#E6E0F5', muted: '#9a92b5' },
      cues: ['lancet window', 'rose window', 'kelp fronds', 'coral bloom'],
      note: 'Cathedral and kelp at once: a lancet window with a leaded rose above the head, kelp fronds climbing both stiles, coral and blooms at the sill.',
      art: v13
    },
    {
      id: 'v14', index: 14, badge: '14', name: 'Wasteland Carnival', subtitle: 'Fairground Gold & Midnight',
      palette: { c1: '#f59e0b', c2: '#fef3c7', bg: '#0a0f1e', side: '#12182e', ink: '#fdf6e3', muted: '#a99a86' },
      cues: ['jester cap + bells', 'bulb ring', 'ferris wheel', 'bunting'],
      note: 'The one theme that wears a hat: a three-point jester cap with bells, a ring of carnival bulbs, sagging bunting and a small ferris wheel.',
      art: v14
    },
    {
      id: 'v15', index: 15, badge: '15', name: 'Frostbound Sanctuary', subtitle: 'Deep Frost & Aurora',
      palette: { c1: '#a8ffd1', c2: '#93c5fd', bg: '#0a0f14', side: '#0e141e', ink: '#d6e2f5', muted: '#8ba0b6' },
      cues: ['shrine dome', 'hanging icicles', 'snowflakes', 'aurora curtain'],
      note: 'The sanctuary becomes a snow-domed shrine: aurora behind the dome, snow on the cap, icicles hanging from the rim and crystal-spike horns instead of the usual zigzag.',
      art: v15
    }
  ];

  root.STICKER_THEMES = THEMES;
})(typeof window !== 'undefined' ? window : globalThis);
