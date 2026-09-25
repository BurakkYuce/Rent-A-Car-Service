import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  Injector,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { tarihBicimle } from '@core/bicim/bicim';
import type { KaydedilmemisDegisiklikSahibi } from '@core/form/kaydedilmemis-degisiklik';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { bugun } from '@core/form/tarih-girdisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';

import { SayfaBandi } from '../../kabuk/sayfa-bandi/sayfa-bandi';
import {
  REZ_SART_DURUMLARI,
  REZ_SART_LISTESI,
  type RezSart,
  type RezSartDurumu,
  type RezSartFormDegeri,
  bekleyenParametreleri,
  kayittanDegerler,
  rezSartGovdesi,
  yeniDegerler,
} from './rez-sart-modeli';
import { rezSartSutunlari } from './rez-sart-sutunlari';
import { RezSartlariStore } from './rez-sartlari.store';

const KOK = '/api/ui/v1/rez-sartlari';
const jsonEsit = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

type Duzenleme = { readonly tur: 'yeni' } | { readonly tur: 'kayit'; readonly id: string };

/**
 * Rez şartları (müşteri özel talepleri, `/app/rez-sartlari`) — Blazor `RezSartList.razor` paritesi:
 * oluştur (talep tarihi bugün), süzgeç (müşteri/durum/talep günü aralığı), "N kayıt · N bekleyen",
 * satırda Karşılandı / Geri al / Düzenle / Sil (onaylı). Operasyonel not defteridir: para ve defter yok.
 *
 * Düzenleme tam değiştirmedir (`PUT` + `surum`); bayat sürüm 409 `cakisma` → güncel kayıt okunur ve
 * KİRLİ forma birleştirilir (dokunulmayan alan güncellenir, dokunulan korunur, ikisi de değiştiyse işaret)
 * — form silinmez, kullanıcı kontrol edip yeniden kaydeder.
 */
