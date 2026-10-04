import { apiDownloadPost } from '@/api/http';
import { saveFile } from '@/lib/download';

/** Used only if the API names no file; its name never holds the DNI/NIE (design D11). */
const FALLBACK_FILE_NAME = 'polvorapp-personal-data.zip';

/** Downloads a GDPR export: the request goes in the body, so the DNI/NIE stays out of the address. */
export async function downloadPersonalData(url: string, body: Record<string, string>): Promise<void> {
  const file = await apiDownloadPost(url, body);
  saveFile(file.blob, file.fileName ?? FALLBACK_FILE_NAME);
}
