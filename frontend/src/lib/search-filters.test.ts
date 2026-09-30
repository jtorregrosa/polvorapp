import { describe, expect, it } from 'vitest';
import { knownFilter, withFilter } from './search-filters';

describe('search filters', () => {
  it('keeps only values the API knows', () => {
    expect(knownFilter('MOORISH', ['MOORISH', 'CHRISTIAN'])).toBe('MOORISH');
    expect(knownFilter('moorish', ['MOORISH', 'CHRISTIAN'])).toBe('');
    expect(knownFilter(null, ['MOORISH'])).toBe('');
  });

  it('sets or removes a filter without touching the others', () => {
    const search = new URLSearchParams('side=MOORISH&includeInactive=true');

    expect(withFilter(search, 'side', 'CHRISTIAN').toString()).toBe('side=CHRISTIAN&includeInactive=true');
    expect(withFilter(search, 'side', '').toString()).toBe('includeInactive=true');
    expect(search.toString()).toBe('side=MOORISH&includeInactive=true');
  });
});
