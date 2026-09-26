import type { Permission } from './oturum-tipleri';

/** Bir düğmenin (ya da bağlantının) görünürlük kapısı ve tetiklediği sunucu ucu. */
export interface DugmeIzni {
  /** Düğme yalnız bu izinlerin HEPSİ varsa görünür (`ben.izinler`, rol değil etkin izin). */
  readonly izinler: readonly Permission[];
  /** Tetiklenen uç: `YÖNTEM /sunucu/rota/şablonu` (ASP.NET rota şablonu, `{id:guid}` dahil). */
  readonly uc: string;
}

/**
 * Düğme → izin haritası (F4.6, Blazor `UcIzinKapsamaTests`'in SPA karşılığı). Düğme ancak ucun gerçekten
 * izin vereceği kullanıcıya gösterilmeli: Blazor'da `/kiralar/cancel` ucu OperationsDelete isterken düğmesi
 * OperationsWrite'a bakıyordu — operatör düğmeyi görüp 403 alıyordu (canlı hata, 2026-08-26).
 *
 * KİLİT: `UiDugmeIzinTests` (backend) bu dosyayı okur; her girişin ucunu gerçek uç tablosunda bulur ve
 * düğmenin izin kümesinin ucun kapısıyla (`IzinMetadata` hepsi + `IzinlerdenBiriMetadata`) TÜM izin
 * kombinasyonlarında aynı kararı verdiğini doğrular. Kullanıcı-bazlı istisna (ek izin/yasak) dahil: grup
 * izni eksik bir düğme (ör. OperationsDelete var, OperationsWrite yok) testte kırmızı olur.
 *
 * Kural: izne göre gizlenen HER düğme/bağlantı izinlerini buradan alır (`OturumServisi.izinlerVar`);
 * izin adı bileşende elle yazılmaz. Asıl kapı her zaman sunucuda — bu yalnız görünürlük.
 */
export const DUGME_IZINLERI = {
  /** Kira listesi "Yeni kira" (SPA formu; kayıt `POST /api/ui/v1/kiralar`). */
  kiraYeni: { izinler: ['OperationsWrite'], uc: 'POST /api/ui/v1/kiralar' },
  /** Kira listesi "Örnek sözleşme" (Blazor PDF ucu). */
  kiraOrnekSozlesme: { izinler: ['OperationsWrite'], uc: 'GET /kiralar/ornek-sozlesme/pdf' },
  /** Kira satırı "Sözleşme PDF" (Blazor PDF ucu). */
  kiraPdf: { izinler: ['OperationsWrite'], uc: 'GET /kiralar/{id:guid}/pdf' },
  /** Kira satırı "İptal" — grup (OperationsWrite) + dar izin (OperationsDelete). */
  kiraIptal: {
    izinler: ['OperationsWrite', 'OperationsDelete'],
    uc: 'POST /api/ui/v1/kiralar/{id:guid}/iptal',
  },
  /** Kira listesi dışa aktarma (Blazor liste export ucu). */
  kiraDisaAktar: { izinler: ['ViewReports'], uc: 'GET /listeler/export/{liste}' },
  /** Kira listesi süzgeçleri: ofis, kaynak, personel seçimleri. */
  kiraSecimLokasyon: { izinler: ['OperationsWrite'], uc: 'GET /api/ui/v1/secim/lokasyon' },
  kiraSecimKaynak: { izinler: ['OperationsWrite'], uc: 'GET /api/ui/v1/secim/rezervasyon-kaynagi' },
  kiraSecimPersonel: { izinler: ['OperationsWrite'], uc: 'GET /api/ui/v1/secim/personel' },
} as const satisfies Readonly<Record<string, DugmeIzni>>;

export type ButtonName = keyof typeof DUGME_IZINLERI;
