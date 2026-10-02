import type { ReactNode } from 'react';
import { Tabs as TabsRoot, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

export interface TabItem {
  id: string;
  /** Already translated. */
  label: string;
  /** A number shown next to the label, e.g. the owned weapons. */
  count?: number;
  content: ReactNode;
}

export interface TabsProps {
  /** Already translated; names the list of tabs. */
  label: string;
  tabs: readonly TabItem[];
  value?: string;
  onValueChange?: (value: string) => void;
}

/**
 * Tabs of a detail page (Radix Tabs): the arrow keys move between tabs, each panel is labelled by
 * its tab, and the chosen tab is marked with a bar and weight. Changes are not animated.
 */
export function Tabs({ label, tabs, value, onValueChange }: TabsProps) {
  return (
    <TabsRoot defaultValue={tabs[0]?.id} value={value} onValueChange={onValueChange}>
      <TabsList aria-label={label}>
        {tabs.map((tab) => (
          <TabsTrigger
            key={tab.id}
            value={tab.id}
            aria-label={tab.count === undefined ? undefined : `${tab.label} (${String(tab.count)})`}
          >
            {tab.label}
            {tab.count !== undefined && (
              <span className="rounded-full bg-muted px-1.5 text-help font-normal text-muted-foreground tabular-nums">
                {tab.count}
              </span>
            )}
          </TabsTrigger>
        ))}
      </TabsList>
      {tabs.map((tab) => (
        <TabsContent key={tab.id} value={tab.id}>
          {tab.content}
        </TabsContent>
      ))}
    </TabsRoot>
  );
}
