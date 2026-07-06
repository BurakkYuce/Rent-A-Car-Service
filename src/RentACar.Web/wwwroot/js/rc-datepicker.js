// Tarih/saat alanlarını flatpickr (themed) ile modernleştirir. KRİTİK: submission formatı ISO'da KALIR
// (date → "Y-m-d", datetime-local → "Y-m-d\TH:i") → FormParse.Date (DateTimeOffset.TryParse InvariantCulture)
// birebir parse eder. min/max attribute'ları minDate/maxDate'e taşınır (client kısıtı korunur; server guard zaten var).
// allowInput: klavye ile yazma serbest. Enhanced-navigation (static SSR kısmi swap) sonrası yeniden init edilir.
(function () {
    function initOne(el) {
        if (el._flatpickr || el.dataset.noFp === "true") return;
        var isDateTime = (el.getAttribute("type") || "").toLowerCase() === "datetime-local";
        try {
            flatpickr(el, {
                locale: "tr",
                allowInput: true,
                enableTime: isDateTime,
                time_24hr: true,
                dateFormat: isDateTime ? "Y-m-d\\TH:i" : "Y-m-d",
                minDate: el.getAttribute("min") || null,
                maxDate: el.getAttribute("max") || null
            });
        } catch (e) { /* flatpickr yoksa/başarısızsa native input olduğu gibi kalır (graceful) */ }
    }
    function initAll() {
        if (typeof flatpickr === "undefined") return;
        document.querySelectorAll('input[type="date"], input[type="datetime-local"]').forEach(initOne);
    }
    // İlk yükleme
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initAll);
    else initAll();
    // Blazor enhanced-navigation (tam reload olmadan içerik swap) → yeni inputları init et
    function hookBlazor() {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener("enhancedload", initAll);
        } else { setTimeout(hookBlazor, 200); }
    }
    hookBlazor();
})();
