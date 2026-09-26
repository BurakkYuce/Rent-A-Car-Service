import { inject } from '@angular/core';
import { type Observable, map } from 'rxjs';

import { ApiIstemcisi, type ApiPath } from '@core/api/api-istemcisi';
import type { SelectionItem } from '@core/api/ui-tipleri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TanimAlani } from '@shared/form/tanim-crud/definition-source';

import { DEFINITION_PATHS, type DefinitionKind } from './definition-paths';

export type { DefinitionKind } from './definition-paths';

export interface DefinitionConfig {
  readonly root: ApiPath;
  /** Çok alanlı tanım: form tablo üstünde ızgara (`panel`). */
  readonly layout: 'row' | 'panel';
  readonly fields: readonly TanimAlani[];
  /**
   * F11.1b uçları sayfalı liste döner (satırda `surum` yok): değer = sunucu sıralama alanı; `pagedDefinitionSource`
   * tüm sayfaları okur. Yoksa F11.1a düz dizi sözleşmesi (`restTanimKaynagi`).
   */
  readonly pagedSort?: string;
}

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/** `/api/ui/v1/secim/*` önerisi (≤ 20, yazılanla `q`): seç-veya-yaz alanlarının datalist'i. */
export type SuggestionSource = (q: string) => Observable<readonly string[]>;

/** Enjeksiyon bağlamında: seçim ucundan öneri metinleri (`etiket` ya da `kod`). */
export function selectionSuggestions(
  path: ApiPath,
  pick: (o: SelectionItem) => string | null = (o) => o.etiket,
): SuggestionSource {
  const api = inject(ApiIstemcisi);
  return (q) =>
    api
      .get<readonly SelectionItem[]>(path, { parametreler: { q, limit: 20 } })
      .pipe(map((list) => list.map(pick).filter((s): s is string => !!s)));
}

/** Ortak alan yapıcıları (etiketler `tanimlar.alan.*`). */
export function commonFields(t: Translate) {
  const l = (k: string) => t(`tanimlar.alan.${k}` as CeviriAnahtari);
  const text = (name: string, label: string, max: number, extra: Partial<TanimAlani> = {}) =>
    ({ ad: name, etiket: label, tur: 'metin', azamiUzunluk: max, ...extra }) as TanimAlani;
  return {
    l,
    text,
    code: (max = 32): TanimAlani => text('kod', l('kod'), max, { zorunlu: true }),
    name: (): TanimAlani => text('ad', l('ad'), 128, { zorunlu: true }),
    /** Aktif/Pasif (Blazor "Durum"): tam PUT'ta zorunlu, yeni kayıtta Aktif. */
    active: (label = l('durum')): TanimAlani => ({
      ad: 'aktif',
      etiket: label,
      tur: 'secim',
      zorunlu: true,
      defaultValue: true,
      secenekler: [
        { deger: true, etiket: l('aktif') },
        { deger: false, etiket: l('pasif') },
      ],
    }),
    yesNo: (name: string, label: string): TanimAlani => ({
      ad: name,
      etiket: label,
      tur: 'secim',
      zorunlu: true,
      defaultValue: false,
      secenekler: [
        { deger: false, etiket: l('hayir') },
        { deger: true, etiket: l('evet') },
      ],
    }),
  };
}

/**
 * Şube (Blazor `BranchList` formunun 29 alanı + Durum; oranlar KESİR 0–1). Tam değiştirme PUT'unda formda olmayan bağlı
 * kasa/banka hesabı `hidden` ile aynen geri gider (silinmesin). İl/İlçe önerisi mevcut şubelerden.
 */
export function branchFields(
  t: Translate,
  suggest: { readonly il: SuggestionSource; readonly ilce: SuggestionSource },
): readonly TanimAlani[] {
  const f = commonFields(t);
  const b = (k: string) => t(`tanimlar.branch.alan.${k}` as CeviriAnahtari);
  const text = (name: string, max: number, extra: Partial<TanimAlani> = {}) =>
    f.text(name, b(name), max, { inList: false, ...extra });
  const num = (name: string, extra: Partial<TanimAlani> = {}): TanimAlani => ({
    ad: name,
    etiket: b(name),
    tur: 'sayi',
    inList: false,
    ...extra,
  });
  return [
    f.code(),
    f.name(),
    text('adres', 512),
    text('telefon', 32, { inList: true }),
    text('eposta', 128),
    { ...text('il', 64, { inList: true }), tur: 'datalist', suggestions: suggest.il },
    { ...text('ilce', 64, { inList: true }), tur: 'datalist', suggestions: suggest.ilce },
    text('yetkili', 128, { inList: true }),
    text('calismaSaatleri', 64, { placeholder: '09:00-18:00' }),
    num('komisyonOran', { fraction: 4, inList: true, placeholder: '0,10' }),
    text('evrakNoOnek', 16, { placeholder: 'MRK-' }),
    text('webIsim', 128),
    text('firmaUnvani', 256),
    num('hizmetKomisyonOran', { fraction: 4 }),
    num('webRezOncesiSaat'),
    num('enlem', { fraction: 6, negative: true }),
    num('boylam', { fraction: 6, negative: true }),
    text('rezervasyonRengi', 7, { placeholder: '#0ea5e9' }),
    { ad: 'alisSubesiDegilMi', etiket: b('alisSubesiDegilMi'), tur: 'onay', inList: false },
    num('webSira'),
    text('webOtoparkId', 64),
    text('bayiCariKod', 64),
    text('bayiOfisId', 64),
    {
      ad: 'komisyonHesabi',
      etiket: b('komisyonHesabi'),
      tur: 'secim',
      inList: false,
      secenekler: [
        { deger: 'Satıştan', etiket: b('satistan') },
        { deger: 'Maliyetten', etiket: b('maliyetten') },
      ],
    },
    text('onlineRezId', 64),
    text('sozlesmeNoFormati', 64),
    text('entegrasyonKodu', 64),
    text('resimDosyasi', 512),
    text('haftalikCalismaSaatleri', 1024),
    { ad: 'nakitHesapId', etiket: 'nakitHesapId', tur: 'metin', hidden: true },
    { ad: 'bankaHesapId', etiket: 'bankaHesapId', tur: 'metin', hidden: true },
    f.active(),
  ];
}

