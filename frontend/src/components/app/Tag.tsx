import { cn } from '@/lib/cn';
import type { ParseKeys } from 'i18next';
import { useEffect, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { tagTone, type TagCategory, type TagTone } from './tags';

const TONE_CLASSES: Readonly<Record<TagTone, string>> = {
  neutral: 'bg-tag-neutral text-tag-neutral-foreground',
  1: 'bg-tag-1 text-tag-1-foreground',
  2: 'bg-tag-2 text-tag-2-foreground',
  3: 'bg-tag-3 text-tag-3-foreground',
  4: 'bg-tag-4 text-tag-4-foreground',
};

export interface TagProps {
  tone?: TagTone;
  children: ReactNode;
  className?: string;
}

/**
 * A categorical value that is not a status, e.g. a role or a side (spec: Tags for fixed values):
 * a label on a categorical tint, with no icon and no semantic colour, so it never reads as a
 * status. Same metrics as `StatusBadge`.
 */
export function Tag({ tone = 'neutral', children, className }: TagProps) {
  return (
    <span
      data-tag=""
      data-tone={tone}
      className={cn(
        'inline-flex w-fit items-center rounded-md px-2 py-0.5 text-xs font-medium whitespace-nowrap',
        TONE_CLASSES[tone],
        className,
      )}
    >
      {children}
    </span>
  );
}

export interface CategoryTagProps {
  category: TagCategory;
  /** Glossary code term, e.g. `MOORISH`, `FIRING_CHIEF`, or `YES`/`NO` for a flag. */
  value: string;
  className?: string;
}

/** A fixed value with its translated label and its tone from `TAG_MAP`. */
export function CategoryTag({ category, value, className }: CategoryTagProps) {
  const { t } = useTranslation('ui');
  const tone = tagTone(category, value);

  useEffect(() => {
    if (tone === undefined && import.meta.env.DEV) {
      console.warn(`CategoryTag: no mapping for ${category} "${value}"`);
    }
  }, [tone, category, value]);

  // Mapped values always have a label (checked by Tag.test.tsx for all three locales).
  const label = tone === undefined ? value : t(`tag.${category}.${value}` as ParseKeys<'ui'>);

  return (
    <Tag tone={tone ?? 'neutral'} className={className}>
      {label}
    </Tag>
  );
}
