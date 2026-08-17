// Public saytın bütün skripti — inline skript YOXDUR, CSP script-src 'self' ilə işləyir.
(function () {
    'use strict';

    document.documentElement.classList.add('has-js');

    // ── Yumşaq görünmə animasiyası ─────────────────────────────────────────
    var revealItems = document.querySelectorAll('.reveal');
    if ('IntersectionObserver' in window) {
        var revealObserver = new IntersectionObserver(function (entries, observer) {
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) return;
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            });
        }, { threshold: 0.08, rootMargin: '0px 0px -30px' });
        revealItems.forEach(function (item) { revealObserver.observe(item); });
    } else {
        revealItems.forEach(function (item) { item.classList.add('is-visible'); });
    }

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
        if (btn.classList.contains('is-active')) return;

        main.classList.add('is-changing');
        window.setTimeout(function () {
            main.src = btn.dataset.src;
            main.srcset = btn.dataset.srcset || '';
            main.alt = btn.dataset.alt;
            main.classList.remove('is-changing');
        }, 130);

        document.querySelectorAll('.thumb').forEach(function (b) {
            b.classList.remove('is-active');
            b.setAttribute('aria-pressed', 'false');
        });
        btn.classList.add('is-active');
        btn.setAttribute('aria-pressed', 'true');

        var count = document.querySelector('.product-gallery__count');
        if (count && btn.dataset.index) {
            var total = document.querySelectorAll('.thumb').length.toString().padStart(2, '0');
            count.textContent = btn.dataset.index + ' / ' + total;
        }
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
