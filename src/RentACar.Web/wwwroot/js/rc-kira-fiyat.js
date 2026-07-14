// Kira formu CANLI fiyat paneli — SUNUCU MOTORUNDAN (GET /kiralar/hesapla → KiraHesapService →
// PricingService/RentalQuoteEngine). Bu dosyada FORMÜL YOKTUR: eski istemci-aynası (round2/computeGun/KDV
// kopyası) kaldırıldı — UI'daki rakam ile testlerdeki golden değer AYNI motordan gelir (sessiz sapma biter).
// 300ms debounce + AbortController (eski istek iptal). Statik SSR: rc-datepicker.js bind deseni
// (DOMContentLoaded + Blazor enhancedload + form-instance guard).
(function () {
    var timer = null, ctrl = null;

    function form() { return document.getElementById('kira-form'); }
    function val(f, n) { var el = f.querySelector('[name="' + n + '"]'); return el ? el.value : ''; }
    function fmt(x) { return Number(x).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
    function fill(k, v) { document.querySelectorAll('[data-fp="' + k + '"]').forEach(function (el) { el.textContent = v; }); }
    function note(msg) { fill('not', msg); }
    function bosla() {
        ['gun', 'gunluk', 'net', 'kdv', 'ektoplam', 'toplam', 'kur', 'tl', 'kalan', 'dokum']
            .forEach(function (k) { fill(k, '—'); });
        document.querySelectorAll('[data-ekrow]').forEach(function (el) { el.textContent = '—'; });
    }

    // İşaretli ek hizmetler → "id:miktar,id:miktar"
    function ekParam(f) {
        var parts = [];
        f.querySelectorAll('input[name="ekSecim"]:checked').forEach(function (cb) {
            var m = f.querySelector('[name="ekMiktar_' + cb.value + '"]');
            var miktar = m && m.value ? m.value.replace(',', '.') : '1';
            parts.push(cb.value + ':' + miktar);
        });
        return parts.join(',');
    }

    async function run() {
        var f = form();
        if (!f) return;
        var bas = val(f, 'basTar'), bit = val(f, 'bitTar');
        if (!bas || !bit) { bosla(); note('Tarih aralığı girin.'); return; }

        if (ctrl) ctrl.abort();
        ctrl = new AbortController();
        var q = new URLSearchParams({ basTar: bas, bitTar: bit });
        // musteriId (A2 segment) + kampanyaKodu (A5) canlı hesapta da — önizleme == kayıt.
        ['vehicleId', 'gunlukUcret', 'fiyatTuru', 'doviz', 'cikisOfisi', 'musteriId', 'kampanyaKodu', 'ikinciSurucuId'].forEach(function (n) {
            var v = val(f, n);
            if (v) q.set(n, v);
        });
        var ek = ekParam(f);
        if (ek) q.set('ek', ek);
        var rid = f.getAttribute('data-rental-id'); // edit modu (PR-D): Kalan = GenelToplam − Tahsilat
        if (rid) q.set('rentalId', rid);

        try {
            var r = await fetch('/kiralar/hesapla?' + q.toString(), {
                signal: ctrl.signal, headers: { 'Accept': 'application/json' }
            });
            if (!r.ok) { note('Hesap alınamadı (' + r.status + ').'); return; }
            var d = await r.json();
            if (!d.ok) { bosla(); note(d.hata || 'Hesaplanamadı.'); return; }

            var dv = d.doviz || 'TL';
            fill('gun', d.gun);
            fill('gunluk', fmt(d.gunlukUcret) + ' ' + dv);
            fill('net', fmt(d.net) + ' ' + dv);
            fill('kdv', fmt(d.kdv) + ' ' + dv);
            fill('ektoplam', fmt(d.ekHizmetToplam) + ' ' + dv);
            fill('toplam', fmt(d.genelToplam) + ' ' + dv);
            fill('kur', d.kur != null ? fmt(d.kur) : '—');
            fill('tl', d.genelToplamTl != null ? fmt(d.genelToplamTl) + ' TL' : '—');
            fill('kalan', fmt(d.kalan) + ' ' + dv);

            // Motor dökümü (Otomatik tarife bileşenleri — bilgi)
            var dok = [];
            if (d.hediyeGun != null) dok.push('Hediye gün: ' + d.hediyeGun);
            if (d.faturalananGun != null) dok.push('Faturalanan gün: ' + d.faturalananGun);
            if (d.iskontoTutar != null) dok.push('İskonto: ' + fmt(d.iskontoTutar) + ' ' + dv);
            if (d.haftaSonuFark != null) dok.push('Hafta sonu farkı: ' + fmt(d.haftaSonuFark) + ' ' + dv);
            fill('dokum', dok.length ? dok.join(' · ') : '—');

            // Ek hizmet satır toplamları
            document.querySelectorAll('[data-ekrow]').forEach(function (el) { el.textContent = '—'; });
            (d.ekKalemler || []).forEach(function (k) {
                document.querySelectorAll('[data-ekrow="' + k.tanimId + '"]')
                    .forEach(function (el) { el.textContent = fmt(k.toplam) + ' ' + dv; });
            });

            var msg = 'Sunucu motoru hesabı — kayıtta da aynı motor çalışır.';
            if (d.notlar && d.notlar.length) msg = d.notlar.join(' · ') + ' — ' + msg;
            note(msg);
        } catch (e) {
            if (e.name !== 'AbortError') note('Hesap alınamadı.');
        }
    }

    function recalc() { clearTimeout(timer); timer = setTimeout(run, 300); }

    // ---- DÖNÜŞ canlı önizlemesi (edit modu) — GET /kiralar/donus-hesapla (ReturnMath; persist yok) ----
    var dTimer = null, dCtrl = null;
    function fillDp(k, v) { document.querySelectorAll('[data-dp="' + k + '"]').forEach(function (el) { el.textContent = v; }); }
    async function runDonus() {
        var f = form();
        var rid = f && f.getAttribute('data-rental-id');
        if (!rid) return;
        function dv(n) { var el = document.querySelector('[form="kira-donus"][name="' + n + '"]'); return el ? el.value : ''; }
        var km = dv('donusKm'), yakit = dv('donusYakit'), don = dv('gercekDonus');
        if (!km || !don) return;
        if (dCtrl) dCtrl.abort();
        dCtrl = new AbortController();
        var q = new URLSearchParams({ id: rid, donusKm: km, donusYakit: yakit || '0', gercekDonus: don });
        var kh = dv('kmHediye');
        if (kh) q.set('kmHediye', kh);
        try {
            var r = await fetch('/kiralar/donus-hesapla?' + q.toString(), { signal: dCtrl.signal, headers: { 'Accept': 'application/json' } });
            if (!r.ok) { fillDp('not', 'Önizleme alınamadı (' + r.status + ').'); return; }
            var d = await r.json();
            if (!d.ok) {
                ['kullanilan', 'fazlaKm', 'fazlaKmBedeli', 'eksikYakit', 'yakitBedeli', 'uzatmaGun', 'uzatmaBedeli', 'yeniGenelToplam', 'kalan']
                    .forEach(function (k) { fillDp(k, '—'); });
                fillDp('not', d.hata || 'Hesaplanamadı.');
                return;
            }
            fillDp('kullanilan', d.kullanilanKm);
            fillDp('fazlaKm', d.fazlaKm);
            fillDp('fazlaKmBedeli', fmt(d.fazlaKmBedeli));
            fillDp('eksikYakit', d.eksikYakit);
            fillDp('yakitBedeli', fmt(d.yakitBedeli));
            fillDp('uzatmaGun', d.uzatmaGun);
            fillDp('uzatmaBedeli', fmt(d.uzatmaBedeli));
            fillDp('yeniGenelToplam', fmt(d.yeniGenelToplam));
            fillDp('kalan', fmt(d.kalan));
            fillDp('not', 'Motor önizlemesi — dönüşte aynı hesap (ReturnMath) kaydedilir.');
        } catch (e) { if (e.name !== 'AbortError') fillDp('not', 'Önizleme alınamadı.'); }
    }
    function recalcDonus() { clearTimeout(dTimer); dTimer = setTimeout(runDonus, 300); }
    function bindDonus() {
        var els = document.querySelectorAll('[data-dp-in]');
        if (!els.length) return;
        els.forEach(function (el) {
            if (el._dpBound) return;
            el._dpBound = true;
            ['input', 'change'].forEach(function (ev) { el.addEventListener(ev, recalcDonus); });
        });
        runDonus();
    }

    function bind() {
        bindDonus(); // edit modunda dönüş önizlemesi
        var f = form();
        if (!f || f._fpBound) return;
        f._fpBound = true;
        // Edit modunda fiyat alanları donuk → fiyat fetch'i KAPALI (server değerleri render edilir).
        if (f.getAttribute('data-mode') === 'edit') return;
        // Delege dinleme: form içindeki HER alan (kanonikler + ek hizmet matris satırları).
        f.addEventListener('input', recalc);
        f.addEventListener('change', recalc);
        run();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bind);
    else bind();
    function hookBlazor() {
        if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener('enhancedload', bind);
        else setTimeout(hookBlazor, 200);
    }
    hookBlazor();
})();
