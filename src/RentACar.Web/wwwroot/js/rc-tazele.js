/* ---------------------------------------------------------------------------
   SAYFA OTOMATİK TAZELEME

   Neden var: canlı sayaç gösteren panolar (Panel, Araç Durum) periyodik olarak
   tazelenmeli. Eskiden bu `<meta http-equiv="refresh" content="120">` ile yapılıyordu.

   Neden meta refresh KALDIRILDI (gerçek hata): Blazor'ın enhanced navigation'ı
   sayfalar arasında GERÇEK belge navigasyonu yapmaz — `history.pushState` ile gövdeyi
   değiştirir. Tarayıcının bekleyen meta-refresh zamanlayıcısı ise BELGE seviyesindedir
   ve yalnız gerçek navigasyonda iptal olur. Sonuç: kullanıcı Panel'de 100 sn durup
   "Yeni Kira"ya geçtiğinde, formu doldururken 20. saniyede sayfa Panel'e (`/`) fırlıyor
   ve girilen tüm veri gidiyordu. Şikayet "durduk yere ana ekrana atıyor" idi.

   Buradaki çözüm: zamanlayıcıyı JS'te tutuyoruz ve `enhancedload`'da ÖNCE iptal edip
   sonra yeni sayfada yeniden kuruyoruz. Sayfa terk edilince tazeleme ölür.

   Kullanım:  <div data-rc-tazele="120">   → o sayfa 120 sn'de bir tazelenir.

   Not: `location.reload()` kullanılır. `location.href = '...'` YAZILMAZ — bu repoda
   `<base href="/">` var ve göreli/boş hedefler köke çözülüyor (aynı hatanın kardeşi).
--------------------------------------------------------------------------- */
(function () {
    var timer = null;

    function iptal() {
        if (timer !== null) {
            clearTimeout(timer);
            timer = null;
        }
    }

    // Kullanıcı bir alana yazıyorsa tazeleme YAPILMAZ, ertelenir. Pano üzerinde
    // satır-içi hızlı tahsilat formu var; ortasında reload girileni siler.
    function yaziyorMu() {
        var el = document.activeElement;
        if (!el || !el.tagName) return false;
        var t = el.tagName.toLowerCase();
        return t === 'input' || t === 'textarea' || t === 'select' || el.isContentEditable === true;
    }

    function kur() {
        iptal();

        var el = document.querySelector('[data-rc-tazele]');
        if (!el) return;

        var sn = parseInt(el.getAttribute('data-rc-tazele'), 10);
        if (!isFinite(sn) || sn < 5) return;   // saçma/eksik değerde tazeleme yok

        timer = setTimeout(function () {
            timer = null;
            // Yazıyorsa ya da bir POST gönderimi sürüyorsa (rc-ui.js çift-gönderim kilidi
            // form[data-rc-kilitli] yazar) bir tur daha bekle: reload, yola çıkmış gönderimin
            // navigasyonunu iptal eder — sunucu yazmış olabilir ama kullanıcı sonucu görmez
            // ve tekrar dener. Gönderimden sonra odak butonda olduğu için yaziyorMu() yakalamaz.
            if (yaziyorMu() || document.querySelector('form[data-rc-kilitli]')) { kur(); return; }
            location.reload();
        }, sn * 1000);
    }

    // Gezinme sonrası yeniden değerlendir: eski sayfanın zamanlayıcısı iptal olur,
    // yeni sayfa öznitelik taşıyorsa kendi zamanlayıcısı kurulur.
    function baglaGezinme() {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener('enhancedload', kur);
        } else setTimeout(baglaGezinme, 200);
    }

    kur();
    baglaGezinme();
})();
