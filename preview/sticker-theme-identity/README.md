# Sticker identity pass — preview only

**Nothing in the app was changed.** This folder is a standalone, offline preview. No file under
`src/` was edited. It exists so the new art can be judged before a single line goes into
`THEME_STICKER_SVGS`.

**Rev 9** — 09's glitch is rebuilt from scratch as a slice-displacement tear: three horizontal bands
cut the rings, the cut content slides sideways inside its band, cyan/magenta fringes mark the tear
lines and a chroma copy of the rings jumps behind. It is invisible at rest and runs for about half a
second every 6 s. Everything else is unchanged from rev 6/7.

```
preview/sticker-theme-identity/
├─ index.html       the gallery — open this file in any browser
├─ stickers.js      the 16 proposed badges + palettes/copy for the page
├─ current.js       a verbatim copy of the stickers shipping today (for the A/B corner badge)
└─ README.md        this file
```

`index.html` works straight from disk (`file://`) — no server, no build step, no dependencies.

**Single-file download** — `sticker-preview.html` is the whole preview in one file (the two scripts
inlined). `serve.py` answers `/download` (also `/download/<anything>` and `?download=1`) with that
file as an `application/octet-stream` attachment, so a plain link saves it instead of rendering it.
The page carries a "Download this preview (.html)" button in the bottom-right corner; inside a
sandboxed preview frame, where browsers block downloads, it opens the link in a new tab and otherwise
shows a panel with the URL to copy.

---

## What the page gives you

- **In context** — the real sidebar geometry (244 px rail, 177 px sticker slot) painted with the
  selected theme, so the badge is judged where it actually lives.
- **All sixteen** — default + themes 1–15, each with its palette, the identity hooks it now carries
  and a one-line note on what changed.
- **Split view only** — every card is new art on the left, the sticker shipping today on the right,
  and both halves animate.
- **Copy SVG** — copies the exact markup with unique gradient ids, ready to paste into the app.
- **Live behaviour** — the same motion the app runs (bounce, aura, blink, smirk, horn wiggle) plus
  eye tracking, so nothing in the new art breaks under animation.
- Sizes 132–248 px to check the badge at the theme-picker thumbnail and at the 310 px background
  watermark.

---

## The design rule used for all sixteen

The imp's face stays the imp's face. Identity comes from three layers, so a badge is still readable
at 96 px in the picker:

1. **silhouette** — the outer frame is now part of the theme (arch, shield, hex, slab, CRT, dome…)
2. **props** — `imp-decor` carries the noun the theme is named after
3. **head/horns** — several themes swap the head shape or regrow the horns

