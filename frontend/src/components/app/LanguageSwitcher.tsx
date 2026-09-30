import { useId, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { matchLanguage, rememberLanguage, SUPPORTED_LANGUAGES } from '@/i18n/config';

/**
 * Switches the UI language at runtime (UC-27). Each option is written in its own language and
 * marked with `lang` so screen readers pronounce it correctly. Restyled in add-design-system.
 */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();
  const id = useId();

  const onChange = (event: ChangeEvent<HTMLSelectElement>): void => {
    const language = matchLanguage(event.target.value);
    if (!language) {
      return;
    }
    rememberLanguage(language);
    void i18n.changeLanguage(language);
  };

  return (
    <div className="language-switcher">
      <label htmlFor={id}>{t('shell.language.label')}</label>
      <select id={id} value={i18n.resolvedLanguage ?? i18n.language} onChange={onChange}>
        {SUPPORTED_LANGUAGES.map((language) => (
          <option key={language} value={language} lang={language}>
            {t(`shell.language.options.${language}`)}
          </option>
        ))}
      </select>
    </div>
  );
}
