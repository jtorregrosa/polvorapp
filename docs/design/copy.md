# Microcopy

The UI ships in **es-ES**, **ca-ES-valencia** and **en** (ADR-0007). Texts live in
`frontend/src/i18n/locales/<language>/<namespace>.json` (`common` for the shell, `ui` for
composites, one namespace per feature such as `identity`); `npm run check-i18n` fails when a key is
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

## Forms

Forms follow the "Registro" rules (design D8 of `redesign-design-system`): every field is
required unless its label says otherwise, and errors are shown when the form is sent.

| | es-ES | ca-ES-valencia | en |
|---|---|---|---|
| Note above a form | "Todos los campos son obligatorios salvo los marcados como opcionales." | "Tots els camps són obligatoris excepte els marcats com a opcionals." | "All fields are required unless marked as optional." |
| Optional label | "Teléfono (opcional)" | "Telèfon (opcional)" | "Phone (optional)" |
| Error summary title | "Hay un problema" | "Hi ha un problema" | "There is a problem" |

- No asterisks: the note and the "(optional)" suffix say it in words.
- An error message says what to do, in the words of the label: "Introduce la fecha de
  nacimiento", "Elige una opción", "La letra no corresponde a los números". Never "Campo
  inválido" or a code.
- The error summary lists the same messages as the fields, each starting with the field's label,
  in the order of the form.
- A refusal that concerns no field (the server's reason) goes in the summary too, never in a
  banner far from the action.
- A value not given in a read-only section shows "No consta" / "No consta" / "Not given".

## Notices

- A saved section says "Cambios guardados" / "Canvis desats" / "Changes saved", without a
  closing full stop and without moving focus.
- The outcome of an action names its object and the new state in one sentence, in the past or
  present: "Arcabucero García está ahora en Reserva.", "Comparsa desactivada.".
- A count after filtering is a phrase, not a bare number: "12 arcabuceros", "1 comparsa".
- A confirmation title asks with the action and its object ("¿Trasladar a Ana Pérez a Comparsa
  Sur?"); its confirm button repeats the verb ("Trasladar"); its description says what cannot be
  undone or who is affected, and never claims what is not known yet.
