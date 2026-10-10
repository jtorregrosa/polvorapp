import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import '@/lib/zod-config';
import { server } from './server';

// jsdom has no matchMedia; default to a light system theme (tests may stub their own).
if (typeof window.matchMedia !== 'function') {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    }),
  });
}

// jsdom has no layout, so no scrolling: links of the error summary scroll fields into view.
if (typeof (Element.prototype as Partial<Element>).scrollIntoView !== 'function') {
  Object.defineProperty(Element.prototype, 'scrollIntoView', {
    configurable: true,
    writable: true,
    value: (): void => undefined,
  });
}

// jsdom has no ResizeObserver; Radix primitives (e.g. Checkbox) only need it to exist.
if (typeof globalThis.ResizeObserver !== 'function') {
  const noop = (): void => undefined;
  globalThis.ResizeObserver = class {
    observe = noop;
    unobserve = noop;
    disconnect = noop;
  };
}

// jsdom decodes no images and fires neither `load` nor `error` for a real object URL, so a chosen
// file would wait for the decode timeout. Fail it as a browser fails an unreadable file; tests of
// readable photos stub the decoding and their own `blob:` URLs instead.
const imageSource = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'src');
if (imageSource?.set) {
  Object.defineProperty(HTMLImageElement.prototype, 'src', {
    ...imageSource,
    set(this: HTMLImageElement, value: string) {
      imageSource.set?.call(this, value);
      if (value.startsWith('blob:nodedata:')) {
        queueMicrotask(() => this.dispatchEvent(new Event('error')));
      }
    },
  });
}

beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' });
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
  document.documentElement.lang = 'es-ES';
});

afterAll(() => {
  server.close();
});
