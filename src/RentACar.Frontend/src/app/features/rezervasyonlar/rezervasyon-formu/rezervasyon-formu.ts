import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import type { AbstractControl } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { TemelStore } from '@core/veri/temel-store';
import { sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';

import { RF_ORTAK } from '../ortak';
import { RezervasyonIslemleri } from '../rezervasyon-islemleri';
import {
  OTA_ALANLARI,
  REZERVASYON_KOKU,
  detaydanDegerler,
  durumRozeti,
  rezervasyonDurumuMu,
  rezervasyonFormuOlustur,
  rezervasyonGovdesi,
  secenekListesi,
  sayiya,
  sunucuDegerleriniBirlestir,
  varsayilanTarihler,
  type RezervasyonAlani,
  type RezervasyonDetayYaniti,
  type RezervasyonFormDegeri,
  type RezervasyonFormSecenekleri,
  type RezervasyonOlusturYaniti,
} from '../rezervasyon-modeli';

const SESSIZ = istekBaglami({ sessiz: true });

/**
 * Rezervasyon formu — TEK bileşen iki rotada: `/app/rezervasyonlar/yeni` (oluştur) ve `/app/rezervasyonlar/:id`
 * (düzenle + durum eylemleri). Blazor `ReservationList.razor`'daki "+ Yeni Rezervasyon" ve satır içi "Düzenle"
 * formlarının alanları (FAZ-48 talep alanları, ödeme/komisyon, broker bilgisi) + PUT tam değiştirme olduğu için
 * kaydın diğer alanları (km limiti, fazla km, yakıt, açıklama) — gövdede olmayan alan boş yazılırdı.
 *
 * Kurallar: tutar/gün/KDV SUNUCUDA (fiyat motoru); hata hiçbir dalda form değerine dokunmaz (`formGonderimi`);
 * PUT detaydaki `surum`u taşır, bayatsa 409 `cakisma` → güncel kayıt okunur ve KİRLİ forma birleştirilir
 * (dokunulmamış alan güncellenir, dokunulan korunur, ikisi de değiştiyse alan işaretlenir) — form SİLİNMEZ.
 * Sayfada `<form>` yok: arama-seçimde Enter yanlışlıkla kaydetmesin; kayıt düğmeyle.
 */
@Component({
  selector: 'rc-rezervasyon-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...RF_ORTAK, RouterLink],
  providers: [RezervasyonIslemleri],
  templateUrl: './rezervasyon-formu.html',
  styleUrl: './rezervasyon-formu.scss',
})
export class RezervasyonFormuSayfasi implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastServisi);
  private readonly bant = inject(UyariBandiServisi);
  private readonly sekme = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();
  protected readonly islemler = inject(RezervasyonIslemleri);

  /** Kayıtlı rezervasyonun kimliği; yenide `null`. Bileşen örneği boyunca değişmez (sekme = rota + id). */
  readonly id: string | null = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly yeni = this.id === null;

  readonly form = rezervasyonFormuOlustur();
  protected readonly kayit = formGonderimi();
  protected readonly otaAlanlari = OTA_ALANLARI;

  readonly detay = new TemelStore(
    (id: string) => this.api.get<RezervasyonDetayYaniti>(`${REZERVASYON_KOKU}/${id}`),
    { oncekiVeriyiKoru: true },
  );
  private readonly secenekler = new TemelStore(() =>
    this.api.get<RezervasyonFormSecenekleri>(`${REZERVASYON_KOKU}/form-secenekleri`, {
      context: SESSIZ,
    }),
  );

  protected readonly musteriKaynagi = sunucuSecimKaynagi('musteri');
  protected readonly aracKaynagi = sunucuSecimKaynagi('arac');
  protected readonly lokasyonKaynagi = sunucuSecimKaynagi('lokasyon');
  protected readonly kaynakKaynagi = sunucuSecimKaynagi('rezervasyon-kaynagi');

  protected readonly rez = computed(() => this.detay.veri()?.rezervasyon ?? null);
  /** Sayfa bandı başlığı (sayfanın tek `<h1>`'i). */
  protected readonly baslik = computed(() =>
    this.yeni
      ? this.t('rezervasyon.yeniBaslik')
      : this.t('rezervasyon.detayBaslik', { no: this.rez()?.no ?? '…' }),
  );
  protected readonly yetkiler = computed(() => this.detay.veri()?.yetkiler ?? null);
  protected readonly bulunamadi = computed(() => this.detay.hata()?.status === 404);
  protected readonly duzenlenebilir = computed(
    () => this.yeni || (this.yetkiler()?.duzenle ?? false),
  );
  /** İşlem/kayıt sonrası kayıt yeniden okunurken Kaydet PASİF (#261 N1): sürüm henüz tazelenmedi. */
  protected readonly kaydedilebilir = computed(
    () => this.duzenlenebilir() && (this.yeni || !this.detay.yukleniyor()),
  );
  protected readonly fiyatTurleri = computed(() =>
    secenekListesi(this.secenekler.veri()?.fiyatTurleri, this.rez()?.fiyatTuru),
  );
  protected readonly talepTurleri = computed(() => this.secenekler.veri()?.talepTurleri ?? []);

  /** Son okunan sunucu hâli: `surum` PUT'a gider; değerler birleştirmede "sunucu neyi değiştirdi" tabanı. */
  private surum: string | null = null;
  private taban: RezervasyonFormDegeri | null = null;

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.secenekler.yukle();
    if (this.id === null) this.yeniyiBaslat();
    else this.duzenlemeyiBaslat(this.id);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  private yeniyiBaslat(): void {
    this.form.reset(varsayilanTarihler());
    // Tenant "Varsayılan Fiyat Türü" (FAZ-82) YALNIZ yeni formun ön-seçimi — kullanıcı seçmediyse.
    effect(() => {
      const v = this.secenekler.veri()?.varsayilanFiyatTuru ?? null;
      untracked(() => {
        const k = this.form.controls.fiyatTuru;
        if (v && k.value === null && k.pristine) k.setValue(v);
      });
    });
  }

  private duzenlemeyiBaslat(id: string): void {
    this.detay.yukle(id);
    effect(() => {
      const d = this.detay.veri();
      if (d) untracked(() => this.detayGeldi(d));
    });
    // Sekmeye dönüşte kayıt yeniden okunur (başka oturum/işlem değiştirmiş olabilir); ilk görünüm sayılmaz.
    let son: number | null = null;
    effect(() => {
      const n = this.sekme.onaGelme();
      untracked(() => {
        if (son !== null && n !== son) this.detay.yenile();
        son = n;
      });
    });
  }

  private detayGeldi(d: RezervasyonDetayYaniti): void {
    this.sekme.etiketAyarla(this.t('rezervasyon.sekmeEtiketi', { no: d.rezervasyon.no }));
    // Kilit birleştirmeden ÖNCE: `enable()` doğrulamayı yeniden koşar ve çakışma işaretlerini silerdi.
    if (!d.yetkiler.duzenle) this.form.disable({ emitEvent: false });
    else if (this.form.disabled) this.form.enable({ emitEvent: false });
    const yeni = detaydanDegerler(d);
    if (!this.form.dirty) {
      this.form.reset(yeni);
    } else {
      const cakisan = sunucuDegerleriniBirlestir(this.form, yeni, this.taban);
      if (cakisan.length > 0) this.cakismaIsaretle(cakisan);
    }
    this.taban = yeni;
    this.surum = d.rezervasyon.surum;
  }

  private cakismaIsaretle(alanlar: readonly RezervasyonAlani[]): void {
    const mesaj = this.t('rezervasyon.cakisma.alan');
    for (const ad of alanlar) {
      const k = this.form.controls[ad] as AbstractControl<unknown>;
      k.setErrors({ ...(k.errors ?? {}), [SUNUCU_HATASI]: [mesaj] });
      k.markAsTouched();
    }
    this.bant.goster({
      tur: 'uyari',
      mesaj: this.t('rezervasyon.cakisma.bant', { sayi: alanlar.length }),
      kod: 'cakisma',
    });
  }

  protected kaydet(): void {
    // Kilitli form (durum ya da tazeleme) `invalid` değildir — istemci doğrulaması onu durdurmaz.
    if (!this.kaydedilebilir()) return;
    if (this.id === null) {
      this.kayit.gonder(
        this.form,
        () =>
          this.api.post<RezervasyonOlusturYaniti>(
            REZERVASYON_KOKU,
            rezervasyonGovdesi(this.form.getRawValue()),
          ),
        { basarili: (y) => this.olusturuldu(y) },
      );
      return;
    }
    const id = this.id;
    this.kayit.gonder(
      this.form,
      () =>
        this.api.put<RezervasyonDetayYaniti>(`${REZERVASYON_KOKU}/${id}`, {
          ...rezervasyonGovdesi(this.form.getRawValue()),
          surum: this.surum ?? '',
        }),
      {
        // Bayat sürüm (409 cakisma): form SİLİNMEZ; güncel kayıt okunur, kirli forma birleştirilir
        // (detayGeldi); kullanıcı kontrol edip yeni sürümle yeniden kaydeder.
        hata: (h) => {
          if (h.kod === 'cakisma') this.detay.yenile();
        },
        basarili: (y) => {
          this.toast.basari(this.t('rezervasyon.bildirim.kaydedildi', { no: y.rezervasyon.no }));
          this.detay.yenile();
        },
      },
    );
  }

  private olusturuldu(y: RezervasyonOlusturYaniti): void {
    this.toast.basari(this.t('rezervasyon.bildirim.olusturuldu', { no: y.no }));
    // "Yeni rezervasyon" sekmesi yaşamaya devam eder: sonraki kayıt için temiz forma döner.
    this.form.reset({
      ...varsayilanTarihler(),
      fiyatTuru: this.secenekler.veri()?.varsayilanFiyatTuru ?? null,
    });
    this.kayit.kilit.yenile();
    void this.router.navigate(['/rezervasyonlar', y.id]);
  }

  // ─── durum eylemleri (sunucu `yetkiler`'ine göre görünür) ─────────────────────────────────

  private hedef() {
    const r = this.rez();
    return r ? { id: r.id, no: r.no } : null;
  }

  protected onayla(): void {
    const h = this.hedef();
    if (h) this.islemler.onayla(h, () => this.detay.yenile());
  }

  protected iptal(): void {
    const h = this.hedef();
    if (h) void this.islemler.iptal(h, () => this.detay.yenile());
  }

  protected kirayaCevir(): void {
    const h = this.hedef();
    if (h) void this.islemler.kirayaCevir(h, () => this.detay.yenile(), this.form.dirty);
  }

  // ─── gösterim ──────────────────────────────────────────────────────────────────────────

  protected rozet(durum: string): string {
    return durumRozeti(durum);
  }

  protected durumEtiketi(durum: string): string {
    return rezervasyonDurumuMu(durum) ? this.t(`rezervasyon.durumlar.${durum}`) : durum;
  }

  protected sayi(v: number | string | null | undefined): number | null {
    return sayiya(v);
  }
}
