import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ceviriFonksiyonu } from '@core/i18n/ceviri';
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
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import {
  FILO_DURUMLARI,
  FILO_LISTESI,
  type FiloDurumu,
  type FiloListeSatiri,
  filoDurumu,
} from './filo-modeli';
import { filoSutunlari } from './filo-sutunlari';
import { FiloListesiStore } from './filo.store';

export const DURUM_ROZETI: Readonly<Record<FiloDurumu, string>> = {
  Aktif: 'rc-rozet--bilgi',
  Tamamlandi: 'rc-rozet--basari',
  Iptal: 'rc-rozet--hata',
};

/**
 * Filo / uzun dönem kiralama listesi (`/app/filo-kiralama`) — Blazor `FiloKiralamaList.razor` paritesi:
 * FAZ-21 arama paneli (müşteri, plaka, serbest arama, durum, başlangıç aralığı), "N sözleşme", sunucu
 * dışa aktarması (Excel/CSV/PDF, ViewReports — Blazor ucu süzgeç almaz), sütunlar. Satır → sözleşme
 * detayı (künye, taksit planı, tamamla/iptal). Bu ekran deftere/bakiyeye yazmaz.
 */
@Component({
  selector: 'rc-filo-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    Ikon,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, FiloListesiStore],
  templateUrl: './filo-listesi.html',
  styleUrl: './filo.scss',
})
export class FiloListesi {
  protected readonly store = inject(FiloListesiStore);
  private readonly oturum = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly t = ceviriFonksiyonu();

  protected readonly liste = listeSorgusuUrlSenkronu(FILO_LISTESI);
  protected readonly sutunlar = filoSutunlari(this.t);
  protected readonly kimlik = (r: FiloListeSatiri) => r.id;
  protected readonly musteriler = sunucuSecimKaynagi('musteri');
  private readonly musteriEtiketleri = new Map<string, string>();

  protected readonly filtreFormu = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null),
    plaka: new FormControl<string | null>(null),
    ara: new FormControl<string | null>(null),
    durum: new FormControl<FiloDurumu | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });
  protected readonly durumSecenekleri: readonly SecenekOgesi<FiloDurumu>[] = FILO_DURUMLARI.map(
    (d) => ({ deger: d, etiket: this.t(`filoKiralama.durumlar.${d}`) }),
  );

  /** Blazor liste dışa aktarması (ViewReports); uç ekran süzgeçlerini okumaz — tüm sözleşmeler. */
  protected readonly disaAktarma = computed<DisaAktarma | null>(() =>
    this.oturum.izinVar('ViewReports')
      ? { yol: '/listeler/export/filo-kiralama', bicimler: ['excel', 'csv', 'pdf'] }
      : null,
  );

  protected readonly ozet = computed(() => {
    const l = this.store.liste.veri();
    return l ? this.t('filoKiralama.ozet', { toplam: l.toplam }) : null;
  });

  constructor() {
    const politika = inject(FetchPolicy);
    politika.baglan({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
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
                    this.musteriEtiketleri.get(f.musteriId) ?? this.t('filoKiralama.seciliMusteri'),
                },
          plaka: f.plaka ?? null,
          ara: f.ara ?? null,
          durum: f.durum ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
  }

  protected filtrele(): void {
    const v = this.filtreFormu.getRawValue();
    if (v.musteri) this.musteriEtiketleri.set(v.musteri.id, v.musteri.etiket);
    void this.liste.degistir({
      filtreler: {
        musteriId: v.musteri?.id ?? undefined,
        plaka: v.plaka ?? undefined,
        ara: v.ara ?? undefined,
        durum: v.durum ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected temizle(): void {
    void this.liste.sifirla();
  }

  protected satiriAc(satir: FiloListeSatiri): void {
    void this.router.navigate(['/filo-kiralama', satir.id]);
  }

  protected rozet(durum: string): string {
    const d = filoDurumu(durum);
    return `rc-rozet ${d ? DURUM_ROZETI[d] : ''}`;
  }

  protected durumEtiketi(durum: string): string {
    const d = filoDurumu(durum);
    return d ? this.t(`filoKiralama.durumlar.${d}`) : durum;
  }
}
