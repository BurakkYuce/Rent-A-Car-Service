/**
 * Türkçe güvenli büyük/küçük harf dönüşümü.
 *
 * Ham `toLowerCase()` / `toUpperCase()` yerel ayardan bağımsızdır: "I" → "i", "i" → "I" olur ve
 * Türkçe metinde arama kaçırır ("IĞDIR" aranınca "ığdır" bulunmaz). Lint bu çağrıları yasaklar;
 * metin dönüşümü yalnız bu dosyadan yapılır.
 */
const TR = 'tr-TR';

/** Türkçe küçük harf: "I" → "ı", "İ" → "i". */
export function trKucukHarf(metin: string): string {
  return metin.toLocaleLowerCase(TR);
}

/** Türkçe büyük harf: "i" → "İ", "ı" → "I". */
export function trBuyukHarf(metin: string): string {
  return metin.toLocaleUpperCase(TR);
}

/** Arama/karşılaştırma anahtarı: Unicode NFC, baş-son boşluk kırpılmış, Türkçe küçük harf. */
export function trNormalize(metin: string): string {
  return trKucukHarf(metin.normalize('NFC').trim());
}

/**
 * Gevşek arama anahtarı (komut paleti, menü araması): `trNormalize` + Türkçe harflerin aksansız
 * karşılığı ("İş" → "is", "Işık" → "isik", "Güneş" → "gunes"). Kullanıcı Türkçe klavye olmadan da
 * yazabilsin diye; ASCII sorgu Türkçe metni, Türkçe sorgu aynı metni bulur. Görüntüleme için değil.
 */
export function trAramaAnahtari(metin: string): string {
  return trNormalize(metin).normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/ı/g, 'i');
}