/** Doluluk fiyat kuralı (Blazor `DolulukFiyatList`): eşik %1–100, çarpan %0–50 (2 hane), geçerlilik takvim günü. */
export function occupancyFields(
  t: Translate,
  suggest: { readonly group: SuggestionSource; readonly branch: SuggestionSource },
): readonly TanimAlani[] {
  const f = commonFields(t);
  const { l, text } = f;
  return [
    f.code(),
    f.name(),
    {
      ...text('aracGrupKod', l('aracGrubu'), 32, { placeholder: t('tanimlar.yerTutucu.tumu') }),
      tur: 'datalist',
      suggestions: suggest.group,
    },
    { ad: 'esikYuzde', etiket: l('esikYuzde'), tur: 'sayi', zorunlu: true },
    { ad: 'carpanYuzde', etiket: l('carpanYuzde'), tur: 'sayi', fraction: 2, zorunlu: true },
    {
      ...text('sube', l('sube'), 64, { placeholder: t('tanimlar.yerTutucu.tumu') }),
      tur: 'datalist',
      suggestions: suggest.branch,
    },
    f.yesNo('sadeceKendiSubeleri', l('sadeceKendiSubeleri')),
    { ad: 'gecerlilikBas', etiket: l('gecerlilikBas'), tur: 'date' },
    { ad: 'gecerlilikBit', etiket: l('gecerlilikBit'), tur: 'date' },
    f.active(l('aktif')),
  ];
}

/**
 * Tanım başına alanlar (Blazor sayfalarının form alanlarıyla birebir; sınırlar uç `ValidateLimits`'iyle aynı).
 * `branchSuggest` / `locationSuggest`: şube ve lokasyon adı önerisi (seç veya yaz).
 */
