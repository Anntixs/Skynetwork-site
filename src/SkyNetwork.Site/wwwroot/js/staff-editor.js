// Staff editors of events and news.
// Language tabs: one form holds the Russian and the English version, a tab shows one of them.
// Banner: the preview follows the chosen height and crop at once and shows a newly picked picture before it is saved,
// for the language of the open tab (each site falls back to the other one's banner). "Whole picture" is never cropped,
// so the crop choice is switched off for it.
(() => {
  for (const form of document.querySelectorAll('[data-lang-editor]')) {
    const box = form.querySelector('[data-banner-editor]');
    const look = box.querySelector('.banner-look');
    const page = box.querySelector('[data-banner-page]');
    const card = box.querySelector('[data-banner-card]');
    const focusChoice = box.querySelector('[data-banner-focus]');
    const picked = name => form.querySelector(`input[name="${name}"]:checked`)?.value ?? '';
    const lang = () => picked('EditLang') === 'en' ? 'en' : 'ru';
    // The picture of each language: the saved one, or a file picked and not saved yet.
    const pictures = { ru: box.dataset.urlRu || null, en: box.dataset.urlEn || null };

    const showPicture = () => {
      const src = pictures[lang()] || pictures[lang() === 'en' ? 'ru' : 'en'];
      look.hidden = !src;
      if (src) for (const img of box.querySelectorAll('[data-banner-src]')) if (img.getAttribute('src') !== src) img.src = src;
    };
    const showLayout = () => {
      const size = picked('BannerSize'), focus = picked('BannerFocus');
      const layout = [size && `banner-${size}`, focus && `focus-${focus}`].filter(Boolean);
      page.className = ['page-banner', ...layout].join(' ');
      card.className = ['card-media', ...layout].join(' ');
      focusChoice.disabled = size === 'full';
    };

    form.addEventListener('change', e => {
      const t = e.target;
      if (t.name === 'EditLang') {
        for (const part of form.querySelectorAll('[data-lang-part]')) part.hidden = part.dataset.langPart !== lang();
        showPicture();
      } else if (t.name === 'BannerSize' || t.name === 'BannerFocus') {
        showLayout();
      } else if (t.type === 'file' && t.files?.[0]) {
        const key = t.name === 'bannerEn' ? 'en' : 'ru';
        if (pictures[key]?.startsWith('blob:')) URL.revokeObjectURL(pictures[key]);
        pictures[key] = URL.createObjectURL(t.files[0]);
        showPicture();
      }
    });
    showLayout();
  }
})();
