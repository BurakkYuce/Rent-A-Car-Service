import { apiHatasinaCevir } from '@core/api/api-hatasi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

/**
 * Giriş hatası → kullanıcı mesajı. `dogrulama` HER ZAMAN genel metin: hangi alanın yanlış olduğu
 * söylenmez (firma/kullanıcı keşfi olmasın), sunucu ayrıntısı gösterilmez.
 */
export function girisHataMesaji(hata: unknown): CeviriAnahtari {
  switch (apiHatasinaCevir(hata).kod) {
    case 'dogrulama':
      return 'oturum.giris.hata.hatali';
    case 'cok_istek':
      return 'oturum.giris.hata.cokIstek';
    case 'kiraci_kapali':
      return 'oturum.giris.hata.kiraciKapali';
    case 'ag':
      return 'oturum.giris.hata.ag';
    default:
      return 'oturum.giris.hata.genel';
  }
}

/**
 * Girişten sonraki dönüş adresi (`returnUrl`). YALNIZ uygulama içi yol kabul edilir: `/` ile başlar,
 * `//` ya da `/\` ile başlamaz (protokol-göreli dış adres), ters eğik çizgi ve kontrol karakteri yok.
 * `/app/...` biçimi (Blazor'dan gelen tam yol) router yoluna çevrilir. Aksi halde `/`.
 * Gezinme `router.navigateByUrl` ile yapılır — dış adrese zaten gidemez; bu ikinci savunma.
 */
export function guvenliDonusAdresi(deger: unknown): string {
  if (typeof deger !== 'string') return '/';
  let adres = deger.trim();
  if (adres === '/app' || adres.startsWith('/app/') || adres.startsWith('/app?')) {
    adres = adres.slice('/app'.length) || '/';
    if (!adres.startsWith('/')) adres = `/${adres}`;
  }
  if (!adres.startsWith('/') || adres.startsWith('//') || adres.includes('\\')) return '/';
  for (let i = 0; i < adres.length; i++) {
    const kod = adres.charCodeAt(i);
    if (kod < 0x20 || kod === 0x7f) return '/';
  }
  // Giriş sayfasına dönüş döngü olur.
  if (adres === '/giris' || adres.startsWith('/giris?') || adres.startsWith('/giris/')) return '/';
  return adres;
}
