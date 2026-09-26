import { mergeMoment, parseMoment, isDay } from '@core/form/tarih-girdisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TanimAlani, DefinitionRow } from '@shared/form/tanim-crud/definition-source';

import { type SuggestionSource, commonFields } from '../definition-catalog';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;
type Raw = Readonly<Record<string, unknown>>;

/** Takvim günü alanları (sunucuda UTC an; formda İstanbul günü). */
export const DATE_FIELDS = ['iseGiris', 'iseCikis', 'sVerilisTarihi', 'dogumTarihi'] as const;

/**
 * Personel (Blazor `PersonelList`, ManageUsers). KVKK:
 * - TC YAZMA-YALNIZ: sunucu hiçbir yanıtta döndürmez; form alanı daima BOŞ açılır. Boş = kayıtlı TC korunur,
 *   "Kayıtlı TC'yi sil" = `tcKimlik: ""`, dolu = yeni değer (11 hane). Değer hiçbir tarayıcı deposuna yazılmaz.
 * - Maaş yalnız ManageUsers'a görünür (tüm uçlar ManageUsers); listede yok, tekil detayda var. Formda silinirse
 *   `maasTemizle: true` gider (boş `maas` sunucuda "koru" demektir).
 */
export function personnelFields(
  t: Translate,
  suggest: { readonly branch: SuggestionSource },
): readonly TanimAlani[] {
  const f = commonFields(t);
  const p = (k: string) => t(`tanimlar.personnel.alan.${k}` as CeviriAnahtari);
  const text = (name: string, max: number, inList = false): TanimAlani =>
    f.text(name, p(name), max, { inList });
  const date = (name: string, inList = false): TanimAlani => ({
    ad: name,
    etiket: p(name),
    tur: 'date',
    inList,
  });
  return [
    f.text('kod', p('kod'), 32, { zorunlu: true }),
    f.text('ad', p('ad'), 128, { zorunlu: true }),
    f.text('soyad', p('soyad'), 128, { zorunlu: true }),
    { ...text('sube', 128, true), tur: 'datalist', suggestions: suggest.branch },
    text('gorevTanimi', 64, true),
    text('cepTel', 32, true),
    date('iseGiris', true),
    date('iseCikis'),
    {
      ...text('tcKimlik', 11),
      placeholder: p('tcYerTutucu'),
    },
    { ad: 'tcTemizle', etiket: p('tcTemizle'), tur: 'onay', inList: false },
    { ad: 'maas', etiket: p('maas'), tur: 'para', inList: false },
    text('mailAdresi', 128),
    text('evTelefonu', 32),
    text('isTelefonu', 32),
    text('adres', 512),
    text('il', 64),
    text('ilce', 64),
    text('mahalle', 128),
    text('surucuBelgeNo', 64),
    text('sSinifi', 16),
    date('sVerilisTarihi'),
    text('sVerilisYeri', 128),
    date('dogumTarihi'),
    text('dogumYeri', 128),
    text('babaAdi', 128),
    text('anaAdi', 128),
    text('ciltNo', 32),
    text('aileSiraNo', 32),
    text('siraNo', 32),
    text('kanGrubu', 8),
    text('racTabletNo', 32),
    text('referans', 256),
    { ...text('aciklama', 1024), tur: 'textarea' },
    f.active(),
  ];
}

/** API satırı → form satırı: an → İstanbul günü; TC alanı daima boş (sunucu zaten göndermez). */
export function personnelToRow(raw: Raw): DefinitionRow {
  const row: Record<string, unknown> = { ...raw, tcKimlik: null, tcTemizle: false };
  for (const k of DATE_FIELDS) row[k] = parseMoment(raw[k])?.gun ?? null;
  return row as DefinitionRow;
}

/** Form değeri → POST/PUT gövdesi (yazma-yalnız TC ve maaş silme kuralı). */
export function personnelToBody(value: Raw): Raw {
  const { tcTemizle: cleanNationalId, tcKimlik: nationalId, ...rest } = value;
  const body: Record<string, unknown> = { ...rest };
  for (const k of DATE_FIELDS) {
    const day = value[k];
    body[k] = isDay(day) ? mergeMoment(day, '00:00') : null;
  }
  const nationalIdValue = typeof nationalId === 'string' ? nationalId.trim() : '';
  // Dolu değer "sil"e üstün gelir (sunucu kuralıyla aynı); ikisi de yoksa alan HİÇ gönderilmez (koru).
  if (nationalIdValue !== '') body['tcKimlik'] = nationalIdValue;
  else if (cleanNationalId === true) body['tcKimlik'] = '';
  body['maasTemizle'] =
    value['maas'] === null || value['maas'] === undefined || value['maas'] === '';
  return body;
}
