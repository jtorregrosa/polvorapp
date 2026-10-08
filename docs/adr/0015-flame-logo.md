# 0015. Flame logo

- Status: Accepted (the maintainer supplied the final logo and asked to adopt it, 2026-10-08)
- Date: 2026-10-08
- Supersedes: [0013](0013-polvora-visual-identity.md), the "P" mark only

## Context

ADR-0013 gave PolvorApp its own identity with an original "P" monogram and an ember spark, drawn
as a placeholder inside the design system. The maintainer has since commissioned a final logo:
a two-tongue flame ("pólvora") with an orange gradient above the name "PolvorApp". It arrived as
a DXF drawing with defects: the letters were made of overlapping contours that a DXF fill turns
into holes, the r–A pair was too open, the two flame parts carried different gradients, and its
coral gradient sat outside the ember accent. ADR-0013 requires a new ADR to change the identity.

## Decision

- The **flame is the PolvorApp mark** and replaces the "P" monogram everywhere: sidebar, public
  header, favicon, installed-app icons and documentation.
- The **wordmark** is "PolvorApp", with capital P and A, set in **Outfit Medium (500)** (OFL-1.1),
  kerned by hand and shipped as outlines only. Outfit is not loaded as a UI font.
- The **corrected masters** and every variant live in `docs/design/brand/`: horizontal (default)
  and stacked lockups, the symbol, one-colour versions, a small-size cut for favicons, app icons,
  a link-preview image and a DXF for print. `docs/design/brand/README.md` says where each goes.
- The flame keeps **one vertical gradient**, `#FF9A52` → `#D9480F`, in the ember family of the
  tokens. It is the only gradient in the system (ADR-0013's "no gradients except the mark"). The
  flat flame uses the `primary` token of each theme.
- In the app, `PolvorAppMark` draws the flame and `PolvorAppWordmark` draws the letters in the
  current text colour, with the translated product name for assistive technology. The PWA icons
  are hand-made files in `frontend/public/` instead of being generated from one SVG, because the
  favicon needs the small-size cut and the app icons need the night tile.
- Everything else in ADR-0013 stands: fonts, palette, night sidebar, ember accent, and no logos or
  brand assets of the Federation or any other organisation in the repository.

## Consequences

- The product has a finished, recognisable logo that matches its name and the ember accent.
- The logo is a fixed drawing: changing it means editing the masters in `docs/design/brand/` and
  copying them to the app (the components and `frontend/public/`), not changing a font or a token.
- The PWA icons no longer regenerate on build; `@vite-pwa/assets-generator` is removed.
- Printed documents keep their current layout; adding the logo to them would be a change to the
  exports spec.

## Alternatives considered

- **Keeping the "P" monogram**: it was a placeholder, and the maintainer chose the flame.
- **Using the DXF as delivered**: the holes in the letters and the uneven spacing show at every
  size.
- **Setting the wordmark in Bricolage Grotesque** to reuse the display face: it changes the
  maintainer's chosen logo, and the brand rule is never to retype the wordmark.
- **Lower-case "polvorapp"**: friendlier, but it reads as one run of letters ("pólvora-pp"), and
  the name is written "PolvorApp" everywhere else.
- **Generating the icons from the favicon SVG** (`pwaAssets`): one source image cannot give both
  the small-size favicon and the padded app icons.
