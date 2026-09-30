/** Whether a navigation entry is the current page: its path or a sub-path of it (not a prefix). */
export function isCurrentPath(pathname: string, to: string): boolean {
  if (to === '/') {
    return pathname === '/';
  }
  return pathname === to || pathname.startsWith(`${to}/`);
}
