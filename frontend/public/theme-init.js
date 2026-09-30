// Applies the theme before the first paint, so there is no light/dark flash (design D4).
// Loaded synchronously from index.html as an external file: the CSP forbids inline scripts.
// Mirrors src/theme/config.ts (storage key and rules).
(function () {
  var preference = 'system';
  try {
    var stored = window.localStorage.getItem('polvorapp.theme');
    if (stored === 'light' || stored === 'dark' || stored === 'system') preference = stored;
  } catch (e) {
    // Storage blocked: follow the system.
  }
  var dark = preference === 'dark';
  try {
    if (preference === 'system') dark = window.matchMedia('(prefers-color-scheme: dark)').matches;
  } catch (e) {
    // No matchMedia: stay light.
  }
  document.documentElement.classList.toggle('dark', dark);
})();
