// Public saytın bütün skripti — inline skript YOXDUR, CSP script-src 'self' ilə işləyir.
(function () {
    'use strict';

    // ── Skan beacon ─────────────────────────────────────────────────────────
    // Q səhifəsi keşdən gəlsə də sayılır; sendBeacon naviqasiyanı gecikdirmir.
    var qrToken = document.body.dataset.qrToken;
    if (qrToken) {
        var payload = JSON.stringify({ token: qrToken });
        if (navigator.sendBeacon) {
            navigator.sendBeacon('/api/public/scans',
                new Blob([payload], { type: 'application/json' }));
        } else {
            fetch('/api/public/scans', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: payload,
                keepalive: true
            });
        }
    }

    // ── Qalereya: thumbnail → əsas şəkil (event delegation) ─────────────────
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.thumb');
        if (!btn) return;
        var main = document.getElementById('main-image');
        if (!main) return;
        main.src = btn.dataset.src;
        main.srcset = btn.dataset.srcset;
        main.alt = btn.dataset.alt;
        document.querySelectorAll('.thumb').forEach(function (b) {
            b.classList.remove('border-emerald-700');
            b.classList.add('border-transparent');
        });
        btn.classList.remove('border-transparent');
        btn.classList.add('border-emerald-700');
    });

    // ── Sorğu forması ───────────────────────────────────────────────────────
    var inquiryForm = document.getElementById('inquiry-form');
    if (inquiryForm) {
        inquiryForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            var data = Object.fromEntries(new FormData(inquiryForm).entries());
            var errorEl = document.getElementById('inquiry-error');
            errorEl.hidden = true;
            try {
                var res = await fetch('/api/public/inquiries', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data)
                });
                if (!res.ok) {
                    var problem = await res.json().catch(function () { return {}; });
                    throw new Error(problem.title || 'Göndərilə bilmədi — yenidən cəhd edin.');
                }
                inquiryForm.hidden = true;
                document.getElementById('inquiry-done').hidden = false;
            } catch (err) {
                errorEl.textContent = err.message;
                errorEl.hidden = false;
            }
        });
    }
})();
