import { TestBed } from '@angular/core/testing';

import { aciliDurumMu, EN_FAZLA_TOAST, ToastServisi, VARSAYILAN_SURE } from './toast-servisi';

describe('ToastServisi', () => {
  let servis: ToastServisi;

  beforeEach(() => {
    vi.useFakeTimers();
    servis = TestBed.inject(ToastServisi);
  });

  afterEach(() => {
    servis.temizle();
    vi.useRealTimers();
  });

  it('altı durum; hata/uyarı acil (role=alert), diğerleri kibar (role=status)', () => {
    for (const durum of ['basari', 'bilgi', 'uyari', 'hata', 'notr', 'bekleme'] as const) {
      servis.goster(durum, durum);
    }
    expect(servis.toastlar().map((t) => t.durum)).toEqual(['uyari', 'hata', 'notr', 'bekleme']); // yığın sınırı: en eski düşer
    expect(servis.toastlar()).toHaveLength(EN_FAZLA_TOAST);
    expect(aciliDurumMu('hata')).toBe(true);
    expect(aciliDurumMu('uyari')).toBe(true);
    expect(aciliDurumMu('basari')).toBe(false);
    expect(aciliDurumMu('bekleme')).toBe(false);
  });

  it('süresi dolunca kapanır; hata daha uzun kalır; bekleme kalıcıdır', () => {
    servis.basari('kaydedildi');
    servis.hata('sunucu hatası');
    servis.bekleme('işleniyor');
    vi.advanceTimersByTime(VARSAYILAN_SURE.basari);
    expect(servis.toastlar().map((t) => t.mesaj)).toEqual(['sunucu hatası', 'işleniyor']);
    vi.advanceTimersByTime(VARSAYILAN_SURE.hata);
    expect(servis.toastlar().map((t) => t.mesaj)).toEqual(['işleniyor']);
  });

  it('aynı durum + mesaj yeni satır açmaz, süresini tazeler', () => {
    const a = servis.hata('sunucu hatası');
    vi.advanceTimersByTime(VARSAYILAN_SURE.hata - 100);
    const b = servis.hata('sunucu hatası');
    expect(b).toBe(a);
    vi.advanceTimersByTime(200);
    expect(servis.toastlar()).toHaveLength(1);
  });

  it('üzerine gelince süre durur, ayrılınca yeniden başlar', () => {
    const id = servis.bilgi('bilgi');
    servis.duraklat(id);
    vi.advanceTimersByTime(VARSAYILAN_SURE.bilgi * 3);
    expect(servis.toastlar()).toHaveLength(1);
    servis.devamEt(id);
    vi.advanceTimersByTime(VARSAYILAN_SURE.bilgi);
    expect(servis.toastlar()).toHaveLength(0);
  });

  it('bekleme → bitir aynı satırı sonuca çevirir; eylem çalışır ve kapatır', () => {
    const id = servis.bekleme('kaydediliyor');
    servis.bitir(id, 'basari', 'kaydedildi');
    expect(servis.toastlar()).toEqual([expect.objectContaining({ id, durum: 'basari' })]);

    const calistir = vi.fn();
    const eylemli = servis.bilgi('Yeni sürüm var', {
      sure: 0,
      eylem: { etiket: 'Yenile', calistir },
    });
    servis.eylemCalistir(eylemli);
    expect(calistir).toHaveBeenCalledTimes(1);
    expect(servis.toastlar().some((t) => t.id === eylemli)).toBe(false);
  });
});
