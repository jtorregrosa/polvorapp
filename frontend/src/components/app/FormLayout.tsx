import { useId, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

export interface FormLayoutSection {
  /** Anchor of the section, for the index and links. */
  id: string;
  /** Already translated. */
  title: string;
  description?: string;
  content: ReactNode;
}

export interface FormLayoutProps {
  sections: readonly FormLayoutSection[];
  /** Help for the whole form, in a column beside it on screens of 1700 px and more. */
  help?: ReactNode;
  /** The form's {@link ActionBar}, fixed at the bottom of the screen. */
  actions: ReactNode;
}

function Section({ id, title, description, content }: FormLayoutSection) {
  const titleId = useId();
  return (
    <section
      id={id}
      aria-labelledby={titleId}
      className="flex min-w-0 scroll-mt-24 flex-col gap-group rounded-lg border bg-card p-4 shadow-e1 sm:p-6"
    >
      <div className="flex flex-col gap-1">
        <h2 id={titleId} className="text-section text-foreground">
          {title}
        </h2>
        {description && <p className="text-help text-muted-foreground">{description}</p>}
      </div>
      {content}
    </section>
  );
}

/**
 * The form template (spec: Page templates): the sections at a readable width, an index of them
 * beside the form from 1280 px, a help column from 1700 px, and the action bar at the bottom.
 * Place it inside `Form`, which adds the error summary and the required note above it.
 */
export function FormLayout({ sections, help, actions }: FormLayoutProps) {
  const { t } = useTranslation('ui');

  return (
    <div className="flex items-start gap-region">
      {sections.length > 1 && (
        <nav aria-label={t('form.index')} className="sticky top-24 hidden w-52 shrink-0 xl:block">
          {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
          <ol role="list" className="flex flex-col gap-1 border-l text-label">
            {sections.map((section) => (
              <li key={section.id}>
                <a
                  href={`#${section.id}`}
                  className="-ml-px block border-l-2 border-transparent py-1 pl-3 break-words text-muted-foreground hover:border-input hover:text-foreground"
                >
                  {section.title}
                </a>
              </li>
            ))}
          </ol>
        </nav>
      )}
      <div className="flex w-full max-w-form min-w-0 flex-1 flex-col gap-section">
        {sections.map((section) => (
          <Section key={section.id} {...section} />
        ))}
        {help && (
          // Below 1700 px the help column is not shown: the same help follows the form, collapsed.
          <details className="rounded-lg border bg-card px-4 py-3 text-help text-muted-foreground wide:hidden">
            <summary className="cursor-pointer text-label text-foreground">{t('form.help')}</summary>
            <div className="mt-2">{help}</div>
          </details>
        )}
        {actions}
      </div>
      {help && (
        <aside
          aria-label={t('form.help')}
          className="sticky top-24 hidden w-72 shrink-0 text-help text-muted-foreground wide:block"
        >
          {help}
        </aside>
      )}
    </div>
  );
}
