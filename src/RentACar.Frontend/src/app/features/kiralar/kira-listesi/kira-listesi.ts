import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { KiraListeSatiri } from '@core/api/ui-tipleri';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { DUGME_IZINLERI, type DugmeAdi } from '@core/oturum/dugme-izinleri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { sayiBicimle } from '@core/bicim/bicim';
import { FetchPolicy } from '@core/veri/fetch-policy';
import type { Filtreler } from '@core/veri/liste-sorgusu';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { KatlanirFiltre } from '@shared/katlanir-filtre/katlanir-filtre';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import {
  KIRA_DURUMLARI,
  KIRA_LISTESI,
  KiraListesiStore,
  OFIS_DURUMLARI,
  TARIH_TURLERI,
  ozetParametreleri,
  type KiraDurumu,
  type OfisDurumu,
  type TarihTuru,
} from './kira-listesi.store';
import { kiraSutunlari, sayi } from './kira-sutunlari';
import { TahsilPaneli } from './tahsil-paneli';

const DURUM_ROZETI: Readonly<Record<string, string>> = {
  Kirada: 'rc-rozet--bilgi',
  Tamamlandi: 'rc-rozet--basari',
  Iptal: 'rc-rozet--hata',
};

type KiraFiltreleri = Filtreler<typeof KIRA_LISTESI.filtreler>;

const jsonEsit = (a: SorguParametreleri, b: SorguParametreleri) =>
  JSON.stringify(a) === JSON.stringify(b);

/**
 * Kira sözleşmeleri listesi (`/app/kiralar`) — Blazor `RentalList.razor` paritesi (F4.2): tüm FAZ-46
 * sütunları ve süzgeçleri, sunucu sayfalama/sıralama (`ListeIstegi`/`Sayfa<T>`, URL tek doğruluk kaynağı),
 * özet satırı, sunucu dışa aktarması, sözleşme PDF bağlantıları, iptal ve `TahsilatAnahtar`'lı "Tahsil Et".
 *
 * Düğmeler SPA tarafında da izne göre gizlenir (asıl kapı sunucuda): PDF ve "Yeni kira" OperationsWrite,
 * dışa aktarma ViewReports, iptal OperationsDelete; "Tahsil Et" yalnız sunucu satıra `tahsilat` verdiyse
 * (FinanceWrite + bakiye > 0 + iptal değil — karar sunucuda). PDF ve dışa aktarma Blazor GET uçlarıdır:
 * tarayıcının kendi gezinmesi (`<a href>`), SPA'ya yönlenmez.
 */
@Component({
  selector: 'rc-kira-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    Ikon,
    KatlanirFiltre,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TahsilPaneli,
    TarihSecici,
  ],
  providers: [FetchPolicy, KiraListesiStore],
  templateUrl: './kira-listesi.html',
  styleUrl: './kira-listesi.scss',
})
export class KiraListesi {
  protected readonly store = inject(KiraListesiStore);
  private readonly oturum = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly api = inject(ApiIstemcisi);
  private readonly onay = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly yikim = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly liste = listeSorgusuUrlSenkronu(KIRA_LISTESI);
  protected readonly sutunlar = kiraSutunlari(this.t);
  protected readonly varsayilanSirala = KIRA_LISTESI.varsayilanSirala;
  protected readonly kimlik = (r: KiraListeSatiri) => r.id;
  protected readonly filtreAcik = signal(false);

