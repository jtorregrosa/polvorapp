# Design

## Context

See `proposal.md` for the motivation and the delta specs for the behaviour. The facts below come
from the current code and shape the approach.

- **Sidebar primitive.** `components/ui/sidebar.tsx` is shadcn's sidebar with local edits (see
  `docs/design/README.md`).
  - `AppLayout` renders `<Sidebar>` with the default `collapsible="offcanvas"`: collapsing moves it
    off screen and makes it `inert`.
  - The primitive already supports `collapsible="icon"`: `--sidebar-width-icon` is 3rem,
    `SidebarMenuButton` has a `tooltip` prop hidden unless collapsed, and `SidebarGroupLabel`
    fades in icon mode.
  - Upstream width and offset transitions were removed by the D5 local edit ("the sidebar never
    animates its width or position"). `styles/motion.test.ts` checks primitives for untokenised
    motion.
  - The open state is not persisted: the cookie was removed, and `defaultOpen` is true.
- **Navigation data.** `app/navigation.ts` is a flat list of `NavigationEntry` (`to`, `labelKey`,
  `icon`, `roles`, `count`, `matches`). `AppShell` filters it by role and `AppLayout` renders one
  `SidebarMenu`. The current entry is chosen by `currentNavigationTarget` (the most specific of `to`
  and `matches`), shipped as a fix before this change.
- **Statuses.** `StatusBadge` + `status.ts` map `kind`/`value` to a tone (`success`, `warning`,
  `destructive`, `info`, `muted`) and an icon. There are no categorical tokens. Contrast is checked
  on the tokens by `styles/contrast.test.ts`.
- **Logo uploads.**
  - `ComparsaLogoSection` → `LogoUpload` → `PhotoUpload`, which renders the image and then
    "Replace {{label}}" and "Remove {{label}}" buttons.
  - `FederationLogoSection` uses the same chain.
  - `PhotoUpload` also serves the arquebusier ID and license photos, which stay as they are.
- **Weapon catalogue.** `WeaponModelsPage` sends `includeInactive` only when its checkbox is on. The
  API defaults to active models only, and the order forms rely on that default.

## Research (task 1.1)

Checked against the installed sources, since the cloud session had no Context7 and code search
outside the repository is out of scope there:
- **shadcn sidebar** (`components/ui/sidebar.tsx`, local copy): `collapsible="icon"` sets
  `data-collapsible="icon"` on the `group` element; the gap and container take
  `--sidebar-width-icon`; `SidebarMenuButton` forces `size-8` and `p-2` in icon mode, which D1
  overrides to 40 px. `SidebarMenuButton`'s `tooltip` renders a Radix `Tooltip` whose content is
  `hidden` unless collapsed and not mobile. `SidebarGroupLabel` is a plain `div` (`asChild`
  available), so the `h2` and `aria-labelledby` of D4 are ours to add.
- **Radix Tooltip 1.2.16** (`radix-ui` 1.6.7): the trigger opens on `focus` at once, not after
  `delayDuration`, unless the focus follows a pointer press, and closes on `blur`. The content is a
  `DismissableLayer`, so Escape closes it (WCAG 1.4.13). The provider's 500 ms delay (local edit of
  `redesign-design-system` D5) only applies to hover.
- **Radix DropdownMenu 2.1.24**: on close, `onCloseAutoFocus` focuses the trigger unless the user
  interacted outside. Opening a file chooser or a dialog from a menu item therefore needs the
  trigger ref of D7 to return focus after those close.
- **Tailwind 4.3.3**: `motion-reduce:` compiles to `@media (prefers-reduced-motion: reduce)`.
  `tokens.css` exposes `ease-drawer` as a utility, and its durations are used through the numeric
  utilities (`duration-200` is `--duration-moderate`, `duration-100` is `--duration-fast`), so D3
  needs no custom CSS beyond the reduced-motion override.

No workaround is needed and no question is open.

## Goals / Non-Goals

**Goals:**
- Reuse the primitive's icon mode instead of building a second sidebar.
- Keep the motion rules testable: the one layout animation is tokenised and covered by
  `motion.test.ts`.
- Tags and picture actions are composites with stories, so later screens reuse them (ADR-0009).

**Non-Goals:**
- Changing the mobile drawer.
- Persisting UI preferences on the server.

## Decisions

### D1. Icon rail through `collapsible="icon"`

`AppLayout` passes `collapsible="icon"`.
- The rail width becomes `--sidebar-width-icon: 3.5rem`, so a 40 px button fits with the focus
  ring inside the night surface. Entries keep `min-h-9`, and are 40 × 40 in the rail (WCAG 2.5.8
  needs 24).
- `SidebarMenuButton` gets `tooltip={label}`. The primitive hides the tooltip unless collapsed, and
  Radix opens it on focus as well as hover.
