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

/* ---------------------------------------------------------------------------
   MOBİL MENÜ ÇEKMECESİ (Faz 1)

   Neden burada: repoda tüm sayfa-JS'i harici dosyalarda ve `data-*` delegasyonuyla bağlanıyor —
   CSP `script-src 'self'` (inline script/onclick YOK). Global davranışların yeri bu dosya.

   Neden delegasyon: statik SSR + enhanced navigation'da sayfa gövdesi değişince doğrudan
   bağlanmış dinleyiciler kopar. `document` üzerinde delege edilen dinleyici her gezinmede
   çalışmaya devam eder — yeniden bağlama gerekmez.
--------------------------------------------------------------------------- */
(function () {
    var ACIK = 'acik';

    function perde() { return document.querySelector('.menu-perde'); }
    function acButonu() { return document.querySelector('[data-menu-ac]'); }

    function ayarla(acik) {
        document.body.setAttribute('data-menu', acik ? ACIK : '');
        var p = perde();
        if (p) p.hidden = !acik;
        var b = acButonu();
        if (b) b.setAttribute('aria-expanded', acik ? 'true' : 'false');

        // Odak yönetimi: açılınca çekmecenin ilk bağlantısına, kapanınca butona döner.
        // Bu olmadan klavye kullanıcısı menüyü açar ama odak arka planda kalır.
        if (acik) {
            var ilk = document.querySelector('#sb-menu a, #sb-menu button');
            if (ilk) ilk.focus();
        } else if (b) {
            b.focus();
        }
    }

    document.addEventListener('click', function (e) {
        if (!e.target.closest) return;
        if (e.target.closest('[data-menu-ac]')) { e.preventDefault(); ayarla(true); return; }
        if (e.target.closest('[data-menu-kapat]')) { ayarla(false); return; }
        // Çekmecedeki bir bağlantıya gidilince menü kapanmalı; aksi halde yeni sayfa
        // açık menünün ARKASINDA yüklenir ve kullanıcı boş ekrana bakar.
        if (document.body.getAttribute('data-menu') === ACIK && e.target.closest('#sb-menu a')) {
            ayarla(false);
        }
    }, true);

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && document.body.getAttribute('data-menu') === ACIK) ayarla(false);
    });

    // Masaüstüne genişletilince açık kalan çekmece, sabit menüyle üst üste binerdi.
    var mq = window.matchMedia('(max-width: 900px)');
    var mqDinle = mq.addEventListener ? mq.addEventListener.bind(mq, 'change') : mq.addListener.bind(mq);
    mqDinle(function (ev) { if (!(ev.matches)) ayarla(false); });

    // Gezinme sonrası: yeni sayfada menü KAPALI başlamalı (body özniteliği korunabiliyor).
    function baglaGezinme() {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener('enhancedload', function () { ayarla(false); });
        } else setTimeout(baglaGezinme, 200);
    }
    baglaGezinme();
})();
