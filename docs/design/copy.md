# Microcopy

The UI ships in **es-ES**, **ca-ES-valencia** and **en** (ADR-0007). Texts live in
`frontend/src/i18n/locales/<language>/{common,ui}.json`; `npm run check-i18n` fails when a key is
missing in any language. Terms follow [glossary.md](../glossary.md).

## Tone

- Short, direct and friendly; the user is a volunteer in a hurry, often on a phone.
- Address the user informally: **tú** in Spanish, **tu** in Valencian; plain "you" in English.
- Say what happened and what to do next: "No se ha podido completar la acción. Inténtalo de nuevo."
- Warnings inform, they do not scold: "Licencia no vigente", not "¡Error de licencia!".
- No jargon, no internal codes (`EXPIRED`), no technical error messages in the UI.

## Conventions

| | es-ES | ca-ES-valencia | en |
|---|---|---|---|
| Buttons | Infinitive: "Guardar", "Eliminar" | Imperative: "Guarda", "Elimina", "Cancel·la" | Imperative: "Save", "Delete" |
| Capitalisation | Sentence case | Sentence case | Sentence case |
| Ellipsis | "Cargando…" (one character) | "Carregant…" | "Loading…" |
| Ranges | "1–10 de 27" (en dash) | "1–10 de 27" | "1–10 of 27" |
| Dates and numbers | Formatted with `Intl` for the active language, never by hand | | |

- Valencian follows the AVL standard in its Valencian forms ("prompte", "esta", "huí", verbs in
  *-ix*). New Valencian texts are flagged in the pull request for review by a native speaker.
- Labels name the thing ("Nombre", "Comparsa"); help text explains it; placeholders are not
  labels.
- Confirm buttons repeat the verb of the dialog title.
- Domain words come from the glossary (*arcabucero*, *comparsa*, *jefe de disparo*, *pedido de
  comparsa*…). Once a Valencian or English equivalent is used in the locale files, keep using the
  same one everywhere; *comparsa* is not translated.
- Screen-reader-only texts (e.g. sort state, alert severity) are translated like any other text.
