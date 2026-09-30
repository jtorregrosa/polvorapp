<!-- Generated from the code by frontend/src/styles/design-docs.test.ts: run `npm run docs:design` in `frontend/`. Do not edit by hand. -->

# Status semantics

The single mapping from domain statuses (glossary code terms) to tone, icon and label lives in
`frontend/src/components/app/status.ts` and is rendered by `StatusBadge`. A status is never
shown by colour alone: the pill always carries its icon and translated label. Compliance
warnings (BR-04) use the warning tone, never `destructive`. A value outside the mapping renders a
neutral pill with the raw code and a development-only console warning.

## `license`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `VALID` | success | CircleCheck | Vigente | Vigent | Valid |
| `EXPIRING` | warning | TriangleAlert | Caduca pronto | Caduca prompte | Expiring soon |
| `EXPIRED` | destructive | CircleX | Caducada | Caducada | Expired |
| `PENDING` | info | Clock | En trámite | En tràmit | Pending |

## `arquebusier`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `ACTIVE` | success | CircleCheck | Activo | Actiu | Active |
| `RESERVE` | muted | CircleDashed | Reserva | Reserva | Reserve |

## `order`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `DRAFT` | muted | CircleDashed | Borrador | Esborrany | Draft |
| `SUBMITTED` | info | Send | Enviado | Enviat | Submitted |
| `RETURNED` | warning | Undo2 | Devuelto | Retornat | Returned |
| `VALIDATED` | success | CircleCheck | Validado | Validat | Validated |

## `edition`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `DRAFT` | muted | CircleDashed | Borrador | Esborrany | Draft |
| `ORDERS_OPEN` | success | CircleCheck | Pedidos abiertos | Comandes obertes | Orders open |
| `CORRECTIONS_OPEN` | warning | TriangleAlert | Correcciones abiertas | Correccions obertes | Corrections open |
| `LOCKED` | info | Lock | Bloqueada | Bloquejada | Locked |
| `CLOSED` | muted | CircleDashed | Cerrada | Tancada | Closed |

## `warning`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `LICENSE` | warning | TriangleAlert | Licencia no vigente | Llicència no vigent | License not valid |
| `COURSE` | warning | TriangleAlert | Sin curso | Sense curs | No course |
| `AGE` | warning | TriangleAlert | Menor de edad | Menor d'edat | Under age |
