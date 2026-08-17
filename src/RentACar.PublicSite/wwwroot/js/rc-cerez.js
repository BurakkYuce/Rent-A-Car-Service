/* =============================================================================
   Çerez bilgilendirme şeridi — halka açık site.

   NEDEN AYRI DOSYA: PublicSite CSP'si `script-src 'self'` (Program.cs) — inline
   <script> ve inline `onclick` tarayıcı tarafından BLOKLANIR, sessizce çalışmaz.
   Bu yüzden hem betik hem olay bağlama (addEventListener) harici dosyada.

   NEDEN ŞERİT SUNUCUDAN `hidden` GELİR: JS kapalıysa/inmezse şerit HİÇ görünmez ve
   sayfa tam çalışır (site %100 statik SSR). Tersi olsaydı (görünür başlayıp JS ile
   gizlemek) JS'siz ziyaretçi kapatılamayan bir şeritle kalırdı.

   DEPOLAMA: `localStorage` — bilinçli olarak çerez DEĞİL. Kararı çerezle saklamak,
   "yalnızca zorunlu çerez var" mesajını kendi kendine yalanlardı.
   ============================================================================= */
(() => {
  'use strict';

  // SÜRÜMLÜ anahtar. Siteye ileride gerçek bir izin gerektiren çerez (analitik, reklam,
  // gömülü harita) eklenirse anahtar v2 olur → eski "okudum" kaydı yeni izni KAPSAMAZ ve
  // şerit herkese yeniden gösterilir. Sürümsüz anahtar, sessizce kapsam genişletirdi.
  const ANAHTAR = 'rc-cerez-bildirim-v1';

  // localStorage gizli sekmede / depolama kapalıyken ERİŞİMDE bile istisna atabilir.
  // Patlarsa: karar saklanamaz, şerit bu ziyarette gösterilir ve kapatılabilir — sayfa bozulmaz.
  function oku() {
    try { return window.localStorage.getItem(ANAHTAR); } catch { return null; }
  }

  function yaz(deger) {
    try { window.localStorage.setItem(ANAHTAR, deger); } catch { /* depolama yok: sessiz geç */ }
  }

  // ---- Gövde telafi boşluğu -------------------------------------------------
  // Şerit `position: fixed` → normal akıştan ÇIKAR ve sayfanın SON içeriğinin (alt bilgi,
  // kapanış CTA'sı, "Teklif İste" düğmesi) üzerine biner. Ölçüldü: mobilde şerit ekranın
  // %43'ünü kaplıyordu ve altındaki her şey ŞERİT KAPATILANA KADAR tıklanamıyordu.
  //
  // Telafi neden JS'te: yükseklik metin uzunluğuna, ekran genişliğine ve font yüklenmesine
  // göre değişiyor (375px'te ~3 satır, 320px'te ~4). CSS'e sabit bir değer yazmak dar
  // ekranda yetersiz, geniş ekranda gereksiz boşluk demekti. Gerçek yüksekliği ölçüp
  // `--cerez-bosluk` değişkenine yazıyoruz; site.css'te `body { padding-bottom: var(...) }`.
  //
  // CSP: `style-src` denetimi inline <style> etiketi ve `style` ÖZNİTELİĞİ içindir;
  // `element.style.setProperty` bir CSSOM çağrısıdır ve denetime girmez → katı CSP'de çalışır.
  const ALT_PAY = 24;                // şeridin kendi `bottom` boşluğu (--s2 = 16px) + nefes payı
  let sonYukseklik = -1;             // aynı değeri tekrar yazmayı önler (ResizeObserver döngüsü)

  function bosluguYaz(serit) {
    const y = serit.offsetHeight;
    if (y === sonYukseklik) return;
    sonYukseklik = y;
    document.documentElement.style.setProperty('--cerez-bosluk', (y + ALT_PAY) + 'px');
  }

  function bosluguSil() {
    sonYukseklik = -1;
    document.documentElement.style.removeProperty('--cerez-bosluk');
  }

  function kur() {
    const serit = document.getElementById('cerez-serit');
    if (!serit) return;              // şerit basılmamış (ör. gelecekte kapatılırsa) → iş yok
    if (oku()) return;               // daha önce kapatılmış → `hidden` kalır, hiç görünmez

    const kapat = serit.querySelector('[data-cerez-kapat]');
    if (kapat) {
      kapat.addEventListener('click', () => {
        yaz('okundu');
        serit.hidden = true;
        bosluguSil();                // şerit gidince telafi boşluğu da gitmeli
      });
    }

    serit.hidden = false;            // görünürlük EN SON: buton bağlanmadan şerit açılmasın
    bosluguYaz(serit);

    // Ekran döndüğünde / pencere daraldığında satır sayısı değişir → boşluk bayatlar.
    // ResizeObserver şeridin KENDİ kutusunu izler: `resize` olayının kaçırdığı geç font
    // yüklemesi kaynaklı reflow'u da yakalar. Yoksa `resize`'a düşülür.
    if ('ResizeObserver' in window) {
      new ResizeObserver(() => { if (!serit.hidden) bosluguYaz(serit); }).observe(serit);
    } else {
      window.addEventListener('resize', () => { if (!serit.hidden) bosluguYaz(serit); });
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', kur);
  } else {
    kur();
  }
})();
