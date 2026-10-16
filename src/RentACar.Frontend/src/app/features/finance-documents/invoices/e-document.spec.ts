import {
  EDOCUMENT_REASON_MAX,
  EDOCUMENT_STATUSES,
  eDocumentKindKey,
  eDocumentLabelKey,
  eDocumentTone,
} from './e-document';

describe('e-belge durumu', () => {
  it('rozet tonu durumu söyler: gönderildi başarı, hata hata, kuyruk bilgi, taslak uyarı, iptal/yok nötr', () => {
    expect(eDocumentTone('Gonderildi')).toBe('basari');
    expect(eDocumentTone('Hata')).toBe('hata');
    expect(eDocumentTone('Gonderilecek')).toBe('bilgi');
    expect(eDocumentTone('Taslak')).toBe('uyari');
    expect(eDocumentTone('TestGonderildi')).toBe('uyari'); // GİB'e ulaşmadı: başarı rengi verilmez
    expect(eDocumentTone('Iptal')).toBeNull();
    expect(eDocumentTone(null)).toBeNull();
    expect(eDocumentTone('Bilinmeyen')).toBeNull();
  });

  it('etiket anahtarı sunucu durumundan; kaydı olmayan eski fatura "kayıt yok"', () => {
    expect(EDOCUMENT_STATUSES).toEqual([
      'Taslak',
      'Gonderilecek',
      'Gonderildi',
      'Hata',
      'Iptal',
      'TestGonderildi',
    ]);
    for (const s of EDOCUMENT_STATUSES) {
      expect(eDocumentLabelKey(s)).toBe(`finansBelge.eBelge.durum.${s}`);
    }
    expect(eDocumentLabelKey(null)).toBe('finansBelge.eBelge.durum.yok');
    expect(eDocumentLabelKey('uydurma')).toBe('finansBelge.eBelge.durum.yok');
  });

  it('tür yalnız gönderimde belirlenir; gerekçe sınırı sunucuyla aynı', () => {
    expect(eDocumentKindKey('EFatura')).toBe('finansBelge.eBelge.tur.EFatura');
    expect(eDocumentKindKey('EArsiv')).toBe('finansBelge.eBelge.tur.EArsiv');
    expect(eDocumentKindKey(null)).toBeNull();
    expect(EDOCUMENT_REASON_MAX).toBe(256);
  });
});
