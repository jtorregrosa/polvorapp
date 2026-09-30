import { Languages } from 'lucide-react';
import { useId, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Label } from '@/components/ui/label';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';
import { matchLanguage, rememberLanguage, SUPPORTED_LANGUAGES } from '@/i18n/config';

/**
 * Switches the UI language at runtime (UC-27). A native select keeps the platform's accessible
 * picker on phones. Each option is written in its own language and marked with `lang`.
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
    <div className="flex items-center gap-2">
      <Label htmlFor={id} className="flex items-center gap-1.5 text-sm text-muted-foreground">
        <Languages aria-hidden="true" className="size-4" />
        <span className="sr-only sm:not-sr-only">{t('shell.language.label')}</span>
      </Label>
      <NativeSelect id={id} value={i18n.resolvedLanguage ?? i18n.language} onChange={onChange}>
        {SUPPORTED_LANGUAGES.map((language) => (
          <NativeSelectOption key={language} value={language} lang={language}>
            {t(`shell.language.options.${language}`)}
          </NativeSelectOption>
        ))}
      </NativeSelect>
    </div>
  );
}
