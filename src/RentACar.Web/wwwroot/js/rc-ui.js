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

    // Tema (açık/koyu) — data-theme + localStorage. CSP-uyumlu (inline handler yok).
    // Sistem-koyu kullanıcılar CSS @media ile ANINDA koyu görür (JS'siz, FOUC yok); yalnız
    // sisteminden FARKLI mod seçen kullanıcı yüklemede kısa bir yanıp-sönme görebilir (kabul).
    var THEME_KEY = 'racar-theme';
    function applyTheme(t) {
        if (t === 'dark' || t === 'light') document.documentElement.setAttribute('data-theme', t);
        else document.documentElement.removeAttribute('data-theme');
        var lbl = document.querySelector('[data-theme-toggle] [data-theme-label]');
        if (lbl) {
            var dark = document.documentElement.getAttribute('data-theme') === 'dark' ||
                (!document.documentElement.getAttribute('data-theme') &&
                    window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
            lbl.textContent = dark ? 'Açık' : 'Koyu';
        }
    }
    try { applyTheme(localStorage.getItem(THEME_KEY)); } catch (_) { }
    document.addEventListener('click', function (e) {
        var btn = e.target.closest ? e.target.closest('[data-theme-toggle]') : null;
        if (!btn) return;
        e.preventDefault();
        var cur = document.documentElement.getAttribute('data-theme');
        var sysDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        var effective = cur || (sysDark ? 'dark' : 'light');
        var next = effective === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem(THEME_KEY, next); } catch (_) { }
        applyTheme(next);
    }, true);
    function hookTheme() {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener('enhancedload', function () { applyTheme(localStorage.getItem(THEME_KEY)); });
        } else setTimeout(hookTheme, 200);
    }
    hookTheme();
})();
