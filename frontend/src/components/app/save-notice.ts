import { createContext, useContext } from 'react';

/** Announces a short, already translated notice such as "Changes saved" (see `SaveNoticeProvider`). */
export type Notify = (text: string) => void;

export const SaveNoticeContext = createContext<Notify | null>(null);

const ignore: Notify = () => undefined;

/**
 * The `notify` of the nearest `SaveNoticeProvider` (the shell mounts one). Outside a provider, a
 * notice is dropped: the action itself already succeeded.
 */
export function useSaveNotice(): Notify {
  return useContext(SaveNoticeContext) ?? ignore;
}
