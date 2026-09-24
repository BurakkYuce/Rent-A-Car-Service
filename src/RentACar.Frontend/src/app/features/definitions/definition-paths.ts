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
  | 'drop'
  // F11.2c
  | 'paymentType'
  | 'fuelKind'
  | 'transmissionType'
  | 'vehicleColor'
  | 'accountCode'
  | 'insuranceCompany'
  | 'vatRate'
  | 'penaltyType'
  | 'documentTemplate';

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
  paymentType: 'odeme-tipleri',
  fuelKind: 'yakit-turleri',
  transmissionType: 'vites-turleri',
  vehicleColor: 'renkler',
  accountCode: 'hesap-kodlari',
  insuranceCompany: 'sigorta-sirketleri',
  vatRate: 'kdv-oranlari',
  penaltyType: 'ceza-turleri',
  documentTemplate: 'belge-sablonlari',
};

/** Uç izniyle birebir: belge şablonları ManageUsers (Blazor `Admin,Yonetici` + uç); diğerleri OperationsWrite. */
export const DEFINITION_PERMISSION: Readonly<
  Partial<Record<DefinitionKind, 'ManageUsers' | 'OperationsWrite'>>
> = {
  documentTemplate: 'ManageUsers',
};
