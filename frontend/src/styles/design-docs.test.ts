/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { STATUS_MAP } from '@/components/app/status';
import { createI18n } from '@/i18n';
import { SUPPORTED_LANGUAGES } from '@/i18n/config';
import { renderStatusDoc, renderTokensDoc } from './design-docs';

// The generated pages of the design guide must match the code (design D12). After changing the
// tokens or the status mapping, run `npm run docs:design` to regenerate them.

describe('design guide', () => {
  it('lists every colour token with both theme values', async () => {
    const css = readFileSync(join(import.meta.dirname, 'tokens.css'), 'utf8');

    await expect(renderTokensDoc(css)).toMatchFileSnapshot('../../../docs/design/tokens.md');
  });

  it('fails on a token without a documented role', () => {
    const css = ':root {\n  --mystery: #000000;\n}\n.dark {\n  --mystery: #ffffff;\n}\n';

    expect(() => renderTokensDoc(css)).toThrow('Token "mystery" has no role');
  });

  it('lists every domain status with tone, icon and labels in the three languages', async () => {
    const i18n = await createI18n();
    const labels = Object.fromEntries(
      SUPPORTED_LANGUAGES.map((language) => [
        language,
        (key: string) => i18n.getFixedT(language, 'ui')(key as never),
      ]),
    );

    await expect(renderStatusDoc(STATUS_MAP, labels)).toMatchFileSnapshot('../../../docs/design/status.md');
  });
});
