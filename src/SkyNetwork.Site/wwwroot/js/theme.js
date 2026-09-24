// Light / dark theme. Loaded in <head> so the page never flashes the wrong theme.
// The choice is kept in the "theme" cookie (read by the server too); without it the system setting wins.
(function () {
  const root = document.documentElement;
  const system = () => matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  window.skyTheme = () => root.dataset.theme || system();

  function apply(theme) {
    root.dataset.theme = theme;
    document.cookie = 'theme=' + theme + '; path=/; max-age=31536000; samesite=lax';
    window.dispatchEvent(new CustomEvent('themechange', { detail: theme }));
  }

  document.addEventListener('click', e => {
    if (e.target.closest('[data-theme-toggle]')) apply(window.skyTheme() === 'dark' ? 'light' : 'dark');
  });
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (!root.dataset.theme) window.dispatchEvent(new CustomEvent('themechange', { detail: system() }));
  });
  // Close the language menu when clicking elsewhere.
  document.addEventListener('click', e => {
    document.querySelectorAll('details.lang-menu[open]').forEach(d => { if (!d.contains(e.target)) d.open = false; });
  });
  // Header edge: the hairline under the header hides while the page sits at the top (a 1px sentinel before the header).
  // This script runs in <head>, so the sentinel is looked up once the body has parsed.
  document.addEventListener('DOMContentLoaded', () => {
    const top = document.getElementById('scroll-top');
    if (top && 'IntersectionObserver' in window) {
      new IntersectionObserver(([e]) => root.classList.toggle('at-top', e.isIntersecting), { threshold: 0 }).observe(top);
    }
  });
})();
