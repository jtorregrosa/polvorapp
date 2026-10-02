/** Long enough for any browser to have read the file before its object URL is released. */
const RELEASE_AFTER_MS = 30_000;

/**
 * Hands a downloaded file to the browser to save, under `fileName`. Some browsers read the file
 * after the click returns, so its object URL is released only later.
 */
export function saveFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => {
    URL.revokeObjectURL(url);
  }, RELEASE_AFTER_MS);
}
