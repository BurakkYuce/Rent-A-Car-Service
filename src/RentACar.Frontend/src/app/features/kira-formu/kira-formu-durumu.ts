import {
  DestroyRef,
  Injectable,
  type Signal,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { type AbstractControl, FormControl, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  type Observable,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  skip,
  startWith,
} from 'rxjs';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { paraBicimle } from '@core/bicim/bicim';
import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { type GunMetni, bugun, gunEkle } from '@core/form/tarih-girdisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { trAramaAnahtari } from '@core/metin/tr-normalize';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { TemelStore } from '@core/veri/temel-store';
import {
  type SecimKaynagi,
  type SecimSecenegi,
  sunucuSecimKaynagi,
} from '@shared/form/arama-secim/secim-kaynagi';
import { type FormGonderimi, formGonderimi } from '@shared/form/form-gonderimi';
import {
  type KiraFormModu,
  type KiraFormu,
  type KiraSunucuDegerleri,
  type KiraSorgusu,
  type MusaitPencere,
  SUNUCU_ALAN_ESLEMESI,
  TAMAMLANMISTA_DONUK,
  aracSecenegi,
  aynaDurumlariniEsitle,
  aynalaraHataKopyala,
  aynalariBagla,
  detaydanDegerler,
  ekHizmetSatiri,
  formuSifirla,
  gunAnina,
  guncelleGovdesi,
  hesaplaParametreleri,
  isoParaBirimi,
  kiraFormuOlustur,
  kiraSorgusuCoz,
  musaitPencere,
  olusturGovdesi,
  penceredenTarihler,
  sayiya,
  secenekListesi,
  sistemKalemiMi,
  sunucuDegerleriniBirlestir,
} from './kira-formu-modeli';
import type {
  AracSecenegi,
  EkHizmetKatalogOgesi,
  EkHizmetKalemi,
  KaynakRezervasyonYaniti,
  KarneOzeti,
  KiraDetayYaniti,
  KiraDonusOnizleme,
  KiraEkHizmetKatalogu,
  KiraEkHizmetYaniti,
  KiraFormVarsayilanlari,
  KiraHesapSonuc,
  KiraMusteriOzeti,
  KiraOlusturYaniti,
  KiraSozlesmesi,
  MusaitArac,
  MusteriHizliYaniti,
  SecimAraci,
  SecimMusterisi,
} from './kira-tipleri';

const KOK = '/api/ui/v1/kiralar' as const;
const SESSIZ = istekBaglami({ sessiz: true });
const ONERI_LIMITI = 20;
/** Öneri kutusuna yazarken sunucu araması gecikmesi (datalist `q` ile sunucuda süzülür). */
const ONERI_GECIKMESI = 250;

/** Katalog sunucuda kesildi mi (tanım sayısı > dönen satır)? Kesikse kalanlar sunucu aramasıyla eklenir. */
export function katalogKesildi(k: KiraEkHizmetKatalogu): boolean {
  return (sayiya(k.toplam) ?? 0) > k.ogeler.length;
}

/** Kayıtlı kiraya ek hizmet eklerken seçenek etiketi (Blazor: "Ad (birim net)"). */
function ekHizmetEtiketi(x: EkHizmetKatalogOgesi, doviz: string): string {
  const fiyat = paraBicimle(sayiya(x.birimUcret), doviz);
  return fiyat ? `${x.ad} (${fiyat} net)` : x.ad;
}

/** Dönüş formundaki "Bitiş sebebi" seçenekleri (Blazor `SekmeDonus` ile aynı liste). */
export const BITIS_SEBEPLERI = [
  'Normal',
  'Erken İade',
  'Hasar',
  'Arıza',
  'Değişim',
  'Diğer',
] as const;

/**
 * Kira formu sayfa durumu (sayfanın `providers`'ında; sekme bileşenleri enjekte eder). Form(lar),
 * veri store'ları, canlı hesap akışı ve eylemler burada; sekme bileşenleri yalnız çizer.
 *
 * Kurallar: canlı hesap SUNUCUDAN (`hesapla`, `donus-hesapla`); hata hiçbir dalda form değerine
 * dokunmaz (`formGonderimi`); ek hizmet ekleme ANAHTARSIZ (servis desteklemiyor) → istek boyunca kilit.
 */
@Injectable()
export class KiraFormuDurumu {
  private readonly api = inject(ApiIstemcisi);
  private readonly rota = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly oturum = inject(OturumServisi);
  private readonly toast = inject(ToastServisi);
  private readonly onay = inject(OnayServisi);
  private readonly bant = inject(UyariBandiServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sekme = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();

  /** Kayıtlı kiranın kimliği; yeni kirada `null`. Bileşen örneği boyunca değişmez (sekme = rota + id). */
  readonly id: string | null = this.rota.snapshot.paramMap.get('id');
  readonly mod: KiraFormModu = this.id === null ? 'yeni' : 'duzenle';
  readonly yeni = this.mod === 'yeni';

  readonly form: KiraFormu = kiraFormuOlustur(this.mod);

  // ─── ikincil formlar (ana formun DIŞINDA gönderilir; kirli sayılır) ──────────────────────
  readonly yeniMusteriFormu = new FormGroup({
    ad: new FormControl<string | null>(null, Validators.maxLength(64)),
    soyad: new FormControl<string | null>(null, Validators.maxLength(64)),
    unvan: new FormControl<string | null>(null, Validators.maxLength(128)),
    tcKimlik: new FormControl<string | null>(null, Validators.maxLength(11)),
    cepTel: new FormControl<string | null>(null, Validators.maxLength(32)),
    email: new FormControl<string | null>(null, [Validators.email, Validators.maxLength(128)]),
    dogumTarihi: new FormControl<GunMetni | null>(null),
    ehliyetNo: new FormControl<string | null>(null, Validators.maxLength(32)),
    ehliyetSinifi: new FormControl<string | null>(null, Validators.maxLength(8)),
    ehliyetTarihi: new FormControl<GunMetni | null>(null),
    ehliyetYeri: new FormControl<string | null>(null, Validators.maxLength(64)),
    il: new FormControl<string | null>(null, Validators.maxLength(64)),
    ilce: new FormControl<string | null>(null, Validators.maxLength(64)),
  });
  readonly musaitFormu = new FormGroup({
    vfrom: new FormControl<GunMetni | null>(null, Validators.required),
    vto: new FormControl<GunMetni | null>(null, Validators.required),
    vgrup: new FormControl<string | null>(null, Validators.maxLength(64)),
  });
  readonly teslimFormu = new FormGroup({
    cikisKm: new FormControl<number | null>(0, [Validators.required, Validators.min(0)]),
    cikisYakit: new FormControl<number | null>(8, [
      Validators.required,
      Validators.min(0),
      Validators.max(12),
    ]),
  });
  readonly donusFormu = new FormGroup({
    donusKm: new FormControl<number | null>(null, [Validators.required, Validators.min(0)]),
    donusYakit: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(0),
      Validators.max(12),
    ]),
    gercekDonus: new FormControl<string | null>(null, Validators.required),
    kmHediye: new FormControl<number | null>(null, Validators.min(0)),
    bitisSebebi: new FormControl<string | null>(null),
    teslimAlanPersonel: new FormControl<SecimSecenegi | null>(null),
  });
  readonly uzatFormu = new FormGroup({
    yeniBitTar: new FormControl<string | null>(null, Validators.required),
  });
  readonly provizyonKapatFormu = new FormGroup({
    kapamaTutar: new FormControl<string | number | null>(null),
    iade: new FormControl<boolean | null>(false),
  });
  readonly ekHizmetEkleFormu = new FormGroup({
    tanim: new FormControl<SecimSecenegi | null>(null, Validators.required),
    miktar: new FormControl<number | null>(1, [Validators.required, Validators.min(0.01)]),
  });

  // ─── gönderimler (her biri kendi kilidi; çift tık tek istek) ───────────────────────────────
  readonly kayit: FormGonderimi = formGonderimi();
  readonly yeniMusteriGonderimi = formGonderimi();
  readonly teslimGonderimi = formGonderimi();
  readonly donusGonderimi = formGonderimi();
  readonly uzatGonderimi = formGonderimi();
  readonly provizyonKapatGonderimi = formGonderimi();
  readonly ekHizmetEkleGonderimi = formGonderimi();
  readonly iptalKilidi = new GonderimKilidi();
  readonly provizyonAlKilidi = new GonderimKilidi();
  readonly ekHizmetSilKilidi = new GonderimKilidi();

  // ─── veri ──────────────────────────────────────────────────────────────────────────────────
  readonly detay = new TemelStore((id: string) => this.api.get<KiraDetayYaniti>(`${KOK}/${id}`), {
    oncekiVeriyiKoru: true,
  });
  readonly varsayilanlar = new TemelStore(() =>
    this.api.get<KiraFormVarsayilanlari>(`${KOK}/form-varsayilanlari`),
  );
  readonly hesap = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<KiraHesapSonuc>(`${KOK}/hesapla`, { parametreler: p, context: SESSIZ }),
    { oncekiVeriyiKoru: true },
  );
  readonly musait = new TemelStore((p: MusaitPencere) =>
    this.api.get<MusaitArac[]>(`${KOK}/musait-arac`, {
      parametreler: { vfrom: p.vfrom, vto: p.vto, vgrup: p.vgrup },
    }),
  );
  readonly donusOnizleme = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<KiraDonusOnizleme>(`${KOK}/${this.id ?? ''}/donus-hesapla`, {
        parametreler: p,
        context: SESSIZ,
      }),
    { oncekiVeriyiKoru: true },
  );
  readonly kaynakRezervasyon = new TemelStore((id: string) =>
    this.api.get<KaynakRezervasyonYaniti>(`${KOK}/${id}/kaynak-rezervasyon`, { context: SESSIZ }),
  );
  readonly karne = new TemelStore((id: string) =>
    this.api.get<KarneOzeti>(`${KOK}/${id}/karne-ozeti`, { context: SESSIZ }),
  );
  readonly kaynakOnerileri = this.oneriStore('rezervasyon-kaynagi');
  readonly ozelKodOnerileri = this.oneriStore('ozel-kod');
  /** F4.3b: Müşteri sekmesinin salt-okunur cari özeti (kimlik/belge no YALNIZ maskeli — sunucu kuralı). */
  readonly musteriOzeti = new TemelStore((id: string) =>
    this.api.get<KiraMusteriOzeti>(`${KOK}/${id}/musteri-ozet`, { context: SESSIZ }),
  );
  /** F4.3b: ek hizmet matrisi (tanım fiyat/KDV'si — yalnız gösterim; satır tutarı `hesapla`'dan). */
  readonly ekHizmetKatalogu = new TemelStore(() =>
    this.api.get<KiraEkHizmetKatalogu>(`${KOK}/ek-hizmet-katalogu`, { context: SESSIZ }),
  );
  readonly belgeSablonlari = new TemelStore(() =>
    this.api.get<readonly SecimSecenegi[]>('/api/ui/v1/secim/belge-sablonu', {
      parametreler: { tur: 0, limit: ONERI_LIMITI },
      context: SESSIZ,
    }),
  );

  // ─── seçim kaynakları ──────────────────────────────────────────────────────────────────────
  readonly musteriKaynagi = this.yetkiliKaynak(sunucuSecimKaynagi('musteri'));
  readonly lokasyonKaynagi = this.yetkiliKaynak(sunucuSecimKaynagi('lokasyon'));
  readonly personelKaynagi = this.yetkiliKaynak(sunucuSecimKaynagi('personel'));
  private readonly sunucuArac = sunucuSecimKaynagi('arac');
  private readonly sunucuEkHizmet = sunucuSecimKaynagi('ek-hizmet');
  /** Müsait liste getirildiyse araç araması O LİSTEDE (Blazor: araç listesi müsaitlerle süzülür). */
  readonly aracKaynagi: SecimKaynagi<AracSecenegi> = (arama, limit) => {
    const liste = this.musait.veri();
    if (!liste) return this.operasyon() ? this.sunucuArac(arama, limit) : of([]);
    const anahtar = trAramaAnahtari(arama);
    return of(
      liste
        .map(aracSecenegi)
        .filter((a) => anahtar === '' || trAramaAnahtari(a.etiket).includes(anahtar))
        .slice(0, limit),
    );
  };
  /**
   * Ek hizmet tanımları (kayıtlı kiraya ekleme + yeni kirada katalog dışı ekleme): katalog TAMSA ondan (yerel
   * arama), etiket Blazor gibi "Ad (birim net)". Katalog yüklenmediyse ya da KESİLDİYSE (toplam > satır — #262 L2:
   * 200'den sonrası eklenemiyordu) F1.6 sunucu araması (`q`) — tüm tanımlarda arar; katalogdaki öğe yine fiyatlı
   * etiketle. Sistem ücret kalemleri (SYS-*) manuel seçilemez (sunucu da reddeder).
   */
  readonly ekHizmetKaynagi: SecimKaynagi = (arama, limit) => {
    if (!this.operasyon()) return of([]);
    const katalog = this.ekHizmetKatalogu.veri() ?? null;
    const doviz = this.kiraDovizi();
    const katalogda = new Map((katalog?.ogeler ?? []).map((x) => [x.id, x]));
    if (katalog === null || katalogKesildi(katalog)) {
      return this.sunucuEkHizmet(arama, limit).pipe(
        map((liste) =>
          liste
            .filter((x) => !sistemKalemiMi(x.kod))
            .map((x) => {
              const k = katalogda.get(x.id);
              return { id: x.id, etiket: k ? ekHizmetEtiketi(k, doviz) : x.etiket };
            }),
        ),
      );
    }
    const anahtar = trAramaAnahtari(arama);
    return of(
      katalog.ogeler
        .filter(
          (x) =>
            anahtar === '' ||
            trAramaAnahtari(x.ad).includes(anahtar) ||
            trAramaAnahtari(x.kod).includes(anahtar),
        )
        .slice(0, limit)
        .map((x) => ({ id: x.id, etiket: ekHizmetEtiketi(x, doviz) })),
    );
  };

  // ─── türetilmiş durum ──────────────────────────────────────────────────────────────────────
  readonly kira: Signal<KiraSozlesmesi | null> = computed(() => this.detay.veri()?.kira ?? null);
  /** Oturumun etkin izni (seçim/varsayılan uçları bunu ister); kayıtta asıl kapı sunucuda. */
  private readonly owIzni = this.oturum.izinVar('OperationsWrite');
  readonly operasyon = computed(() =>
    this.yeni ? this.owIzni : (this.detay.veri()?.yetkiler.operasyon ?? this.owIzni),
  );
  readonly silme = computed(() => this.detay.veri()?.yetkiler.silme ?? false);
  readonly finans = computed(() =>
    this.yeni ? this.oturum.izinVar('FinanceWrite') : (this.detay.veri()?.yetkiler.finans ?? false),
  );
  /** Risk onayı yalnız Yönetici/Admin (servis rolü AYRICA doğrular) — Blazor `AuthorizeView Roles`. */
  readonly riskOnayGorunur = computed(() => {
    const rol = this.oturum.ben()?.rol;
    return rol === 'Admin' || rol === 'Yonetici';
  });
  readonly durum = computed(() => this.kira()?.durum ?? null);
  readonly kirada = computed(() => this.durum() === 'Kirada');
  readonly iptal = computed(() => this.durum() === 'Iptal');
  readonly teslimEdildi = computed(() => {
    const k = this.kira();
    return k !== null && k.cikisKm !== null;
  });
  /**
   * #261 yeniden doğrulama N1: bir işlemden (teslim, ek hizmet, uzat, provizyon, sabit panel para işlemi) ya da
   * Kaydet'ten sonra kayıt yeniden okunurken Kaydet PASİF — işlem kiranın sürümünü değiştirdi; tazeleme bitmeden
   * gönderilen PUT kendi değişikliği yüzünden 409 "başka oturumda değişti" alıyordu. İşlem yanıtındaki sürümü
   * tabana yazmak yetmez: işlemin yazdığı alanlar (provizyon tarihi, çıkış km…) forma birleşmeden gönderilen tam
   * değiştirme onları geri alırdı — birleştirme detay yenilemesinde yapılır.
   */
  readonly kayitTazeleniyor = computed(() => !this.yeni && this.detay.yukleniyor());
  readonly kaydedilebilir = computed(
    () => this.operasyon() && !this.iptal() && !this.kayitTazeleniyor(),
  );

  // Sabit seçenek listeleri (sunucudan; kayıtlı eski değer listede yoksa eklenir — kaybolmaz).
  private readonly vars = computed(() => this.varsayilanlar.veri());
  readonly kiralamaTurleri = computed(() =>
    secenekListesi(this.vars()?.kiralamaTurleri, this.kira()?.kiralamaTuru),
  );
  readonly fiyatTurleri = computed(() =>
    secenekListesi(this.vars()?.fiyatTurleri, this.kira()?.fiyatTuru),
  );
  readonly dovizler = computed(() => secenekListesi(this.vars()?.dovizler, this.kira()?.doviz));
  readonly faturalamaTipleri = computed(() =>
    secenekListesi(this.vars()?.faturalamaTipleri, this.kira()?.faturalamaTipi),
  );
  readonly odemeSekilleri = computed(() => this.vars()?.odemeSekilleri ?? []);
  readonly belgeSablonuSecenekleri = computed(() => {
    const liste = (this.belgeSablonlari.veri() ?? []).map((s) => ({
      deger: s.id,
      etiket: s.etiket,
    }));
    const mevcut = this.kira()?.belgeSablonId;
    if (mevcut && !liste.some((s) => s.deger === mevcut)) {
      liste.push({ deger: mevcut, etiket: this.t('kiraFormu.alan.kayitliSablon') });
    }
    return liste;
  });

  readonly secilenArac = this.degerSinyali(this.form.controls.arac);
  readonly secilenMusteri = this.degerSinyali(this.form.controls.musteri);
  private readonly dovizDegeri = this.degerSinyali(this.form.controls.doviz);
  readonly kiraDovizi = computed(() =>
    isoParaBirimi(this.yeni ? this.dovizDegeri() : this.kira()?.doviz),
  );
  /** Yeni kirada ek hizmet satırları değişince şablon yeniden çizilsin (FormArray sinyal değil). */
  readonly ekSatirSurumu = signal(0);
  readonly yeniMusteriAcik = signal(false);
  /** Müsaitlik sonucu notu (liste yenilendi / önceki araç müsait değil). */
  readonly musaitNotu = signal<string | null>(null);
  /** `?varac=` araç kimliği; müsait liste gelince etiketiyle çözülür. */
  private bekleyenArac: string | null = null;
  /**
   * Son okunan sunucu hâli (F4.3 adversarial F2): `surum` PUT'a gider (bayatsa 409 `cakisma`), değerler
   * kirli formla birleştirmede "sunucu bu arada neyi değiştirdi" karşılaştırmasının tabanıdır.
   */
  private taban: KiraSozlesmesi | null = null;
  private tabanDegerleri: KiraSunucuDegerleri | null = null;
  /**
   * #261 N2: sürüm çakışmasından (409 `cakisma`) sonra TEK SEFERLİK sessiz yeniden gönderim hakkı. Güncel kayıt
   * gelince birleştirmede kullanıcının DOKUNDUĞU alanlarla çakışan sunucu değişikliği YOKSA (ör. başka sekmede
   * 5 TL tahsilat — PUT alanlarına dokunmaz) birleştirilmiş gövde yeni sürümle yeniden gönderilir; çakışan alan
   * varsa bant + işaretleme (bugünkü davranış). Yalnız sürüm GERÇEKTEN değiştiyse (başka tür 409 — ör. müsaitlik
   * — aynı sonucu verir, tekrar edilmez) ve yalnız bir kez (ikinci 409 yeniden göndermez).
   */
  private otomatikYeniden: { readonly surum: string; readonly gonder: () => void } | null = null;

  constructor() {
    for (const abonelik of aynalariBagla(this.form)) {
      this.destroyRef.onDestroy(() => abonelik.unsubscribe());
    }
    if (this.owIzni) {
      this.varsayilanlar.yukle();
      this.belgeSablonlari.yukle();
      this.ekHizmetKatalogu.yukle();
      // Datalist önerileri YAZILANLA sunucuda süzülür (`q`; en çok 20) — ilk 20'nin dışındaki kayıt da bulunur.
      this.oneriAramasi(this.form.controls.kaynak, this.kaynakOnerileri);
      this.oneriAramasi(this.form.controls.ozelKod, this.ozelKodOnerileri);
    }
    if (this.id !== null) this.duzenlemeyiBaslat(this.id);
    else this.yeniyiBaslat();
  }

  // ─── yeni kira ─────────────────────────────────────────────────────────────────────────────

  private yeniyiBaslat(): void {
    if (!this.operasyon()) this.form.disable();
    this.sorguyuUygula(kiraSorgusuCoz((ad) => this.rota.snapshot.queryParamMap.get(ad)));
    // Açık "yeni kira" sekmesine yeni bir bağlantıyla gelinirse (aynı bileşen yaşar) bağlantı uygulanır.
    this.rota.queryParamMap
      .pipe(skip(1), takeUntilDestroyed(this.destroyRef))
      .subscribe((p) => this.sorguyuUygula(kiraSorgusuCoz((ad) => p.get(ad))));

    // Tenant varsayılan fiyat türü — yalnız ön doldurma, kullanıcı seçmediyse.
    effect(() => {
      const v = this.varsayilanlar.veri();
      untracked(() => {
        const kontrol = this.form.controls.fiyatTuru;
        if (v?.fiyatTuru && kontrol.value === null && kontrol.pristine) {
          kontrol.setValue(v.fiyatTuru);
          this.form.controls.ayna.controls.fiyatTuru.setValue(v.fiyatTuru, { emitEvent: false });
        }
      });
    });

    // Müsait liste geldi: araç seçimi/tarihler (Blazor `bindMusaitAjax` davranışı).
    effect(() => {
      const liste = this.musait.veri();
      if (liste) untracked(() => this.musaitListesiGeldi(liste));
    });

    // Canlı hesap: 300 ms gecikme, son istek kazanır (TemelStore switchMap), tarih yoksa istek yok.
    this.form.valueChanges
      .pipe(
        startWith(null),
        debounceTime(300),
        map(() => hesaplaParametreleri(this.form.getRawValue())),
        distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((p) => (p === null ? this.hesap.sifirla() : this.hesap.yukle(p)));
  }

  /** Sorgu sözleşmesi: araç, müşteri, müsaitlik penceresi (bozuk parametre sessizce yok sayılır). */
  sorguyuUygula(s: KiraSorgusu): void {
    const pencere = musaitPencere(s);
    this.musaitFormu.reset({ vfrom: s.vfrom, vto: s.vto, vgrup: s.vgrup });
    if (s.musteriId !== null && this.form.controls.musteri.value?.id !== s.musteriId) {
      this.form.controls.musteri.setValue({
        id: s.musteriId,
        etiket: this.t('kiraFormu.musteri.baglantidan'),
      });
      this.musteriEtiketiniCoz(s.musteriId);
    }
    if (pencere) {
      const tarihler = penceredenTarihler(pencere);
      this.form.patchValue(tarihler);
      this.musait.yukle(pencere);
    }
    if (s.varac !== null) {
      this.bekleyenArac = s.varac;
      this.form.controls.arac.setValue({
        id: s.varac,
        etiket: this.t('kiraFormu.arac.baglantidan'),
      });
      // Pencere yoksa (araç durumu "Kirala"): etiket kimlikle sunucudan (F4.3b `secim/arac/{id}`); tam kart
      // için bugün–yarın müsait listesine de bakılır. Bulunamazsa kimlik yine geçerlidir (kayıt YALNIZ kimlikle).
      if (!pencere) {
        this.aracEtiketiniCoz(s.varac);
        this.aracKartiniCoz(s.varac);
      }
    }
  }

  /** `?musteriId=` → gerçek görünen ad (F4.3b `secim/musteri/{id}`; PII yok). Hata/yok → geçici etiket kalır. */
  private musteriEtiketiniCoz(musteriId: string): void {
    if (!this.operasyon()) return;
    this.api
      .get<SecimMusterisi>(`/api/ui/v1/secim/musteri/${musteriId}`, { context: SESSIZ })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (m) => {
          const kontrol = this.form.controls.musteri;
          if (m?.id === musteriId && kontrol.value?.id === musteriId) {
            kontrol.setValue({ id: m.id, etiket: m.etiket });
          }
        },
        error: () => undefined,
      });
  }

  /** `?varac=` (pencere yok) → plaka etiketi kimlikle (şube kapsamı sunucuda; kapsam dışı → geçici etiket). */
  private aracEtiketiniCoz(aracId: string): void {
    if (!this.operasyon()) return;
    this.api
      .get<SecimAraci>(`/api/ui/v1/secim/arac/${aracId}`, { context: SESSIZ })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (a) => {
          const kontrol = this.form.controls.arac;
          // Müsait listeden tam kart zaten geldiyse (marka dolu) ezilmez.
          if (a?.id === aracId && kontrol.value?.id === aracId && !kontrol.value.marka) {
            kontrol.setValue({ ...a });
          }
        },
        error: () => undefined,
      });
  }

  private aracKartiniCoz(aracId: string): void {
    const gun = bugun();
    this.api
      .get<MusaitArac[]>(`${KOK}/musait-arac`, {
        parametreler: { vfrom: gun, vto: gunEkle(gun, 1) },
        context: SESSIZ,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (liste) => {
          const bulunan = liste.find((a) => a.id === aracId);
          if (bulunan && this.form.controls.arac.value?.id === aracId) {
            this.form.controls.arac.setValue(aracSecenegi(bulunan));
          }
        },
        error: () => undefined,
      });
  }

  musaitGetir(): void {
    this.musaitFormu.markAllAsTouched();
    const d = this.musaitFormu.getRawValue();
    const pencere = musaitPencere({ vfrom: d.vfrom, vto: d.vto, vgrup: d.vgrup?.trim() || null });
    if (!pencere) {
      if (d.vfrom && d.vto) {
        this.musaitFormu.controls.vto.setErrors({
          [SUNUCU_HATASI]: [this.t('kiraFormu.arac.aralikHatali')],
        });
      }
      return;
    }
    this.musaitNotu.set(null);
    this.musait.yukle(pencere);
  }

  musaitTemizle(): void {
    this.musait.sifirla();
    this.musaitNotu.set(null);
  }

  private musaitListesiGeldi(liste: readonly MusaitArac[]): void {
    const arac = this.form.controls.arac;
    const bekleyen = this.bekleyenArac;
    this.bekleyenArac = null;
    const hedefId = bekleyen ?? arac.value?.id ?? null;
    const bulunan = hedefId === null ? undefined : liste.find((a) => a.id === hedefId);
    if (bulunan) {
      arac.setValue(aracSecenegi(bulunan));
    } else if (hedefId !== null) {
      // Blazor: önceki seçim artık müsait değilse seçim düşer (kayıt da reddedilirdi).
      arac.setValue(null);
      arac.markAsDirty();
      this.musaitNotu.set(this.t('kiraFormu.arac.secimMusaitDegil'));
    }
    // Tarihler yalnız BOŞSA doldurulur (kullanıcının girdiği ezilmez).
    const pencere = musaitPencere(this.musaitFormu.getRawValue());
    if (pencere && !this.form.controls.basTar.value && !this.form.controls.bitTar.value) {
      this.form.patchValue(penceredenTarihler(pencere));
    }
    if (!this.musaitNotu()) {
      this.musaitNotu.set(this.t('kiraFormu.arac.musaitSayisi', { sayi: liste.length }));
    }
  }

  aracSec(a: MusaitArac): void {
    this.form.controls.arac.setValue(aracSecenegi(a));
    this.form.controls.arac.markAsDirty();
  }

  ekHizmetSatiriEkle(tanim: SecimSecenegi | null): void {
    if (tanim === null) return;
    const dizi = this.form.controls.ekHizmetler;
    if (!dizi.controls.some((s) => s.controls.tanim.value?.id === tanim.id)) {
      dizi.push(ekHizmetSatiri(tanim));
      dizi.markAsDirty();
      this.ekSatirSurumu.update((s) => s + 1);
    }
  }

  /** Matris onay kutusu (Blazor `ekSecim`): işaret → satır (miktar 1), kaldır → satır çıkar. */
  ekHizmetSecimi(oge: EkHizmetKatalogOgesi, secili: boolean): void {
    const sira = this.form.controls.ekHizmetler.controls.findIndex(
      (s) => s.controls.tanim.value?.id === oge.id,
    );
    if (secili && sira < 0) this.ekHizmetSatiriEkle({ id: oge.id, etiket: oge.ad });
    else if (!secili && sira >= 0) this.ekHizmetSatiriSil(sira);
  }

  ekHizmetSatiriSil(sira: number): void {
    const dizi = this.form.controls.ekHizmetler;
    dizi.removeAt(sira);
    dizi.markAsDirty();
    this.ekSatirSurumu.update((s) => s + 1);
  }

  /** Ana form müşteri dışında geçerli mi? Geçersiz alanlar "dokunuldu" olur (hata görünür); müşteri alanı hariç. */
  private musteriDisindaGecerli(): boolean {
    let gecerli = true;
    const gez = (grup: FormGroup, atla: string): void => {
      for (const [ad, kontrol] of Object.entries(
        grup.controls as Record<string, AbstractControl>,
      )) {
        if (ad === atla) continue;
        if (kontrol instanceof FormGroup) {
          gez(kontrol, atla);
          continue;
        }
        kontrol.markAllAsTouched();
        if (kontrol.invalid) gecerli = false;
      }
    };
    gez(this.form, 'musteri');
    return gecerli;
  }

  yeniMusteriDolu(): boolean {
    return Object.values(this.yeniMusteriFormu.getRawValue()).some((v) => (v ?? '') !== '');
  }

  /** Hızlı müşteri: cari ANINDA açılır ve seçilir; form yerinde kalır. PII yalnız bu istekte. */
  yeniMusteriKaydet(sonra?: () => void): void {
    const d = this.yeniMusteriFormu.getRawValue();
    this.yeniMusteriGonderimi.gonder(
      this.yeniMusteriFormu,
      () =>
        this.api.post<MusteriHizliYaniti>(`${KOK}/musteri`, {
          ...d,
          dogumTarihi: gunAnina(d.dogumTarihi),
          ehliyetTarihi: gunAnina(d.ehliyetTarihi),
        }),
      {
        basarili: (m) => {
          const secim: SecimSecenegi = { id: m.id, etiket: m.etiket };
          this.form.controls.musteri.setValue(secim);
          this.form.controls.musteri.markAsDirty();
          this.yeniMusteriFormu.reset();
          this.yeniMusteriAcik.set(false);
          this.toast.basari(this.t('kiraFormu.musteri.kaydedildi', { ad: m.etiket }));
          sonra?.();
        },
      },
    );
  }

  // ─── düzenleme ─────────────────────────────────────────────────────────────────────────────

  private duzenlemeyiBaslat(id: string): void {
    this.detay.yukle(id);
    let ilk = true;
    effect(() => {
      const d = this.detay.veri();
      if (!d) return;
      untracked(() => {
        this.detayGeldi(d, ilk);
        ilk = false;
      });
    });
    // N2: yeniden okuma başarısızsa sessiz yeniden gönderim hakkı düşer (sonraki bir okumada kendiliğinden
    // gönderim olmasın).
    effect(() => {
      if (this.detay.tur() === 'hata') this.otomatikYeniden = null;
    });
    effect(() => {
      const v = this.varsayilanlar.veri();
      if (v && this.teslimFormu.pristine) {
        untracked(() => this.teslimFormu.controls.cikisYakit.setValue(Number(v.cikisYakit)));
      }
    });
    // Sekmeye dönüşte kayıt yeniden okunur (başka oturum/işlem değiştirmiş olabilir): temiz form güncel hâle
    // gelir, kirli formda yalnız dokunulmamış alanlar (F4.3 adversarial F2). İlk görünüm sayılmaz.
    let sonGorunum: number | null = null;
    effect(() => {
      const n = this.sekme.onaGelme();
      untracked(() => {
        if (sonGorunum !== null && n !== sonGorunum) this.yenile();
        sonGorunum = n;
      });
    });
    // Dönüş canlı önizlemesi (ReturnMath): girdiler değişince, 300 ms gecikmeyle.
    this.donusFormu.valueChanges
      .pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.donusOnizle());
  }

  private detayGeldi(d: KiraDetayYaniti, ilk: boolean): void {
    const k = d.kira;
    this.sekme.etiketAyarla(this.t('kiraFormu.sekmeEtiketi', { no: k.sozlesmeNo }));
    // Temiz form sunucu hâline sıfırlanır. KİRLİ formda kullanıcının dokunduğu alanlar EZİLMEZ; dokunmadığı
    // alanlar sunucu değerine çekilir — işlem (provizyon al, teslim…) ya da başka oturumun yazdığı değer bayat
    // tam değiştirmeyle geri alınmasın (F4.3 adversarial F2 / P261-10). İkisi de değiştiyse alan işaretlenir.
    const yeni = detaydanDegerler(d, this.t('kiraFormu.alan.kayitBulunamadi'));
    const otomatik = this.otomatikYeniden;
    this.otomatikYeniden = null;
    if (!this.form.dirty) {
      formuSifirla(this.form, yeni);
      this.durumaGoreKilitle(d);
    } else {
      const cakisan = sunucuDegerleriniBirlestir(this.form, yeni, this.tabanDegerleri);
      this.durumaGoreKilitle(d);
      if (cakisan.length > 0) {
        this.cakismaIsaretle(cakisan);
      } else if (otomatik && k.surum !== otomatik.surum && this.kaydedilebilirDurumda(d)) {
        // N2: çakışma yok → birleştirilmiş gövde yeni sürümle, sessiz ve tek sefer (409 bandı kapanır).
        if (this.bant.bant()?.kod === 'cakisma') this.bant.kapat();
        queueMicrotask(otomatik.gonder);
      }
    }
    this.taban = k;
    this.tabanDegerleri = yeni;
    if (this.teslimFormu.pristine) {
      this.teslimFormu.reset({
        cikisKm: d.arac ? Number(d.arac.km) : 0,
        cikisYakit: Number(this.varsayilanlar.veri()?.cikisYakit ?? 8),
      });
    }
    if (this.donusFormu.pristine) {
      this.donusFormu.reset(
        {
          donusKm: k.cikisKm === null ? null : Number(k.cikisKm),
          donusYakit: k.cikisYakit === null ? 8 : Number(k.cikisYakit),
          gercekDonus: k.bitTar,
          kmHediye: null,
          bitisSebebi: null,
          teslimAlanPersonel: null,
        },
        { emitEvent: false },
      );
      this.donusOnizle();
    }
    if (this.uzatFormu.pristine) {
      this.uzatFormu.reset({
        yeniBitTar: new Date(Date.parse(k.bitTar) + 86_400_000).toISOString(),
      });
    }
    if (this.provizyonKapatFormu.pristine) {
      this.provizyonKapatFormu.reset({ kapamaTutar: k.provizyon ?? null, iade: false });
    }
    if (ilk) {
      if (k.reservationId) this.kaynakRezervasyon.yukle(k.id);
      if (d.yetkiler.finans) this.karne.yukle(k.id);
      this.musteriOzeti.yukle(k.id);
    }
  }

  private durumaGoreKilitle(d: KiraDetayYaniti): void {
    const k = d.kira;
    if (!d.yetkiler.operasyon || k.durum === 'Iptal') {
      this.form.disable({ emitEvent: false });
    } else if (k.durum === 'Tamamlandi') {
      for (const ad of TAMAMLANMISTA_DONUK) this.form.controls[ad].disable({ emitEvent: false });
    }
    aynaDurumlariniEsitle(this.form);
  }

  /** Hem kullanıcının hem başka oturumun değiştirdiği alanlar: alanın altında not + bant (engellemez —
   *  bir sonraki Kaydet'te sunucu hataları temizlenir, kullanıcının değeri bilinçli olarak yazılır). */
  /** Yeni okunan kayıtta Kaydet anlamlı mı (izin + iptal değil) — sessiz yeniden gönderim için. */
  private kaydedilebilirDurumda(d: KiraDetayYaniti): boolean {
    return d.yetkiler.operasyon && d.kira.durum !== 'Iptal';
  }

  private cakismaIsaretle(alanlar: readonly (keyof KiraSunucuDegerleri)[]): void {
    const mesaj = this.t('kiraFormu.cakisma.alan');
    for (const ad of alanlar) {
      const kontrol = this.form.controls[ad] as AbstractControl<unknown>;
      kontrol.setErrors({ ...(kontrol.errors ?? {}), [SUNUCU_HATASI]: [mesaj] });
      kontrol.markAsTouched();
    }
    aynalaraHataKopyala(this.form);
    this.bant.goster({
      tur: 'uyari',
      mesaj: this.t('kiraFormu.cakisma.bant', { sayi: alanlar.length }),
      kod: 'cakisma',
    });
  }

  private donusOnizle(): void {
    const k = this.kira();
    const d = this.donusFormu.getRawValue();
    if (!k || k.durum !== 'Kirada' || k.cikisKm === null) return;
    if (d.donusKm === null || !d.gercekDonus) {
      this.donusOnizleme.sifirla();
      return;
    }
    this.donusOnizleme.yukle({
      donusKm: d.donusKm,
      donusYakit: d.donusYakit ?? 0,
      gercekDonus: d.gercekDonus,
      kmHediye: d.kmHediye,
    });
  }

  yenile(): void {
    this.detay.yenile();
  }

  // ─── kaydet ────────────────────────────────────────────────────────────────────────────────

  /**
   * Ana formu gönderir. Yeni kirada müşteri seçilmemiş ama "yeni müşteri" alanları doluysa önce cari
   * açılır, sonra kira (Blazor tek adım davranışı). `gecersizeGit` bileşenden: hatalı alanın sekmesine.
   */
  kaydet(gecersizeGit: () => void, otomatikHakki = true): void {
    if (this.yeni && this.form.controls.musteri.value === null && this.yeniMusteriDolu()) {
      // F4.3 adversarial F7: önce ANA form (müşteri dışında) doğrulanır — araç/tarih eksikken cari açılıp
      // kira hiç açılmazsa PII'li yetim cari kalırdı.
      if (!this.musteriDisindaGecerli()) {
        gecersizeGit();
        return;
      }
      this.yeniMusteriAcik.set(true);
      this.yeniMusteriKaydet(() => this.kaydet(gecersizeGit));
      return;
    }
    const esleme = {
      ...SUNUCU_ALAN_ESLEMESI,
      // Görünmeyen kontrole yazılan hata kaybolmasın: form üstü hataya düşer.
      ...(this.riskOnayGorunur() ? {} : { riskOnay: '-' }),
    };
    const gecersiz = (): void => {
      aynalaraHataKopyala(this.form);
      gecersizeGit();
    };
    if (this.yeni) {
      this.kayit.gonder(
        this.form,
        () => this.api.post<KiraOlusturYaniti>(KOK, olusturGovdesi(this.form.getRawValue())),
        { esleme, gecersiz, basarili: (y) => this.olusturuldu(y) },
      );
      return;
    }
    const id = this.id ?? '';
    const gonderilenSurum = this.taban?.surum ?? '';
    this.kayit.gonder(
      this.form,
      () =>
        this.api.put<KiraSozlesmesi>(
          `${KOK}/${id}`,
          guncelleGovdesi(this.form.getRawValue(), {
            surum: gonderilenSurum,
            provizyonTarihAni: this.taban?.provizyonTarih ?? null,
            provizyonTarihDegisti: this.form.controls.provizyonTarih.dirty,
          }),
          { context: istekBaglami({ mukerrerdeYenile: () => this.yenile() }) },
        ),
      {
        esleme,
        gecersiz,
        // Bayat sürüm (409 cakisma): form SİLİNMEZ; güncel kayıt okunur, dokunulmamış alanlar güncellenir,
        // dokunulanlar korunur (detayGeldi → birleştirme); kullanıcı kontrol edip yeniden kaydeder.
        hata: (h) => {
          if (h.kod !== 'cakisma') return;
          // N2: alansız (sürüm) çakışmada tek seferlik sessiz yeniden gönderim hakkı — karar güncel kayıt
          // birleştirilince (detayGeldi) verilir.
          this.otomatikYeniden =
            otomatikHakki && h.alanlar === undefined
              ? { surum: gonderilenSurum, gonder: () => this.kaydet(gecersizeGit, false) }
              : null;
          this.yenile();
        },
        basarili: () => {
          this.toast.basari(this.t('kiraFormu.bildirim.kaydedildi'));
          this.yenile();
        },
      },
    );
  }

  private olusturuldu(y: KiraOlusturYaniti): void {
    if (y.uyari) this.toast.uyari(y.uyari, { sure: 0 });
    this.toast.basari(this.t('kiraFormu.bildirim.olusturuldu', { no: y.sozlesmeNo }));
    // Kayıt yapıldı: "yeni kira" sekmesi temiz bir forma döner (sonraki kira için).
    formuSifirla(this.form, { fiyatTuru: this.varsayilanlar.veri()?.fiyatTuru ?? null });
    this.ekSatirSurumu.update((s) => s + 1);
    this.hesap.sifirla();
    this.musait.sifirla();
    this.musaitFormu.reset();
    this.musaitNotu.set(null);
    // Çıkış ofisi kapsamı sunucuda GİRİŞTE denetlenir (F4.1 M1): dönen kira oturumun kapsamında.
    void this.router.navigate(['/kiralar', y.id]);
  }

  // ─── operasyon eylemleri (F4.1 uçları) ─────────────────────────────────────────────────────

  teslimEt(): void {
    const d = this.teslimFormu.getRawValue();
    this.teslimGonderimi.gonder(
      this.teslimFormu,
      () =>
        this.api.post<KiraSozlesmesi>(`${KOK}/${this.id ?? ''}/teslim`, {
          cikisKm: d.cikisKm,
          cikisYakit: d.cikisYakit,
        }),
      { basarili: () => this.eylemTamam(this.teslimFormu, 'kiraFormu.bildirim.teslimEdildi') },
    );
  }

  donusYap(): void {
    const d = this.donusFormu.getRawValue();
    this.donusGonderimi.gonder(
      this.donusFormu,
      () =>
        this.api.post<KiraSozlesmesi>(`${KOK}/${this.id ?? ''}/donus`, {
          donusKm: d.donusKm,
          donusYakit: d.donusYakit,
          gercekDonus: d.gercekDonus,
          kmHediye: d.kmHediye,
          bitisSebebi: d.bitisSebebi,
          teslimAlanPersonelId: d.teslimAlanPersonel?.id ?? null,
        }),
      {
        esleme: { teslimAlanPersonelId: 'teslimAlanPersonel' },
        basarili: () => this.eylemTamam(this.donusFormu, 'kiraFormu.bildirim.donusYapildi'),
      },
    );
  }

  uzat(): void {
    const d = this.uzatFormu.getRawValue();
    this.uzatGonderimi.gonder(
      this.uzatFormu,
      () =>
        this.api.post<KiraSozlesmesi>(`${KOK}/${this.id ?? ''}/uzat`, { yeniBitTar: d.yeniBitTar }),
      { basarili: () => this.eylemTamam(this.uzatFormu, 'kiraFormu.bildirim.uzatildi') },
    );
  }

  provizyonKapat(): void {
    const d = this.provizyonKapatFormu.getRawValue();
    this.provizyonKapatGonderimi.gonder(
      this.provizyonKapatFormu,
      () =>
        this.api.post<KiraSozlesmesi>(`${KOK}/${this.id ?? ''}/provizyon/kapat`, {
          kapamaTutar: d.iade ? null : d.kapamaTutar,
          iade: d.iade ?? false,
        }),
      {
        basarili: () =>
          this.eylemTamam(this.provizyonKapatFormu, 'kiraFormu.bildirim.provizyonKapandi'),
      },
    );
  }

  provizyonAl(): void {
    this.kilitliEylem(
      this.provizyonAlKilidi,
      () => this.api.post<KiraSozlesmesi>(`${KOK}/${this.id ?? ''}/provizyon/al`, {}),
      'kiraFormu.bildirim.provizyonAlindi',
    );
  }

  async iptalEt(): Promise<void> {
    const onay = await this.onay.sor({
      baslik: this.t('kiraFormu.iptal.baslik'),
      mesaj: this.t('kiraFormu.iptal.mesaj'),
      onayEtiketi: this.t('kiraFormu.iptal.onayla'),
      tehlikeli: true,
    });
    if (!onay) return;
    this.kilitliEylem(
      this.iptalKilidi,
      () => this.api.post<KiraSozlesmesi>(`${KOK}/${this.id ?? ''}/iptal`, {}),
      'kiraFormu.bildirim.iptalEdildi',
    );
  }

  /**
   * Ek hizmet ekleme: servis idempotency anahtarı DESTEKLEMİYOR (Blazor'la aynı: iki istek iki kalem).
   * Koruma: `formGonderimi` kilidi istek boyunca ikinci gönderimi yok sayar, düğme pasif.
   */
  ekHizmetEkle(): void {
    const d = this.ekHizmetEkleFormu.getRawValue();
    this.ekHizmetEkleGonderimi.gonder(
      this.ekHizmetEkleFormu,
      () =>
        this.api.post<KiraEkHizmetYaniti>(`${KOK}/${this.id ?? ''}/ek-hizmetler`, {
          ekHizmetTanimId: d.tanim?.id ?? null,
          miktar: d.miktar,
        }),
      {
        esleme: { ekHizmetTanimId: 'tanim' },
        basarili: () => {
          this.ekHizmetEkleFormu.reset({ tanim: null, miktar: 1 });
          this.toast.basari(this.t('kiraFormu.bildirim.ekHizmetEklendi'));
          this.yenile();
        },
      },
    );
  }

  async ekHizmetSil(kalem: EkHizmetKalemi): Promise<void> {
    const onay = await this.onay.sor({
      baslik: this.t('kiraFormu.ekHizmet.silBaslik'),
      mesaj: this.t('kiraFormu.ekHizmet.silMesaj', { ad: kalem.ad }),
      tehlikeli: true,
    });
    if (!onay) return;
    this.kilitliEylem(
      this.ekHizmetSilKilidi,
      () => this.api.delete<KiraEkHizmetYaniti>(`${KOK}/${this.id ?? ''}/ek-hizmetler/${kalem.id}`),
      'kiraFormu.bildirim.ekHizmetSilindi',
    );
  }

  private eylemTamam(form: AbstractControl, mesaj: CeviriAnahtari): void {
    form.markAsPristine();
    this.toast.basari(this.t(mesaj));
    this.yenile();
  }

  private kilitliEylem<T>(
    kilit: GonderimKilidi,
    istek: () => Observable<T>,
    mesaj: CeviriAnahtari,
  ): void {
    kilit
      .gonder(istek)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.basari(this.t(mesaj));
          this.yenile();
        },
        error: (hata: unknown) => this.eylemHatasi(hata),
      });
  }

  /** Alan formu olmayan eylemin hatası: interceptor göstermediyse (400 doğrulama…) hata bildirimi. */
  private eylemHatasi(ham: unknown): void {
    const hata = apiHatasinaCevir(ham);
    if (genelGosterilir(hata)) return;
    if (hata.kod === 'cakisma' && hata.alanlar === undefined) return; // bant zaten gösterdi
    this.toast.hata(hata.detay);
  }

  /** Çıkışta ve yeni kayda geçişte kirli sayılacak tüm formlar. */
  kirliMi(): boolean {
    return (
      this.form.dirty ||
      this.yeniMusteriFormu.dirty ||
      this.teslimFormu.dirty ||
      this.donusFormu.dirty ||
      this.uzatFormu.dirty ||
      this.provizyonKapatFormu.dirty ||
      this.ekHizmetEkleFormu.dirty
    );
  }

  // ─── yardımcılar ───────────────────────────────────────────────────────────────────────────

  private oneriStore(
    uc: 'rezervasyon-kaynagi' | 'ozel-kod',
  ): TemelStore<readonly SecimSecenegi[], string> {
    return new TemelStore(
      (q: string) =>
        this.api.get<readonly SecimSecenegi[]>(`/api/ui/v1/secim/${uc}`, {
          parametreler: { q: q === '' ? null : q, limit: ONERI_LIMITI },
          context: SESSIZ,
        }),
      { oncekiVeriyiKoru: true },
    );
  }

  /** Datalist önerisi: alanın değeri değiştikçe (gecikmeli, aynı metin tekrar sorulmaz) `q` ile sunucu araması. */
  private oneriAramasi(
    kontrol: AbstractControl<string | null>,
    store: TemelStore<readonly SecimSecenegi[], string>,
  ): void {
    kontrol.valueChanges
      .pipe(
        startWith(kontrol.value),
        map((v) => (v ?? '').trim()),
        debounceTime(ONERI_GECIKMESI),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((q) => store.yukle(q));
  }

  /** Seçim uçları OperationsWrite ister; izni olmayan oturumda istek atılmaz (403 bandı çıkmasın). */
  private yetkiliKaynak<T extends SecimSecenegi>(kaynak: SecimKaynagi<T>): SecimKaynagi<T> {
    return (arama, limit) => (this.operasyon() ? kaynak(arama, limit) : of([]));
  }

  private degerSinyali<T>(kontrol: AbstractControl<T>): Signal<T> {
    return toSignal(kontrol.valueChanges.pipe(startWith(kontrol.value)), {
      initialValue: kontrol.value,
    });
  }
}
