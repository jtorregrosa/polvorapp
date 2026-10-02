/**
 * Rejection for a {@link ConfirmDialog} action that knows why it failed: the dialog shows
 * `message`, which must already be translated. Any other rejection shows a generic failure.
 */
export class ConfirmFailure extends Error {
  override name = 'ConfirmFailure';
}

/**
 * Runs a confirmed action; a failure rejects with a {@link ConfirmFailure} carrying `explain`'s
 * translated reason, so the dialog stays open and says why.
 */
export async function explainFailure(
  action: () => Promise<unknown>,
  explain: (error: unknown) => string,
): Promise<void> {
  try {
    await action();
  } catch (error) {
    throw new ConfirmFailure(explain(error));
  }
}
