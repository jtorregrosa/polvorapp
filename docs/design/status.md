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
| `DRAFT` | muted | CircleDashed | En preparación | En preparació | In preparation |
| `IN_PROGRESS` | success | CirclePlay | En curso | En curs | In progress |
| `CLOSED` | muted | Archive | Cerrada | Tancada | Closed |

## `participation`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `FIRST_YEAR` | info | Sparkles | Primer año | Primer any | First year |

## `orders`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `OPEN` | success | CircleCheck | Pedidos abiertos | Comandes obertes | Orders open |
| `CLOSED` | info | Lock | Pedidos cerrados | Comandes tancades | Orders closed |

## `billing`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `PROVISIONAL` | info | Clock | Provisional | Provisional | Provisional |
| `FINAL` | success | CircleCheck | Definitivo | Definitiu | Final |

## `user`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `INVITED` | info | Send | Invitado | Convidat | Invited |
| `ACTIVE` | success | CircleCheck | Activo | Actiu | Active |
| `DEACTIVATED` | muted | CircleX | Desactivado | Desactivat | Deactivated |

## `catalog`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `ACTIVE` | success | CircleCheck | Activo | Actiu | Active |
| `INACTIVE` | muted | CircleDashed | Inactivo | Inactiu | Inactive |

## `proxy`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `NOT_APPLICABLE` | warning | TriangleAlert | El titular ya no recoge | El titular ja no recull | Holder no longer collects |
| `LICENSE_INVALID` | warning | TriangleAlert | Licencia no válida ese día | Llicència no vàlida eixe dia | License not valid on the day |

## `warning`

| Value | Tone | Icon | es-ES | ca-ES-valencia | en |
|---|---|---|---|---|---|
| `LICENSE_MISSING` | warning | TriangleAlert | Sin licencia | Sense llicència | No license |
| `LICENSE_PENDING` | warning | TriangleAlert | Licencia en trámite | Llicència en tràmit | License pending |
| `LICENSE_EXPIRED` | warning | TriangleAlert | Licencia caducada | Llicència caducada | License expired |
| `LICENSE_EXPIRING` | warning | TriangleAlert | Licencia caduca pronto | Llicència caduca prompte | License expiring soon |
| `COURSE_MISSING` | warning | TriangleAlert | Sin curso | Sense curs | No course |
| `UNDER_AGE` | warning | TriangleAlert | Menor de edad | Menor d'edat | Under 18 |
| `ID_PHOTO_MISSING` | warning | TriangleAlert | Sin foto de carnet | Sense foto de carnet | No ID photo |
| `LICENSE_PHOTOS_MISSING` | warning | TriangleAlert | Sin fotos de la licencia | Sense fotos de la llicència | No license photos |
