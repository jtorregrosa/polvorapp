/** Matched segments when `pathname` is `pattern` or under it (a `*` segment matches any one segment). */
function matchedSegments(pathname: string, pattern: string): number | undefined {
  if (pattern === '/') {
    return pathname === '/' ? 0 : undefined;
  }
  const wanted = pattern.split('/').filter(Boolean);
  const actual = pathname.split('/').filter(Boolean);
  if (actual.length < wanted.length) {
    return undefined;
  }
  return wanted.every((segment, index) => segment === '*' || segment === actual[index])
    ? wanted.length
    : undefined;
}

/** Whether a navigation entry is the current page: its path or a sub-path of it (not a prefix). */
export function isCurrentPath(pathname: string, to: string): boolean {
  return matchedSegments(pathname, to) !== undefined;
}

export interface NavigationTarget {
  to: string;
  /** Other paths the entry owns, e.g. `/editions/*\/orders` for Orders; `*` is any one segment. */
  matches?: readonly string[];
}

/**
 * The entry to mark as current: the one whose path or extra paths match most segments, so an
 * edition's orders mark Orders, not Editions. Undefined when no entry matches.
 */
export function currentNavigationTarget(
  pathname: string,
  entries: readonly NavigationTarget[],
): string | undefined {
  let best: { to: string; segments: number } | undefined;
  for (const { to, matches = [] } of entries) {
    for (const pattern of [to, ...matches]) {
      const segments = matchedSegments(pathname, pattern);
      if (segments !== undefined && (best === undefined || segments > best.segments)) {
        best = { to, segments };
      }
    }
  }
  return best?.to;
}
