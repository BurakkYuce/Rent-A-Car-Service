import { HttpErrorResponse } from '@angular/common/http';

import { loginErrorMessage, postLoginTarget, safeReturnUrl } from './giris-hatasi';

const hata = (status: number, code?: string, detail = 'Sunucu ayrıntısı') =>
  new HttpErrorResponse({ status, error: code ? { status, kod: code, detail } : null });

describe('girisHataMesaji', () => {
  it('dogrulama her zaman genel metin (hangi alanın yanlış olduğu söylenmez)', () => {
    expect(loginErrorMessage(hata(400, 'dogrulama', 'Kullanıcı bulunamadı'))).toBe(
      'oturum.giris.hata.hatali',
    );
  });

  it('cok_istek, kiraci_kapali, ağ ve diğerleri', () => {
    expect(loginErrorMessage(hata(429, 'cok_istek'))).toBe('oturum.giris.hata.cokIstek');
    expect(loginErrorMessage(hata(401, 'kiraci_kapali'))).toBe('oturum.giris.hata.kiraciKapali');
    expect(loginErrorMessage(hata(0))).toBe('oturum.giris.hata.ag');
    expect(loginErrorMessage(hata(500))).toBe('oturum.giris.hata.genel');
    expect(loginErrorMessage(new Error('x'))).toBe('oturum.giris.hata.genel');
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
  ])('%s → %s', (input, expected) => expect(safeReturnUrl(input)).toBe(expected));

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
  ])('%s reddedilir → /', (input) => expect(safeReturnUrl(input)).toBe('/'));

  it('metin olmayan değer → /', () => {
    expect(safeReturnUrl(null)).toBe('/');
    expect(safeReturnUrl(['/kiralar'])).toBe('/');
  });
});

describe('girisSonrasiHedef (F4.6 tek giriş)', () => {
  const spa = (path: string) => ({ tur: 'spa', yol: path });
  const server = (address: string) => ({ tur: 'sunucu', adres: address });

  it.each([
    // pilot: /app dönüşü SPA içinde; dönüş yok / kök / dış adres / giriş döngüsü → Panel
    [true, null, spa('/panel')],
    [true, '/', spa('/panel')],
    [true, '/app', spa('/panel')],
    [true, '/app/kiralar/5?sekme=odeme', spa('/kiralar/5?sekme=odeme')],
    [true, '/app/giris?returnUrl=%2Fapp', spa('/panel')],
    [true, '//kotu.example', spa('/panel')],
    [true, 'javascript:alert(1)', spa('/panel')],
    // pilot: Blazor dönüşü sunucu kapısından (harita/GuvenliDonus sunucuda)
    [true, '/kiralar/yeni?varac=5', server('/login?ReturnUrl=%2Fkiralar%2Fyeni%3Fvarac%3D5')],
    [true, '/vehicles', server('/login?ReturnUrl=%2Fvehicles')],
    // pilot değil: yeni arayüz kapalı → Blazor
    [false, null, server('/')],
    [false, '/app/kiralar', server('/')],
    [false, '//kotu.example', server('/')],
    [false, '/vehicles?x=1', server('/login?ReturnUrl=%2Fvehicles%3Fx%3D1')],
  ])('pilot=%s, dönüş=%s', (pilot, returnInfo, expected) =>
    expect(postLoginTarget(pilot, returnInfo)).toEqual(expected),
  );
});