export function definitionConfig(
  kind: DefinitionKind,
  t: Translate,
  suggest: { readonly branch: SuggestionSource; readonly location: SuggestionSource },
): DefinitionConfig {
  const f = commonFields(t);
  const { l, text } = f;
  const root = `/api/ui/v1/${DEFINITION_PATHS[kind]}` as ApiPath;
  const master = { root, layout: 'row' as const, fields: [f.code(), f.name(), f.active()] };
  switch (kind) {
    case 'brand':
    case 'cancelReason':
    case 'country':
    case 'customerGroup':
    case 'department':
    case 'bank':
    case 'paymentType':
    case 'fuelKind':
    case 'transmissionType':
    case 'vehicleColor':
      return master;
    case 'accountCode':
      return {
        ...master,
        fields: [
          f.code(),
          text('ad', l('ad'), 200, { zorunlu: true }),
          text('aciklama', l('aciklama'), 512),
          f.active(),
        ],
      };
    case 'insuranceCompany':
      return {
        ...master,
        pagedSort: 'kod',
        fields: [f.code(), f.name(), text('telefon', l('telefon'), 32), f.active()],
      };
    case 'vatRate':
      return {
        ...master,
        pagedSort: 'kod',
        fields: [
          f.code(),
          f.name(),
          {
            ad: 'oran',
            etiket: l('kdvOrani'),
            tur: 'sayi',
            fraction: 2,
            zorunlu: true,
            defaultValue: 0.2,
            placeholder: t('tanimlar.yerTutucu.kdvOrani'),
          },
          f.active(),
        ],
      };
    case 'penaltyType':
      return {
        ...master,
        pagedSort: 'kod',
        fields: [
          f.code(),
          f.name(),
          { ad: 'varsayilanTutar', etiket: l('varsayilanTutar'), tur: 'para' },
          f.active(),
        ],
      };
    case 'documentTemplate':
      return documentTemplateConfig(root, t);
    case 'accessory':
      return {
        ...master,
        fields: [f.code(), f.name(), text('aciklama', l('aciklama'), 512), f.active()],
      };
    case 'currency':
      return {
        ...master,
        fields: [
          f.code(3),
          f.name(),
          text('sembol', l('sembol'), 8),
          text('ulke', l('ulke'), 64),
          f.active(),
        ],
      };
    case 'customCode':
      return {
        ...master,
        fields: [
          f.code(),
          f.name(),
          text('aciklama', l('aciklama'), 512),
          text('turu', l('tur'), 512),
          f.active(),
        ],
      };
    case 'expenseCategory':
      return {
        ...master,
        fields: [
          f.code(),
          f.name(),
          text('tur', l('tur'), 512, { placeholder: t('tanimlar.yerTutucu.giderTuru') }),
          f.active(),
        ],
      };
    case 'account':
      return {
        root,
        layout: 'panel',
        fields: [
          f.code(),
          f.name(),
          {
            ad: 'tur',
            etiket: l('tur'),
            tur: 'secim',
            zorunlu: true,
            secenekler: [
              { deger: 'Kasa', etiket: l('kasa') },
              { deger: 'Banka', etiket: l('banka') },
            ],
          },
          text('doviz', l('doviz'), 3, { placeholder: 'TRY' }),
          text('iban', l('iban'), 34),
          text('hesapNo', l('hesapNo'), 64, { inList: false }),
          text('banka', l('banka'), 128),
          { ...text('sube', l('sube'), 128), tur: 'datalist', suggestions: suggest.branch },
          text('ozelKod', l('ozelKod'), 32),
          { ad: 'hediyeCek', etiket: l('hediyeCek'), tur: 'onay' },
          text('uyariMailListesi', l('uyariMailListesi'), 512, {
            placeholder: t('tanimlar.yerTutucu.mailListesi'),
            inList: false,
          }),
          f.active(),
        ],
      };
    case 'drop':
      return {
        root,
        layout: 'panel',
        fields: [
          {
            ...text('lokasyon', l('donusLokasyonu'), 150, { zorunlu: true }),
            tur: 'datalist',
            suggestions: suggest.location,
          },
          {
            ...text('sube', l('cikisSubesi'), 150, { zorunlu: true }),
            tur: 'datalist',
            suggestions: suggest.branch,
          },
          {
            ...text('cikisLokasyon', l('cikisLokasyonu'), 150, {
              placeholder: t('tanimlar.yerTutucu.tumOfisler'),
            }),
            tur: 'datalist',
            suggestions: suggest.location,
          },
          {
            ad: 'minGun',
            etiket: l('asgariGun'),
            tur: 'sayi',
            placeholder: t('tanimlar.yerTutucu.kosulsuz'),
          },
          text('karsilamaSekli', l('karsilamaSekli'), 100),
          text('calismaSekli', l('calismaSekli'), 100),
          text('ozelIletisim', l('ozelIletisim'), 200),
          { ad: 'ucret', etiket: l('dropUcreti'), tur: 'para' },
          { ad: 'drop2', etiket: l('drop2'), tur: 'para' },
          { ad: 'manSuresi', etiket: l('karsilamaSuresi'), tur: 'sayi' },
          f.active(),
        ],
      };
  }
}

/**
 * Belge şablonu (Blazor `BelgeSablonList`, ManageUsers). Metinler DÜZ METİN (PDF'e metin olarak basılır; SPA'da
 * da yalnız metin bağlaması — innerHTML yok). Sınırlar servis `Validate` ile aynı: ad 128, başlık 256, metinler
 * 4000, alt bilgi 512. Belge türü zorunlu (Blazor'daki sessiz "kira sözleşmesi" varsayılanı yok).
 */
function documentTemplateConfig(root: ApiPath, t: Translate): DefinitionConfig {
  const f = commonFields(t);
  const d = (k: string) => t(`tanimlar.documentTemplate.alan.${k}` as CeviriAnahtari);
  const area = (name: string): TanimAlani => ({
    ad: name,
    etiket: d(name),
    tur: 'textarea',
    azamiUzunluk: 4000,
    inList: false,
  });
  return {
    root,
    layout: 'panel',
    pagedSort: 'ad',
    fields: [
      {
        ad: 'belgeTuru',
        etiket: d('belgeTuru'),
        tur: 'secim',
        zorunlu: true,
        defaultValue: 'KiraSozlesmesi',
        secenekler: [
          { deger: 'KiraSozlesmesi', etiket: d('kiraSozlesmesi') },
          { deger: 'Fatura', etiket: d('fatura') },
          { deger: 'Makbuz', etiket: d('makbuz') },
        ],
      },
      f.text('ad', d('ad'), 128, { zorunlu: true }),
      f.yesNo('varsayilanMi', d('varsayilanMi')),
      f.active(),
      f.text('belgeBasligi', d('belgeBasligi'), 256, { inList: false }),
      area('hukukiMetinSol'),
      area('hukukiMetinSag'),
      area('ekKosullarVarsayilan'),
      f.text('altBilgi', d('altBilgi'), 512, {
        inList: false,
        placeholder: '{FirmaMarka} — {BelgeNo}',
      }),
      { ...f.yesNo('imzaAlaniGoster', d('imzaAlaniGoster')), defaultValue: true, inList: false },
    ],
  };
}
