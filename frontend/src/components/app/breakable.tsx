import { Fragment, type ReactNode } from 'react';

/**
 * An email address that may break after "@" and after each "." instead of mid-word ("polvorapp.e /
 * xample") in a narrow column (UI audit T10); any other value as it is.
 */
export function breakable(value: ReactNode): ReactNode {
  if (typeof value !== 'string' || !/^[^\s@]+@[^\s@]+$/.test(value)) return value;
  return value.split(/(?<=[@.])/).map((part, index) => (
    <Fragment key={index}>
      {index > 0 && <wbr />}
      {part}
    </Fragment>
  ));
}
