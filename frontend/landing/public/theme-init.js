// Follows the system's light/dark preference (the shared tokens switch on the `dark` class), before
// the first paint and when it changes. A file, not an inline script, because the CSP forbids inline
// scripts. The landing has no theme switch of its own.
;(function () {
  if (!window.matchMedia) return
  var query = window.matchMedia('(prefers-color-scheme: dark)')
  var apply = function () {
    document.documentElement.classList.toggle('dark', query.matches)
  }
  apply()
  if (query.addEventListener) query.addEventListener('change', apply)
})()
