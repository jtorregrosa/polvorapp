/**
 * Rejection for a {@link ConfirmDialog} action that knows why it failed: the dialog shows
 * `message`, which must already be translated. Any other rejection shows a generic failure.
 */
export class ConfirmFailure extends Error {
  override name = 'ConfirmFailure';
}
