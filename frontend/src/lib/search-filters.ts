/** A filter from the address, if it is one the API knows; otherwise `''` (no filter). */
export function knownFilter<T extends string>(value: string | null, allowed: readonly T[]): T | '' {
  return allowed.find((candidate) => candidate === value) ?? '';
}

/** The address query with `key` set to `value`, or removed when `value` is empty. */
export function withFilter(search: URLSearchParams, key: string, value: string): URLSearchParams {
  const next = new URLSearchParams(search);
  if (value) {
    next.set(key, value);
  } else {
    next.delete(key);
  }
  return next;
}
