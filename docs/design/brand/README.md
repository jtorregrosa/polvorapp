# PolvorApp brand

The PolvorApp logo (ADR-0015): a two-tongue flame with the ember gradient and the "PolvorApp"
wordmark in Outfit Medium. The files here are the masters; the app uses its own copies
(`frontend/public/`, `PolvorAppMark`, `PolvorAppWordmark`). Overview: [`brand-board.png`](brand-board.png).

## Files

| Path | Use |
|---|---|
| `svg/lockup/polvorapp-horizontal.svg` | **Default logo** on light surfaces |
| `svg/lockup/polvorapp-horizontal-on-dark.svg` | Default logo on the night surface and dark backgrounds |
| `svg/lockup/polvorapp-stacked.svg`, `-on-dark.svg` | Square spaces, splash screens, covers, posters |
| `svg/lockup/*-ink.svg`, `*-white.svg` | One colour: stamps, black-and-white print, white on ember or photos |
| `svg/lockup/polvorapp-wordmark-*.svg` | Wordmark alone, when the flame is already nearby |
| `svg/symbol/polvorapp-symbol.svg` | The flame alone (gradient master): avatars, loading states, merchandise |
| `svg/symbol/polvorapp-symbol-ember.svg`, `-ember-on-dark.svg` | Flat flame in the `primary` token of each theme |
| `svg/symbol/polvorapp-symbol-ink.svg`, `-white.svg` | One-colour flame |
| `svg/symbol/polvorapp-symbol-small.svg` | Small-size cut (wider gaps, softened tips): favicons and 24 px or less |
| `svg/app-icon/polvorapp-app-icon.svg` | App icon: flame on the night tile (PWA 64/192/512) |
| `svg/app-icon/polvorapp-maskable.svg`, `apple-touch-icon.svg` | Full-bleed icons for Android and iOS |
| `svg/app-icon/polvorapp-app-icon-ember.svg`, `-light.svg` | Alternative tiles (ember; light for dark docks) |
| `svg/app-icon/favicon.svg` | Favicon (`frontend/public/icon.svg`) |
| `svg/og-image.svg`, `png/og-image-1200x630.png` | Link preview for messaging apps and social networks |
| `png/` | Raster lockups at 2× and the symbol at 1024 px, for slides and documents |
| `dxf/polvorapp-stacked.dxf` | Stacked lockup for CAD, vinyl or laser cutting (layers `SYMBOL`, `WORDMARK`) |

## Where the app uses each variant

| Place | Variant |
|---|---|
| Night sidebar | Mark + wordmark (horizontal lockup); the mark alone in the icon rail |
| Pages before signing in | Mark + wordmark in the header |
| Browser tab | `icon.svg` (small cut on the night tile) and `favicon.ico` 16/32/48 |
| Installed app | `pwa-64x64.png`, `pwa-192x192.png`, `pwa-512x512.png`, `maskable-icon-512x512.png`, `apple-touch-icon-180x180.png` |
| Repository README | Horizontal lockup, light or dark by the reader's colour scheme |

## Colour

| | Value | Use |
|---|---|---|
| Flame gradient | `#FF9A52` (base) → `#D9480F` (tip), vertical | The mark only; the only gradient in the system |
| Ember | `#B8430B` / `#FF8A4A` | Flat flame in the light / dark theme (`primary` token) |
| Night | `#191524` | App icon tile, sidebar |
| Ink | `#16131F` | Wordmark on light surfaces |

## Rules

- Clear space: half the wordmark's cap height on every side.
- Minimum widths on screen: horizontal 96 px, stacked 64 px, symbol 12 px (small cut).
- Do not stretch, rotate, recolour, add effects or retype the wordmark: use these files.

## Typeface

The wordmark is **Outfit Medium (500)**, © The Outfit Project Authors, SIL Open Font License 1.1
([`licenses/outfit-OFL.txt`](licenses/outfit-OFL.txt), https://github.com/Outfitio/Outfit-Fonts).
It ships as outlines: the app loads no Outfit font file, and its UI faces stay Bricolage Grotesque,
Geist and Geist Mono (ADR-0013).

## Corrections made to the source artwork

The source was a DXF drawing. It was corrected before these masters were made:

1. Outfit keeps overlapping contours, which a DXF fill renders even-odd: P, v, r, A and both p
   showed holes. The wordmark is rebuilt from the Outfit Medium outlines, with nonzero fill.
2. Kerning: r–A tightened (it read "Polvor App"), P–o tightened, l–v and A–p opened.
3. One vertical gradient shared by both flame parts (they had a vertical and a horizontal one).
4. Gradient moved from coral (`#EB7F31 → #E45742`) into the ember family of the tokens.
5. Gap between flame and wordmark in the stacked lockup: 0.31 → 0.6 of the cap height.
6. A zero-length segment removed from the flame.
7. A small-size cut of the flame added for favicons.
