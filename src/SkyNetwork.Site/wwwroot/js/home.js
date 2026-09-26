// Home page: reveal-on-scroll, live count mirror, screenshot slideshow. Safe without JS: nothing here hides content.
(function () {
  const root = document.documentElement;
  const motion = root.classList.contains('motion');   // set by the inline head snippet (no reduced motion, observer present)

  // ---- reveals ----
  const reveals = [...document.querySelectorAll('[data-reveal]')];
  const show = el => {
    el.classList.add('in');
    // After the transition the attribute goes, so the element's own hover transitions apply again.
    const i = parseInt(el.style.getPropertyValue('--i')) || 0;
    setTimeout(() => el.removeAttribute('data-reveal'), 650 + i * 70);
  };
  const showAll = () => reveals.forEach(show);
  if (motion && reveals.length) {
    // A group staggers its children 70 ms apart; its value is a base offset (the numbers strip waits for the screen).
    document.querySelectorAll('[data-reveal-group]').forEach(g => {
      const base = parseInt(g.getAttribute('data-reveal-group')) || 0;
      [...g.children].forEach((c, i) => { if (c.hasAttribute('data-reveal')) c.style.setProperty('--i', Math.min(base + i, 8)); });
    });
    const io = new IntersectionObserver(entries => {
      entries.forEach(e => { if (e.isIntersecting) { show(e.target); io.unobserve(e.target); } });
    }, { rootMargin: '0px 0px -10% 0px', threshold: 0 });
    reveals.forEach(el => io.observe(el));
    // Belt and braces: what is already on screen shows after a slow load; everything shows on a bfcache restore or when leaving.
    setTimeout(() => reveals.forEach(el => { if (el.getBoundingClientRect().top < innerHeight) show(el); }), 3000);
    addEventListener('pageshow', e => { if (e.persisted) showAll(); });
    addEventListener('pagehide', showAll);
  } else {
    showAll();
  }

  // ---- live counts: map.js writes #pilot-count / #atc-count every 15 s; the numbers strip mirrors them, grouped like the server ----
  const fmt = v => { const n = Number(v); return Number.isFinite(n) ? n.toLocaleString(root.lang || 'en') : v; };
  const mirror = (id, sel) => {
    const src = document.getElementById(id), dst = document.querySelectorAll(sel);
    if (!src || !dst.length || !('MutationObserver' in window)) return;
    new MutationObserver(() => { const t = fmt(src.textContent.trim()); dst.forEach(d => { d.textContent = t; }); })
      .observe(src, { childList: true, characterData: true, subtree: true });
  };
  mirror('pilot-count', '[data-mirror="pilot-count"]');
  mirror('atc-count', '[data-mirror="atc-count"]');
})();
