// Global UI davranışları (CSP script-src 'self' uyumlu — inline on* handler YOK).
// Eskiden razor'larda inline `onsubmit="return confirm('...')"` ve `onclick="this.select()"` vardı;
// bunlar CSP'de script-src 'unsafe-inline' gerektiriyordu. Artık öznitelikle işaretlenip burada bağlanır:
//   <form data-confirm="Mesaj">  → submit'te confirm(); iptalde submit engellenir.
//   <input data-select-all>      → tıklayınca içeriği seçilir (kopyalama kolaylığı).
(function () {
    // Onaylı submit — delegasyon (submit event'i kabarır). Enhanced-nav'dan ÖNCE yakalamak için capture.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (form && form.matches && form.matches('form[data-confirm]')) {
            if (!window.confirm(form.getAttribute('data-confirm'))) {
                e.preventDefault();
                e.stopPropagation();
            }
        }
    }, true);

    // Tıklayınca tümünü seç (ör. takvim abonelik linki).
    document.addEventListener('click', function (e) {
        var el = e.target;
        if (el && el.matches && el.matches('[data-select-all]') && typeof el.select === 'function') {
            el.select();
        }
    }, true);
})();
