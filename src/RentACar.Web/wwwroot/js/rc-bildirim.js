/* ---------------------------------------------------------------------------
   İŞLEM SONUCU ŞERİDİ

   Sunucu tarafı `?bilgi=<mesaj>` ile döner (Common/Sonuc.cs), MainLayout şeridi render eder.
   Bu dosya iki şey yapar:
     1. Şeridi 5 sn sonra kapatır; tıklayınca hemen kapatır.
     2. `bilgi` parametresini URL'den düşürür → F5 / geri tuşunda mesaj TEKRAR görünmez.

   CSP: script-src 'self' → inline script yasak, bu yüzden harici dosya (App.razor'da <script src>).

   TUZAK — history.replaceState hedefi TAM YOL olmalı:
   `<base href="/">` yüzünden çıplak '?'/'#' hedefleri belge base'ine çözülür ve kullanıcıyı köke
   düşürür. Bu repoda aynı hata canlıda yaşandı (bkz. rc-tazele.js başlığı). Bu yüzden aşağıda
   daima `location.pathname + arananKisim + location.hash` yazılır — rc-kira-tabs.js setHash ile
   aynı disiplin.
--------------------------------------------------------------------------- */
(function () {
    var GIZLE_MS = 5000;

    function temizleUrl() {
        try {
            if (!window.history || !history.replaceState) return;
            var p = new URLSearchParams(location.search);
            if (!p.has('bilgi')) return;
            p.delete('bilgi');
            var q = p.toString();
            history.replaceState(null, '', location.pathname + (q ? '?' + q : '') + location.hash);
        } catch (_) { /* URL temizliği kozmetik: başarısız olursa şerit yine çalışır */ }
    }

    function kapat(el) {
        if (!el || !el.parentNode) return;
        el.parentNode.removeChild(el);
    }

    function kur() {
        var el = document.querySelector('[data-rc-bildirim]');
        if (!el || el._rcBound) return;
        el._rcBound = true;

        temizleUrl();
        var zamanlayici = setTimeout(function () { kapat(el); }, GIZLE_MS);
        el.addEventListener('click', function () { clearTimeout(zamanlayici); kapat(el); });
    }

    // Enhanced navigation'da gövde değişir → yeni sayfada şerit varsa yeniden bağlanmalı.
    function baglaGezinme() {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener('enhancedload', kur);
        } else setTimeout(baglaGezinme, 200);
    }

    kur();
    baglaGezinme();
})();
