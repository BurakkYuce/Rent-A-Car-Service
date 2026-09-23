import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { DUGME_IZINLERI } from '@core/oturum/dugme-izinleri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
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

import { RezervasyonIslemleri } from '../rezervasyon-islemleri';
import {
  REZERVASYON_DURUMLARI,
  durumRozeti,
  rezervasyonDurumuMu,
  type RezervasyonDurumu,
  type RezervasyonListeSatiri,
} from '../rezervasyon-modeli';
import {
  REZERVASYON_LISTESI,
  RezervasyonListesiStore,
  disaAktarmaParametreleri,
} from './rezervasyon-listesi.store';
import { rezervasyonSutunlari } from './rezervasyon-sutunlari';

/**
 * Rezervasyon listesi (`/app/rezervasyonlar`) — Blazor `ReservationList.razor` paritesi (F5.2a): FAZ-48 sütunları
 * ve süzgeçleri, sunucu sayfalama/sıralama (URL tek doğruluk kaynağı), süzgeci taşıyan dışa aktarma, satır
 * eylemleri Onayla / Kiraya çevir, "Düzenle" = rezervasyon formu (`/rezervasyonlar/:id`; iptal orada — kapısı
 * sunucunun `yetkiler.iptal`'i). Sayfa OperationsWrite ister (rota kapısı + sunucu); dışa aktarma ViewReports.
 */
@Component({
  selector: 'rc-rezervasyon-listesi',
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
    TarihSecici,
  ],
  providers: [FetchPolicy, RezervasyonListesiStore, RezervasyonIslemleri],
  templateUrl: './rezervasyon-listesi.html',
  styleUrl: './rezervasyon-listesi.scss',
})
export class RezervasyonListesi {
  protected readonly store = inject(RezervasyonListesiStore);
  protected readonly islemler = inject(RezervasyonIslemleri);
  private readonly oturum = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly t = ceviriFonksiyonu();

  protected readonly liste = listeSorgusuUrlSenkronu(REZERVASYON_LISTESI);
  protected readonly sutunlar = rezervasyonSutunlari(this.t);
  protected readonly varsayilanSirala = REZERVASYON_LISTESI.varsayilanSirala;
  protected readonly kimlik = (r: RezervasyonListeSatiri) => r.id;
  protected readonly filtreAcik = signal(false);
  protected readonly kaynaklar = sunucuSecimKaynagi('rezervasyon-kaynagi');

  // Dışa aktarma Blazor liste ucu (ViewReports) — izin haritası tek yerde (`DUGME_IZINLERI`, UiDugmeIzinTests).
  private readonly rapor = computed(() =>
    this.oturum.izinlerVar(DUGME_IZINLERI.kiraDisaAktar.izinler),
  );
  protected readonly disaAktarma = computed<DisaAktarma | null>(() =>
    this.rapor()
      ? {
          yol: '/listeler/export/rezervasyonlar',
          parametreler: disaAktarmaParametreleri(this.liste.apiParametreleri()),
          bicimler: ['excel', 'csv', 'pdf'],
        }
      : null,
  );

  protected readonly filtreFormu = new FormGroup({
    q: new FormControl<string | null>(null),
    durum: new FormControl<RezervasyonDurumu | null>(null),
    basMin: new FormControl<string | null>(null),
    basMax: new FormControl<string | null>(null),
    kaynak: new FormControl<SecimSecenegi | null>(null),
  });

  protected readonly durumSecenekleri: readonly SecenekOgesi<RezervasyonDurumu>[] =
    REZERVASYON_DURUMLARI.map((d) => ({ deger: d, etiket: this.t(`rezervasyon.durumlar.${d}`) }));

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.sifirla(),
      // Durum başka sekmede (form, takvim, teklif kabulü) değişebilir: dönüşte taze liste.
      sekmeyeDonunce: 'yenile',
    });

    // URL → form (geri/ileri, paylaşılan bağlantı, "Temizle").
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() =>
        this.filtreFormu.reset({
          q: f.q ?? null,
          durum: f.durum ?? null,
          basMin: f.basMin ?? null,
          basMax: f.basMax ?? null,
          kaynak: f.kaynak === undefined ? null : { id: f.kaynak, etiket: f.kaynak },
        }),
      );
    });
  }

  protected filtrele(): void {
    const v = this.filtreFormu.getRawValue();
    void this.liste.degistir({
      filtreler: {
        q: v.q ?? undefined,
        durum: v.durum ?? undefined,
        basMin: v.basMin ?? undefined,
        basMax: v.basMax ?? undefined,
        kaynak: v.kaynak?.etiket ?? undefined,
      },
    });
  }

  protected temizle(): void {
    void this.liste.sifirla();
  }

  protected satiriAc(satir: RezervasyonListeSatiri): void {
    void this.router.navigate(['/rezervasyonlar', satir.id]);
  }

  protected rozet(durum: string): string {
    return durumRozeti(durum);
  }

  protected durumEtiketi(durum: string): string {
    return rezervasyonDurumuMu(durum) ? this.t(`rezervasyon.durumlar.${durum}`) : durum;
  }

  protected acik(satir: RezervasyonListeSatiri): boolean {
    return satir.durum === 'Rezerv' || satir.durum === 'Onayli';
  }

  protected onayla(satir: RezervasyonListeSatiri): void {
    this.islemler.onayla({ id: satir.id, no: satir.no }, () => this.store.liste.yenile());
  }

  protected kirayaCevir(satir: RezervasyonListeSatiri): void {
    void this.islemler.kirayaCevir({ id: satir.id, no: satir.no }, () => this.store.liste.yenile());
  }
}
