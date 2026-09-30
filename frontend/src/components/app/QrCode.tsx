import { toString as qrToSvg } from 'qrcode';
import { useEffect, useState } from 'react';
import { Skeleton } from '@/components/ui/skeleton';

export interface QrCodeProps {
  /** Text to encode, e.g. an `otpauth://` URI. */
  value: string;
  /** Accessible description of what the code is for. */
  label: string;
}

/**
 * A QR code rendered as an SVG image. Always dark on white, whatever the theme, so phone
 * cameras read it; served as a `data:` URL, which the content security policy allows for images.
 */
export function QrCode({ value, label }: QrCodeProps) {
  const [source, setSource] = useState<string>();

  useEffect(() => {
    let current = true;
    void qrToSvg(value, {
      type: 'svg',
      margin: 2,
      errorCorrectionLevel: 'M',
      color: { dark: '#000000', light: '#ffffff' },
    }).then((svg) => {
      if (current) {
        setSource(`data:image/svg+xml;base64,${btoa(svg)}`);
      }
    });
    return () => {
      current = false;
    };
  }, [value]);

  return source ? (
    <img src={source} alt={label} className="size-48 rounded-md border" />
  ) : (
    <Skeleton className="size-48" aria-busy="true" aria-label={label} role="img" />
  );
}
