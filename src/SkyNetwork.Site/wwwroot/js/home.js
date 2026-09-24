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
    document.querySelectorAll('[data-reveal-group]').forEach(g => {
      [...g.children].forEach((c, i) => { if (c.hasAttribute('data-reveal')) c.style.setProperty('--i', Math.min(i, 6)); });
    });
    const io = new IntersectionObserver(entries => {
      entries.forEach(e => { if (e.isIntersecting) { show(e.target); io.unobserve(e.target); } });
    }, { rootMargin: '0px 0px -10% 0px', threshold: 0 });
    reveals.forEach(el => io.observe(el));
    // Belt and braces: nothing stays hidden after a slow load, a bfcache restore or when leaving the page.
    setTimeout(showAll, 3000);
    addEventListener('pageshow', showAll);
    addEventListener('pagehide', showAll);
  } else {
    showAll();
  }

  // ---- live counts: map.js writes #pilot-count / #atc-count every 15 s; the numbers strip mirrors them ----
  const mirror = (id, sel) => {
    const src = document.getElementById(id), dst = document.querySelectorAll(sel);
    if (!src || !dst.length || !('MutationObserver' in window)) return;
    new MutationObserver(() => dst.forEach(d => { d.textContent = src.textContent; }))
      .observe(src, { childList: true, characterData: true, subtree: true });
  };
  mirror('pilot-count', '[data-mirror="pilot-count"]');
  mirror('atc-count', '[data-mirror="atc-count"]');

  // ---- gallery ----
  const gallery = document.querySelector('.gallery');
  if (gallery) {
    const slides = [...gallery.querySelectorAll('.slide')];
    const dotsRow = gallery.querySelector('.gallery-dots');
    const dots = dotsRow ? [...dotsRow.querySelectorAll('button')] : [];
    const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
    let current = Math.max(0, slides.findIndex(s => s.classList.contains('active')));
    let timer = 0, hovered = false, focused = false, visible = true;

    const go = i => {
      const prev = current;
      current = (i + slides.length) % slides.length;
      slides.forEach((s, k) => {
        s.classList.toggle('active', k === current);
        s.classList.toggle('was', k === prev && prev !== current);   // stays opaque underneath while the next one fades in
      });
      dots.forEach((d, k) => d.setAttribute('aria-pressed', k === current ? 'true' : 'false'));
    };
    const paused = () => still || hovered || focused || !visible || document.hidden || slides.length < 2;
    const stop = () => { clearTimeout(timer); timer = 0; };
    const arm = () => { stop(); if (!paused()) timer = setTimeout(() => { go(current + 1); arm(); }, 6000); };

    if (dotsRow && slides.length > 1) {
      dotsRow.hidden = false;
      dots.forEach((d, k) => {
        d.addEventListener('click', () => { go(k); arm(); });
        d.addEventListener('keydown', e => {
          if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
          e.preventDefault();
          go(current + (e.key === 'ArrowRight' ? 1 : -1));
          dots[current].focus();
          arm();
        });
      });
    }
    gallery.addEventListener('mouseenter', () => { hovered = true; stop(); });
    gallery.addEventListener('mouseleave', () => { hovered = false; arm(); });
    gallery.addEventListener('focusin', () => { focused = true; stop(); });
    gallery.addEventListener('focusout', () => { focused = false; arm(); });
    document.addEventListener('visibilitychange', () => document.hidden ? stop() : arm());
    if ('IntersectionObserver' in window) {
      new IntersectionObserver(([e]) => { visible = e.isIntersecting; visible ? arm() : stop(); }, { threshold: 0.2 }).observe(gallery);
    }
    arm();
  }
})();