  // ---- izinler (görünürlük; asıl kapı sunucuda). Her kapı DUGME_IZINLERI'nden — UiDugmeIzinTests her girişi
  // tetiklediği ucun izin kapısıyla karşılaştırır (düğme görünür ⇔ uç izin verir).
  private readonly izin = (ad: DugmeAdi) =>
    computed(() => this.oturum.izinlerVar(DUGME_IZINLERI[ad].izinler));
  protected readonly yeniKiraIzni = this.izin('kiraYeni');
  protected readonly ornekSozlesmeIzni = this.izin('kiraOrnekSozlesme');
  protected readonly pdfIzni = this.izin('kiraPdf');
  protected readonly iptalIzni = this.izin('kiraIptal');
  protected readonly lokasyonSecimIzni = this.izin('kiraSecimLokasyon');
  protected readonly kaynakSecimIzni = this.izin('kiraSecimKaynak');
  protected readonly personelSecimIzni = this.izin('kiraSecimPersonel');
  private readonly rapor = this.izin('kiraDisaAktar');

  /** Dışa aktarma: Blazor liste export ucu; ekrandaki süzgeçler taşınır (sayfa taşınmaz). */
  protected readonly disaAktarma = computed<DisaAktarma | null>(() =>
    this.rapor()
      ? {
          yol: '/listeler/export/kiralar',
          parametreler: this.liste.apiParametreleri(),
          bicimler: ['excel', 'csv', 'pdf'],
        }
      : null,
  );

  protected readonly ozet = computed(() => {
    const o = this.store.ozet.veri();
    return o
      ? this.t('kiraListesi.ozet', {
          toplam: sayiBicimle(sayi(o.toplam), '1.0-0'),
          kirada: sayiBicimle(sayi(o.kirada), '1.0-0'),
          faturasiz: sayiBicimle(sayi(o.faturasiz), '1.0-0'),
        })
      : null;
  });

  // ---- süzgeç formu (uygula düğmesiyle; URL'e yazılır, URL'den geri okunur)
  protected readonly filtreFormu = new FormGroup({
    q: new FormControl<string | null>(null),
    durum: new FormControl<KiraDurumu | null>(null),
    fatura: new FormControl<boolean | null>(null),
    tarihTuru: new FormControl<TarihTuru | null>(null),
    basMin: new FormControl<string | null>(null),
    basMax: new FormControl<string | null>(null),
    ofis: new FormControl<SecimSecenegi | null>(null),
    ofisDurum: new FormControl<OfisDurumu | null>(null),
    sahip: new FormControl<string | null>(null),
    grup: new FormControl<string | null>(null),
    kaynak: new FormControl<SecimSecenegi | null>(null),
    personel: new FormControl<SecimSecenegi | null>(null),
  });

  protected readonly durumSecenekleri: readonly SecenekOgesi<KiraDurumu>[] = KIRA_DURUMLARI.map(
    (d) => ({ deger: d, etiket: this.t(`kiraListesi.durumlar.${d}`) }),
  );
  protected readonly faturaSecenekleri: readonly SecenekOgesi<boolean>[] = [
    { deger: true, etiket: this.t('kiraListesi.filtre.faturali') },
    { deger: false, etiket: this.t('kiraListesi.filtre.faturasiz') },
  ];
  protected readonly tarihTuruSecenekleri: readonly SecenekOgesi<TarihTuru>[] = TARIH_TURLERI.map(
    (d) => ({ deger: d, etiket: this.t(`kiraListesi.tarihTurleri.${d}`) }),
  );
  protected readonly ofisDurumSecenekleri: readonly SecenekOgesi<OfisDurumu>[] = OFIS_DURUMLARI.map(
    (d) => ({ deger: d, etiket: this.t(`kiraListesi.ofisDurumlari.${d}`) }),
  );
  protected readonly sahipSecenekleri = computed(() =>
    this.metinSecenekleri(
      this.store.secenekler.veri()?.sahipler,
      this.liste.sorgu().filtreler.sahip,
    ),
  );
  protected readonly grupSecenekleri = computed(() =>
    this.metinSecenekleri(this.store.secenekler.veri()?.gruplar, this.liste.sorgu().filtreler.grup),
  );

