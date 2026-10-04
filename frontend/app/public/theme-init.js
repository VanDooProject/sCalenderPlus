// Applies the stored theme before the first paint (no flash of the wrong theme). A file, not an
// inline script, because the CSP forbids inline scripts. Keep in sync with src/composables/theme.ts.
;(function () {
  var preference = null
  try {
    preference = localStorage.getItem('scal.theme')
  } catch {
    // Storage unavailable: follow the system.
  }
  var dark =
    preference === 'dark' ||
    (preference !== 'light' &&
      window.matchMedia &&
      window.matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.classList.toggle('dark', dark)
})()
