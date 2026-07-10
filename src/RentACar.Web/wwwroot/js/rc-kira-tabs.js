// Kira mega-form istemci altyapısı (statik SSR — circuit YOK; rc-datepicker.js bind deseni):
//  1) Sekme mekanizması: [data-tabs=G] şeridindeki [data-tab=K] butonları, [data-panel-group=G][data-panel=K]
//     panellerini gösterir/gizler (hidden attr — DOM'da kalır, form submit'ine girer). Hash deep-link:
//     #sekme=fiyat&alt=aksesuar&fin=nakit.
//  2) invalid-yakalama: gizli paneldeki required alan submit'i sessizce öldürmesin — alanın sekmesine atla.
//  3) Ayna senkronu: [data-mirror=N] <-> [name=N] iki yönlü (flatpickr-farkındalıklı; değer-karşılaştırma
//     döngü guard'ı). Kanonik değişince 'input' event tetiklenir (canlı fiyat fetch'i dinler).
//  4) Arama (datalist) çözümü: [data-lookup-target=N] input'u, datalist option'ına BİREBİR eşleşirse
//     [name=N] hedefini option'ın data-id'siyle doldurur; araç kartı [data-veh=*] alanlarını option
//     data-*'larından günceller.
//  5) bitTar.min = basTar (tarih tutarlılığı; sunucu doğrulaması ayrıca var).
(function () {
    function setVal(el, v) {
        if (!el || el.value === v) return;
        if (el._flatpickr) el._flatpickr.setDate(v, false);
        el.value = v;
    }

    // ---- hash yardımcıları: #sekme=x&alt=y&fin=z ----
    function hashMap() {
        var m = {};
        location.hash.replace(/^#/, '').split('&').forEach(function (p) {
            var i = p.indexOf('=');
            if (i > 0) m[p.slice(0, i)] = decodeURIComponent(p.slice(i + 1));
        });
        return m;
    }
    function setHash(g, k) {
        var m = hashMap();
        m[g] = k;
        var s = Object.keys(m).map(function (x) { return x + '=' + encodeURIComponent(m[x]); }).join('&');
        // TAM yol verilir: çıplak '#...' Blazor enhanced-nav'da <base href="/">'e göre çözülüp
        // path'i köke düşürüyordu (canlı Playwright bulgusu) — pathname+search açıkça korunur.
        history.replaceState(null, '', location.pathname + location.search + '#' + s);
    }

    function activate(strip, key, updateHash) {
        var g = strip.getAttribute('data-tabs');
        strip.querySelectorAll('[data-tab]').forEach(function (b) {
            b.setAttribute('aria-selected', b.getAttribute('data-tab') === key ? 'true' : 'false');
        });
        document.querySelectorAll('[data-panel-group="' + g + '"]').forEach(function (p) {
            p.hidden = p.getAttribute('data-panel') !== key;
        });
        if (g === 'sekme') { // edit modunda POST-redirect aynı sekmeye dönsün (hidden alan → #sekme fragment)
            var sek = document.querySelector('#kira-form [data-kf-sekme]');
            if (sek) sek.value = key;
        }
        if (updateHash) setHash(g, key);
    }

    function bindTabs() {
        document.querySelectorAll('[data-tabs]').forEach(function (strip) {
            if (strip._kfBound) return;
            strip._kfBound = true;
            strip.querySelectorAll('[data-tab]').forEach(function (b) {
                b.addEventListener('click', function () { activate(strip, b.getAttribute('data-tab'), true); });
            });
            // İlk durum: hash'te varsa o, yoksa ilk buton.
            var m = hashMap();
            var g = strip.getAttribute('data-tabs');
            var first = strip.querySelector('[data-tab]');
            var key = m[g] || (first && first.getAttribute('data-tab'));
            if (key) activate(strip, key, false);
        });
    }

    // Gizli paneldeki invalid alan → önce dış sekme sonra iç alt-sekme açılır, alana odaklanılır.
    function bindInvalid() {
        if (document._kfInvalidBound) return;
        document._kfInvalidBound = true;
        document.addEventListener('invalid', function (e) {
            var chain = [];
            var p = e.target.closest('[data-panel-group]');
            while (p) {
                chain.unshift(p); // dıştan içe sırala
                p = p.parentElement ? p.parentElement.closest('[data-panel-group]') : null;
            }
            chain.forEach(function (panel) {
                var strip = document.querySelector('[data-tabs="' + panel.getAttribute('data-panel-group') + '"]');
                if (strip) activate(strip, panel.getAttribute('data-panel'), true);
            });
            if (chain.length) setTimeout(function () { try { e.target.focus(); } catch (_) { } }, 0);
        }, true);
    }

    // ---- ayna senkronu ----
    function bindMirrors() {
        var form = document.getElementById('kira-form');
        if (!form) return;
        document.querySelectorAll('[data-mirror]').forEach(function (mirror) {
            if (mirror._kfBound) return;
            mirror._kfBound = true;
            var name = mirror.getAttribute('data-mirror');
            var canon = form.querySelector('[name="' + name + '"]');
            if (!canon) return;
            setVal(mirror, canon.value); // ilk yüklemede kanonik değeri yansıt
            ['input', 'change'].forEach(function (ev) {
                mirror.addEventListener(ev, function () {
                    if (canon.value === mirror.value) return;
                    setVal(canon, mirror.value);
                    canon.dispatchEvent(new Event('input', { bubbles: true })); // fiyat fetch tetiklensin
                });
                canon.addEventListener(ev, function () { setVal(mirror, canon.value); });
            });
        });
    }

    // ---- datalist arama → id çözümü ----
    function bindLookups() {
        var form = document.getElementById('kira-form');
        if (!form) return;
        document.querySelectorAll('[data-lookup-target]').forEach(function (inp) {
            if (inp._kfBound) return;
            inp._kfBound = true;
            var target = form.querySelector('[name="' + inp.getAttribute('data-lookup-target') + '"]');
            var list = document.getElementById(inp.getAttribute('list'));
            if (!target || !list) return;
            function resolve() {
                var opt = Array.prototype.find.call(list.querySelectorAll('option'), function (o) {
                    return o.value === inp.value;
                });
                var id = opt ? (opt.getAttribute('data-id') || '') : '';
                if (target.value !== id) {
                    target.value = id;
                    target.dispatchEvent(new Event('change', { bubbles: true }));
                }
                // Araç bilgi kartı: option data-* → [data-veh=*] gri alanları
                if (opt) {
                    ['marka', 'tip', 'yil', 'vites', 'yakit', 'grup', 'segment', 'km', 'sube'].forEach(function (k) {
                        var v = opt.getAttribute('data-' + k);
                        if (v !== null) document.querySelectorAll('[data-veh="' + k + '"]')
                            .forEach(function (el) { el.textContent = v || '—'; });
                    });
                }
            }
            ['input', 'change'].forEach(function (ev) { inp.addEventListener(ev, resolve); });
        });
    }

    // ---- bitTar.min = basTar ----
    function bindDateRange() {
        var form = document.getElementById('kira-form');
        if (!form || form._kfDateBound) return;
        form._kfDateBound = true;
        var bas = form.querySelector('[name="basTar"]');
        var bit = form.querySelector('[name="bitTar"]');
        if (!bas || !bit) return;
        function apply() {
            if (!bas.value) return;
            bit.min = bas.value;
            if (bit._flatpickr) bit._flatpickr.set('minDate', bas.value);
        }
        ['input', 'change'].forEach(function (ev) { bas.addEventListener(ev, apply); });
        apply();
    }

    function bind() {
        bindTabs();
        bindInvalid();
        bindMirrors();
        bindLookups();
        bindDateRange();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bind);
    else bind();
    function hookBlazor() {
        if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener('enhancedload', bind);
        else setTimeout(hookBlazor, 200);
    }
    hookBlazor();
})();
