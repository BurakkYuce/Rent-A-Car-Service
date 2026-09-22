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
import { EMPTY, type Observable, startWith } from 'rxjs';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TemelStore } from '@core/veri/temel-store';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';
import { type FormGonderimi, formGonderimi } from '@shared/form/form-gonderimi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import type { KiraDetayYaniti } from '../kira-tipleri';
import {
  KANALLAR,
  TahsilatKopyasi,
  depozitoAlGovdesi,
  disHizmetGovdesi,
  dovizKodu,
  faturaGovdesi,
  iratGovdesi,
  odemeGovdesi,
  onDoldurmaTutari,
  paraMetni,
  tahsilatGovdesi,
} from './finans-modeli';
import type {
  DonemFaturaIstegi,
  DonemFaturaYaniti,
  FinansHesapOgesi,
  FinansIslemYaniti,
  HesapTuru,
  KiraCezaHgs,
  KiraDisHizmet,
  KiraDonem,
  KiraFatura,
  KurSecimOgesi,
  TahsilatBilgisi,
} from './finans-tipleri';

const KIRA = '/api/ui/v1/kiralar' as const;
const FINANS = '/api/ui/v1/finans' as const;
const SESSIZ = istekBaglami({ sessiz: true });
const KUR_LIMITI = 20;

type Para = string | number | null;
const tutarKontrolu = () =>
  new FormControl<Para>(null, [Validators.required, Validators.min(0.01)]);
const secenekKontrolu = (deger: string | null = null) => new FormControl<string | null>(deger);

/** Sabit paneldeki tahsilat formu (Nakit = Kasa, Kart/Havale = Banka): değerler + satır kopyası + gönderim. */
export interface TahsilatFormu {
  readonly hesap: HesapTuru;
  readonly form: FormGroup<{
    tutar: FormControl<Para>;
    doviz: FormControl<string | null>;
    kur: FormControl<Para>;
    hesapId: FormControl<string | null>;
    kanal: FormControl<string | null>;
    aciklama: FormControl<string | null>;
  }>;
  readonly kopya: TahsilatKopyasi;
  readonly gonderim: FormGonderimi;
  /** Seçili döviz (ISO) — kur alanı ve para simgesi için. */
  readonly doviz: Signal<string>;
  /** Yazılan tutar (invariant metin) — yalnız kalan bakiye UYARISI için karşılaştırılır, hesap yapılmaz. */
  readonly tutar: Signal<Para>;
}

/** Dönem satırı mini formu (tahsilat istendi mi + hesap). */
export type DonemFormu = FormGroup<{
  tahsilat: FormControl<boolean | null>;
  hesap: FormControl<HesapTuru | null>;
}>;

/**
 * Kira formunun sabit yan panelindeki finans işlemleri (F4.4) — durum + eylemler (panel bileşeninin
 * `providers`'ında; alt bileşenler yalnız çizer). Blazor `StickyPanel.razor` paritesi.
 *
 * Para kuralları (docs/api/idempotency-envanteri.md "SPA sözleşmesi"):
 * - Her işlem KENDİ gönderimiyle (`formGonderimi` → kendi `GonderimKilidi`, kendi `Idempotency-Key`'i):
 *   çift tık tek istek, anahtar yeniden denemede aynı, her 2xx ve 409 `mukerrer` sonrası yeni.
 * - Tahsilat anahtarı DETAYDAN (`tahsilat.anahtar`, deterministik); başlık gönderilmez, gövde satır
 *   kopyasından kurulur ve sonuçlanmamış gönderimde kopya DONAR (`TahsilatKopyasi`).
 * - 409 `mukerrer`de otomatik yeniden gönderim YOK: kayıt yeniden yüklenir (interceptor
 *   `mukerrerdeYenile`), sunucu `detail`'ı bilgi olarak gösterilir.
 * - `dogrulama`/`cakisma` formu silmez (`formGonderimi`); `yetki_yok` bant (interceptor).
 * - İşlem sonrası kira detayı + panel alt kayıtları tazelenir (`degisti`).
 */
