# Proposal

## Why

After the MVP sequence, the maintainer reviewed the screens and asked for several changes to the
navigation and the lists:
- Collapsing the sidebar hides it completely. The maintainer wants it to keep its icons, and wants
  the sidebar to animate.
- Orders and Distribution belong to the current edition but sit as loose top-level entries.
- Fixed values (roles, sides, weapon kinds, yes/no flags) read as plain text where a badge would
  scan faster.
- Replacing or removing a logo takes two extra buttons under the image.
- The weapon catalogue hides inactive models unless asked.

These choices contradict decisions recorded in the specs: the sidebar never animates (design D5 of
`redesign-design-system`), and the weapon list shows only active models by default. So they need a
change rather than a fix.

Capabilities (from `docs/mvp.md`): **`platform`** (application shell), **`design-system`** (motion,
tags, data tables, logo picture actions), **`federation-catalog`** (weapon list, logo screens) and
**`identity-access`** (users list). It follows ADR-0009 (composites only) and ADR-0013 (the
"Pólvora" identity, night sidebar), NFR-01 (360 px) and NFR-07 (WCAG 2.2 AA). It changes no business
rule.

## What Changes

- **Sidebar collapses to an icon rail** on wide screens instead of disappearing. Each entry keeps
  its icon, with its name as a tooltip and as its accessible name. Counters become a dot with the
  count in the name, and the FiringChief's comparsa cards become their logos. The chosen state is
  remembered on the device. Phones keep the drawer.
- **Sidebar motion**: expanding and collapsing animates the sidebar's width and the content beside
  it over 200 ms. This is an explicit exception to "never animate layout", for this one element. With
  reduced motion it changes at once. The current-item bar and hover states fade.
- **Grouped navigation** with labelled sections:
  - *Home*;
  - *Registry*: Arquebusiers, Comparsas, Statistics;
  - *Festival*: Editions, Orders, Distribution. Orders and Distribution are now explicitly part of
    the edition flow and stay marked on an edition's own orders, exports and distribution pages;
  - *Administration* (Admins): Weapon models, Users, Audit log, Privacy.
  In the icon rail, section names give way to separators.
- **Tags for fixed values**: a new `Tag` composite for categorical values that are not statuses. It
  shows a translated label, a categorical tone from new tokens, and no semantic colour. It is used
  for roles, sides, weapon kinds and the rentable flag. Two-step verification becomes a status:
  `ENABLED` → success, `NOT_SET` → muted.
- **Links inside rows**: a cell MAY hold its own link, e.g. the comparsa of an arquebusier. That
  link opens its own target, and the rest of the row still opens the record. This records the
  behaviour already shipped as a fix on the arquebusier list.
- **Logo picture actions**: the comparsa logo and the Federation logo are managed from the picture
  itself. The picture is a button that opens a menu (replace, remove); without a logo, it is an
  "Add logo" button. A hover and focus overlay hints at it. No separate replace or remove buttons
  remain. Keyboard and touch work the same as the pointer.
- **Weapon catalogue lists every model by default**, active and inactive. A "Only active" filter
  replaces "Include inactive". The API default is unchanged (`includeInactive` false), so the order
  forms still get only active models.

## Non-goals

- A full UI/UX review of each screen (the distribution page above all). The maintainer will run a
  dedicated session for that.
- Changing the comparsas list default (it keeps showing only active comparsas).
- Nested or collapsible navigation sections, favourites, or user-ordered navigation.
- Animating route changes, tables, filters or anything else the Motion requirement excludes.
- New statistics or a settings page (separate changes: `add-statistics-trends`,
  `add-federation-settings`).
- Changing the photo screens of arquebusiers (ID and license photos keep their section).

## Capabilities

### New Capabilities
- none.

### Modified Capabilities
- `platform`: requirement "Application shell". The collapsible icon rail, remembered state, grouped
  navigation with section labels, and the current entry matching an edition's sub-pages.
- `design-system`:
  - "Motion": the sidebar width exception and its reduced-motion behaviour;
  - "Data tables": links inside a row;
  - "Status semantics": two-step verification;
  - new requirement "Tags for fixed values";
  - new requirement "Picture actions".
- `federation-catalog`:
  - "Weapon catalogue access": the UI shows every model by default and badges the fixed values;
  - "Logo display" and "Federation logo": actions on the picture;
  - "Comparsa visibility (BR-12)": the side as a tag.
- `identity-access`: requirement "User management by Admins". Role as a tag, two-step verification
  as a status, every column sortable.

## Impact

- **Frontend**:
  - `components/ui/sidebar.tsx`: local edit for icon collapse and motion;
  - `components/app/`:
    - `AppLayout`: groups, rail, tooltips;
    - new `Tag` composite with stories;
    - `StatusBadge` (`twoFactor` kind);
    - `PhotoUpload` (picture-menu variant);
    - `DataTable` (documents inner links);
  - `app/navigation.ts` (sections);
  - `styles/tokens.css` (categorical tag tokens, sidebar motion tokens) and `motion.test.ts`;
  - federation-catalog and identity-access pages;
  - i18n (three locales).
- **Backend**: none. The API already has `includeInactive`.
- **Docs**: `docs/design/README.md` (supersedes the "never animates" sidebar note of D5),
  `docs/design/patterns.md` (navigation sections, tags, picture actions), `docs/design/tokens.md`.
- **Security and GDPR**: no change. The navigation stays role-filtered on the client, and access
  stays enforced on the server.
