import { createCn } from 'cn/config';

/**
 * Class-name merging that knows the design tokens (src/styles/tokens.css). The default merge
 * takes an unknown `text-*` for a colour, so `cn('text-label', 'text-foreground')` would drop the
 * type role; the token scales are declared here so roles, spacing and shadows merge correctly.
 * Keep it in step with the `@theme` block of tokens.css (`lib/cn.test.ts` checks it).
 */
export const cn = createCn({
  extend: {
    theme: {
      text: ['page', 'record', 'section', 'figure', 'body', 'label', 'help', 'id'],
      spacing: ['field', 'group', 'section', 'region', 'gutter', 'control', 'row', 'topbar', 'action-bar'],
      container: ['page', 'form', 'prose', 'field-id', 'field-short', 'field-name', 'field-long'],
      shadow: ['e1', 'e2'],
      ease: ['drawer'],
      breakpoint: ['wide'],
    },
  },
});
