import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Side, UserRole, WeaponKind } from '@/api/generated/model';
import caUi from '@/i18n/locales/ca-ES-valencia/ui.json';
import enUi from '@/i18n/locales/en/ui.json';
import esUi from '@/i18n/locales/es-ES/ui.json';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { TAG_MAP, type TagCategory } from './tags';
import { CategoryTag, Tag } from './Tag';

const everyTag = (Object.keys(TAG_MAP) as TagCategory[]).flatMap((category) =>
  Object.keys(TAG_MAP[category]).map((value) => [category, value] as const),
);

const tagOf = (text: string) => screen.getByText(text).closest('[data-tag]');

describe('Tag', () => {
  it('maps exactly the sides, roles and weapon kinds of the API, and yes/no', () => {
    expect(Object.keys(TAG_MAP.side).sort()).toEqual(Object.values(Side).sort());
    expect(Object.keys(TAG_MAP.role).sort()).toEqual(Object.values(UserRole).sort());
    expect(Object.keys(TAG_MAP.weaponKind).sort()).toEqual(Object.values(WeaponKind).sort());
    expect(TAG_MAP.yesNo).toEqual({ YES: 2, NO: 'neutral' });
  });

  it.each(everyTag)(
    'renders %s %s with a translated label, its tone and no icon',
    async (category, value) => {
      await renderWithProviders(<CategoryTag category={category} value={value} />);

      const tag = screen.getByText((_, element) => element?.hasAttribute('data-tag') ?? false);
      expect(tag.textContent).not.toBe('');
      expect(tag.textContent).not.toBe(value);
      expect(tag.querySelector('svg')).toBeNull();
      expect(tag).toHaveAttribute(
        'data-tone',
        String((TAG_MAP[category] as Record<string, string | number>)[value]),
      );
    },
  );

  it.each([
    ['es-ES', esUi],
    ['ca-ES-valencia', caUi],
    ['en', enUi],
  ])('has a %s label for every mapped value', (_, resources) => {
    for (const [category, value] of everyTag) {
      const labels = resources.tag[category] as Record<string, string | undefined>;
      expect(labels[value], `${category}.${value}`).toBeTruthy();
    }
  });

  it('shows the two sides in Valencian in two different tones, never a semantic one', async () => {
    await renderWithProviders(
      <>
        <CategoryTag category="side" value="MOORISH" />
        <CategoryTag category="side" value="CHRISTIAN" />
      </>,
      'ca-ES-valencia',
    );

    const moorish = tagOf('Moro');
    const christian = tagOf('Cristià');
    expect(moorish?.getAttribute('data-tone')).not.toBe(christian?.getAttribute('data-tone'));
    // Categorical tokens, never a semantic tone.
    expect(moorish).toHaveClass('bg-tag-3', 'text-tag-3-foreground');
    expect(christian).toHaveClass('bg-tag-1', 'text-tag-1-foreground');
  });

  it('shows "no" in the neutral tone', async () => {
    await renderWithProviders(<CategoryTag category="yesNo" value="NO" />, 'en');

    expect(tagOf('No')).toHaveAttribute('data-tone', 'neutral');
  });

  it('shows an unknown value as a neutral tag with the raw code and reports it in development', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    await renderWithProviders(<CategoryTag category="side" value="NEUTRAL" />);

    expect(tagOf('NEUTRAL')).toHaveAttribute('data-tone', 'neutral');
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('NEUTRAL'));
  });

  it.each(['constructor', 'toString'])('treats %s as an unknown value, not a list entry', async (value) => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    await renderWithProviders(<CategoryTag category="role" value={value} />);

    expect(tagOf(value)).toHaveAttribute('data-tone', 'neutral');
    expect(warn).toHaveBeenCalledWith(expect.stringContaining(value));
  });

  it('renders a free tag with the given tone', async () => {
    await renderWithProviders(<Tag tone={3}>Custom</Tag>);

    expect(tagOf('Custom')).toHaveAttribute('data-tone', '3');
  });

  // Contrast is checked on the tokens (contrast.test.ts) and in the browser: jsdom has no styles.
  it('has no automatically detectable accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <ul>
        {everyTag.map(([category, value]) => (
          <li key={`${category}-${value}`}>
            <CategoryTag category={category} value={value} />
          </li>
        ))}
      </ul>,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
