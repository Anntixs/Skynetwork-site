// Banner editor (staff: events and news). The preview follows the chosen height and crop at once and shows a newly
// picked picture before it is saved. "Whole picture" is never cropped, so the crop choice is switched off for it.
(() => {
  for (const box of document.querySelectorAll('[data-banner-editor]')) {
    const look = box.querySelector('.banner-look');
    const page = box.querySelector('[data-banner-page]');
    const card = box.querySelector('[data-banner-card]');
    const focusChoice = box.querySelector('[data-banner-focus]');
    const picked = name => box.querySelector(`input[name="${name}"]:checked`)?.value ?? '';

    const apply = () => {
      const size = picked('BannerSize'), focus = picked('BannerFocus');
      const layout = [size && `banner-${size}`, focus && `focus-${focus}`].filter(Boolean);
      page.className = ['page-banner', ...layout].join(' ');
      card.className = ['card-media', ...layout].join(' ');
      focusChoice.disabled = size === 'full';
    };
    box.addEventListener('change', e => {
      if (e.target.name === 'BannerSize' || e.target.name === 'BannerFocus') apply();
    });

    let shown = null;
    box.querySelector('input[type=file]').addEventListener('change', e => {
      const file = e.target.files?.[0];
      if (!file) return;
      if (shown) URL.revokeObjectURL(shown);
      shown = URL.createObjectURL(file);
      for (const img of box.querySelectorAll('[data-banner-src]')) img.src = shown;
      look.hidden = false;
    });
  }
})();
