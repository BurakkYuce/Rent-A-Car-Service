import { Subject, of, throwError } from 'rxjs';
import { ApiHatasi } from '../api/api-hatasi';
import { GonderimKilidi, yeniIslemAnahtari } from './gonderim-kilidi';

/** Sayaçlı üretici: anahtarlar tahmin edilebilir (`a1`, `a2`, …). */
function kilitKur(): { kilit: GonderimKilidi; anahtarlar: string[] } {
  let n = 0;
  const kilit = new GonderimKilidi(() => `a${++n}`);
  return { kilit, anahtarlar: [] };
}

const hata = (kod: 'dogrulama' | 'mukerrer' | 'cakisma', status: number) =>
  new ApiHatasi({ status, kod, detay: kod });

describe('GonderimKilidi — anahtar kuralı', () => {
  it('2xx sonrası anahtar yenilenir: sabit paneldeki ikinci meşru gönderim yeni anahtarla', () => {
    const { kilit, anahtarlar } = kilitKur();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a2']);
    expect(kilit.bekleyenAnahtar).toBeNull();
  });

  it('hata sonrası AYNI gönderimin yeniden denemesi aynı anahtarı kullanır', () => {
    const { kilit, anahtarlar } = kilitKur();
    kilit
      .gonder((a) => (anahtarlar.push(a), throwError(() => hata('dogrulama', 400))))
      .subscribe({
        error: () => undefined,
      });
    kilit
      .gonder((a) => (anahtarlar.push(a), throwError(() => new Error('ağ'))))
      .subscribe({
        error: () => undefined,
      });
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a1', 'a1', 'a2']);
  });

  it('409 mukerrer işlemi sonuçlandırır → anahtar yenilenir (otomatik yeniden gönderim yok)', () => {
    const { kilit, anahtarlar } = kilitKur();
    let cagri = 0;
    kilit
      .gonder((a) => (anahtarlar.push(a), cagri++, throwError(() => hata('mukerrer', 409))))
      .subscribe({ error: () => undefined });
    expect(cagri).toBe(1);
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a2']);
  });

  it('409 cakisma formu korur, anahtar KORUNUR (düzeltip aynı gönderim)', () => {
    const { kilit, anahtarlar } = kilitKur();
    kilit
      .gonder((a) => (anahtarlar.push(a), throwError(() => hata('cakisma', 409))))
      .subscribe({ error: () => undefined });
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a1']);
  });

  it('deterministik sunucu anahtarı DOKUNULMADAN geçer, istemci anahtarını tüketmez', () => {
    const { kilit, anahtarlar } = kilitKur();
    const tahsilat = '7d0c1f6e-3b9a-5c2e-8f41-0a9b8c7d6e5f';
    kilit
      .gonder((a) => (anahtarlar.push(a), throwError(() => new Error('ağ'))))
      .subscribe({
        error: () => undefined,
      });
    kilit
      .gonder((a) => (anahtarlar.push(a), of('ok')), { deterministikAnahtar: tahsilat })
      .subscribe();
    expect(anahtarlar).toEqual(['a1', tahsilat]);
    // Deterministik 2xx istemci anahtarını yenilemez: bekleyen istemci gönderimi hâlâ a1.
    expect(kilit.bekleyenAnahtar).toBe('a1');
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar.at(-1)).toBe('a1');
  });

  it('boş deterministik anahtar yok sayılır', () => {
    const { kilit, anahtarlar } = kilitKur();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok')), { deterministikAnahtar: '  ' }).subscribe();
    expect(anahtarlar).toEqual(['a1']);
  });

  it('istek uçarken ikinci gönderim yok sayılır (çift tık tek istek), bitince kilit açılır', () => {
    const { kilit } = kilitKur();
    const yanit = new Subject<string>();
    let cagri = 0;
    const istek = () => (cagri++, yanit);
    kilit.gonder(istek).subscribe();
    expect(kilit.gonderiliyor()).toBe(true);
    let ikinciTamamlandi = false;
    kilit.gonder(istek).subscribe({ complete: () => (ikinciTamamlandi = true) });
    expect(cagri).toBe(1);
    expect(ikinciTamamlandi).toBe(true);
    yanit.next('ok');
    yanit.complete();
    expect(kilit.gonderiliyor()).toBe(false);
    kilit.gonder(() => (cagri++, of('ok'))).subscribe();
    expect(cagri).toBe(2);
  });

  it('abonelik iptal edilirse kilit açılır, anahtar korunur (sonuç bilinmiyor)', () => {
    const { kilit, anahtarlar } = kilitKur();
    const abonelik = kilit.gonder((a) => (anahtarlar.push(a), new Subject<string>())).subscribe();
    abonelik.unsubscribe();
    expect(kilit.gonderiliyor()).toBe(false);
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a1']);
  });

  it('yenile(): form yeni kayda sıfırlanınca bir sonraki gönderim yeni anahtarla', () => {
    const { kilit, anahtarlar } = kilitKur();
    kilit
      .gonder((a) => (anahtarlar.push(a), throwError(() => new Error('x'))))
      .subscribe({
        error: () => undefined,
      });
    kilit.yenile();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a2']);
  });
});

describe('yeniIslemAnahtari', () => {
  it('UUIDv4, sunucunun 16–128 görünür ASCII kuralına uyar, tekrar etmez', () => {
    const a = yeniIslemAnahtari();
    const b = yeniIslemAnahtari();
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(a).not.toBe(b);
  });

  it('randomUUID yoksa (güvensiz http bağlamı) getRandomValues yedeği', () => {
    const asil = globalThis.crypto.randomUUID;
    Object.defineProperty(globalThis.crypto, 'randomUUID', {
      value: undefined,
      configurable: true,
    });
    try {
      expect(yeniIslemAnahtari()).toMatch(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      );
    } finally {
      Object.defineProperty(globalThis.crypto, 'randomUUID', { value: asil, configurable: true });
    }
  });
});