  // Seçim uçları OperationsWrite ister; bu alanlar yalnız o izinle çizilir (Muhasebe'de 403 bandı olmasın).
  protected readonly lokasyonlar = sunucuSecimKaynagi('lokasyon');
  protected readonly kaynaklar = sunucuSecimKaynagi('rezervasyon-kaynagi');
  protected readonly personeller = sunucuSecimKaynagi('personel');
  /** Seçilen personelin etiketi (URL'de yalnız kimlik durur — ad yazılmaz). Yalnız bellekte. */
  private readonly personelEtiketleri = new Map<string, string>();

  // ---- satır işlemleri
  protected readonly tahsilSatiri = signal<KiraListeSatiri | null>(null);
  private readonly tahsilPaneli = viewChild(TahsilPaneli);
  /** Tahsilat isteği uçarken başka satır açılamaz (uçan istek iptal edilip sonucu kaybolmasın). */
  protected readonly tahsilSuruyor = computed(() => this.tahsilPaneli()?.gonderiliyor() ?? false);
  protected readonly iptalEdilen = signal<string | null>(null);

  constructor() {
    const politika = inject(FetchPolicy);
    politika.baglan({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.sifirla(),
      // Bakiye ve tahsilat anahtarı başka sekmede (kira formu, panel) değişebilir: dönüşte taze veri.
      sekmeyeDonunce: 'yenile',
    });
    politika.baglan({
      parametre: computed(() => ozetParametreleri(this.liste.apiParametreleri()), {
        equal: jsonEsit,
      }),
      yukle: (p) => this.store.ozet.yukle(p),
      sifirla: () => this.store.ozet.sifirla(),
      sekmeyeDonunce: 'yenile',
      esit: jsonEsit,
    });
    politika.baglan({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.secenekler.yukle(),
      sifirla: () => this.store.secenekler.sifirla(),
    });

    // URL → form (geri/ileri tuşu, paylaşılan bağlantı, "Temizle").
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() => this.filtreFormu.reset(this.formDegeri(f)));
    });

    // Açık tahsilat paneli bayat veriyle kalmasın: liste yenilenince satır yoksa ya da sunucu yeni
    // anahtar verdiyse (bakiye/işlem sayısı değişti) panel kapanır; kullanıcı güncel satırdan yeniden açar.
    effect(() => {
      const acik = this.tahsilSatiri();
      const durum = this.store.liste.durum();
      if (acik === null || durum.tur !== 'hazir' || this.tahsilSuruyor()) return;
      const guncel = durum.veri.kayitlar.find((r) => r.id === acik.id);
      if (guncel?.tahsilat && guncel.tahsilat.anahtar === acik.tahsilat?.anahtar) return;
      untracked(() => {
        this.tahsilSatiri.set(null);
        this.toast.uyari(this.t('kiraListesi.tahsil.satirDegisti', { no: acik.sozlesmeNo }));
      });
    });
  }

  // ------------------------------------------------------------------ süzgeç

  protected filtrele(): void {
    const v = this.filtreFormu.getRawValue();
    if (v.personel) this.personelEtiketleri.set(v.personel.id, v.personel.etiket);
    void this.liste.degistir({
      filtreler: {
        q: v.q ?? undefined,
        durum: v.durum ?? undefined,
        fatura: v.fatura ?? undefined,
        tarihTuru: v.tarihTuru ?? undefined,
        basMin: v.basMin ?? undefined,
        basMax: v.basMax ?? undefined,
        ofis: v.ofis?.etiket ?? undefined,
        ofisDurum: v.ofisDurum ?? undefined,
        sahip: v.sahip ?? undefined,
        grup: v.grup ?? undefined,
        kaynak: v.kaynak?.etiket ?? undefined,
        personelId: v.personel?.id ?? undefined,
      },
    });
  }

  protected temizle(): void {
    void this.liste.sifirla();
  }

  private formDegeri(f: KiraFiltreleri) {
    const secenek = (deger: string | undefined): SecimSecenegi | null =>
      deger === undefined ? null : { id: deger, etiket: deger };
    return {
      q: f.q ?? null,
      durum: f.durum ?? null,
      fatura: f.fatura ?? null,
      tarihTuru: f.tarihTuru ?? null,
      basMin: f.basMin ?? null,
      basMax: f.basMax ?? null,
      ofis: secenek(f.ofis),
      ofisDurum: f.ofisDurum ?? null,
      sahip: f.sahip ?? null,
      grup: f.grup ?? null,
      kaynak: secenek(f.kaynak),
      personel:
        f.personelId === undefined
          ? null
          : {
              id: f.personelId,
              etiket:
                this.personelEtiketleri.get(f.personelId) ??
                this.t('kiraListesi.filtre.seciliPersonel'),
            },
    };
  }

  /** Sunucu öneri listesi + URL'deki değer (listede yoksa da görünsün; seçim kutusu boş kalmasın). */
  private metinSecenekleri(
    liste: readonly string[] | undefined,
    secili: string | undefined,
  ): readonly SecenekOgesi<string>[] {
    const degerler = [...(liste ?? [])];
    if (secili !== undefined && !degerler.includes(secili)) degerler.unshift(secili);
    return degerler.map((d) => ({ deger: d, etiket: d }));
  }

  // ------------------------------------------------------------------ satır

  protected satiriAc(satir: KiraListeSatiri): void {
    void this.router.navigate(['/kiralar', satir.id]);
  }

  protected rozet(durum: string): string {
    return `rc-rozet ${DURUM_ROZETI[durum] ?? ''}`;
  }

  protected durumEtiketi(durum: string): string {
    return (KIRA_DURUMLARI as readonly string[]).includes(durum)
      ? this.t(`kiraListesi.durumlar.${durum as KiraDurumu}`)
      : durum;
  }

  /** Blazor sözleşme PDF'i (tarayıcıda görüntülenir; SPA'ya yönlenmez). */
  protected pdfAdresi(satir: KiraListeSatiri): string {
    return `/kiralar/${encodeURIComponent(satir.id)}/pdf`;
  }

  protected tahsilAc(satir: KiraListeSatiri): void {
    if (this.tahsilSuruyor() || satir.tahsilat === null) return;
    this.tahsilSatiri.set(satir);
  }

  protected tahsilKapat(): void {
    if (this.tahsilSuruyor()) return;
    this.tahsilSatiri.set(null);
  }

  /** 2xx ya da 409 `mukerrer`: panel kapanır, liste (yeni anahtarla) yeniden yüklenir. Yeniden gönderim YOK. */
  protected tahsilSonuclandi(): void {
    this.tahsilSatiri.set(null);
    this.yenile();
  }

  protected async iptal(satir: KiraListeSatiri): Promise<void> {
    if (this.iptalEdilen() !== null) return;
    const evet = await this.onay.sor({
      baslik: this.t('kiraListesi.iptalBaslik'),
      mesaj: this.t('kiraListesi.iptalMesaj', { no: satir.sozlesmeNo }),
      onayEtiketi: this.t('kiraListesi.iptalOnay'),
      tehlikeli: true,
    });
    if (!evet || this.iptalEdilen() !== null) return;
    this.iptalEdilen.set(satir.id);
    this.api
      .post<unknown>(`/api/ui/v1/kiralar/${satir.id}/iptal`, null)
      .pipe(
        finalize(() => this.iptalEdilen.set(null)),
        takeUntilDestroyed(this.yikim),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('kiraListesi.iptalEdildi', { no: satir.sozlesmeNo }));
          this.yenile();
        },
        error: (ham: unknown) => {
          // Bant/toast'ta gösterilenler (yetki, 5xx…) interceptor'da; iş kuralı hatası (400) burada.
          const hata = apiHatasinaCevir(ham);
          if (!genelGosterilir(hata)) this.toast.hata(hata.detay);
          this.yenile();
        },
      });
  }

  private yenile(): void {
    this.store.liste.yenile();
    this.store.ozet.yenile();
  }
}
