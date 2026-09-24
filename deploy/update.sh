#!/usr/bin/env bash
# Updates the FSD server and the site from GitHub and restarts both. Run as root on the server:
#   bash ~/Skynetwork-site/deploy/update.sh
set -euo pipefail

FSD_SRC=~/Skynetwork-fsd
SITE_SRC=~/Skynetwork-site

echo "== FSD-сервер"
git -C "$FSD_SRC" pull --ff-only
cmake -S "$FSD_SRC" -B "$FSD_SRC/build" >/dev/null
cmake --build "$FSD_SRC/build" -j
install -D -t /opt/skynetwork/fsd "$FSD_SRC/build/skynet-fsd" "$FSD_SRC/build/skynet-admin"

echo "== Сайт"
git -C "$SITE_SRC" pull --ff-only
rm -rf ~/site-build
dotnet publish "$SITE_SRC/src/SkyNetwork.Site" -c Release -o ~/site-build --nologo -v q
mkdir -p /opt/skynetwork/site
# Stopped first: overwriting the DLLs of a running site crashes it.
systemctl stop skynetwork-site
cp -r ~/site-build/. /opt/skynetwork/site/

# Older installs set ASPNETCORE_URLS, which appsettings.json overrides (the site then listened on 0.0.0.0).
sed -i 's#^Environment=ASPNETCORE_URLS=#Environment=Urls=#' /etc/systemd/system/skynetwork-site.service

echo "== Перезапуск"
systemctl daemon-reload
systemctl restart skynet-fsd skynetwork-site
sleep 5

echo "== Проверка"
systemctl is-active skynet-fsd skynetwork-site
curl -s -o /dev/null -w "сайт: HTTP %{http_code}\n" http://127.0.0.1:8000/
curl -s -o /dev/null -w "карта: HTTP %{http_code}\n" http://127.0.0.1:8000/tiles/light/3/4/2.png
ss -ltn | grep -E ':(6809|8000) ' || true
echo "Готово."
