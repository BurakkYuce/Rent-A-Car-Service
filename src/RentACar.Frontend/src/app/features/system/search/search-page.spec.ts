import { safeHitUrl } from './search-page';

/**
 * F11.2b güvenlik L1 (r304 probe'undan kalıcı): tarayıcı URL ayrıştırıcısı TAB/LF/CR'yi siler → "/\t/evil.com"
 * "//evil.com" olur ve başka kökene gider. Kontrol karakteri, boşluk, ters bölü, başka köken ya da bilinmeyen kök
 * bağlantı olmaz (null). Kök: sayfanın kendi origin'i (jsdom).
 */
describe('safeHitUrl', () => {
  it.each([
    '/\t/evil.com',
    '/\n/evil.com',
    '/\r/evil.com',
    '/\t\t/evil.com/x',
    '//evil.com',
    '/\\evil.com',
    'javascript:alert(1)',
    '/%2F%2Fevil.com',
    '/ /evil.com',
    '/kiralar/\u0000x',
    '/kiralar x',
    '/ayarlar',
    '/kiralarx/1',
    'kiralar/1',
    '',
  ])('reddedilir: %j', (u) => {
    expect(safeHitUrl(u)).toBeNull();
  });

  it.each([
    '/araclar/6f1c2c8e-0000-4000-8000-000000000001',
    '/cariler/6f1c2c8e-0000-4000-8000-000000000002',
    '/kiralar/6f1c2c8e-0000-4000-8000-000000000003',
    '/rezervasyonlar',
    '/faturalar',
  ])('arama sonucunun kendi yolu kabul edilir: %j', (u) => {
    expect(safeHitUrl(u)).toBe(u);
  });
});
