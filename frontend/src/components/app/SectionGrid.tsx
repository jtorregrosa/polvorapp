import type { ReactNode } from 'react';

/**
 * Lays the sections of a detail page out in one column, two from 1024 px and three from 1700 px
 * (spec: Page templates). Give a `SectionCard` `span="full"` to take the whole row.
 */
export function SectionGrid({ children }: { children: ReactNode }) {
  return (
    <div className="grid grid-cols-1 items-start gap-section lg:grid-cols-2 wide:grid-cols-3">{children}</div>
  );
}