@Component({
  selector: 'rc-rez-sartlari',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SayfaBandi,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, RezSartlariStore],
  templateUrl: './rez-sartlari.html',
  styleUrl: './rez-sartlari.scss',
})
export class RezSartlari implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(RezSartlariStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly onay = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly bant = inject(UyariBandiServisi);
  private readonly yikim = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly eleman = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly liste = listeSorgusuUrlSenkronu(REZ_SART_LISTESI);
  protected readonly sutunlar = rezSartSutunlari(this.t);
  protected readonly kimlik = (r: RezSart) => r.id;
  protected readonly musteriler = sunucuSecimKaynagi('musteri');

  // ---- süzgeç
  protected readonly filtreFormu = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null),
    durum: new FormControl<RezSartDurumu | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });
  protected readonly durumSecenekleri: readonly SecenekOgesi<RezSartDurumu>[] =
    REZ_SART_DURUMLARI.map((d) => ({ deger: d, etiket: this.t(`rezSartlari.durumlar.${d}`) }));
  /** Seçilen müşterinin etiketi (URL'de yalnız kimlik durur). Yalnız bellekte. */
  private readonly musteriEtiketleri = new Map<string, string>();

  protected readonly ozet = computed(() => {
    const l = this.store.liste.veri();
    if (!l) return null;
    const bekleyen =
      this.liste.sorgu().filtreler.durum === 'karsilanan' ? 0 : this.store.bekleyen.veri()?.toplam;
    return bekleyen === undefined
      ? this.t('rezSartlari.ozetKayit', { toplam: l.toplam })
      : this.t('rezSartlari.ozet', { toplam: l.toplam, bekleyen });
  });

  // ---- oluştur / düzenle formu
  protected readonly duzenleme = signal<Duzenleme | null>(null);
  protected readonly taban = signal<RezSart | null>(null);
  protected readonly detayYukleniyor = signal(false);
  protected readonly form = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null, Validators.required),
    sart: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(512)]),
    grup: new FormControl<string | null>(null, Validators.maxLength(64)),
    basTar: new FormControl<string | null>(null),
    bitTar: new FormControl<string | null>(null),
    talepTarihi: new FormControl<string | null>(null),
    karsilamaTarihi: new FormControl<string | null>(null),
    teslimEden: new FormControl<string | null>(null, Validators.maxLength(128)),
  });
  protected readonly gonderim = formGonderimi();
  protected readonly islemde = signal<string | null>(null);
  protected readonly grupOnerileri = computed(() => this.store.gruplar.veri() ?? []);

  constructor() {
    const politika = inject(FetchPolicy);
    politika.baglan({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    politika.baglan({
      parametre: computed(() => bekleyenParametreleri(this.liste.apiParametreleri()), {
        equal: jsonEsit,
      }),
      yukle: (p) => (p === null ? this.store.bekleyen.sifirla() : this.store.bekleyen.yukle(p)),
      sifirla: () => this.store.bekleyen.sifirla(),
      sekmeyeDonunce: 'yenile',
      esit: jsonEsit,
    });
    politika.baglan({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.gruplar.yukle(),
      sifirla: () => this.store.gruplar.sifirla(),
    });
    // URL'deki müşteri kimliğinin etiketi (paylaşılan bağlantı / yenileme) sunucudan çözülür.
    politika.baglan({
      parametre: computed(() => this.liste.sorgu().filtreler.musteriId ?? null),
      yukle: (id) => {
        if (id !== null && !this.musteriEtiketleri.has(id)) this.store.musteri.yukle(id);
      },
    });
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      const cozulen = this.store.musteri.veri();
      if (cozulen) this.musteriEtiketleri.set(cozulen.id, cozulen.etiket);
      untracked(() =>
        this.filtreFormu.reset({
          musteri:
            f.musteriId === undefined
              ? null
              : {
                  id: f.musteriId,
                  etiket:
                    this.musteriEtiketleri.get(f.musteriId) ?? this.t('rezSartlari.seciliMusteri'),
                },
          durum: f.durum ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.duzenleme() !== null && this.form.dirty;
  }

  // ------------------------------------------------------------------ süzgeç

  protected filtrele(): void {
    const v = this.filtreFormu.getRawValue();
    if (v.musteri) this.musteriEtiketleri.set(v.musteri.id, v.musteri.etiket);
    void this.liste.degistir({
      filtreler: {
        musteriId: v.musteri?.id ?? undefined,
        durum: v.durum ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected temizle(): void {
    void this.liste.sifirla();
  }

  // ------------------------------------------------------------------ form

  protected async yeni(): Promise<void> {
    if (!(await this.acikFormuBirak())) return;
    this.taban.set(null);
    this.formuDoldur(yeniDegerler(bugun()));
    this.duzenleme.set({ tur: 'yeni' });
    this.odakla();
  }

  protected async duzenle(satir: RezSart): Promise<void> {
    if (!(await this.acikFormuBirak())) return;
    this.duzenleme.set({ tur: 'kayit', id: satir.id });
    this.taban.set(null);
    this.formuDoldur(kayittanDegerler(satir));
    this.detayOku(satir.id, true);
    this.odakla();
  }

  protected async vazgec(): Promise<void> {
    if (this.form.dirty) {
      const evet = await this.onay.sor({
        baslik: this.t('rezSartlari.vazgecBaslik'),
        mesaj: this.t('rezSartlari.vazgecMesaj'),
      });
      if (!evet) return;
    }
    this.kapat();
  }

  protected kaydet(): void {
    const d = this.duzenleme();
    if (d === null) return;
    const taban = this.taban();
    if (d.tur === 'kayit' && taban === null) return; // sürüm okunmadan tam değiştirme gönderilmez
    const govde = rezSartGovdesi(this.form.getRawValue() as RezSartFormDegeri, taban);
    const esleme = { musteriId: 'musteri' };
    if (d.tur === 'yeni') {
      this.gonderim.gonder(
        this.form,
        (anahtar) => this.api.post<RezSart>(KOK, govde, { islemAnahtari: anahtar }),
        {
          esleme,
          basarili: () => {
            this.toast.basari(this.t('rezSartlari.olusturuldu'));
            this.kapat();
            this.yenile();
          },
        },
      );
      return;
    }
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        this.api.put<RezSart>(`${KOK}/${encodeURIComponent(d.id)}`, govde, {
          islemAnahtari: anahtar,
        }),
      {
        esleme,
        basarili: () => {
          this.toast.basari(this.t('rezSartlari.kaydedildi'));
          this.kapat();
          this.yenile();
        },
        // Bayat sürüm: güncel kayıt okunur, kirli forma birleştirilir (form SİLİNMEZ; yeniden gönderim yok).
        hata: (h) => {
          if (h.kod === 'cakisma') this.detayOku(d.id, false);
        },
      },
    );
  }

  // ------------------------------------------------------------------ satır işlemleri

  protected karsilandi(satir: RezSart): void {
    this.islem(
      satir,
      `${KOK}/${encodeURIComponent(satir.id)}/karsilandi`,
      { teslimEden: null },
      'rezSartlari.karsilandiBildirim',
    );
  }

  protected geriAl(satir: RezSart): void {
    this.islem(
      satir,
      `${KOK}/${encodeURIComponent(satir.id)}/geri-al`,
      null,
      'rezSartlari.geriAlindi',
    );
  }

  protected async sil(satir: RezSart): Promise<void> {
    if (this.islemde() !== null) return;
    const evet = await this.onay.sor({
      baslik: this.t('rezSartlari.silBaslik'),
      mesaj: this.t('rezSartlari.silMesaj', { sart: satir.sart }),
      onayEtiketi: this.t('rezSartlari.sil'),
      tehlikeli: true,
    });
    if (!evet || this.islemde() !== null) return;
    this.islemde.set(satir.id);
    this.api
      .delete<unknown>(`${KOK}/${encodeURIComponent(satir.id)}`)
      .pipe(
        finalize(() => this.islemde.set(null)),
        takeUntilDestroyed(this.yikim),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t('rezSartlari.silindi'));
          const d = this.duzenleme();
          if (d?.tur === 'kayit' && d.id === satir.id) this.kapat();
          this.yenile();
        },
        error: (ham: unknown) => this.islemHatasi(ham),
      });
  }

  private islem(
    satir: RezSart,
    yol: `/api/ui/v1/${string}`,
    govde: unknown,
    bildirim: 'rezSartlari.karsilandiBildirim' | 'rezSartlari.geriAlindi',
  ): void {
    if (this.islemde() !== null) return;
    this.islemde.set(satir.id);
    this.api
      .post<RezSart>(yol, govde)
      .pipe(
        finalize(() => this.islemde.set(null)),
        takeUntilDestroyed(this.yikim),
      )
      .subscribe({
        next: (guncel) => {
          this.toast.basari(this.t(bildirim));
          // Açık düzenleme formu aynı kayıtsa yeni sürüm ve değerler birleştirilir (bayat PUT olmasın).
          const d = this.duzenleme();
          if (d?.tur === 'kayit' && d.id === satir.id) this.detayGeldi(guncel, false);
          this.yenile();
        },
        error: (ham: unknown) => this.islemHatasi(ham),
      });
  }

  private islemHatasi(ham: unknown): void {
    const hata = apiHatasinaCevir(ham);
    if (!genelGosterilir(hata)) this.toast.hata(hata.detay);
    this.yenile();
  }

  // ------------------------------------------------------------------ yardımcılar

  protected durumMetni(satir: RezSart): string {
    return satir.karsilamaTarihi
      ? this.t('rezSartlari.karsilandiTarih', { tarih: tarihBicimle(satir.karsilamaTarihi) })
      : this.t('rezSartlari.bekliyor');
  }

  private detayOku(id: string, ilk: boolean): void {
    this.detayYukleniyor.set(true);
    this.api
      .get<RezSart>(`${KOK}/${encodeURIComponent(id)}`)
      .pipe(
        finalize(() => this.detayYukleniyor.set(false)),
        takeUntilDestroyed(this.yikim),
      )
      .subscribe({
        next: (s) => {
          const d = this.duzenleme();
          if (d?.tur === 'kayit' && d.id === id) this.detayGeldi(s, ilk);
        },
        error: (ham: unknown) => {
          const hata = apiHatasinaCevir(ham);
          if (!genelGosterilir(hata)) this.toast.hata(hata.detay);
        },
      });
  }

  /** Güncel kayıt: temiz form sıfırlanır; kirli formda birleştirme (dokunulan alan korunur). */
  private detayGeldi(s: RezSart, ilk: boolean): void {
    const yeni = kayittanDegerler(s);
    const eski = this.taban();
    if (ilk || !this.form.dirty || eski === null) {
      if (!this.form.dirty) this.formuDoldur(yeni);
    } else {
      const cakisan = sunucuDegerleriniBirlestir(
        this.form,
        { ...yeni },
        { ...kayittanDegerler(eski) },
        this.t('rezSartlari.cakismaAlan'),
      );
      if (cakisan.length > 0) {
        this.bant.goster({
          tur: 'uyari',
          mesaj: this.t('rezSartlari.cakismaBant', { sayi: cakisan.length }),
          kod: 'cakisma',
        });
      }
    }
    this.taban.set(s);
  }

  private formuDoldur(v: RezSartFormDegeri): void {
    this.form.reset({ ...v });
  }

  private kapat(): void {
    this.duzenleme.set(null);
    this.taban.set(null);
    this.form.reset(yeniDegerler(bugun()));
  }

  /** Açık kirli form başka kayda geçmeden önce sorulur. */
  private async acikFormuBirak(): Promise<boolean> {
    if (this.duzenleme() === null || !this.form.dirty) return true;
    return this.onay.sor({
      baslik: this.t('rezSartlari.vazgecBaslik'),
      mesaj: this.t('rezSartlari.vazgecMesaj'),
    });
  }

  private odakla(): void {
    afterNextRender(
      () => this.eleman.nativeElement.querySelector<HTMLElement>('.duzenleyici h2')?.focus(),
      { injector: this.injector },
    );
  }

  private yenile(): void {
    this.store.liste.yenile();
    this.store.bekleyen.yenile();
    this.store.gruplar.yenile();
  }
}
