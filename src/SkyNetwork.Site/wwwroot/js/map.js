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
  // Leaflet's default prefix carries a flag; just the name here.
  map.attributionControl.setPrefix('<a href="https://leafletjs.com">Leaflet</a>');

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

  // VOR and NDB positions (OurAirports), for routes typed by hand.
  let navaids = null, navaidsLoad = null;
  const loadNavaids = () => navaidsLoad ??= Promise.all([loadAirports(), fetch('/data/navaids.json').then(r => r.json())])
    .then(([, n]) => navaids = n).catch(() => navaids = {});

  // Route text → points: airports, VOR/NDB (the one nearest to the previous point) and coordinates like 5530N03730E.
  // Airways, SIDs/STARs and speed/level groups are skipped, so the line goes straight between what is known.
  function resolveRoute(fp, dep, arr) {
    if (!navaids || !airports) return null;
    const points = dep ? [[fp.departure, dep[0], dep[1]]] : [];
    let prev = dep;
    for (const raw of String(fp.route || '').toUpperCase().split(/\s+/)) {
      const token = raw.split('/')[0];
      if (!token || token === 'DCT' || token === fp.departure || token === fp.destination) continue;
      let at = coordinate(token);
      if (!at) {
        const found = [...(navaids[token] ?? []), ...(token.length === 4 && airports[token] ? [airports[token]] : [])];
        if (!found.length) continue;
        const ref = prev ?? arr;
        at = ref ? found.reduce((best, c) => distNm(ref, c) < distNm(ref, best) ? c : best) : found[0];
        // The same identifier on another continent is not this route's point.
        if (ref && distNm(ref, at) > 1200) continue;
      }
      points.push([token, at[0], at[1]]);
      prev = at;
    }
    if (arr) points.push([fp.destination, arr[0], arr[1]]);
    return points.length > 2 ? points : null;
  }
  function coordinate(t) {
    const m = /^(\d{2})(\d{2})?([NS])(\d{3})(\d{2})?([EW])$/.exec(t);
    if (!m) return null;
    const lat = (+m[1] + (+m[2] || 0) / 60) * (m[3] === 'S' ? -1 : 1), lon = (+m[4] + (+m[5] || 0) / 60) * (m[6] === 'W' ? -1 : 1);
    return lat <= 90 && lon <= 180 ? [lat, lon] : null;
  }

  // Great-circle arcs through all points, kept continuous across the date line.
  function path(points) {
    const out = [];
    for (let i = 1; i < points.length; i++) {
      let seg = arc(points[i - 1], points[i]);
      if (out.length) {
        const shift = Math.round((out[out.length - 1][1] - seg[0][1]) / 360) * 360;
        seg = seg.slice(1).map(([la, lo]) => [la, lo + shift]);
      }
      out.push(...seg);
    }
    return out.length ? out : points;
  }

  // Index of the route point the aircraft is flying to: the leg it is closest to lying on.
  function nextPoint(points, at) {
    if (!at) return 1;
    let best = 1, cost = Infinity;
    for (let i = 1; i < points.length; i++) {
      const c = distNm(points[i - 1], at) + distNm(at, points[i]) - distNm(points[i - 1], points[i]);
      if (c < cost) { cost = c; best = i; }
    }
    return best;
  }

  // The selected aircraft's route points and flown track, refreshed with the live data.
  let route = null;
  async function loadRoute(cs) {
    try {
      const r = await fetch(`/api/v1/pilots/${encodeURIComponent(cs)}/route`, { cache: 'no-store' });
      if (!r.ok) return;
      const d = await r.json();
      if (selected?.kind === 'pilot' && selected.key === cs) { route = { callsign: cs, ...d }; updateCard(); }
    } catch { }
  }

  // METAR for airport cards, at most one request per airport every 5 minutes.
  const metars = new Map();
  function metarOf(code) {
    const m = metars.get(code);
    if (!m || Date.now() - m.at > 300000) {
      metars.set(code, { at: Date.now(), text: m?.text ?? null });
      fetch(`/api/v1/metar/${encodeURIComponent(code)}`).then(r => r.ok ? r.json() : null)
        .then(d => { metars.set(code, { at: Date.now(), text: d?.metar ?? '' }); if (selected?.key === code) updateCard(); })
        .catch(() => { });
    }
    return metars.get(code).text;
  }

  // Route point names only when zoomed in enough to read them.
  const labelZoom = () => el.classList.toggle('wp-hide', map.getZoom() < 6);
  map.on('zoomend', labelZoom);
  labelZoom();

  // ---- airport diagrams (OpenStreetMap) when zoomed in, like a ground radar ----
  const LAYOUT_ZOOM = 12;
  map.createPane('layout').style.zIndex = 350;
  map.createPane('layoutLabels').style.zIndex = 460;
  map.getPane('layoutLabels').style.pointerEvents = 'none';
  const layoutRenderer = L.canvas({ pane: 'layout', padding: .5 });
  const layoutLayer = L.layerGroup().addTo(map);
  const layouts = new Map();   // ICAO → diagram, or 'loading'

  async function loadLayouts() {
    if (compact || map.getZoom() < LAYOUT_ZOOM) return drawLayouts();
    await loadAirports();
    const view = map.getBounds().pad(.2), c = map.getCenter();
    const near = Object.entries(airports).filter(([, a]) => view.contains([a[0], a[1]]))
      .sort((x, y) => map.distance(c, [x[1][0], x[1][1]]) - map.distance(c, [y[1][0], y[1][1]])).slice(0, 4);
    for (const [code] of near) {
      if (layouts.has(code)) continue;
      layouts.set(code, 'loading');
      fetch(`/api/v1/airports/${code}/layout`).then(r => { if (!r.ok) throw r; return r.json(); })
        .then(d => { layouts.set(code, d); drawLayouts(); })
        .catch(() => setTimeout(() => layouts.delete(code), 60000));   // try again in a minute
    }
    drawLayouts();
  }

  function drawLayouts() {
    layoutLayer.clearLayers();
    const z = map.getZoom();
    if (compact || z < LAYOUT_ZOOM) return;
    const near = map.getBounds().pad(.5), view = map.getBounds().pad(.1);
    const col = { apron: css('--apt-apron'), building: css('--apt-building'), runway: css('--apt-runway'), taxiway: css('--apt-taxiway'),
      stand: css('--apt-label'), gate: css('--accent') };
    const tag = (at, text, kind) => L.marker(at, { pane: 'layoutLabels', interactive: false, keyboard: false,
      icon: L.divIcon({ className: '', iconSize: null, html: `<span class="apt-lbl ${kind}">${esc(text)}</span>` }) }).addTo(layoutLayer);
    for (const [code, d] of layouts) {
      const home = airport(code);
      if (!d || d === 'loading' || !home || !near.contains([home[0], home[1]])) continue;
      // Real widths: metres → pixels at this zoom.
      const mpp = 40075016.686 * Math.cos(home[0] * RAD) / (256 * 2 ** z);
      const px = m => Math.max(1, m / mpp);
      const shape = { renderer: layoutRenderer, interactive: false };
      for (const a of d.areas)
        L.polygon(a.ring, { ...shape, stroke: false, fillColor: a.kind === 'apron' ? col.apron : col.building, fillOpacity: 1 }).addTo(layoutLayer);
      for (const t of d.taxiways)
        L.polyline(t.line, { ...shape, color: col.taxiway, weight: px(t.width), lineCap: 'round', lineJoin: 'round' }).addTo(layoutLayer);
      for (const r of d.runways)
        L.polyline(r.line, { ...shape, color: col.runway, weight: px(r.width), lineCap: 'butt' }).addTo(layoutLayer);

      for (const r of d.runways) {
        const [a, b] = String(r.ref).split('/');
        if (a) tag(r.line[0], a, 'rwy');
        if (b) tag(r.line[r.line.length - 1], b, 'rwy');
      }
      if (z >= 14) {
        // One name per taxiway piece, not repeated within 300 m.
        const placed = new Map();
        for (const t of d.taxiways) {
          if (!t.ref || t.lane) continue;
          const mid = t.line[Math.floor(t.line.length / 2)];
          if (!view.contains(mid) || (placed.get(t.ref) ?? []).some(p => map.distance(p, mid) < 300)) continue;
          placed.set(t.ref, [...(placed.get(t.ref) ?? []), mid]);
          tag(mid, t.ref, 'twy');
        }
      }
      if (z >= 15)
        for (const st of d.stands) {
          if (!view.contains(st.at)) continue;
          L.circleMarker(st.at, { ...shape, radius: z >= 16 ? 3 : 2, stroke: false, fillColor: st.gate ? col.gate : col.stand, fillOpacity: .9 }).addTo(layoutLayer);
          if (z >= 16 && st.ref) tag(st.at, st.ref, st.gate ? 'gate' : 'stand');
        }
    }
  }
  map.on('moveend', loadLayouts);

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
    if (!(selected?.kind === kind && selected.key === key)) route = null;
    selected = { kind, key };
    history.replaceState(null, '', '#' + encodeURIComponent(key));
    render();
    if (kind === 'pilot') loadRoute(key);
    if (fly) flyTo();
  }
  function deselect() {
    if (!selected) return;
    selected = null;
    route = null;
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

    // Route points: from the SimBrief import when there is one, otherwise worked out from the route text.
    const mine = route?.callsign === p.callsign ? route : null;
    let points = mine?.waypoints?.length > 1 ? mine.waypoints : null, fromSimbrief = !!points;
    if (!points && fp) {
      if (!navaids) loadNavaids().then(updateCard);
      points = resolveRoute(fp, dep, arr);
    }
    const track = mine?.track ?? [];

    // Flown track solid, the planned route dashed, like on a radar's route display.
    const color = css('--map-route');
    if (track.length > 1) L.polyline(path([...track, ...(at ? [at] : [])]), { color, weight: 2.5, interactive: false }).addTo(routeLayer);
    else if (at && dep) L.polyline(arc(dep, at), { color, weight: 2, interactive: false }).addTo(routeLayer);

    let next = null, flown = null, left = null;
    if (points) {
      const ll = points.map(w => [w[1], w[2]]);
      const i = nextPoint(ll, at);
      L.polyline(path(ll), { color, weight: 1.5, opacity: .35, dashArray: '4 6', interactive: false }).addTo(routeLayer);
      if (at) L.polyline(path([at, ...ll.slice(i)]), { color, weight: 2, dashArray: '6 6', interactive: false }).addTo(routeLayer);
      points.slice(1, -1).forEach((w, k) => L.circleMarker([w[1], w[2]], {
        radius: 3, color, weight: 1.5, fillColor: css('--paper'), fillOpacity: 1, opacity: k + 1 < i ? .45 : 1, interactive: false,
      }).bindTooltip(esc(w[0]), { permanent: true, direction: 'right', offset: [5, 0], className: 'wp-label' }).addTo(routeLayer));
      if (at) {
        next = points[i];
        left = distNm(at, ll[i]);
        for (let k = i + 1; k < ll.length; k++) left += distNm(ll[k - 1], ll[k]);
        let total = 0;
        for (let k = 1; k < ll.length; k++) total += distNm(ll[k - 1], ll[k]);
        flown = Math.max(0, total - left);
      }
    } else if (at && arr) {
      L.polyline(arc(at, arr), { color, weight: 2, dashArray: '6 6', interactive: false }).addTo(routeLayer);
    }
    if (flown == null && at && dep && arr) { flown = distNm(dep, at); left = distNm(at, arr); }

    for (const [code, a] of [[fp?.departure, dep], [fp?.destination, arr]])
      if (a) L.marker(a, { icon: L.divIcon({ className: '', iconSize: [10, 10], html: '<div class="apt-dot"></div>' }) })
        .bindTooltip(esc(code), { permanent: true, direction: 'right', offset: [8, 0] })
        .on('click', () => select('airport', code, false)).addTo(routeLayer);

    let progress = '';
    if (flown != null) {
      const pct = Math.min(100, Math.round(flown / Math.max(1, flown + left) * 100));
      const eta = p.groundspeed > 50 && left > 1 ? 'прибытие ≈ ' + utc(new Date(Date.now() + left / p.groundspeed * 3600000)) : `${pct}%`;
      progress = `<div class="mc-progress" style="margin-top:10px"><i style="width:${pct}%"></i></div>
        <div class="mc-progress-text"><span>${Math.round(flown)} nm</span><span>${eta}</span><span>${Math.round(left)} nm</span></div>`;
      if (next && at && p.groundspeed >= 40)
        progress += `<div class="small" style="margin-top:6px"><span class="muted">Следующая точка:</span> <b class="mono">${esc(next[0])}</b>
          <span class="muted">· ${Math.round(distNm(at, [next[1], next[2]]))} nm</span></div>`;
    }
    const routeNote = points ? `<div class="muted small" style="margin-top:4px">${fromSimbrief ? `${points.length} точек маршрута из SimBrief`
      : `${points.length} точек найдено по базе VOR/NDB — без промежуточных точек трасс`}</div>` : '';

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
        ${fp?.route ? `<div class="mc-block"><div class="eyebrow">Маршрут</div><div class="mc-text">${esc(fp.route)}</div>${routeNote}</div>` : ''}
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
    const metar = metarOf(code);
    return head(esc(code), esc(info?.[2] ?? '')) + `
      <div class="mc-body">
        <div class="mc-block"><div class="eyebrow">METAR</div>${metar ? `<div class="mc-text">${esc(metar)}</div>`
          : `<div class="muted small">${metar === '' ? 'нет данных' : 'загрузка…'}</div>`}</div>
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
    drawLayouts();
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
    if (selected?.kind === 'pilot') loadRoute(selected.key);
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
