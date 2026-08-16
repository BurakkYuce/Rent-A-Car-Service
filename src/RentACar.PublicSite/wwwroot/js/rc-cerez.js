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

  function kur() {
    const serit = document.getElementById('cerez-serit');
    if (!serit) return;              // şerit basılmamış (ör. gelecekte kapatılırsa) → iş yok
    if (oku()) return;               // daha önce kapatılmış → `hidden` kalır, hiç görünmez

    const kapat = serit.querySelector('[data-cerez-kapat]');
    if (kapat) {
      kapat.addEventListener('click', () => {
        yaz('okundu');
        serit.hidden = true;
      });
    }

    serit.hidden = false;            // görünürlük EN SON: buton bağlanmadan şerit açılmasın
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', kur);
  } else {
    kur();
  }
})();
