// Global UI davranışları (CSP script-src 'self' uyumlu — inline on* handler YOK).
// Eskiden razor'larda inline `onsubmit="return confirm('...')"` ve `onclick="this.select()"` vardı;
// bunlar CSP'de script-src 'unsafe-inline' gerektiriyordu. Artık öznitelikle işaretlenip burada bağlanır:
//   <form data-confirm="Mesaj">             → submit'te confirm(); iptalde submit engellenir.
//   <button type="submit" data-confirm="…"> → aynısı, yalnız O butonla gönderilince (butonun mesajı kazanır).
//   <form data-kilit="yok">                 → çift-gönderim kilidinden muaf (aşağıdaki bloğa bakın).
//   <input data-select-all>                 → tıklayınca içeriği seçilir (kopyalama kolaylığı).

/* ---------------------------------------------------------------------------
   ONAY + ÇİFT-GÖNDERİM KİLİDİ

   1) ONAY. data-confirm hem <form>'da hem GÖNDEREN butonda (e.submitter) okunur; ikisi de
      varsa BUTONUN mesajı kazanır (daha özgül olan o: aynı formda "Kaydet" sormaz, "Sil" sorar).
      Neden: yedi geri-alınamaz işlem data-confirm'ü <button>'a koymuştu — toplu fatura kesimi,
      cari bakiye düzeltme, otomatik tahsilat, gider ödemesi ve ücretli WhatsApp/e-posta/SMS
      testleri. Eski kod yalnız form[data-confirm]'e baktığı için bunlar HİÇ sormuyordu.
      e.submitter, `form="…"` özniteliğiyle formun DIŞINDAN bağlanan butonu da verir (kira
      mega-formundaki ikincil işlem deseni). Tüm güncel tarayıcılar destekler; Blazor'ın kendi
      enhanced-form işleyicisi de ona dayanır.
      İptal → preventDefault + stopPropagation ve KİLİT YOK (kullanıcı başka bir şey yapabilmeli).

   2) KİLİT. method=post bir gönderim GERÇEKTEN yola çıkınca formun gönder butonları kapatılır;
      çift tık ikinci bir POST (ikinci fatura, ikinci tahsilat) üretemez. Asıl güvence sunucu
      tarafındaki idempotency'dir; bu kilit onun önündeki kullanıcı-dostu ilk savunmadır.
      Kilitlenmeyenler:
        - GET formlar (süzgeç/arama/rapor dönemi) — tekrar gönderimi zararsızdır.
        - type="button" butonlar — fetch ile çalışır, hiç submit olayı üretmez (rc-kira-tabs.js).
        - data-kilit="yok" taşıyan form ya da gönderen buton (muafiyet, aşağıda).
        - Yeni pencereye giden gönderimler: target/formtarget="_blank" ya da adlı pencere/iframe.

   TUZAKLAR (hepsi aşağıda kodla karşılanıyor, harfleri kodda referans veriliyor):
   (a) Butonu submit olayının İÇİNDE kapatmak, gönderilen veriden onun name/value'sunu düşürür:
       tarayıcı gönderilecek alan listesini submit olayı BİTTİKTEN sonra kurar ve disabled
       kontroller o listeye girmez. Bu yüzden kilit setTimeout(0) ile ERTELENİR — zamanlayıcı
       çalıştığında liste kurulmuş, istek yola çıkmıştır. (Chromium'da ölçüldü: senkron kapatmada
       gövde `islem=…` alanı OLMADAN gitti; ertelenmiş kilitte alan gövdede. Depozito ekranındaki
       gibi tek formda birden çok gönder butonu olan yerlerde bu alan hangi işlemin yapılacağını
       taşır.)
   (b) "Gerçekten yola çıktı mı" ancak TÜM dinleyiciler çalıştıktan sonra bilinir: bu dinleyici
       capture'da İLK çalışır; başkaları (ör. rc-kira-tabs.js müsaitlik formu, Blazor'ın
       enhanced-form işleyicisi) kabarma aşamasında SONRA çalışır ve preventDefault edebilir.
       Ertelenmiş kontrol bu yüzden e.defaultPrevented'a bakar; iptal edilmiş gönderim kilitlenmez.
   (c) Sayfayı terk etmeyen gönderimler: yeni sekmeye giden (target=_blank) hiç kilitlenmez.
       (Bugün PDF yazdır/görüntüle ve Excel/CSV/PDF export'ları GET bağlantısıdır; hiçbir POST
       formu _blank değil, hiçbir POST ucu dosya dönmüyor — kural, yarın eklenecek biri için.)
       Dosya indiren bir POST (Content-Disposition: attachment) da sayfayı terk
       etmez ve bunu JS'ten ayırt etmek mümkün değildir → her kilit KILIT_EMNIYET_MS sonra
       kendiliğinden çözülür; buton sonsuza dek ölü kalmaz. Aynı emniyet, Chromium'da Ctrl/Cmd+tık
       ile sonucu arka sekmede açılan gönderimi de kapsar (olay bu tuşları taşımaz, ayırt
       edilemez). Bedeli: bu süreden UZUN süren bir POST, süre dolunca yeniden gönderilebilir
       (sunucu idempotency'si yine korur).
   (d) Geri tuşu: bfcache sayfayı kapalı butonlarıyla geri getirir → 'pageshow'da çözülür.
       Firefox bfcache kullanmadığında da kontrollerin disabled DURUMUNU geçmişten geri yükler;
       kapattığımız butona autocomplete="off" yazılır ki Firefox o durumu saklamasın.
   (e) Blazor enhanced navigation belgeyi değiştirmeden gövdeyi değiştirir → 'enhancedload'da
       tüm kilitler çözülür (hookTheme ile aynı yeniden-deneme deseni).
   (f) Sunucunun ZATEN disabled bastığı butona dokunulmaz; çözerken yalnız bizim kapattıklarımız
       (data-rc-kilitledi) açılır. Aksi halde çözme adımı, kullanıcıya kapalı tutulan bir butonu
       açardı.
   (g) Olay ile ertelenmiş kilit arasındaki BOŞLUK: tarayıcı kullanıcı girdisini zamanlayıcılardan
       önce işleyebilir. Sayfa meşgulken (ya da onay penceresi kapanırken) kuyruğa girmiş ikinci
       tık, setTimeout(0) çalışmadan gelir; buton henüz açık olduğundan ikinci bir submit doğar ve
       onay İKİNCİ KEZ sorulur (Playwright çift tıkıyla ölçüldü). Bu yüzden kilitlenecek
       gönderimin OLAYI hemen formda saklanır (_rcBekleyen); sonraki submit, önceki olay iptal
       EDİLMEMİŞSE yutulur. Önceki olayın dağıtımı bittiği için defaultPrevented'ı artık kesindir —
       zamanlamaya değil olgunun kendisine bakılır; iptal edilmiş bir gönderim yeniden denemeyi
       engellemez.

   MUAFİYET: <form data-kilit="yok"> ya da <button data-kilit="yok"> — sayfada kalıp art arda
   gönderilmesi GEREKEN bir form için. Koymadan önce sunucu ucunun idempotent olduğundan emin olun.
   Görsel durum: form aria-busy="true", gönderen buton .rc-gonderiliyor, kapatılan butonlar
   [data-rc-kilitledi] (stil app.css'te).
--------------------------------------------------------------------------- */
(function () {
    var KILIT_EMNIYET_MS = 10000;
    var KILITLI = 'data-rc-kilitli';     // form: gönderimi sürüyor
    var KILITLEDI = 'data-rc-kilitledi'; // buton: BİZ kapattık — çözerken yalnız bunlar açılır (f)
    var MESGUL = 'rc-gonderiliyor';      // gönderen butonun "meşgul" sınıfı
    var kilitliler = [];

    // Formun gönder butonları. form.elements, form="…" ile DIŞARIDAN bağlananları da içerir.
    // type="button"/"reset" burada elenir: fetch'le çalışan butonlara dokunulmaz.
    // <input type="image"> da gönderir (bugün repoda yok; eklenirse kilitsiz kalmasın).
    function gonderButonlari(form) {
        var sonuc = [];
        for (var i = 0; i < form.elements.length; i++) {
            var el = form.elements[i];
            if ((el.tagName === 'BUTTON' && el.type === 'submit') ||
                (el.tagName === 'INPUT' && (el.type === 'submit' || el.type === 'image'))) sonuc.push(el);
        }
        return sonuc;
    }

    // formmethod/formtarget gönderen butonda formunkini EZER (tarayıcı kuralı) — önce ona bakılır.
    function kilitlenirMi(form, gonderen) {
        if (form.getAttribute('data-kilit') === 'yok') return false;
        if (gonderen && gonderen.getAttribute('data-kilit') === 'yok') return false;
        var yontem = ((gonderen && gonderen.getAttribute('formmethod')) || form.getAttribute('method') || 'get')
            .trim().toLowerCase();
        if (yontem !== 'post') return false; // GET süzgeçleri asla kilitlenmez
        var hedef = ((gonderen && gonderen.getAttribute('formtarget')) || form.getAttribute('target') || '')
            .trim().toLowerCase();
        if (hedef === '_blank') return false;                // (c) yeni sekme: bu sayfa yerinde kalır
        if (hedef !== '' && hedef !== '_self') return false; // (c) adlı pencere/iframe: aynı sınıf
        return true;
    }

    function kilitle(form, gonderen) {
        // Ertelenen an içinde enhanced-nav sayfayı değiştirmiş olabilir.
        if (!form.isConnected || form.hasAttribute(KILITLI)) return;
        form.setAttribute(KILITLI, '');
        form.setAttribute('aria-busy', 'true');
        gonderButonlari(form).forEach(function (b) {
            if (b.disabled) return;                 // (f)
            b.disabled = true;
            b.setAttribute(KILITLEDI, '');
            b.setAttribute('autocomplete', 'off');  // (d) Firefox durum geri yüklemesi
        });
        if (gonderen) gonderen.classList.add(MESGUL);
        form._rcGonderen = gonderen;
        form._rcKilitSayaci = setTimeout(function () { coz(form); }, KILIT_EMNIYET_MS); // (c) emniyet
        kilitliler.push(form);
    }

    function coz(form) {
        clearTimeout(form._rcKilitSayaci);
        form.removeAttribute(KILITLI);
        form.removeAttribute('aria-busy');
        gonderButonlari(form).forEach(function (b) {
            if (!b.hasAttribute(KILITLEDI)) return; // (f) sunucunun kapattığı kapalı kalır
            b.disabled = false;
            b.removeAttribute(KILITLEDI);
        });
        if (form._rcGonderen) form._rcGonderen.classList.remove(MESGUL);
        form._rcGonderen = null;
        var i = kilitliler.indexOf(form);
        if (i >= 0) kilitliler.splice(i, 1);
    }

    function hepsiniCoz() {
        kilitliler.slice().forEach(coz);
    }

    // Capture: Blazor'ın ve sayfa betiklerinin (kabarma) dinleyicilerinden ÖNCE çalışır; onay
    // iptalinde olayı onlara hiç ulaştırmaz.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        var gonderen = e.submitter || null;

        // Kilitliyken gelen ikinci gönderim yutulur — onay da İKİNCİ KEZ sorulmaz. Butonlar zaten
        // kapalı; buraya butonsuz yollarla (tek alanlı formda Enter, requestSubmit) ya da kilit
        // kurulmadan önceki boşlukta kuyruktan gelen ikinci tıkla (g) gelinir.
        var onceki = form._rcBekleyen;
        if (form.hasAttribute(KILITLI) || (onceki && !onceki.defaultPrevented)) {
            e.preventDefault();
            e.stopPropagation();
            return;
        }

        // Gönderen butonun mesajı formunkini EZER (daha özgül olan o).
        var mesaj = (gonderen && gonderen.getAttribute('data-confirm')) || form.getAttribute('data-confirm');
        if (mesaj && !window.confirm(mesaj)) {
            e.preventDefault();
            e.stopPropagation();
            return; // iptal → KİLİT YOK
        }

        if (!kilitlenirMi(form, gonderen)) return;
        form._rcBekleyen = e; // (g) boşluk bekçisi — butonlara henüz DOKUNMADAN
        // (a) + (b): karar da kapatma da, tüm dinleyiciler çalışıp alan listesi kurulduktan SONRA.
        setTimeout(function () {
            if (form._rcBekleyen === e) form._rcBekleyen = null; // bundan sonrasını KILITLI taşır
            if (e.defaultPrevented) return;
            kilitle(form, gonderen);
        }, 0);
    }, true);

    // (d) bfcache'ten dönüş. Taze yüklemede liste zaten boş — koşulsuz çağırmak zararsız.
    window.addEventListener('pageshow', hepsiniCoz);

    // (e) Enhanced navigation sonrası. Blazor betiği henüz hazır değilse yeniden dene.
    function baglaKilitCozme() {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener('enhancedload', hepsiniCoz);
        } else setTimeout(baglaKilitCozme, 200);
    }
    baglaKilitCozme();
})();

(function () {
    // Hata bandındaki "Yenile". Eskiden `<a href=".">` idi; `<base href="/">` yüzünden "."
    // köke çözülüp kullanıcıyı bulunduğu sayfadan ana ekrana atıyordu.
    document.addEventListener('click', function (e) {
        if (e.target.closest && e.target.closest('[data-rc-reload]')) {
            e.preventDefault();
            location.reload();
        }
    }, true);

    // "Geri dön" — history.back(). <a href="#"> KULLANILAMAZ: base href yüzünden köke çözülür.
    document.addEventListener('click', function (e) {
        if (e.target.closest && e.target.closest('[data-rc-geri]')) {
            e.preventDefault();
            if (history.length > 1) history.back(); else location.assign('/');
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