@Injectable()
export class KiraFinansDurumu {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly onay = inject(OnayServisi);
  private readonly oturum = inject(OturumServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  /** Sayfaya "kayıt değişti" bildirimi (panelin `degisti` çıktısı); panel kurucuda bağlar. */
  degisti: () => void = () => undefined;

  private readonly _detay = signal<KiraDetayYaniti | null>(null);
  readonly detay: Signal<KiraDetayYaniti | null> = this._detay.asReadonly();
  readonly kira = computed(() => this._detay()?.kira ?? null);
  readonly finans = computed(() => this._detay()?.yetkiler.finans ?? false);
  readonly iptal = computed(() => this.kira()?.durum === 'Iptal');
  /** Dış hizmet iptali dar izin ister (Blazor `AuthorizeView Policy="izin:FinanceReverse"`); kapı sunucuda. */
  readonly tersIzni = computed(() => this.oturum.izinVar('FinanceReverse'));

  // ─── alt kayıtlar (sekme ilk açılınca yüklenir; işlemden sonra yüklü olanlar tazelenir) ────────
  readonly faturalar = new TemelStore(
    (id: string) => this.api.get<KiraFatura[]>(`${KIRA}/${id}/faturalar`),
    { oncekiVeriyiKoru: true },
  );
  readonly cezalar = new TemelStore(
    (id: string) => this.api.get<KiraCezaHgs>(`${KIRA}/${id}/cezalar`),
    { oncekiVeriyiKoru: true },
  );
  /** Dönem planı OperationsWrite ister (servis guard'ı; Blazor'da da); izinsizde sessiz hata notu. */
  readonly donemler = new TemelStore(
    (id: string) => this.api.get<KiraDonem[]>(`${KIRA}/${id}/donem-plani`, { context: SESSIZ }),
    { oncekiVeriyiKoru: true },
  );
  readonly disHizmetler = new TemelStore(
    (id: string) => this.api.get<KiraDisHizmet[]>(`${KIRA}/${id}/dis-hizmetler`),
    { oncekiVeriyiKoru: true },
  );
  /** TCMB günün kurları (`secim/kur`, OperationsWrite VEYA FinanceWrite); hata sessiz not. */
  readonly kurlar = new TemelStore(() =>
    this.api.get<KurSecimOgesi[]>('/api/ui/v1/secim/kur', {
      parametreler: { limit: KUR_LIMITI },
      context: SESSIZ,
    }),
  );
  readonly kasaHesaplari = this.hesapStore('Kasa');
  readonly bankaHesaplari = this.hesapStore('Banka');

  // ─── formlar ────────────────────────────────────────────────────────────────────────────────
  readonly nakit: TahsilatFormu = this.tahsilatFormu('Kasa');
  readonly kart: TahsilatFormu = this.tahsilatFormu('Banka');
  readonly odemeFormu = new FormGroup({
    tutar: tutarKontrolu(),
    hesapId: secenekKontrolu(),
    kanal: secenekKontrolu(KANALLAR[0]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  readonly depozitoAlFormu = new FormGroup({
    tutar: tutarKontrolu(),
    hesap: new FormControl<HesapTuru | null>('Kasa', Validators.required),
    hesapId: secenekKontrolu(),
  });
  readonly iratFormu = new FormGroup({
    tutar: tutarKontrolu(),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  readonly faturaFormu = new FormGroup({
    otv: new FormControl<Para>(null, Validators.min(0)),
    tevkifatOran: new FormControl<Para>(null, [Validators.min(0), Validators.max(100)]),
    tevkifatTutar: new FormControl<Para>(null, Validators.min(0)),
    damgaVergisi: new FormControl<Para>(null, Validators.min(0)),
    iadeMi: new FormControl<boolean | null>(false),
    manuelMi: new FormControl<boolean | null>(false),
  });
  readonly disHizmetFormu = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    alinanHizmet: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(256),
    ]),
    hizmetAlinanFirma: new FormControl<string | null>(null, Validators.maxLength(256)),
    hizmetBedeli: tutarKontrolu(),
    komisyonOran: new FormControl<Para>(null, [Validators.min(0), Validators.max(100)]),
    doviz: secenekKontrolu('TRY'),
    kur: new FormControl<Para>(null, Validators.min(0.000001)),
    komisyonFaturaNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  private readonly donemFormlari = new Map<number, DonemFormu>();
  /** Depozito tutarı kiranın depozitosuyla yalnız bir kez (depozito alınana dek) önerilir (adversarial L2). */
  private depozitoOnDoldur = true;
  /** Dış hizmet formunda seçili döviz (kur alanı için). */
  readonly disHizmetDovizi = this.dovizSinyali(this.disHizmetFormu.controls.doviz);
  /** Depozito al formunda seçili hesap türü (hesap listesi için). */
  readonly depozitoHesapTuru = this.degerSinyali(this.depozitoAlFormu.controls.hesap);

  // ─── gönderimler (her işlem kendi kilidi + kendi anahtarı) ─────────────────────────────────
  readonly odemeGonderimi = formGonderimi();
  readonly depozitoAlGonderimi = formGonderimi();
  readonly iratGonderimi = formGonderimi();
  readonly faturaGonderimi = formGonderimi();
  readonly disHizmetGonderimi = formGonderimi();
  readonly donemKilidi = new GonderimKilidi();
  readonly iptalKilidi = new GonderimKilidi();
  /** Hangi dönem satırı gönderiliyor (düğme metni için). */
  readonly gonderilenDonem = signal<number | null>(null);
  /** Son dönem işleminin sunucu bilgisi (`tahsilatYazildi=false` + `bilgi`) — panelde kalıcı not. */
  readonly donemBilgisi = signal<string | null>(null);

  readonly kanalSecenekleri: readonly SecenekOgesi<string>[] = KANALLAR.map((k) => ({
    deger: k,
    etiket: k,
  }));

  constructor() {
    effect(() => {
      const d = this._detay();
      untracked(() => this.detayGeldi(d));
    });
  }

  /** Panel girdisi değişti (kira yüklendi/tazelendi). Aynı nesne yeniden verilirse hiçbir şey olmaz. */
  detayAyarla(d: KiraDetayYaniti | null): void {
    this._detay.set(d);
  }

  /** Sekme ilk kez açıldı: o sekmenin verisi yüklenir (bir kez; sonra işlemlerle tazelenir). */
  sekmeAcildi(sekme: string): void {
    const id = this.kira()?.id;
    if (!id) return;
    const ilk = <T, P>(store: TemelStore<T, P>, p: P): void => {
      if (store.tur() === 'bos') store.yukle(p);
    };
    switch (sekme) {
      case 'nakit':
      case 'kart':
        if (this.finans()) {
          ilk(this.kasaHesaplari, undefined);
          ilk(this.bankaHesaplari, undefined);
        }
        break;
      case 'faturalar':
        ilk(this.faturalar, id);
        break;
      case 'donem':
        ilk(this.donemler, id);
        break;
      case 'dishizmet':
        ilk(this.disHizmetler, id);
        break;
      case 'kurlar':
        ilk(this.kurlar, undefined);
        break;
      case 'ceza':
        ilk(this.cezalar, id);
        break;
    }
  }

  hesapSecenekleri(tur: HesapTuru | null): readonly SecenekOgesi<string>[] {
    const store =
      tur === 'Banka' ? this.bankaHesaplari : tur === 'Kasa' ? this.kasaHesaplari : null;
    return (store?.veri() ?? []).map((h) => ({ deger: h.id, etiket: h.etiket }));
  }

  donemFormu(sira: number): DonemFormu {
    let f = this.donemFormlari.get(sira);
    if (!f) {
      f = new FormGroup({
        tahsilat: new FormControl<boolean | null>(false),
        hesap: new FormControl<HesapTuru | null>('Kasa'),
      });
      this.donemFormlari.set(sira, f);
    }
    return f;
  }

  /**
   * Sayfa terk koruması (adversarial L3): panel formlarından biri kirli ya da bir para gönderimi SONUÇLANMADI
   * (donmuş tahsilat kopyası / bekleyen `Idempotency-Key`) — sayfadan ayrılınca anahtar sessizce kaybolmasın.
   */
  kirliMi(): boolean {
    const formlar = [
      this.nakit.form,
      this.kart.form,
      this.odemeFormu,
      this.depozitoAlFormu,
      this.iratFormu,
      this.faturaFormu,
      this.disHizmetFormu,
    ];
    const bekleyen = [
      this.odemeGonderimi,
      this.depozitoAlGonderimi,
      this.iratGonderimi,
      this.disHizmetGonderimi,
    ].some((g) => g.kilit.bekleyenAnahtar !== null);
    return (
      formlar.some((f) => f.dirty) ||
      bekleyen ||
      this.nakit.kopya.sonuclanmamis ||
      this.kart.kopya.sonuclanmamis
    );
  }

  // ─── işlemler ──────────────────────────────────────────────────────────────────────────────

  /**
   * Kira tahsilatı (E01). Anahtar = detaydaki deterministik `tahsilatAnahtar` (satır kopyası); başlık YOK.
   * 409 `mukerrer`: sunucu anahtarı YENİDEN hesaplar — ekran açıldıktan sonra kirada işlem olduysa (ya da
   * çift gönderimde ilki yazıldıysa) bayat anahtar 409 alır, bu istekte HİÇBİR ŞEY yazılmaz. "Mükerrer işlem"
   * başlığı parayı kaydedildi sandırır: çekirdek `mukerrerBasligi` ile nötr "Kira kaydı değişmiş" uyarısı +
   * sunucu `detail`'ı gösterilir (F4.2 "Tahsil Et" ile aynı), kayıt yeniden yüklenir, yeni anahtar tazelenen
   * detaydan alınır. Otomatik yeniden gönderim YOK; kullanıcının yazdıkları korunur.
   */
  tahsilatYap(tf: TahsilatFormu): void {
    if (!tf.kopya.gonderilebilir()) return;
    tf.gonderim.gonder(
      tf.form,
      () => {
        const kopya = tf.kopya.gonderiliyor();
        if (kopya === null) return EMPTY; // düğme zaten kapalı; kilit finalize'la bırakılır
        return this.api.post<FinansIslemYaniti>(
          `${FINANS}/tahsilat`,
          tahsilatGovdesi(kopya, tf.hesap, tf.form.getRawValue()),
          {
            context: istekBaglami({
              mukerrerdeYenile: () => this.yenile(),
              mukerrerBasligi: this.t('kiraFinans.kayitDegismis'),
            }),
          },
        );
      },
      {
        deterministikAnahtar: tf.kopya.kopya()?.anahtar ?? null,
        basarili: () => {
          tf.kopya.sonuclandi();
          this.tamam('kiraFinans.bildirim.tahsilat');
        },
        hata: (h) => {
          if (h.kod !== 'mukerrer') return;
          // HIGH-1: tutar TEMİZLENİR ve yeniden ön-doldurulmaz. `mevcut` (işlem zaten yazıldı — kaybolan
          // yanıt): form tamamen temizlenir; bilgi interceptor'da ("İşlem zaten kaydedildi" + No). Yoksa
          // (bayat anahtar): kullanıcı güncel bakiyeyi görüp tutarı bilinçli yeniden girer.
          tf.kopya.sonuclandi(false);
          if (h.mevcut) tf.form.reset(this.tahsilatVarsayilanlari(tf.kopya.kopya(), false));
          else tf.form.controls.tutar.setValue(null);
        },
      },
    );
  }

  /** Giden havale (E02): başlık anahtarı zorunlu; kiraya bağlanmaz (Blazor paritesi). */
  odemeYap(): void {
    const cariId = this.kira()?.musteriId;
    if (!cariId) return;
    this.odemeGonderimi.gonder(
      this.odemeFormu,
      (anahtar) =>
        this.api.post<FinansIslemYaniti>(
          `${FINANS}/odeme`,
          odemeGovdesi(cariId, this.odemeFormu.getRawValue()),
          { islemAnahtari: anahtar, context: this.mukerrerBaglami() },
        ),
      {
        basarili: () => {
          this.odemeFormu.reset({ tutar: null, hesapId: null, kanal: KANALLAR[0], aciklama: null });
          this.tamam('kiraFinans.bildirim.odeme');
        },
      },
    );
  }

  /** Depozito al (E09): başlık anahtarı zorunlu; aynı içerik tekrarında sunucu aynı kaydı döner. */
  depozitoAl(): void {
    const cariId = this.kira()?.musteriId;
    if (!cariId) return;
    this.depozitoAlGonderimi.gonder(
      this.depozitoAlFormu,
      (anahtar) =>
        this.api.post<FinansIslemYaniti>(
          `${FINANS}/depozito/al`,
          depozitoAlGovdesi(cariId, this.depozitoAlFormu.getRawValue()),
          { islemAnahtari: anahtar, context: this.mukerrerBaglami() },
        ),
      {
        basarili: () => {
          this.depozitoOnDoldur = false;
          this.depozitoAlFormu.reset({ tutar: null, hesap: 'Kasa', hesapId: null });
          this.tamam('kiraFinans.bildirim.depozitoAl');
        },
      },
    );
  }

  /** Depozito irat (E12): GERİ ALINAMAZ → önce onay; gelir bu kiranın aracına atfedilir. */
  async depozitoIrat(): Promise<void> {
    const k = this.kira();
    if (!k || this.iratGonderimi.gonderiliyor()) return;
    this.iratFormu.markAllAsTouched();
    if (this.iratFormu.invalid) return;
    const onay = await this.onay.sor({
      baslik: this.t('kiraFinans.irat.onayBaslik'),
      mesaj: this.t('kiraFinans.irat.onayMesaj'),
      onayEtiketi: this.t('kiraFinans.irat.onayla'),
      tehlikeli: true,
    });
    if (!onay) return;
    this.iratGonderimi.gonder(
      this.iratFormu,
      (anahtar) =>
        this.api.post<FinansIslemYaniti>(
          `${FINANS}/depozito/irat`,
          iratGovdesi(k.musteriId, k.id, this.iratFormu.getRawValue()),
          { islemAnahtari: anahtar, context: this.mukerrerBaglami() },
        ),
      {
        basarili: () => {
          this.iratFormu.reset();
          this.tamam('kiraFinans.bildirim.irat');
        },
      },
    );
  }

  /** Kiradan fatura (E15, yapısal): anahtar kullanılmaz; ikinci çağrı sunucudan 400 (form üstü hata). */
  faturaKes(gecersiz?: () => void): void {
    const k = this.kira();
    if (!k) return;
    this.faturaGonderimi.gonder(
      this.faturaFormu,
      () =>
        this.api.post<FinansIslemYaniti>(
          `${FINANS}/fatura`,
          faturaGovdesi(k.id, this.faturaFormu.getRawValue()),
        ),
      {
        // L4: geçersiz alan kapalı <details> içindeyse görünmüyordu (sessiz "Fatura kes") → bileşen açıp odaklar.
        ...(gecersiz ? { gecersiz } : {}),
        basarili: () => {
          this.faturaFormu.reset(this.faturaVarsayilanlari());
          this.tamam('kiraFinans.bildirim.fatura');
        },
      },
    );
  }

  /**
   * Dönem faturası kes (+ isteğe bağlı tahsilat) — E18/E19 yapısal + deterministik `RowKey(kira, sıra)`:
   * tekrar SESSİZ 200 (aynı fatura). `tahsilatYazildi=false` ise sunucunun `bilgi`'si GİZLENMEZ.
   */
  donemKes(d: KiraDonem): void {
    const k = this.kira();
    if (!k || this.donemKilidi.gonderiliyor()) return;
    const sira = Number(d.donemSira);
    const f = this.donemFormu(sira).getRawValue();
    const govde: DonemFaturaIstegi = {
      kiraId: k.id,
      donemSira: sira,
      tahsilat: f.tahsilat ?? false,
      ...(f.tahsilat ? { hesap: f.hesap } : {}),
    };
    this.gonderilenDonem.set(sira);
    this.kilitli(
      this.donemKilidi,
      () => this.api.post<DonemFaturaYaniti>(`${FINANS}/donem-fatura`, govde),
      (y) => {
        this.gonderilenDonem.set(null);
        const bilgi = govde.tahsilat && !y.tahsilatYazildi ? y.bilgi : null;
        this.donemBilgisi.set(bilgi);
        if (bilgi) this.toast.bilgi(bilgi, { baslik: this.t('kiraFinans.donem.tahsilatYok') });
        this.tamam(
          govde.tahsilat && y.tahsilatYazildi
            ? 'kiraFinans.bildirim.donemTahsil'
            : 'kiraFinans.bildirim.donem',
        );
      },
      () => {
        this.gonderilenDonem.set(null);
        this.yenile(); // L7: 400 (ör. başka sekmede kesildi) sonrası plan ve kira tazelensin
      },
    );
  }

  /** B2B dış hizmet alımı (E33): başlık anahtarı zorunlu. */
  disHizmetKaydet(): void {
    const k = this.kira();
    if (!k) return;
    const d = this.disHizmetFormu.getRawValue();
    this.disHizmetGonderimi.gonder(
      this.disHizmetFormu,
      (anahtar) =>
        this.api.post<FinansIslemYaniti>(
          `${FINANS}/dis-hizmet`,
          disHizmetGovdesi(k.id, { ...d, cariId: d.cari?.id ?? null }),
          { islemAnahtari: anahtar, context: this.mukerrerBaglami() },
        ),
      {
        esleme: { cariId: 'cari' },
        basarili: () => {
          this.disHizmetFormu.reset({ doviz: 'TRY' });
          this.tamam('kiraFinans.bildirim.disHizmet');
        },
      },
    );
  }

  /** Dış hizmet iptali (E34, FinanceReverse): ters kayıt, geri alınamaz → onay. */
  async disHizmetIptal(s: KiraDisHizmet): Promise<void> {
    if (this.iptalKilidi.gonderiliyor()) return;
    const onay = await this.onay.sor({
      baslik: this.t('kiraFinans.disHizmet.iptalBaslik', { no: s.no }),
      mesaj: this.t('kiraFinans.disHizmet.iptalMesaj'),
      onayEtiketi: this.t('kiraFinans.disHizmet.iptalOnay'),
      tehlikeli: true,
    });
    if (!onay) return;
    this.kilitli(
      this.iptalKilidi,
      () => this.api.post<null>(`${FINANS}/dis-hizmet/${s.id}/iptal`, null),
      () => this.tamam('kiraFinans.bildirim.disHizmetIptal'),
    );
  }

  /** Kayıt + panel verisi yeniden yüklenir (işlem sonrası ve `mukerrer`de). */
  yenile(): void {
    for (const s of [this.faturalar, this.cezalar, this.donemler, this.disHizmetler]) {
      if (s.tur() !== 'bos') s.yenile();
    }
    this.degisti();
  }

  // ─── iç ────────────────────────────────────────────────────────────────────────────────────

  private tamam(mesaj: CeviriAnahtari): void {
    this.toast.basari(this.t(mesaj));
    this.yenile();
  }

  private mukerrerBaglami() {
    return istekBaglami({ mukerrerdeYenile: () => this.yenile() });
  }

  /** Alan formu olmayan işlem: kilit + hata bildirimi (interceptor göstermediyse). */
  private kilitli<T>(
    kilit: GonderimKilidi,
    istek: () => Observable<T>,
    basarili: (y: T) => void,
    hatada?: () => void,
  ): void {
    kilit
      .gonder(istek)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: basarili,
        error: (ham: unknown) => {
          hatada?.();
          const h = apiHatasinaCevir(ham);
          if (genelGosterilir(h) || (h.kod === 'cakisma' && h.alanlar === undefined)) return;
          this.toast.hata(h.detay);
        },
      });
  }

  private detayGeldi(d: KiraDetayYaniti | null): void {
    if (d === null) return;
    const k = d.kira;
    for (const tf of [this.nakit, this.kart]) {
      const sonuc = tf.kopya.detayGeldi(d.tahsilat ?? null, tf.form.dirty);
      if (sonuc === 'ondoldur') tf.form.reset(this.tahsilatVarsayilanlari(tf.kopya.kopya(), true));
    }
    // L2: kiranın depozitosu yalnız ilk açılışta önerilir; alındıktan sonra yeniden ön-doldurulmaz (ikinci tık
    // ikinci depozito yazıyordu).
    if (this.depozitoAlFormu.pristine && this.depozitoOnDoldur) {
      this.depozitoAlFormu.reset({ tutar: paraMetni(k.depozito, 2), hesap: 'Kasa', hesapId: null });
    }
    if (this.faturaFormu.pristine) this.faturaFormu.reset(this.faturaVarsayilanlari());
  }

  /** Tahsilat formunun boş hâli; `ondoldur` ise tutar sunucunun önerisiyle (yalnız pozitifse) dolar. */
  private tahsilatVarsayilanlari(bilgi: TahsilatBilgisi | null, ondoldur: boolean) {
    return {
      tutar: ondoldur ? onDoldurmaTutari(bilgi?.varsayilanTutar) : null,
      doviz: dovizKodu(bilgi?.doviz ?? this.kira()?.doviz),
      kur: null,
      hesapId: null,
      kanal: KANALLAR[0],
      aciklama: null,
    };
  }

  private faturaVarsayilanlari() {
    return {
      otv: null,
      tevkifatOran: null,
      tevkifatTutar: null,
      damgaVergisi: paraMetni(this.kira()?.damgaVergisi, 2),
      iadeMi: false,
      manuelMi: false,
    };
  }

  private tahsilatFormu(hesap: HesapTuru): TahsilatFormu {
    const form = new FormGroup({
      tutar: tutarKontrolu(),
      doviz: new FormControl<string | null>('TRY', Validators.required),
      kur: new FormControl<Para>(null, Validators.min(0.000001)),
      hesapId: secenekKontrolu(),
      kanal: secenekKontrolu(KANALLAR[0]),
      aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
    });
    // L6: kullanıcı dövizi değiştirince DOKUNULMAMIŞ ön-dolu tutar temizlenir (TRY bakiye "2.600" USD gitmesin).
    form.controls.doviz.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (form.controls.doviz.dirty && form.controls.tutar.pristine)
        form.controls.tutar.setValue(null);
    });
    return {
      hesap,
      form,
      kopya: new TahsilatKopyasi(),
      gonderim: formGonderimi(),
      doviz: this.dovizSinyali(form.controls.doviz),
      tutar: this.degerSinyali(form.controls.tutar),
    };
  }

  private degerSinyali<T>(kontrol: AbstractControl<T>): Signal<T> {
    return toSignal(kontrol.valueChanges.pipe(startWith(kontrol.value)), {
      initialValue: kontrol.value,
    });
  }

  private dovizSinyali(kontrol: AbstractControl<string | null>): Signal<string> {
    const deger = this.degerSinyali(kontrol);
    return computed(() => dovizKodu(deger()));
  }

  private hesapStore(tur: HesapTuru): TemelStore<FinansHesapOgesi[]> {
    return new TemelStore(() =>
      this.api.get<FinansHesapOgesi[]>(`${FINANS}/hesaplar`, {
        parametreler: { tur },
        context: SESSIZ,
      }),
    );
  }
}
