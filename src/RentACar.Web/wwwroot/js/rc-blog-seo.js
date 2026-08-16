// Blog SEO alanları — canlı arama-sonucu önizlemesi + karakter sayacı.
// CSP script-src 'self' uyumlu: harici dosya, inline <script>/on* handler YOK (rc-web-ilan.js deseni).
//
// İLERİCİ ZENGİNLEŞTİRME: bu dosya yüklenmese de ekran EKSİKSİZ çalışır. Önizleme kutusu ve
// sayaçlar SUNUCUDAN kaydedilmiş değerlerle dolu basılır (SeoAlanlari.razor); JS yalnızca
// kullanıcı yazarken bunları tazeler. Hiçbir alan JS'e bağımlı değildir.
//
// SUNUCU KURALLARININ AYNASI (SeoAlanlari.razor + PublicSite/BlogYaziDetay.razor):
//   başlık   = SeoBaslik  || Baslik
//   açıklama = MetaAciklama || Ozet || gövdenin ilk cümleleri
// Aynısını burada da yazmak zorundayız (statik SSR'de sunucuya sormadan gösteriyoruz); zincir
// değişirse İKİ yer birden güncellenmeli — bu yüzden ikisinde de aynı yorum duruyor.
//
// SAYAÇ SINIRI TAVSİYEDİR: aşılınca alan uyarı rengine döner ama KAYIT ENGELLENMEZ. Sebep:
// arama motoru uzun metni yalnızca sonuç listesinde kırpar, içerik kaybolmaz; yazarın metnini
// zorla kesmek ya da kaydı reddetmek gerçek bir hatayı değil bir stil tercihini dayatmak olurdu.
(function () {
    var kutular = document.querySelectorAll('[data-seo]');
    if (!kutular.length) return;

    // Gövdeden özet: sunucudaki IcerikMetni.Ozet ile aynı fikir — işaretleyiciler ayıklanır,
    // ilk 160 karakter KELİME ortasından kesilmeden alınır.
    function govdeOzet(metin, uzunluk) {
        var duz = (metin || '')
            .split('\n')
            .map(function (s) { return s.replace(/^\s*#{2,3}\s*/, '').trim(); })
            .filter(function (s) { return s.length > 0; })
            .join(' ')
            .trim();
        if (!duz) return '';
        if (duz.length <= uzunluk) return duz;
        var kes = duz.slice(0, uzunluk);
        var bosluk = kes.lastIndexOf(' ');
        return (bosluk > uzunluk / 2 ? kes.slice(0, bosluk) : kes).replace(/[,.;:]+$/, '') + '…';
    }

    // Önizleme adresi için KABA slug. Gerçek slug'ı SUNUCU üretir (Türkçe normalizasyon +
    // benzersizleştirme); burada amaç yalnız "adres aşağı yukarı böyle olacak" hissi vermek.
    var TR = { 'ç': 'c', 'ğ': 'g', 'ı': 'i', 'i': 'i', 'ö': 'o', 'ş': 's', 'ü': 'u', 'İ': 'i', 'I': 'i' };
    function slugKaba(s) {
        return (s || '')
            .replace(/[çğıiöşüİI]/g, function (h) { return TR[h] || h; })
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    function metin(el) { return el && typeof el.value === 'string' ? el.value.trim() : ''; }

    Array.prototype.forEach.call(kutular, function (kutu) {
        var form = kutu.closest('form');
        if (!form) return;

        // Kaynak alanlar ANA formdan okunur (SEO kutusu formun bir parçasıdır, ayrı form DEĞİL).
        var baslik = form.querySelector('[name="baslik"]');
        var ozet = form.querySelector('[name="ozet"]');
        var icerik = form.querySelector('[name="icerik"]');
        // Yayınlanmış yazıda adres alanı disabled ve name'siz basılıyor → data-* ile bulunur.
        var slug = form.querySelector('[data-seo-slug]');

        var seoBaslik = kutu.querySelector('[data-seo-alan="seoBaslik"]');
        var metaAciklama = kutu.querySelector('[data-seo-alan="metaAciklama"]');

        var ozBaslik = kutu.querySelector('[data-seo-oniz="baslik"]');
        var ozAdres = kutu.querySelector('[data-seo-oniz="adres"]');
        var ozAciklama = kutu.querySelector('[data-seo-oniz="aciklama"]');
        var sayaclar = kutu.querySelectorAll('[data-seo-sayac]');

        function sayacTazele() {
            Array.prototype.forEach.call(sayaclar, function (s) {
                var ad = s.getAttribute('data-seo-sayac');
                var limit = parseInt(s.getAttribute('data-seo-limit') || '0', 10);
                var kaynak = ad === 'seoBaslik' ? seoBaslik : metaAciklama;
                var uz = kaynak ? kaynak.value.length : 0;
                s.textContent = uz + ' / ' + limit;
                // Sınır aşımı UYARIDIR, engel değil: submit'e dokunulmaz.
                s.classList.toggle('uyari', limit > 0 && uz > limit);
            });
        }

        function onizlemeTazele() {
            if (ozBaslik) {
                ozBaslik.textContent = metin(seoBaslik) || metin(baslik) || '(başlık yok)';
            }
            if (ozAciklama) {
                ozAciklama.textContent = metin(metaAciklama) || metin(ozet) ||
                    govdeOzet(icerik ? icerik.value : '', 160) ||
                    '(açıklama yok — arama motoru sayfadan rastgele bir cümle seçer)';
            }
            if (ozAdres) {
                var s = metin(slug) || slugKaba(metin(baslik)) || 'yazi-adresi';
                ozAdres.textContent = 'siteniz.com/blog/' + s;
            }
        }

        function tazele() { sayacTazele(); onizlemeTazele(); }

        [baslik, ozet, icerik, slug, seoBaslik, metaAciklama].forEach(function (el) {
            if (el) el.addEventListener('input', tazele);
        });

        tazele(); // sunucudan gelen hâli doğrula/hizala (JS zinciriyle sunucu zinciri aynı olmalı)
    });
})();
