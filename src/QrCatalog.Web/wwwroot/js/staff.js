// İşçi məlumat ekranının yeganə skripti. Xarici istinad yoxdur (CSP: script-src 'self').
//
// «Buradayam?» — qeydə alınmış mövqe ilə telefonun cari mövqeyi arasındakı məsafə.
// Sahədə bu üç real hadisəni tutur: nüsxə köçürülüb · etiket səhv nüsxəyə yapışdırılıb ·
// işçi başqa obyektdədir. Yazma əməliyyatı YOXDUR — yalnız göstərir.
(function () {
    'use strict';

    var button = document.querySelector('[data-here]');
    if (!button) return;

    var output = document.querySelector('[data-here-result]');
    var lat = parseFloat(button.getAttribute('data-lat'));
    var lon = parseFloat(button.getAttribute('data-lon'));
    if (isNaN(lat) || isNaN(lon)) return;

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
            show('Bu brauzer mövqeyi müəyyən edə bilmir.', 'warn');
            return;
        }

        show('Mövqe alınır…', '');
        navigator.geolocation.getCurrentPosition(
            function (pos) {
                var meters = distanceMeters(
                    pos.coords.latitude, pos.coords.longitude, lat, lon);

                // Qeyd olunmuş nöqtə çox vaxt obyektin ümumi koordinatıdır (±150 m),
                // ona görə hədlər geniş tutulub — yanlış həyəcan faydadan pisdir.
                if (meters < 200) {
                    show('Təxminən ' + Math.round(meters) + ' m — doğru yerdəsiniz.', 'ok');
                } else if (meters < 2000) {
                    show(Math.round(meters) + ' m aralıdır — qeyd olunmuş nöqtə obyektin ' +
                        'ümumi koordinatı ola bilər.', '');
                } else {
                    show(Math.round(meters / 1000) + ' km aralıdır — bu nüsxə başqa yerdə ' +
                        'qeydə alınıb. Köçürülübsə qeyd yenilənməlidir.', 'warn');
                }
            },
            function () {
                show('Mövqe alınmadı — brauzerdə icazə verilməyib.', 'warn');
            },
            { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 });
    });
})();
