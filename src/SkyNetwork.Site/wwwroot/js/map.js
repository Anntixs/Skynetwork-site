// Live traffic map: pilots as aircraft pointing along their track, controllers as labelled circles.
(function () {
  const el = document.getElementById('map');
  if (!el || !window.L) return;
  const compact = el.dataset.compact === '1';
  const map = L.map(el, { zoomControl: !compact, attributionControl: true, worldCopyJump: true, scrollWheelZoom: !compact })
    .setView([55.75, 37.6], compact ? 4 : 5);
  // Tiles come through the site (see TileProxy), not straight from the provider; light or dark with the theme.
  const theme = () => (window.skyTheme ? window.skyTheme() : 'light');
  let tiles = null;
  function setTiles() {
    if (tiles) map.removeLayer(tiles);
    tiles = L.tileLayer('/tiles/' + theme() + '/{z}/{x}/{y}.png', {
      maxZoom: 18,
      attribution: '&copy; OpenStreetMap, &copy; CARTO'
    }).addTo(map);
  }
  setTiles();
  window.addEventListener('themechange', setTiles);
  const text = { updated: el.dataset.updated || 'Updated', offline: el.dataset.offline || 'Server not responding' };

  const layer = L.layerGroup().addTo(map);
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const plane = heading => L.divIcon({
    className: 'plane', iconSize: [20, 20], iconAnchor: [10, 10],
    html: `<svg width="20" height="20" viewBox="0 0 24 24" style="transform:rotate(${heading ?? 0}deg)"><path fill="currentColor" d="M12 2c.8 0 1.3.7 1.3 1.6v5.6l7.7 4.6v2l-7.7-2.3v4.4l2.2 1.7v1.6L12 20.2l-3.5 1v-1.6l2.2-1.7v-4.4L3 15.8v-2l7.7-4.6V3.6C10.7 2.7 11.2 2 12 2z"/></svg>`
  });
  const radiusNm = f => ({ CTR: 180, FSS: 250, APP: 45, TWR: 12, GND: 4, DEL: 3 }[f] ?? 10);
  let fitted = false;

  async function refresh() {
    let data;
    try {
      const r = await fetch('/api/v1/online', { cache: 'no-store' });
      data = await r.json();
    } catch { return; }
    layer.clearLayers();
    const points = [];
    for (const c of data.controllers) {
      if (c.latitude == null) continue;
      const ll = [c.latitude, c.longitude];
      L.circle(ll, { radius: radiusNm(c.facility) * 1852, color: getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#1f5fbf', weight: 1, fillOpacity: .06 }).addTo(layer);
      L.marker(ll, { icon: L.divIcon({ className: '', html: `<span class="atc-label">${esc(c.callsign)}</span>`, iconAnchor: [0, 8] }) })
        .bindPopup(`<div class="pop-cs">${esc(c.callsign)}</div>${esc(c.name)} · ${esc(c.rating)}<br><span class="mono">${esc(c.frequency)}</span>`)
        .addTo(layer);
      points.push(ll);
    }
    for (const p of data.pilots) {
      if (p.latitude == null) continue;
      const ll = [p.latitude, p.longitude];
      const fp = p.flightPlan;
      L.marker(ll, { icon: plane(p.heading) })
        .bindPopup(`<div class="pop-cs">${esc(p.callsign)}</div>${esc(p.name)}<br>` +
          (fp ? `${esc(fp.aircraft)} · ${esc(fp.departure)} → ${esc(fp.destination)}<br>` : '') +
          `<span class="mono">${esc(p.altitude)} ft · ${esc(p.groundspeed)} kt</span>`)
        .addTo(layer);
      points.push(ll);
    }
    const pc = document.getElementById('pilot-count'), ac = document.getElementById('atc-count'), up = document.getElementById('map-updated');
    if (pc) pc.textContent = data.pilots.length;
    if (ac) ac.textContent = data.controllers.length;
    if (up) up.textContent = data.available ? text.updated + ' ' + new Date(data.updated).toISOString().slice(11, 16) + 'z' : text.offline;
    if (!fitted && points.length > 0) {
      map.fitBounds(points, { maxZoom: 7, padding: [30, 30] });
      fitted = true;
    }
  }
  refresh();
  setInterval(refresh, 15000);
})();
