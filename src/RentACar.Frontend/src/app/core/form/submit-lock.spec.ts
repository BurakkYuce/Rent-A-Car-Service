import { Subject, of, throwError } from 'rxjs';
import { ApiHatasi } from '../api/api-hatasi';
import { SubmitLock, newOperationKey } from './submit-lock';

/** Sayaçlı üretici: anahtarlar tahmin edilebilir (`a1`, `a2`, …). */
function setUpLock(): { kilit: SubmitLock; anahtarlar: string[] } {
  let n = 0;
  const lockEntry = new SubmitLock(() => `a${++n}`);
  return { kilit: lockEntry, anahtarlar: [] };
}

const hata = (code: 'dogrulama' | 'mukerrer' | 'cakisma', status: number) =>
  new ApiHatasi({ status, kod: code, detay: code });

describe('GonderimKilidi — anahtar kuralı', () => {
  it('2xx sonrası anahtar yenilenir: sabit paneldeki ikinci meşru gönderim yeni anahtarla', () => {
    const { kilit, anahtarlar } = setUpLock();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a2']);
    expect(kilit.pendingKey).toBeNull();
  });

  it('hata sonrası AYNI gönderimin yeniden denemesi aynı anahtarı kullanır', () => {
    const { kilit, anahtarlar } = setUpLock();
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
    const { kilit, anahtarlar } = setUpLock();
    let call = 0;
    kilit
      .gonder((a) => (anahtarlar.push(a), call++, throwError(() => hata('mukerrer', 409))))
      .subscribe({ error: () => undefined });
    expect(call).toBe(1);
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a2']);
  });

  it('409 cakisma formu korur, anahtar KORUNUR (düzeltip aynı gönderim)', () => {
    const { kilit, anahtarlar } = setUpLock();
    kilit
      .gonder((a) => (anahtarlar.push(a), throwError(() => hata('cakisma', 409))))
      .subscribe({ error: () => undefined });
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a1']);
  });

  it('deterministik sunucu anahtarı DOKUNULMADAN geçer, istemci anahtarını tüketmez', () => {
    const { kilit, anahtarlar } = setUpLock();
    const collection = '7d0c1f6e-3b9a-5c2e-8f41-0a9b8c7d6e5f';
    kilit
      .gonder((a) => (anahtarlar.push(a), throwError(() => new Error('ağ'))))
      .subscribe({
        error: () => undefined,
      });
    kilit
      .gonder((a) => (anahtarlar.push(a), of('ok')), { deterministikAnahtar: collection })
      .subscribe();
    expect(anahtarlar).toEqual(['a1', collection]);
    // Deterministik 2xx istemci anahtarını yenilemez: bekleyen istemci gönderimi hâlâ a1.
    expect(kilit.pendingKey).toBe('a1');
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar.at(-1)).toBe('a1');
  });

  it('boş deterministik anahtar yok sayılır', () => {
    const { kilit, anahtarlar } = setUpLock();
    kilit.gonder((a) => (anahtarlar.push(a), of('ok')), { deterministikAnahtar: '  ' }).subscribe();
    expect(anahtarlar).toEqual(['a1']);
  });

  it('istek uçarken ikinci gönderim yok sayılır (çift tık tek istek), bitince kilit açılır', () => {
    const { kilit } = setUpLock();
    const response = new Subject<string>();
    let call = 0;
    const request = () => (call++, response);
    kilit.gonder(request).subscribe();
    expect(kilit.gonderiliyor()).toBe(true);
    let secondCompleted = false;
    kilit.gonder(request).subscribe({ complete: () => (secondCompleted = true) });
    expect(call).toBe(1);
    expect(secondCompleted).toBe(true);
    response.next('ok');
    response.complete();
    expect(kilit.gonderiliyor()).toBe(false);
    kilit.gonder(() => (call++, of('ok'))).subscribe();
    expect(call).toBe(2);
  });

  it('abonelik iptal edilirse kilit açılır, anahtar korunur (sonuç bilinmiyor)', () => {
    const { kilit, anahtarlar } = setUpLock();
    const subscription = kilit
      .gonder((a) => (anahtarlar.push(a), new Subject<string>()))
      .subscribe();
    subscription.unsubscribe();
    expect(kilit.gonderiliyor()).toBe(false);
    kilit.gonder((a) => (anahtarlar.push(a), of('ok'))).subscribe();
    expect(anahtarlar).toEqual(['a1', 'a1']);
  });

  it('yenile(): form yeni kayda sıfırlanınca bir sonraki gönderim yeni anahtarla', () => {
    const { kilit, anahtarlar: keys } = setUpLock();
    kilit
      .gonder((a) => (keys.push(a), throwError(() => new Error('x'))))
      .subscribe({
        error: () => undefined,
      });
    kilit.yenile();
    kilit.gonder((a) => (keys.push(a), of('ok'))).subscribe();
    expect(keys).toEqual(['a1', 'a2']);
  });
});

describe('yeniIslemAnahtari', () => {
  it('UUIDv4, sunucunun 16–128 görünür ASCII kuralına uyar, tekrar etmez', () => {
    const a = newOperationKey();
    const b = newOperationKey();
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(a).not.toBe(b);
  });

  it('randomUUID yoksa (güvensiz http bağlamı) getRandomValues yedeği', () => {
    const original = globalThis.crypto.randomUUID;
    Object.defineProperty(globalThis.crypto, 'randomUUID', {
      value: undefined,
      configurable: true,
    });
    try {
      expect(newOperationKey()).toMatch(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      );
    } finally {
      Object.defineProperty(globalThis.crypto, 'randomUUID', {
        value: original,
        configurable: true,
      });
    }
  });
});
