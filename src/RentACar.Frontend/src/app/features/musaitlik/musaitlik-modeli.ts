import type { SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { listeTanimi } from '@core/veri/liste-sorgusu';

export type MusaitlikYaniti = Sema<'MusaitlikYaniti'>;
export type MusaitlikSatiri = Sema<'MusaitlikSatiri'>;
export type MusaitlikSecenekleri = Sema<'MusaitlikSecenekleri'>;
export type KiralaSorgusu = Sema<'KiralaSorgusu'>;

/**
 * Müsaitlik URL ↔ API sözleşmesi (`GET /api/ui/v1/musaitlik`, `PlanlamaApi.MusaitlikSorgusu`). Blazor
 * `/musaitlik` süzgeçlerinin tamamı (FAZ-48/73): pencere (başlangıç günü + bitiş günü YA DA gün sayısı +
 * alış/dönüş saati), grup, şube, rezervasyon kaynağı (fiyat kanalı + broker çiti), döviz süzgeci, plaka.
 * Sayfalama yok; `sayfa`/`boyut` API'ye gitmez.
 */
export const MUSAITLIK = listeTanimi({
  filtreler: {
    basGun: { tur: 'tarih' },
    bitGun: { tur: 'tarih' },
    gun: { tur: 'tamsayi', enAz: 1, enFazla: 365 },
    basSaat: { tur: 'metin', enFazla: 5 },
    bitSaat: { tur: 'metin', enFazla: 5 },
    grup: { tur: 'metin', enFazla: 64 },
    sube: { tur: 'metin', enFazla: 128 },
    rezKaynak: { tur: 'metin', enFazla: 128 },
    doviz: { tur: 'metin', enFazla: 3 },
    plaka: { tur: 'metin', enFazla: 32 },
  },
});

const SAAT = /^([01]\d|2[0-3]):[0-5]\d$/;

/**
 * Arama parametreleri; başlangıç günü yoksa `null` (Blazor: ilk açılışta arama YAPILMAZ). Bozuk saat
 * (`25:00`, elle yazılmış URL) gönderilmez — sunucu bağlama hatası üretmesin; saat verilmezse 00:00.
 */
export function aramaParametreleri(p: SorguParametreleri): SorguParametreleri | null {
  if (typeof p['basGun'] !== 'string') return null;
  const sonuc: Record<string, SorguParametreleri[string]> = {};
  for (const [ad, deger] of Object.entries(p)) {
    if (ad === 'sayfa' || ad === 'boyut' || ad === 'sirala') continue;
    if ((ad === 'basSaat' || ad === 'bitSaat') && (typeof deger !== 'string' || !SAAT.test(deger)))
      continue;
    sonuc[ad] = deger;
  }
  return sonuc;
}

/**
 * Kira formuna araç + ÇÖZÜLMÜŞ pencere taşıyan bağlantının sorgusu (F4.3 `?varac&vfrom&vto&vgrup`).
 * Tarihler sunucunun `kiralaSorgusu`'ndan (İstanbul takvim günü; gün-sayısı modunda bitiş alanı boştur,
 * ham alan taşınsaydı kira formuna eksik tarih giderdi). Grup yalnız süzgeçte seçildiyse.
 */
export function kiralaParametreleri(aracId: string, s: KiralaSorgusu): Record<string, string> {
  const sonuc: Record<string, string> = { varac: aracId, vfrom: s.vfrom, vto: s.vto };
  if (s.vgrup) sonuc['vgrup'] = s.vgrup;
  return sonuc;
}

/** JSON sayısı (`number | string`) → gösterim sayısı. YALNIZ gösterim. */
export function sayi(deger: number | string | null | undefined): number | null {
  if (typeof deger === 'number') return Number.isFinite(deger) ? deger : null;
  if (typeof deger === 'string' && deger.trim() !== '') {
    const n = Number(deger);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}
