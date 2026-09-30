import { describe, expect, it } from 'vitest';
import { axeViolations } from './axe';

describe('test setup', () => {
  it('reports no violations for accessible markup', async () => {
    document.body.innerHTML = '<main><h1>Title</h1><button type="button">Save</button></main>';

    expect(await axeViolations(document.body)).toEqual([]);
  });

  it('reports violations for inaccessible markup', async () => {
    document.body.innerHTML = '<main><img src="x.png"></main>';

    const violations = await axeViolations(document.body);

    expect(violations.map((v) => v.id)).toContain('image-alt');
  });
});
