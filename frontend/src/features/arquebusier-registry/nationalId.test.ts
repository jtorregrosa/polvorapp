import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { parseNationalId } from './nationalId';

interface Vector {
  name: string;
  input: string;
  normalized?: string;
  error?: string;
}

// The same synthetic vectors the backend tests read, so both validators accept exactly the same
// values (BR-01, design D4). The path is relative to this file, whatever folder Vitest runs from.
const vectors = (
  JSON.parse(
    readFileSync(
      resolve(
        dirname(fileURLToPath(import.meta.url)),
        '../../../../contracts/test-vectors/national-ids.json',
      ),
      'utf8',
    ),
  ) as {
    cases: Vector[];
  }
).cases;

describe('parseNationalId', () => {
  it('reads every shared vector, each with one expected outcome', () => {
    expect(vectors).toHaveLength(39);
    for (const vector of vectors) {
      expect((vector.normalized === undefined) !== (vector.error === undefined)).toBe(true);
    }
  });

  it.each(vectors)('$name', ({ input, normalized, error }) => {
    expect(parseNationalId(input)).toEqual(normalized ? { value: normalized } : { error });
  });

  it('treats an absent value as required', () => {
    expect(parseNationalId(undefined)).toEqual({ error: 'required' });
  });
});