| # | Theme | Status | What it carries now |
|---|-------|--------|---------------------|
| 00 | Default — Frosted Glass & Aurora | **improved** | the shipping sticker itself: rounded plate, aurora wash, diamond, bottom ribbons and four sparkles — the ribbons are now three layers of light, the diamond gets a faint inner facet and the plate catches one soft highlight |
| 01 | Neon Obsidian | **revised** | four matching neon chevrons (stray shards gone), live equaliser bars (`imp-eq-bar`), fangs in the theme gradient |
| 02 | Titanium Slate & Mercury-Mint | **revised** | bigger hex plate — border out to the frame edge, heaviest stroke of the set, bolts pushed to match — plus **mint sprigs** growing up both lower corners (four leaves each); original thin zig-zag horns in mercury-mint |
| 03 | Minimalist Studio | **revised** | round head, original horns; selection frame, handles and ticks kept — pointer removed |
| 04 | Crimson Velvet & Gold | **revised** | lancet arch, tracery, fleur-de-lis with the **original** crown; drapes rebuilt as one clean velvet panel per side |
| 05 | Royal Amethyst & Silver | **revised** | new chrome shield, orbit, drips and chrome horns, with the **old** crown |
| 06 | Cyber Yellow & Lime | **revised** | hazard corners, caution tape and a **thunder hit that picks its own spot**: three strike points (right shoulder, left shoulder, crown), one chosen at random every 1–3 s. Nothing sits on the sticker at rest — bolt, flash, star and ring exist only for the hit (`imp-strike.is-hit`, `imp-thunder*`) |
| 07 | Molten Amber & Copper | kept | basalt octagon, caldera, lava beads, brick kiln |
| 08 | Abyssal Cobalt | **revised** | original scalloped shell and wakes restored + angler lure, bubbles, new head treatment |
| 09 | Cyber Cherry & Cyan | **revised** | original dashed rings, crosshair and dots kept, plus a **slice-displacement glitch** — three bands cut the art, their contents slide sideways (masked so they never cross the face), cyan/magenta fringes mark the tears, and a chroma copy of the rings jumps behind. Invisible at rest, alive for ~0.5 s every 6 s (`imp-glitch-band-a/b/c`, `imp-glitch-shift`, `imp-glitch-ghost`, `imp-glitch-px`) |
| 10 | Nordic Twilight | kept | aurora ribbons, snow-capped fjord, layered firs, antlers |
| 11 | Moss & Brass | kept | laurel wreath, leaf-antlers, acorns, brass rings |
| 12 | Cinderforge Kiln | **improved** | hammers sit at their **shipping start position** (top corners, heads out) and swing at each other from there, **clashing at random every 1–3 s** (`imp-hammer-l`, `imp-hammer-r`, `imp-clash`); banded heads, anvil with horn and billet, coal bed, drifting embers |
| 13 | Kelp Cathedral | **improved** | the shipping circle kept — long-dash outer ring, dotted inner ring, three faceted crystals joined by sigil lines — with **demon horns** rooted lower on the head so they sit closer to the eyes: thick at the root, swept out and back, sharp at the tip, mint-lit along the edge |
| 14 | Wasteland Carnival | **rebuilt** | striped big top, scalloped valance, pennant, bunting, high striker, popcorn, harlequin head + bow tie, the **original head colour** (#121824) at full opacity so nothing shows through it, and the **original** horns |
| 15 | Frostbound Sanctuary | **improved** | the shipping shrine unchanged at its core — arched frost frame, stepped jambs, mint hairline arch, dashed inner arch, columns, floor bars, finial dot, **original head fill and both eye colours** — with **runes** carved into it: three under the arch and two down each jamb, lit from behind, plus a frost bloom off the floor |

Every card is a split view against the shipping sticker — including the ones marked
*kept* — new art on the left, today's sticker on the right, and **both** halves are animated.

Theme 12 was left alone on purpose: it set the bar for the other fifteen.

---

## The anatomy contract (why this can drop in later)

Every proposal keeps the class names that the rest of the app drives:

```
.imp-outer-frame          frame + its own animation
.imp-decor                themed props
.imp-face-group
  .imp-head-circle        aura pulse
  .imp-horns               horn wiggle (in theme 14 this IS the jester cap)
  .imp-eyes-track
    .imp-eyes-blink       blink loop
  .imp-grin               smirk loop
```

Eye tracking, blinking, the click-dodge reactions, the leave prompt and the JSON-throw cinematics
all locate the sticker by those classes, so none of them need to change.

A few optional micro-animations were added in rev 2 and rev 4, all plain CSS:

```css
.imp-eq-bar          /* theme 01 — the equaliser strips level (scaleY, staggered) */
.imp-glitch          /* theme 09 — container for the whole tear layer */
.imp-glitch-band-a/b/c  /* theme 09 — the three cut bands, on only during the burst */
.imp-glitch-shift    /* theme 09 — the cut content, sliding sideways inside its band */
.imp-glitch-px       /* theme 09 — stray pixels, flickering */
.imp-glitch-ghost    /* theme 09 — offset chroma copy of the rings, only mid-burst */
.imp-ember           /* theme 12 — embers drift up off the forge */
.imp-strike          /* theme 06 — one of three strike spots, hidden at rest */
.imp-thunder         /* theme 06 — the bolt, visible only while .is-hit is on */
.imp-thunder-flash   /* theme 06 — impact star + sparks */
.imp-thunder-ring    /* theme 06 — shock ring expands and fades */
.imp-hammer-l/-r     /* theme 12 — the two hammers (transform-origin at the grip) */
.imp-clash           /* theme 12 — spark where the heads meet */
```

Two tiny drivers in `index.html` supply the randomness, because "at a random place every 1–3 s" is
not something CSS can express: `fireThunder()` picks one `.imp-strike` and adds `.is-hit` for
560 ms, `fireClash()` adds `.clash` for 800 ms. Both re-arm with `1000 + Math.random() * 2000`.
Without them the stickers simply rest — nothing breaks.

If those classes are absent, nothing breaks — the strips and embers simply stay still.

Gradient ids follow the app's convention (`st_<theme>…_{uid}`), so a copy-paste into
`THEME_STICKER_SVGS` cannot collide across the two sticker instances (sidebar + background).

## If you decide to wire one in

`resources/Raw/wwwroot/index.html` → `THEME_STICKER_SVGS` → replace that theme's template string.
Two things the app already handles, worth knowing:

- `decorateStickerEyeGlow()` injects the eye halo into `.imp-eyes-track` — the art must keep that
  group, which all of these do.
- The boot overlay flattens `url(#…)` paints via `lsFlatten()` before reuse; solid fills are safest
  there, and the gradients above are only used for strokes/fills that read fine flattened.

Say which ones survive and they can be wired in one theme at a time, all themes, or just the heads.
