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

    // ---- ANINDA yeni müşteri: fetch ile cari aç, sayfa yenilenmeden seç (form durumu korunur) ----
    function bindYeniMusteri() {
        var btn = document.querySelector('[data-yeni-musteri-kaydet]');
        var form = document.getElementById('kira-form');
        if (!btn || !form || btn._kfBound) return;
        btn._kfBound = true;
        btn.addEventListener('click', async function () {
            var msg = document.querySelector('[data-yeni-musteri-mesaj]');
            function de(t, cls) { if (msg) { msg.textContent = t; msg.className = cls + ' small'; } }
            var body = new URLSearchParams();
            form.querySelectorAll('input[name^="yeni"]').forEach(function (i) { if (i.value) body.set(i.name, i.value); });
            var tok = form.querySelector('input[name="__RequestVerificationToken"]');
            if (tok) body.set('__RequestVerificationToken', tok.value); // prod antiforgery
            btn.disabled = true;
            try {
                var r = await fetch('/kiralar/musteri-olustur', { method: 'POST', body: body, headers: { 'Accept': 'application/json' } });
                var d = await r.json();
                if (!d.ok) { de(d.hata || 'Kaydedilemedi.', 'error'); return; }
                // musteriId + 2.sürücü select'lerine ekle; müşteride SEÇ
                ['musteriId', 'ikinciSurucuId'].forEach(function (n) {
                    var sel = form.querySelector('select[name="' + n + '"]');
                    if (!sel) return;
                    var opt = document.createElement('option');
                    opt.value = d.id;
                    opt.textContent = d.ad;
                    sel.appendChild(opt);
                    if (n === 'musteriId') { sel.value = d.id; sel.dispatchEvent(new Event('change', { bubbles: true })); }
                });
                // Hızlı Giriş arama datalist'i + kutu görüntüsü (KiraFormVm.MusteriGoruntu deseniyle)
                var goruntu = d.ad + ' · #' + String(d.id).slice(0, 8);
                var dl = document.getElementById('dl-kf-musteri');
                if (dl) { var o = document.createElement('option'); o.value = goruntu; o.setAttribute('data-id', d.id); dl.appendChild(o); }
                var arama = document.querySelector('[data-lookup-target="musteriId"]');
                if (arama) arama.value = goruntu;
                // Yeni-müşteri alanlarını temizle (flatpickr-farkındalıklı)
                form.querySelectorAll('input[name^="yeni"]').forEach(function (i) {
                    if (i._flatpickr) i._flatpickr.clear(); else i.value = '';
                });
                de('Müşteri kaydedildi ve seçildi: ' + d.ad, 'ok');
            } catch (e) {
                de('Kaydedilemedi (bağlantı hatası).', 'error');
            } finally { btn.disabled = false; }
        });
    }

    // ---- Müsaitlik GET yenilemesinde müşteri seçimini koru (hidden musteriId → query → preselect) ----
    function bindMusaitKoru() {
        var mf = document.getElementById('musait-form');
        var form = document.getElementById('kira-form');
        if (!mf || !form || mf._kfBound) return;
        mf._kfBound = true;
        mf.addEventListener('submit', function () {
            var sel = form.querySelector('select[name="musteriId"]');
            var h = mf.querySelector('input[name="musteriId"]');
            if (sel && h) h.value = sel.value;
        });
    }

    // ---- PDF Yazdır: gizli iframe'e inline PDF yükle → print (indirme klasörüne dokunmadan) ----
    function bindPdfYazdir() {
        document.querySelectorAll('[data-pdf-yazdir]').forEach(function (a) {
            if (a._kfBound) return;
            a._kfBound = true;
            a.addEventListener('click', function (e) {
                e.preventDefault();
                var url = a.getAttribute('data-pdf-yazdir');
                var fr = document.createElement('iframe');
                fr.style.display = 'none';
                fr.src = url;
                fr.onload = function () {
                    try { fr.contentWindow.focus(); fr.contentWindow.print(); }
                    catch (_) { window.open(url, '_blank'); } // görüntüleyici engellerse: aç, oradan yazdır
                };
                document.body.appendChild(fr);
            });
        });
    }

    // ---- Paylaşım barı: girilen numaraya WhatsApp, girilen adrese GMAIL compose (hazır özet metinle) ----
    function bindPaylas() {
        var bar = document.querySelector('[data-paylas]');
        if (!bar || bar._kfBound) return;
        bar._kfBound = true;
        var mesaj = bar.getAttribute('data-mesaj') || '';
        var konu = bar.getAttribute('data-konu') || '';
        function hata(t) { var el = bar.querySelector('[data-paylas-hata]'); if (el) el.textContent = t; }
        // Numara normalizasyonu (sunucudaki eski WaLink kuralları): 00+ülke → önek at; 05xx/5xx → 90'lı TR
        function normalizeTel(tel) {
            var d = (tel || '').replace(/\D/g, '');
            if (d.indexOf('00') === 0) d = d.slice(2);
            else if (d.indexOf('0') === 0) d = '9' + d;
            else if (d.indexOf('5') === 0) d = '90' + d;
            return (d.length >= 10 && d.length <= 15) ? d : null;
        }
        bar.querySelector('[data-paylas-wa]').addEventListener('click', function () {
            var d = normalizeTel(bar.querySelector('[data-paylas-tel]').value);
            if (!d) { hata('Geçerli bir GSM girin (örn. 05xx xxx xx xx).'); return; }
            hata('');
            window.open('https://wa.me/' + d + '?text=' + encodeURIComponent(mesaj), '_blank', 'noopener');
        });
        bar.querySelector('[data-paylas-gmail]').addEventListener('click', function () {
            var mail = (bar.querySelector('[data-paylas-mail]').value || '').trim();
            if (!mail || mail.indexOf('@') < 1) { hata('Geçerli bir e-posta adresi girin.'); return; }
            hata('');
            window.open('https://mail.google.com/mail/?view=cm&fs=1&to=' + encodeURIComponent(mail) +
                '&su=' + encodeURIComponent(konu) + '&body=' + encodeURIComponent(mesaj), '_blank', 'noopener');
        });
    }

    function bind() {
        bindTabs();
        bindInvalid();
        bindMirrors();
        bindLookups();
        bindDateRange();
        bindYeniMusteri();
        bindMusaitKoru();
        bindPdfYazdir();
        bindPaylas();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bind);
    else bind();
    function hookBlazor() {
        if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener('enhancedload', bind);
        else setTimeout(hookBlazor, 200);
    }
    hookBlazor();
})();