- The link's text stays in the DOM as `sr-only` in rail mode, so the accessible name is unchanged.
  With a counter, the existing `aria-label` already carries "label, N with warnings".
- `SidebarMenuBadge` is replaced in the rail by a 8 px dot (`aria-hidden`).
- The comparsa cards render only their `media` in the rail, with the name as `sr-only` and a
  tooltip.
- `inert` stays only for the off-canvas case, which is now only the mobile sheet.
- On wide screens the top-bar trigger is named after what it does next ("Collapse navigation" /
  "Expand navigation", D9) and drops `aria-expanded`, so its state is not announced twice. The
  phone trigger keeps "Show or hide navigation" with `aria-expanded`, as the drawer is unchanged.
- In the rail the labels stay in the DOM, clipped by the 40 px button and faded with opacity, so
  each link keeps its name; the counter badge is replaced by the dot only while the rail shows
  (a collapsed state on a phone still shows the drawer with its badges).

*Alternative*: a custom rail component. Rejected, because it would duplicate the primitive's
keyboard shortcut, state and tooltip wiring.

### D2. Remembered state in `localStorage`

A local edit in `SidebarProvider` reads `polvorapp.sidebar` (`expanded` | `collapsed`) once for
`defaultOpen` and writes it on change. Access is wrapped in `try`, falling back to expanded. It is a
per-device preference that holds no personal data, so it needs no consent banner (functional
storage) and no server round-trip.

*Alternative*: the upstream cookie. Rejected, because the API never needs the value and cookies
travel with every request.

### D3. Sidebar motion: one tokenised exception

The upstream `transition-[left,right,width]` on the gap and the fixed container is restored with
`duration-moderate` (200 ms) and `ease-drawer`. `--duration-moderate` and `--ease-drawer` already
exist in `tokens.css`.
- Labels, the counter badge, the group labels and the API version use `opacity` with
  `duration-fast` (100 ms). The opacity is keyed off `data-state`, with a delay so they fade out
  before the rail narrows and in after it widens. This avoids wrapping during the animation.
- Under `prefers-reduced-motion: reduce`, `globals.css` sets the width transition to `none` and
  keeps a 100 ms opacity fade, as for other components.
- The Ctrl/Cmd+B handler is unchanged; the transition applies to it too, as the spec now allows.
- `motion.test.ts` keeps the rule "no layout transitions in primitives" with one allow-listed class
  list in `sidebar.tsx`, marked `Local edit (refine-navigation-and-lists D3)`.
- The `docs/design/README.md` note "the sidebar never animates its width or position" is replaced.
- *As implemented*: nothing else changes size while the width moves, so the content does not jump.
  Entries are 40 px with the same padding expanded and in the rail; the mark keeps its padding; the
  section labels give way to a line in their own slot instead of collapsing with a negative margin
  (the separators between sections are dropped for it); labels do not wrap while
  `data-moving` is set on the wrapper (200 ms after each toggle). The FiringChief's comparsa cards
  drop their padding at once in the rail, and the API version disappears at once (it takes no
  room in the rail); the trigger exposes the shortcut with `aria-keyshortcuts`. Tooltips are
  controlled and closed outside the rail, so an expanded entry or a drawer link gets no repeated
  description and Escape closes the drawer at once. The labels share one class
  list (`SIDEBAR_LABEL` in `AppLayout.tsx`), checked by `motion.test.ts`; reduced motion removes
  the width transition and the labels' delay in `globals.css`.

*Alternative*: animate with a `transform` on the sidebar and keep the content jump. Rejected,
because the content would snap while the sidebar slid, which feels worse than animating both.

### D4. Navigation sections as data

`NavigationEntry` gains a `section: 'home' | 'registry' | 'festival' | 'administration'`.
- `NAVIGATION` keeps the order in the spec.
- `AppShell` groups the filtered entries by section and passes `sections: { labelKey?, items }[]`
  to `AppLayout`, so a section left empty by the role filter disappears.
