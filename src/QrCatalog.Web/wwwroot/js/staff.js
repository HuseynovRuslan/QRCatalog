// İşçi məlumat ekranının skripti. Xarici istinad yoxdur (CSP: script-src 'self');
// Leaflet öz serverimizdən verilir (/vendor/leaflet), xəritə fonu OpenStreetMap-dır
// (img-src icazəsi var).
(function () {
    'use strict';

    // ── Xəritə: «Xəritədə göstər» toxunuşunda bir dəfə qurulur.
    // Koordinat rəqəm kimi GÖSTƏRİLMİR — sahədə heç kim rəqəm oxumur, xəritəyə baxır.
    var mapButton = document.querySelector('[data-map-toggle]');
    var mapBox = document.getElementById('staff-map');
    var mapBuilt = false;

    if (mapButton && mapBox) {
        mapButton.addEventListener('click', function () {
            var open = mapBox.parentElement.hasAttribute('hidden');
            if (open) {
                mapBox.parentElement.removeAttribute('hidden');
                mapButton.setAttribute('aria-expanded', 'true');
                mapButton.textContent = 'Xəritəni bağla';
            } else {
                mapBox.parentElement.setAttribute('hidden', '');
                mapButton.setAttribute('aria-expanded', 'false');
                mapButton.textContent = 'Xəritədə göstər';
                return;
            }

            if (mapBuilt || typeof L === 'undefined') return;
            mapBuilt = true;

            var lat = parseFloat(mapBox.getAttribute('data-lat'));
            var lon = parseFloat(mapBox.getAttribute('data-lon'));
            var map = L.map(mapBox, { scrollWheelZoom: false }).setView([lat, lon], 16);
            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '© OpenStreetMap',
            }).addTo(map);

            // circleMarker, L.marker YOX: standart marker PNG ikon yoluna baxır və
            // o yol burada mövcud deyil — xəritə «işləyir, amma nöqtə görünmür» olar.
            L.circleMarker([lat, lon], {
                radius: 9,
                color: '#173f32',
                weight: 3,
                fillColor: '#c88848',
                fillOpacity: 1,
            }).addTo(map);

            // Konteyner gizli ikən Leaflet ölçünü səhv götürür — açılandan sonra düzəldilir
            setTimeout(function () { map.invalidateSize(); }, 50);
        });
    }

    // ── «Mövqeyi doğrula»: qeydə alınmış nöqtə ilə telefonun cari mövqeyi arasındakı
    // məsafə. Üç real hadisəni tutur: avadanlıq köçürülüb · etiket səhv yapışdırılıb ·
    // işçi başqa obyektdədir. Yalnız göstərir, heç nə yazmır.
    var button = document.querySelector('[data-here]');
    if (!button) return;

    var output = document.querySelector('[data-here-result]');
    var lat2 = parseFloat(button.getAttribute('data-lat'));
    var lon2 = parseFloat(button.getAttribute('data-lon'));
    if (isNaN(lat2) || isNaN(lon2)) return;

    function distanceMeters(aLat, aLon, bLat, bLon) {
        var R = 6371000;
        var toRad = Math.PI / 180;
        var dLat = (bLat - aLat) * toRad;
        var dLon = (bLon - aLon) * toRad;
        var h = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(aLat * toRad) * Math.cos(bLat * toRad) *
            Math.sin(dLon / 2) * Math.sin(dLon / 2);
        return 2 * R * Math.asin(Math.sqrt(h));
    }

    function show(text, tone) {
        if (!output) return;
        output.textContent = text;
        output.className = 'staff-here__out' + (tone ? ' is-' + tone : '');
    }

    button.addEventListener('click', function () {
        if (!navigator.geolocation) {
            show('Bu brauzer mövqe xidmətini dəstəkləmir.', 'warn');
            return;
        }

        show('Mövqe müəyyən edilir…', '');
        navigator.geolocation.getCurrentPosition(
            function (pos) {
                var meters = distanceMeters(
                    pos.coords.latitude, pos.coords.longitude, lat2, lon2);

                // Qeyd olunmuş nöqtə çox vaxt obyektin ümumi koordinatıdır (±150 m),
                // ona görə hədlər genişdir — yanlış həyəcan faydadan pisdir.
                if (meters < 200) {
                    show('Mövqe təsdiqləndi — qeydə alınmış yerdəsiniz (' +
                        Math.round(meters) + ' m).', 'ok');
                } else if (meters < 2000) {
                    show('Qeydə alınmış yerdən ' + Math.round(meters) + ' m aralısınız — ' +
                        'qeyd obyektin ümumi nöqtəsi ola bilər.', '');
                } else {
                    show('Qeydə alınmış yerdən ' + Math.round(meters / 1000) + ' km aralısınız. ' +
                        'Avadanlıq köçürülübsə, qeyd yenilənməlidir.', 'warn');
                }
            },
            function () {
                show('Mövqe alınmadı — brauzer parametrlərində bu sayta məkan icazəsi verin.', 'warn');
            },
            { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 });
    });
})();
