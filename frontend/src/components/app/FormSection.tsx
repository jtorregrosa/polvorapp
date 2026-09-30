import { useId, type ReactNode } from 'react';

export interface FormSectionProps {
  title: string;
  description?: string;
  children: ReactNode;
}

/** Groups related fields under a legend (design guide: form template). */
export function FormSection({ title, description, children }: FormSectionProps) {
  const descriptionId = useId();

  return (
    <fieldset
      aria-describedby={description ? descriptionId : undefined}
      className="flex flex-col gap-4 rounded-lg border bg-card p-4 sm:p-6"
    >
      <legend className="px-1 text-base font-semibold text-foreground">{title}</legend>
      {description && (
        <p id={descriptionId} className="-mt-2 text-sm text-muted-foreground">
          {description}
        </p>
      )}
      {children}
    </fieldset>
  );
}
