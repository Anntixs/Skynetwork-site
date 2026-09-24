// Live traffic map, radar style: aircraft with their flight plans, controllers with their sectors and airports.
(function () {
  const el = document.getElementById('map');
  if (!el || !window.L) return;
  const compact = el.dataset.compact === '1';
  const root = document.documentElement;
  const theme = () => root.dataset.theme === 'dark' ? 'dark' : 'light';
  const css = name => getComputedStyle(root).getPropertyValue(name).trim();

  const map = L.map(el, { zoomControl: false, worldCopyJump: true, scrollWheelZoom: !compact })
    .setView([55.75, 37.6], compact ? 4 : 5);
  if (!compact) L.control.zoom({ position: 'bottomright' }).addTo(map);

  // Tiles come through the site (see TileProxy); the base map follows the site theme, labels sit above the sectors.
  map.createPane('labels').classList.add('labels-pane');
  map.getPane('labels').style.zIndex = 450;
  const tileOptions = { maxZoom: 18, maxNativeZoom: 16 };
  const base = L.tileLayer(`/tiles/${theme()}/{z}/{x}/{y}.png`, {
    ...tileOptions, attribution: '&copy; Esri, HERE, Garmin, &copy; OpenStreetMap'
  }).addTo(map);
  const labels = L.tileLayer(`/tiles/${theme()}-labels/{z}/{x}/{y}.png`, { ...tileOptions, pane: 'labels' }).addTo(map);

  const firOutline = L.layerGroup();              // every sector border (switchable)
  const sectors = L.layerGroup().addTo(map);      // staffed sectors and approach areas
  const traffic = L.layerGroup().addTo(map);      // aircraft, controller labels, airport badges
  const routeLayer = L.layerGroup().addTo(map);   // the selected flight

  const card = document.getElementById('map-card');
  const search = document.getElementById('map-search');
  const firToggle = document.getElementById('fir-toggle');
  let data = null, selected = null, fitted = false;
  let pendingHash = compact ? '' : decodeURIComponent(location.hash.slice(1));

  // ---- helpers ----
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const prefix = cs => String(cs).split('_')[0].toUpperCase();
  const pad = (n, w) => String(n).padStart(w, '0');
  const hm = min => !(min > 0) ? '—' : min < 60 ? `${min} мин` : `${Math.floor(min / 60)} ч ${pad(min % 60, 2)} мин`;
  const utc = d => d.toISOString().slice(11, 16) + 'z';
  const onlineFor = iso => hm(Math.max(1, Math.round((Date.now() - new Date(iso)) / 60000)));
  const feet = ft => `${Math.round(ft).toLocaleString('ru-RU')} ft`;
  const hhmm = t => /^\d{4}$/.test(t || '') && t !== '0000' ? `${t.slice(0, 2)}:${t.slice(2)}z` : '—';
  const RAD = Math.PI / 180;

  function distNm(a, b) {
    const dLat = (b[0] - a[0]) * RAD, dLon = (b[1] - a[1]) * RAD;
    const h = Math.sin(dLat / 2) ** 2 + Math.cos(a[0] * RAD) * Math.cos(b[0] * RAD) * Math.sin(dLon / 2) ** 2;
    return 2 * 3440.065 * Math.asin(Math.sqrt(h));
  }

  // Great-circle arc, the way a long flight actually goes; longitudes unwrapped so the line never jumps across the map.
  function arc(a, b) {
    const [p1, l1, p2, l2] = [a[0] * RAD, a[1] * RAD, b[0] * RAD, b[1] * RAD];
    const d = 2 * Math.asin(Math.sqrt(Math.sin((p2 - p1) / 2) ** 2 + Math.cos(p1) * Math.cos(p2) * Math.sin((l2 - l1) / 2) ** 2));
    if (d < 0.01) return [a, b];
    const n = Math.ceil(d / 0.02), points = [];
    let prev = null;
    for (let i = 0; i <= n; i++) {
      const A = Math.sin((1 - i / n) * d) / Math.sin(d), B = Math.sin(i / n * d) / Math.sin(d);
      const x = A * Math.cos(p1) * Math.cos(l1) + B * Math.cos(p2) * Math.cos(l2);
      const y = A * Math.cos(p1) * Math.sin(l1) + B * Math.cos(p2) * Math.sin(l2);
      const z = A * Math.sin(p1) + B * Math.sin(p2);
      let lon = Math.atan2(y, x) / RAD;
      if (prev !== null) { while (lon - prev > 180) lon -= 360; while (lon - prev < -180) lon += 360; }
      prev = lon;
      points.push([Math.atan2(z, Math.hypot(x, y)) / RAD, lon]);
    }
    return points;
  }

  const label = (html, onClick) => {
    const m = L.marker([0, 0], { icon: L.divIcon({ className: '', iconSize: null, html }), riseOnHover: true });
    if (onClick) m.on('click', onClick);
    return m;
  };
  const open = (kind, key) => () => compact ? location.href = '/map#' + encodeURIComponent(key) : select(kind, key, false);

  const plane = (p, isSelected) => L.divIcon({
    className: 'plane' + (p.groundspeed < 40 ? ' ground' : '') + (isSelected ? ' selected' : ''),
    iconSize: [22, 22], iconAnchor: [11, 11],
    html: `<svg width="22" height="22" viewBox="0 0 24 24" style="transform:rotate(${p.heading ?? 0}deg)"><path fill="currentColor" d="M12 2c.8 0 1.3.7 1.3 1.6v5.6l7.7 4.6v2l-7.7-2.3v4.4l2.2 1.7v1.6L12 20.2l-3.5 1v-1.6l2.2-1.7v-4.4L3 15.8v-2l7.7-4.6V3.6C10.7 2.7 11.2 2 12 2z"/></svg>`
  });

  // ---- reference data ----
  // Sector borders (see data/firs.LICENSE.txt): features by id, callsign prefixes → sector, upper sectors → several FIRs.
  let firs = { features: [], prefixes: {}, uirs: {} }, firById = new Map();
  fetch('/data/firs.json').then(r => r.json()).then(d => {
    firs = d;
    firById = new Map(d.features.map(f => [f.properties.id, f]));
    drawOutline();
    render();
  }).catch(() => { });

  // Sector of a CTR/FSS position, the longest callsign prefix first: UUWV_N_CTR → UUWV-N, UUWV_CTR → UUWV, RU-WRC_FSS → UMMV+UUWV+UWWW.
  function sectorOf(c) {
    const parts = String(c.callsign).toUpperCase().split('_').slice(0, -1);
    for (let n = parts.length; n > 0; n--) {
      const key = parts.slice(0, n).join('_');
      const fir = firs.prefixes[key], f = fir && firById.get(fir.b);
      if (f) return { id: fir.b, name: fir.n, features: [f], label: f.properties.lat != null ? [f.properties.lat, f.properties.lon] : null };
      const uir = firs.uirs[key], list = uir ? uir.b.map(id => firById.get(id)).filter(Boolean) : [];
      if (list.length) return { id: key, name: uir.n, features: list, label: null };
    }
    return null;
  }

  // Canvas: hundreds of borders draw much faster there than as SVG.
  const outlineRenderer = L.canvas({ padding: .5 });
  function drawOutline() {
    firOutline.clearLayers();
    L.geoJSON(firs.features.filter(f => f.properties.top), {
      interactive: false, renderer: outlineRenderer, style: { color: css('--map-fir'), weight: 1, fill: false }
    }).addTo(firOutline);
  }
  try { if (firToggle && localStorage.getItem('map-firs') === '0') firToggle.checked = false; } catch { }
  const showOutline = () => (!firToggle || firToggle.checked) ? firOutline.addTo(map) : firOutline.remove();
  showOutline();
  firToggle?.addEventListener('change', () => {
    showOutline();
    try { localStorage.setItem('map-firs', firToggle.checked ? '1' : '0'); } catch { }
  });

  // Airport coordinates (OurAirports), loaded the first time a flight or an airport is opened.
  let airports = null, airportsLoad = null;
  const loadAirports = () => airportsLoad ??= fetch('/data/airports.json').then(r => r.json()).then(a => airports = a).catch(() => airports = {});
  const airport = code => airports?.[String(code || '').toUpperCase()] ?? null;

  // ---- drawing ----
  function render() {
    if (!data) return [];
    sectors.clearLayers();
    traffic.clearLayers();
    const points = [];
    const ctr = css('--f-ctr'), app = css('--f-app');
    const staffed = new Map();   // sector id → { features, controllers }
    const towers = new Map();    // airport → controllers (DEL/GND/TWR/APP)

    for (const c of data.controllers) {
      if (c.facility === 'OBS') continue;
      const at = c.latitude != null ? [c.latitude, c.longitude] : null;
      if (at) points.push(at);
      if (c.facility === 'CTR' || c.facility === 'FSS') {
        const sector = sectorOf(c);
        if (sector) {
          const s = staffed.get(sector.id) ?? { ...sector, controllers: [] };
          s.controllers.push(c);
          staffed.set(sector.id, s);
        } else if (at) {
          // No known border for this callsign: a circle of the usual size instead.
          L.circle(at, { radius: (c.facility === 'FSS' ? 250 : 180) * 1852, color: ctr, weight: 1.2, fillOpacity: .06, bubblingMouseEvents: false })
            .on('click', open('atc', c.callsign)).addTo(sectors);
          label(`<span class="atc-label" style="transform:translate(-50%,-50%);display:inline-block">${esc(c.callsign)}</span>`, open('atc', c.callsign))
            .setLatLng(at).addTo(traffic);
        }
        continue;
      }
      const code = prefix(c.callsign);
      towers.set(code, [...(towers.get(code) ?? []), c]);
      if (c.facility === 'APP' && at)
        L.circle(at, { radius: 45 * 1852, color: app, weight: 1, dashArray: '4 4', fillOpacity: .05, bubblingMouseEvents: false })
          .on('click', open('airport', code)).addTo(sectors);
    }

    for (const [, s] of staffed) {
      const cs = s.controllers[0].callsign;
      const shape = L.geoJSON(s.features, { style: { color: ctr, weight: 1.6, fillColor: ctr, fillOpacity: .1 }, bubblingMouseEvents: false })
        .on('click', open('atc', cs)).addTo(sectors);
      const names = s.controllers.map(c => esc(c.callsign)).join('<br>');
      label(`<span class="atc-label" style="transform:translate(-50%,-50%);display:inline-block;text-align:center">${names}</span>`, open('atc', cs))
        .setLatLng(s.label ?? shape.getBounds().getCenter()).addTo(traffic);
    }

    const order = ['DEL', 'GND', 'TWR', 'APP'];
    for (const [code, list] of towers) {
      const c = list.find(x => x.latitude != null);
      const at = c ? [c.latitude, c.longitude] : airport(code);
      if (!at) continue;
      const chips = order.filter(f => list.some(x => x.facility === f)).map(f => `<i class="${f}">${f[0]}</i>`).join('');
      label(`<span class="apt-badge" style="transform:translate(-50%,-50%)">${esc(code)}${chips}</span>`, open('airport', code))
        .setLatLng(at).addTo(traffic);
    }

    for (const p of data.pilots) {
      if (p.latitude == null) continue;
      const at = [p.latitude, p.longitude];
      points.push(at);
      const isSelected = selected?.kind === 'pilot' && selected.key === p.callsign;
      const fp = p.flightPlan;
      L.marker(at, { icon: plane(p, isSelected), zIndexOffset: isSelected ? 1000 : 0 })
        .bindTooltip(esc(p.callsign) + (fp?.aircraft ? ' · ' + esc(fp.aircraft) : ''), { direction: 'top', offset: [0, -12] })
        .on('click', open('pilot', p.callsign))
        .addTo(traffic);
    }
    updateCard();
    return points;
  }

  // ---- selection card ----
  function select(kind, key, fly) {
    selected = { kind, key };
    history.replaceState(null, '', '#' + encodeURIComponent(key));
    render();
    if (fly) flyTo();
  }
  function deselect() {
    if (!selected) return;
    selected = null;
    history.replaceState(null, '', location.pathname);
    render();
  }
  map.on('click', deselect);
  document.addEventListener('keydown', e => { if (e.key === 'Escape') deselect(); });
  card?.addEventListener('click', e => {
    if (e.target.closest('.close')) return deselect();
    const t = e.target.closest('[data-select]');
    if (!t) return;
    e.preventDefault();
    const [kind, key] = t.dataset.select.split('|');
    select(kind, key, true);
  });

  const pilotOf = cs => data?.pilots.find(p => p.callsign === cs);
  const atcOf = cs => data?.controllers.find(c => c.callsign === cs);

  async function flyTo() {
    if (!selected) return;
    const { kind, key } = selected;
    if (kind === 'pilot') {
      const p = pilotOf(key);
      if (p?.latitude != null) map.flyTo([p.latitude, p.longitude], Math.max(map.getZoom(), 6), { duration: .8 });
    } else if (kind === 'atc') {
      const c = atcOf(key), sector = c && sectorOf(c);
      if (sector) map.flyToBounds(L.geoJSON(sector.features).getBounds(), { padding: [40, 40], duration: .8 });
      else if (c?.latitude != null) map.flyTo([c.latitude, c.longitude], Math.max(map.getZoom(), 6), { duration: .8 });
    } else {
      await loadAirports();
      const c = data?.controllers.find(x => prefix(x.callsign) === key && x.latitude != null);
      const at = airport(key) ?? (c ? [c.latitude, c.longitude] : null);
      if (at) map.flyTo(at, Math.max(map.getZoom(), 8), { duration: .8 });
    }
  }

  const head = (title, who, chips) => `
    <div class="mc-head">
      <div>
        <div class="cs">${title}</div>
        ${who ? `<div class="who">${who}</div>` : ''}
        ${chips ? `<div class="mc-chips">${chips}</div>` : ''}
      </div>
      <button class="close" type="button" aria-label="Закрыть">×</button>
    </div>`;
  const cell = (name, value) => `<div><span>${name}</span><b>${value}</b></div>`;
  const aptLink = (code, info) => code
    ? `<a href="#" data-select="airport|${esc(code)}"><b>${esc(code)}</b></a><span>${esc(info?.[2] ?? '')}</span>`
    : '<b>—</b>';

  function updateCard() {
    routeLayer.clearLayers();
    if (!card) return;
    if (!selected || !data) { card.hidden = true; return; }
    const { kind, key } = selected;
    card.innerHTML = kind === 'pilot' ? pilotCard(key) : kind === 'atc' ? atcCard(key) : airportCard(key);
    card.hidden = false;
  }

  function pilotCard(cs) {
    const p = pilotOf(cs);
    if (!p) return head(esc(cs), 'Не в сети');
    const fp = p.flightPlan;
    if (fp && !airports) loadAirports().then(updateCard);
    const at = p.latitude != null ? [p.latitude, p.longitude] : null;
    const dep = airport(fp?.departure), arr = airport(fp?.destination);

    // Flown part solid, the rest dashed, like on a radar's route display.
    const color = css('--map-route');
    if (at && dep) L.polyline(arc(dep, at), { color, weight: 2, interactive: false }).addTo(routeLayer);
    if (at && arr) L.polyline(arc(at, arr), { color, weight: 2, dashArray: '6 6', interactive: false }).addTo(routeLayer);
    for (const [code, a] of [[fp?.departure, dep], [fp?.destination, arr]])
      if (a) L.marker(a, { icon: L.divIcon({ className: '', iconSize: [10, 10], html: '<div class="apt-dot"></div>' }) })
        .bindTooltip(esc(code), { permanent: true, direction: 'right', offset: [8, 0] })
        .on('click', () => select('airport', code, false)).addTo(routeLayer);

    let progress = '';
    if (at && dep && arr) {
      const flown = distNm(dep, at), left = distNm(at, arr);
      const pct = Math.min(100, Math.round(flown / Math.max(1, flown + left) * 100));
      const eta = p.groundspeed > 50 && left > 1 ? 'прибытие ≈ ' + utc(new Date(Date.now() + left / p.groundspeed * 3600000)) : `${pct}%`;
      progress = `<div class="mc-progress" style="margin-top:10px"><i style="width:${pct}%"></i></div>
        <div class="mc-progress-text"><span>${Math.round(flown)} nm</span><span>${eta}</span><span>${Math.round(left)} nm</span></div>`;
    }

    const chips = (fp?.aircraft ? `<span class="badge accent">${esc(fp.aircraft)}</span>` : '') +
      (fp?.rules ? `<span class="badge">${esc(fp.rules)}</span>` : '') +
      (p.groundspeed < 40 ? '<span class="badge">На земле</span>' : '');
    return head(esc(p.callsign), `<a href="/members/${p.cid}">${esc(p.name)}</a> · CID ${p.cid}`, chips) + `
      <div class="mc-body">
        ${fp ? `<div><div class="mc-route"><div class="apt">${aptLink(fp.departure, dep)}</div><div class="arrow">→</div><div class="apt">${aptLink(fp.destination, arr)}</div></div>${progress}</div>`
             : '<div class="muted small">План полёта не подан</div>'}
        <div class="mc-grid">
          ${cell('Высота', feet(p.altitude))}
          ${cell('Скорость', p.groundspeed + ' kt')}
          ${cell('Курс', p.heading != null ? pad(Math.round(p.heading) % 360, 3) + '°' : '—')}
          ${cell('Сквок', esc(p.transponder || '—'))}
          ${cell('Эшелон', esc(fp?.cruiseAltitude || '—'))}
          ${cell('TAS', fp?.cruiseSpeed ? fp.cruiseSpeed + ' kt' : '—')}
          ${cell('Вылет', hhmm(fp?.departureTime))}
          ${cell('В пути', hm(fp?.enrouteMinutes ?? 0))}
          ${cell('Топливо', hm(fp?.fuelMinutes ?? 0))}
        </div>
        ${fp?.alternate ? `<div class="small"><span class="muted">Запасной:</span> ${aptLink(fp.alternate, null).replace('<span></span>', '')}</div>` : ''}
        ${fp?.route ? `<div class="mc-block"><div class="eyebrow">Маршрут</div><div class="mc-text">${esc(fp.route)}</div></div>` : ''}
        ${fp?.remarks ? `<div class="mc-block"><div class="eyebrow">Примечания</div><div class="mc-text">${esc(fp.remarks)}</div></div>` : ''}
        <div class="muted small">В сети ${onlineFor(p.logonTime)}</div>
      </div>`;
  }

  function atcCard(cs) {
    const c = atcOf(cs);
    if (!c) return head(esc(cs), 'Не в сети');
    const sector = (c.facility === 'CTR' || c.facility === 'FSS') ? sectorOf(c) : null;
    const code = prefix(c.callsign);
    return head(esc(c.callsign), `<a href="/members/${c.cid}">${esc(c.name)}</a> · CID ${c.cid}`,
      `<span class="badge accent">${esc(c.facility)}</span><span class="badge">${esc(c.rating)}</span>`) + `
      <div class="mc-body">
        <div class="mc-grid">
          ${cell('Частота', esc(c.frequency || '—'))}
          ${cell('Рейтинг', esc(c.rating || '—'))}
          ${cell('В сети', onlineFor(c.logonTime))}
        </div>
        ${sector ? `<div class="small"><span class="muted">Сектор:</span> ${esc(sector.name)}</div>`
                 : `<div class="small"><span class="muted">Аэропорт:</span> <a href="#" data-select="airport|${esc(code)}">${esc(code)}</a></div>`}
      </div>`;
  }

  function airportCard(code) {
    if (!airports) loadAirports().then(updateCard);
    const info = airport(code);
    const atc = data.controllers.filter(c => prefix(c.callsign) === code && c.facility !== 'OBS');
    const deps = data.pilots.filter(p => p.flightPlan?.departure?.toUpperCase() === code);
    const arrs = data.pilots.filter(p => p.flightPlan?.destination?.toUpperCase() === code);
    const strips = atc.map(c => `<a class="strip ${esc(c.facility)}" href="#" data-select="atc|${esc(c.callsign)}">
        <span class="cs">${esc(c.callsign)}</span><span class="who">${esc(c.name)}</span><span class="freq">${esc(c.frequency)}</span></a>`).join('');
    const flights = list => list.length
      ? `<div class="mc-chips">${list.map(p => `<a class="badge" href="#" data-select="pilot|${esc(p.callsign)}">${esc(p.callsign)}</a>`).join('')}</div>`
      : '<div class="muted small">нет</div>';
    return head(esc(code), esc(info?.[2] ?? '')) + `
      <div class="mc-body">
        <div class="mc-block"><div class="eyebrow">Диспетчеры</div>${strips ? `<div class="mc-list">${strips}</div>` : '<div class="muted small">никого</div>'}</div>
        <div class="mc-block"><div class="eyebrow">Вылеты · ${deps.length}</div>${flights(deps)}</div>
        <div class="mc-block"><div class="eyebrow">Прилёты · ${arrs.length}</div>${flights(arrs)}</div>
      </div>`;
  }

  // ---- search ----
  async function find(q) {
    q = q.trim().toUpperCase();
    if (!q || !data) return;
    // Exact callsign or CID first, then an airport code, then the start of a callsign.
    const exact = list => list.find(x => x.callsign.toUpperCase() === q || String(x.cid) === q);
    const starts = list => list.find(x => x.callsign.toUpperCase().startsWith(q));
    const p = exact(data.pilots), c = exact(data.controllers);
    if (p) return select('pilot', p.callsign, true);
    if (c) return select('atc', c.callsign, true);
    await loadAirports();
    if (airport(q)) return select('airport', q, true);
    const sp = starts(data.pilots), sc = starts(data.controllers);
    if (sp) return select('pilot', sp.callsign, true);
    if (sc) return select('atc', sc.callsign, true);
    if (search) { search.value = ''; search.placeholder = `Не найдено: ${q}`; }
  }
  search?.addEventListener('change', () => find(search.value));

  // ---- theme ----
  new MutationObserver(() => {
    base.setUrl(`/tiles/${theme()}/{z}/{x}/{y}.png`);
    labels.setUrl(`/tiles/${theme()}-labels/{z}/{x}/{y}.png`);
    drawOutline();
    render();
  }).observe(root, { attributes: true, attributeFilter: ['data-theme'] });

  // ---- live data ----
  async function refresh() {
    try {
      const r = await fetch('/api/v1/online', { cache: 'no-store' });
      data = await r.json();
    } catch { return; }
    const pc = document.getElementById('pilot-count'), ac = document.getElementById('atc-count'), up = document.getElementById('map-updated');
    if (pc) pc.textContent = data.pilots.length;
    if (ac) ac.textContent = data.controllers.length;
    if (up) up.textContent = data.available ? 'Обновлено ' + utc(new Date(data.updated)) : 'Сервер не отвечает';
    const list = document.getElementById('map-callsigns');
    if (list) list.innerHTML = [...data.pilots, ...data.controllers].map(x => `<option value="${esc(x.callsign)}">`).join('');

    const points = render();
    if (pendingHash) {
      const q = pendingHash;
      pendingHash = '';
      fitted = true;
      find(q);
    } else if (!fitted && points.length > 0) {
      map.fitBounds(points, { maxZoom: 7, padding: [30, 30] });
      fitted = true;
    }
  }
  refresh();
  setInterval(refresh, 15000);
})();
