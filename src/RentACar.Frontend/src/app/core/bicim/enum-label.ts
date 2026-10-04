/**
 * Sunucu kodu (enum adı / defter kaynağı / ödeme yöntemi) → ekranda okunacak Türkçe etiket. TEK harita: tablolar,
 * süzgeç seçenekleri ve detaylar bunu kullanır (kabul testi: "Planlandi", "Tamamlandi", "AcikHesap", "BakiyeDuzeltme"
 * gibi ham kodlar görünüyordu). Kod değeri DEĞİŞMEZ (süzgeç/gövde ham kodla gider); yalnız gösterim.
 * Bilinmeyen kod olduğu gibi gösterilir — yeni bir sunucu kodu sessizce kaybolmaz.
 */
const LABELS: Readonly<Record<string, string>> = {
  // Durumlar (kira, fatura, dönem, dış hizmet, kampanya…)
  Planlandi: 'Planlandı',
  Kesildi: 'Kesildi',
  Tamamlandi: 'Tamamlandı',
  Kirada: 'Kirada',
  Rezerv: 'Rezerv',
  Iptal: 'İptal',
  Kayitli: 'Kayıtlı',
  Acik: 'Açık',
  Kapali: 'Kapalı',
  Aktif: 'Aktif',
  Pasif: 'Pasif',
  Taslak: 'Taslak',
  Beklemede: 'Beklemede',
  Odendi: 'Ödendi',
  Yansitildi: 'Yansıtıldı',
  // Ödeme yöntemleri / hesap türleri
  Nakit: 'Nakit',
  Kasa: 'Kasa',
  Banka: 'Banka',
  Kart: 'Kart',
  Havale: 'Havale',
  AcikHesap: 'Açık hesap',
  // Defter kaynakları (ekstre, toplu kapatma)
  Tahsilat: 'Tahsilat',
  Odeme: 'Ödeme',
  TersKayit: 'Ters kayıt',
  Virman: 'Virman',
  CariVirman: 'Cari virman',
  BakiyeDuzeltme: 'Bakiye düzeltme',
  Fatura: 'Fatura',
  FaturaIade: 'İade faturası',
  Gider: 'Gider',
  AracSatis: 'Araç satışı',
  Ceza: 'Ceza',
  CezaOdeme: 'Ceza ödemesi',
  Hgs: 'HGS',
  Depozito: 'Depozito',
  DepozitoIrat: 'Depozito irat',
  DisHizmet: 'Dış hizmet',
  DonemKapanis: 'Dönem kapanışı',
  MtvOdeme: 'MTV ödemesi',
  MuayeneOdeme: 'Muayene ödemesi',
  SigortaOdeme: 'Sigorta ödemesi',
  ServisYansitma: 'Servis yansıtma',
  KrediTaksit: 'Kredi taksiti',
};

export function enumLabel(code: string | null | undefined): string {
  if (code === null || code === undefined) return '';
  return Object.hasOwn(LABELS, code) ? (LABELS[code] ?? code) : code;
}
