// Kira açma formunda CANLI fiyat önizlemesi (TAHMİNİ). Sunucu PricingService.KdvModuUygula ile birebir ayna:
//   gun = 24h tam blok + kısmi >= 2.9sa ? +1 (min 1); KDV %20 sabit; brüt Tutar moda göre.
// KESİN tutar sunucuda (PricingService). Statik SSR: enhanced-navigation sonrası yeniden bağlanır
// (rc-datepicker.js deseni). flatpickr datetime input'ları ISO değeri korur → new Date(value) çalışır.
(function () {
    // C# MidpointRounding.AwayFromZero eşdeğeri (yarım-kuruş yukarı; float-altı düzeltmesi için epsilon).
    function round2(x) {
        var s = x < 0 ? -1 : 1;
        return s * Math.round((Math.abs(x) + 1e-9) * 100) / 100;
    }
    function computeGun(bas, bit) {
        var t = (bit - bas) / 3600000; // saat
        if (!(t > 0)) return 1;
        var tam = Math.floor(t / 24);
        var kismi = t - tam * 24;
        return Math.max(1, tam + (kismi >= 2.9 ? 1 : 0));
    }
    function fmt(x) { return x.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }

    function recalc(form) {
        var out = {};
        ['gun', 'net', 'kdv', 'toplam', 'kalan', 'not'].forEach(function (k) { out[k] = form.querySelector('[data-fp=' + k + ']'); });
        if (!out.toplam) return;
        var uEl = form.querySelector('[name=gunlukUcret]');
        var basEl = form.querySelector('[name=basTar]');
        var bitEl = form.querySelector('[name=bitTar]');
        var mod = (form.querySelector('[name=fiyatTuru]') || {}).value || '';
        var sym = (form.querySelector('[name=doviz]') || {}).value || 'TL';

        var u = parseFloat(((uEl && uEl.value) || '').replace(',', '.'));
        var basD = basEl && basEl.value ? new Date(basEl.value) : null;
        var bitD = bitEl && bitEl.value ? new Date(bitEl.value) : null;
        var gun = (basD && bitD && !isNaN(basD) && !isNaN(bitD)) ? computeGun(basD, bitD) : null;

        if (out.gun) out.gun.textContent = gun != null ? gun : '—';

        function bosla(msg) {
            ['net', 'kdv', 'toplam', 'kalan'].forEach(function (k) { if (out[k]) out[k].textContent = '—'; });
            if (out.not) out.not.textContent = msg;
        }
        if (mod === 'Otomatik') { bosla('Otomatik: tutar sunucuda tarifeden hesaplanır.'); return; }
        if (isNaN(u) || gun == null) { bosla('Ücret ve tarih girin (tahmini).'); return; }

        var tutar;
        if (mod === 'Günlük') tutar = round2(gun * round2(u * 1.2));
        else if (mod === 'KDV Dahil Toplam') tutar = round2(u);
        else if (mod === 'Toplam') tutar = round2(u * 1.2);
        else tutar = round2(gun * u); // KDV Dahil Günlük / boş / bilinmeyen

        var net = round2(tutar / 1.2);
        var kdv = round2(tutar - net);
        if (out.net) out.net.textContent = fmt(net) + ' ' + sym;
        if (out.kdv) out.kdv.textContent = fmt(kdv) + ' ' + sym;
        if (out.toplam) out.toplam.textContent = fmt(tutar) + ' ' + sym;
        if (out.kalan) out.kalan.textContent = fmt(tutar) + ' ' + sym;
        var toplamNot = (mod === 'KDV Dahil Toplam' || mod === 'Toplam') ? '"Günlük Ücret" burada TOPLAM girdisidir. ' : '';
        if (out.not) out.not.textContent = toplamNot + 'Tahmini — kesin tutar kayıtta.';
    }

    function bind() {
        var form = document.getElementById('kira-create-form');
        if (!form || form._fpBound) return;
        form._fpBound = true;
        ['gunlukUcret', 'basTar', 'bitTar', 'fiyatTuru', 'doviz'].forEach(function (n) {
            var el = form.querySelector('[name=' + n + ']');
            if (el) {
                el.addEventListener('input', function () { recalc(form); });
                el.addEventListener('change', function () { recalc(form); });
            }
        });
        recalc(form);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bind);
    else bind();
    // Enhanced-navigation sonrası yeni form için yeniden bağla
    function hookBlazor() {
        if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener('enhancedload', bind);
        else setTimeout(hookBlazor, 200);
    }
    hookBlazor();
})();