- `AppLayout` renders a `SidebarGroup` per section, with a `SidebarGroupLabel` naming it through
  `aria-labelledby` on `role="group"`. *As implemented*: the label is not a heading (three `h2`s
  before every page's `h1` were heading noise, a11y review), and in the rail it gives way to a line
  in its own slot instead of separate `SidebarSeparator`s (see D3).
- The Admin-only `roles` on the administration entries stay as they are; the section only groups
  them.

### D5. `Tag` composite and categorical tokens

`components/app/Tag.tsx` takes `{ tone: 'neutral' | 1 | 2 | 3 | 4; children }`.
- It renders a `span` with the same metrics as `StatusBadge` and no icon.
- New tokens `--tag-{1..4}` and `--tag-{1..4}-foreground` (light and dark), plus `--tag-neutral*`
  reusing `muted`, are added to `contrast.test.ts` at 4.5:1. Hues are chosen away from the semantic
  greens, ambers, reds and blues: violet, teal, rose and slate-indigo, so a tag never reads as a
  status.
- Fixed mappings live in `components/app/tags.ts`, beside `status.ts`, so each value keeps its tone
  everywhere:
  - `Side`: `MOORISH` → 3, `CHRISTIAN` → 1;
  - `UserRole`: `ADMIN` → 2, `FIRING_CHIEF` → 4;
  - `WeaponKind`: `TRABUCO` → 1, `ARCABUZ` → 2, `PISTOL` → 4;
  - yes/no flag: yes → 2, no → neutral.

  A `CategoryTag` helper `({ category, value })` translates `ui:tag.<category>.<value>`.

*Alternative*: extend `StatusBadge` with neutral kinds. Rejected, because statuses carry icons and
semantic colours by spec ("Status semantics").

### D6. Two-step verification as a status

`status.ts` gains `twoFactor: { ENABLED: success, NOT_SET: muted }`, with icons `ShieldCheck` and
`ShieldOff`. The users page maps `twoFactorEnabled` to these values. The `users.enabled` and
`users.disabled` texts move to `ui:status.twoFactor.*`.

### D7. Picture actions in `PhotoUpload`

`PhotoUpload` gains `variant: 'section' | 'picture'`, defaulting to `'section'`, which keeps the
arquebusier photos unchanged.
- In `'picture'`:
  - the image (or placeholder) is wrapped in a `Button`-styled trigger;
  - with an image, the trigger opens a `DropdownMenu` (shadcn primitive, already used by
    `UserMenu`) with "Replace" and "Remove";
  - without an image, the trigger opens the file chooser directly.
- The overlay is a `Pencil` icon in a small opaque `card` chip at the top right of the picture,
  shown on `group-hover` and `group-focus-visible` with a `duration-100` opacity fade. *Implemented
  as a chip instead of the translucent scrim first planned*: a scrim needs an opacity modifier on a
  contrast token, which the ESLint guardrails forbid, and the chip keeps the picture visible.
- The trigger lives in `components/app/picture-trigger.tsx`, a helper of `PhotoUpload` (kept out of
  the catalogue like `clear-field.tsx`), so `PhotoUpload.tsx` stays under the 800-line ceiling.
- After the crop dialog, the confirmation or the menu closes, focus is returned to the trigger
  through a ref.
- Announcements reuse the existing live region.
- `LogoUpload` passes `variant="picture"`. Both logo sections drop their own buttons.

*Alternative*: a hover-only overlay with buttons. Rejected, because it fails on touch and is not
discoverable by keyboard (WCAG 2.1.1, 1.4.13).

### D8. Weapon catalogue default

`WeaponModelsPage` reads `?onlyActive=true`.
- Without it, the page sends `includeInactive=true`.
- With it, the page sends no flag, so the API default applies.
- The checkbox label becomes "Only active".
- No API change: the default stays "active only" for every other caller (order forms, edition
  rental models), which the spec keeps.

### D9. i18n keys (es-ES, ca-ES-valencia, en)

- `ui`:
  - `nav.sections.registry`, `nav.sections.festival`, `nav.sections.administration`;
  - `nav.expand`, `nav.collapse` (the trigger's name follows the state);
  - `status.twoFactor.ENABLED`, `status.twoFactor.NOT_SET`;
  - `tag.side.*`, `tag.role.*`, `tag.weaponKind.*`, `tag.yesNo.YES`, `tag.yesNo.NO`;
  - `pictureActions.options` ("{{label}}, options"), `pictureActions.replace`,
    `pictureActions.remove`, `pictureActions.add`.
- `catalog`: `weaponModels.filters.onlyActive` replaces `includeInactive`.
- The labels that become tags move to `ui:tag.*`, so lists and details share them:
  `catalog:side.*`, `catalog:kind.*`, `identity:roles.*` and `weaponModels.yes`/`no`. Forms keep
  their own option labels.

## Risks / Trade-offs

- [The width animation reflows the page for 200 ms, which can jank on big tables] → It runs only on
  an explicit toggle, never on navigation. Reduced motion turns it off. It is checked on the
  arquebusier list with 450 rows in the Playwright run.
- [A tag tone could still be mistaken for a status] → No icon, non-semantic hues, and the label is
  always present. The design guide documents when to use which.
- [`localStorage` can be blocked, e.g. in private mode] → Reads and writes are wrapped. The sidebar
  falls back to expanded.
- [The tooltip covers content near the rail] → It opens to the right, like the primitive's default,
  and closes on Escape (WCAG 1.4.13).

## Migration Plan

Frontend only, no data. Deploying is enough. Rollback is reverting the change. The stored
preference is ignored by older builds.
