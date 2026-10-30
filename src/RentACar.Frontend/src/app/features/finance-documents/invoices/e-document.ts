import type { Schema } from '@core/api/ui-types';
import type { CeviriAnahtari } from '@core/i18n/translation-keys';

/** Faturanın e-belge (e-Fatura / e-Arşiv) durumu — `GET /faturalar/{id}` `eBelge`. */
export type EDocument = Schema<'EDocumentDto'>;
export type EDocumentCancelRequest = Schema<'EDocumentCancelRequest'>;

/** Sunucunun `EDocumentStatus` adları (sıra yaşam döngüsü sırası). */
export const EDOCUMENT_STATUSES = [
  'Taslak',
  'Gonderilecek',
  'Gonderildi',
  'Hata',
  'Iptal',
  // Entegratörün test ortamı kabul etti; GİB'e ulaşmadı — "gönderildi" sayılmaz, canlıda yeniden gönderilir.
  'TestGonderildi',
] as const;
export type EDocumentStatus = (typeof EDOCUMENT_STATUSES)[number];

/** İptal gerekçesinin sunucu sınırı (`EDocumentService.ReasonMaxLength`). */
export const EDOCUMENT_REASON_MAX = 256;

function isStatus(value: string | null | undefined): value is EDocumentStatus {
  return (EDOCUMENT_STATUSES as readonly string[]).includes(value ?? '');
}

/**
 * Rozet tonu (renk = durum): gönderildi → başarı, hata → hata, kuyrukta → bilgi, taslak (bağlı değil / iade) ve test
 * ortamına gönderildi (GİB'e ulaşmadı) → uyarı. İptal ve kayıt yok → nötr (`null`).
 */
export function eDocumentTone(
  status: string | null | undefined,
): 'basari' | 'uyari' | 'hata' | 'bilgi' | null {
  switch (status) {
    case 'Gonderildi':
      return 'basari';
    case 'Hata':
      return 'hata';
    case 'Gonderilecek':
      return 'bilgi';
    case 'Taslak':
    case 'TestGonderildi':
      return 'uyari';
    default:
      return null;
  }
}

/** Durum etiketi anahtarı; tanınmayan/boş durum (özelliğin öncesinden kalan fatura) → "kayıt yok". */
export function eDocumentLabelKey(status: string | null | undefined): CeviriAnahtari {
  return isStatus(status)
    ? (`finansBelge.eBelge.durum.${status}` as CeviriAnahtari)
    : 'finansBelge.eBelge.durum.yok';
}

/** Tür etiketi anahtarı (gönderilmeden önce tür belirlenmez → `null`). */
export function eDocumentKindKey(kind: string | null | undefined): CeviriAnahtari | null {
  return kind === 'EFatura' || kind === 'EArsiv'
    ? (`finansBelge.eBelge.tur.${kind}` as CeviriAnahtari)
    : null;
}
