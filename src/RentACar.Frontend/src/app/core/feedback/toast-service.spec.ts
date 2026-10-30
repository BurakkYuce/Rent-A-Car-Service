import { TestBed } from '@angular/core/testing';

import { isUrgentState, MAX_TOASTS, ToastService, DEFAULT_DURATION } from './toast-service';

describe('ToastServisi', () => {
  let service: ToastService;

  beforeEach(() => {
    vi.useFakeTimers();
    service = TestBed.inject(ToastService);
  });

  afterEach(() => {
    service.clear();
    vi.useRealTimers();
  });

  it('altı durum; hata/uyarı acil (role=alert), diğerleri kibar (role=status)', () => {
    for (const status of ['basari', 'bilgi', 'uyari', 'hata', 'notr', 'bekleme'] as const) {
      service.show(status, status);
    }
    expect(service.toasts().map((t) => t.durum)).toEqual(['uyari', 'hata', 'notr', 'bekleme']); // yığın sınırı: en eski düşer
    expect(service.toasts()).toHaveLength(MAX_TOASTS);
    expect(isUrgentState('hata')).toBe(true);
    expect(isUrgentState('uyari')).toBe(true);
    expect(isUrgentState('basari')).toBe(false);
    expect(isUrgentState('bekleme')).toBe(false);
  });

  it('süresi dolunca kapanır; hata daha uzun kalır; bekleme kalıcıdır', () => {
    service.basari('kaydedildi');
    service.hata('sunucu hatası');
    service.wait('işleniyor');
    vi.advanceTimersByTime(DEFAULT_DURATION.basari);
    expect(service.toasts().map((t) => t.mesaj)).toEqual(['sunucu hatası', 'işleniyor']);
    vi.advanceTimersByTime(DEFAULT_DURATION.hata);
    expect(service.toasts().map((t) => t.mesaj)).toEqual(['işleniyor']);
  });

  it('aynı durum + mesaj yeni satır açmaz, süresini tazeler', () => {
    const a = service.hata('sunucu hatası');
    vi.advanceTimersByTime(DEFAULT_DURATION.hata - 100);
    const b = service.hata('sunucu hatası');
    expect(b).toBe(a);
    vi.advanceTimersByTime(200);
    expect(service.toasts()).toHaveLength(1);
  });

  it('üzerine gelince süre durur, ayrılınca yeniden başlar', () => {
    const id = service.bilgi('bilgi');
    service.pause(id);
    vi.advanceTimersByTime(DEFAULT_DURATION.bilgi * 3);
    expect(service.toasts()).toHaveLength(1);
    service.resume(id);
    vi.advanceTimersByTime(DEFAULT_DURATION.bilgi);
    expect(service.toasts()).toHaveLength(0);
  });

  it('bekleme → bitir aynı satırı sonuca çevirir; eylem çalışır ve kapatır', () => {
    const id = service.wait('kaydediliyor');
    service.finish(id, 'basari', 'kaydedildi');
    expect(service.toasts()).toEqual([expect.objectContaining({ id, durum: 'basari' })]);

    const run = vi.fn();
    const withAction = service.bilgi('Yeni sürüm var', {
      sure: 0,
      eylem: { etiket: 'Yenile', calistir: run },
    });
    service.runAction(withAction);
    expect(run).toHaveBeenCalledTimes(1);
    expect(service.toasts().some((t) => t.id === withAction)).toBe(false);
  });
});
