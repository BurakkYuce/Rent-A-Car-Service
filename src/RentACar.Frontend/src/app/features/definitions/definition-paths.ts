/**
 * Genel tanım ekranları (tek bileşen, tanım başına yapılandırma). Rota dosyası ilk pakette olduğu için bu
 * küçük dosya ayrı: alan yapılandırması (`definition-catalog`) sayfayla tembel gelir.
 */
export type DefinitionKind =
  | 'brand'
  | 'cancelReason'
  | 'country'
  | 'customerGroup'
  | 'department'
  | 'accessory'
  | 'bank'
  | 'currency'
  | 'customCode'
  | 'expenseCategory'
  | 'account'
  | 'drop';

/** Tür → rota yolu (Blazor rotalarıyla ve `/api/ui/v1` kökleriyle birebir). */
export const DEFINITION_PATHS: Readonly<Record<DefinitionKind, string>> = {
  brand: 'markalar',
  cancelReason: 'iptal-sebepleri',
  country: 'ulkeler',
  customerGroup: 'musteri-gruplari',
  department: 'departmanlar',
  accessory: 'aksesuarlar',
  bank: 'bankalar',
  currency: 'dovizler',
  customCode: 'ozel-kodlar',
  expenseCategory: 'gider-turleri',
  account: 'hesaplar',
  drop: 'drop-tanimlari',
};
