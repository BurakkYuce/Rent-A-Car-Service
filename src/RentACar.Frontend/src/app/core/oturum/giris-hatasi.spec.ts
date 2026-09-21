import { HttpErrorResponse } from '@angular/common/http';

import { girisHataMesaji, guvenliDonusAdresi } from './giris-hatasi';

const hata = (status: number, kod?: string, detail = 'Sunucu ayrıntısı') =>
  new HttpErrorResponse({ status, error: kod ? { status, kod, detail } : null });

describe('girisHataMesaji', () => {
  it('dogrulama her zaman genel metin (hangi alanın yanlış olduğu söylenmez)', () => {
    expect(girisHataMesaji(hata(400, 'dogrulama', 'Kullanıcı bulunamadı'))).toBe(
      'oturum.giris.hata.hatali',
    );
  });

  it('cok_istek, kiraci_kapali, ağ ve diğerleri', () => {
    expect(girisHataMesaji(hata(429, 'cok_istek'))).toBe('oturum.giris.hata.cokIstek');
    expect(girisHataMesaji(hata(401, 'kiraci_kapali'))).toBe('oturum.giris.hata.kiraciKapali');
    expect(girisHataMesaji(hata(0))).toBe('oturum.giris.hata.ag');
    expect(girisHataMesaji(hata(500))).toBe('oturum.giris.hata.genel');
    expect(girisHataMesaji(new Error('x'))).toBe('oturum.giris.hata.genel');
  });
});

describe('guvenliDonusAdresi (yalnız uygulama içi yol)', () => {
  it.each([
    ['/kiralar/5?sekme=odeme', '/kiralar/5?sekme=odeme'],
    ['/app/kiralar/5', '/kiralar/5'],
    ['/app', '/'],
    ['/app/', '/'],
    ['/app?bilgi=x', '/?bilgi=x'],
    ['/', '/'],
  ])('%s → %s', (girdi, beklenen) => expect(guvenliDonusAdresi(girdi)).toBe(beklenen));

  it.each([
    ['//kotu.example/yol'],
    ['/\\kotu.example'],
    ['data:text/html,x'],
    ['javascript:alert(1)'],
    ['kiralar'],
    ['/kira\nlar'],
    ['/giris'],
    ['/giris?returnUrl=%2Fgiris'],
    [''],
  ])('%s reddedilir → /', (girdi) => expect(guvenliDonusAdresi(girdi)).toBe('/'));

  it('metin olmayan değer → /', () => {
    expect(guvenliDonusAdresi(null)).toBe('/');
    expect(guvenliDonusAdresi(['/kiralar'])).toBe('/');
  });
});
