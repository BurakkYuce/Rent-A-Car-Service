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
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ayBasi, ayBasligi, bugun } from '@core/form/tarih-girdisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';

import {
  TAKVIM,
  type TakvimAraci,
  ayGecerli,
  ayinGunleri,
  doluluk,
  kiralaSorgusu,
  takvimParametreleri,
} from './takvim-modeli';
import { TakvimStore } from './takvim.store';

/** Sunucunun satır tavanı (`PlanlamaApi.TakvimAracSiniri`); aşılırsa kullanıcı süzgece yönlendirilir. */
const ARAC_SINIRI = 200;

/**
 * Rezervasyon takvimi (`/app/takvim`) — Blazor `ReservationCalendar.razor` paritesi: ay ızgarası (araç ×
 * gün; sarı = rezervasyon Rezerv/Onaylı, mavi = aktif kira, kira öncelikli), ay gezinmesi süzgeçleri
 * KORUR, süzgeçler plaka/marka araması + grup (öneri listesi) + şube. Doluluk SUNUCUDA hesaplanır; gün
 * sınırı İstanbul günüdür. Plaka, aracı ön seçili yeni kira formuna bağlanır (`?varac=`).
 */
@Component({
  selector: 'rc-takvim-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, TranslocoPipe, Alan, Ikon, MetinGirdisi, Secim],
  providers: [FetchPolicy, TakvimStore],
  templateUrl: './takvim-sayfasi.html',
  styleUrl: './takvim-sayfasi.scss',
})
export class TakvimSayfasi {
  protected readonly store = inject(TakvimStore);
  private readonly t = ceviriFonksiyonu();
  protected readonly liste = listeSorgusuUrlSenkronu(TAKVIM);

  protected readonly filtreFormu = new FormGroup({
    plaka: new FormControl<string | null>(null),
    grup: new FormControl<string | null>(null),
    sube: new FormControl<string | null>(null),
  });

  /** Görüntülenen ay: sunucu yanıtı (kanonik) → URL → İstanbul'da bu ay. */
  protected readonly ay = computed(() => {
    const veri = this.store.izgara.veri();
    if (veri) return veri.ay;
    const url = this.liste.sorgu().filtreler.ay;
    return ayGecerli(url) ? url : bugun().slice(0, 7);
  });
  protected readonly ayEtiketi = computed(() => ayBasligi(`${this.ay()}-01`));
  protected readonly oncekiAy = computed(
    () => this.store.izgara.veri()?.oncekiAy ?? ayBasi(`${this.ay()}-01`, -1).slice(0, 7),
  );
  protected readonly sonrakiAy = computed(
    () => this.store.izgara.veri()?.sonrakiAy ?? ayBasi(`${this.ay()}-01`, 1).slice(0, 7),
  );
  protected readonly oncekiEtiket = computed(() => ayBasligi(`${this.oncekiAy()}-01`));
  protected readonly sonrakiEtiket = computed(() => ayBasligi(`${this.sonrakiAy()}-01`));

  protected readonly gunler = computed(() => {
    const veri = this.store.izgara.veri();
    return veri ? ayinGunleri(veri.ay, Number(veri.gunSayisi)) : [];
  });
  protected readonly araclar = computed(() => this.store.izgara.veri()?.araclar ?? []);
  protected readonly kesildi = computed(() => {
    const veri = this.store.izgara.veri();
    return veri !== undefined && Number(veri.aracToplam) > veri.araclar.length;
  });
  protected readonly aracToplam = computed(() => Number(this.store.izgara.veri()?.aracToplam ?? 0));
  protected readonly sinir = ARAC_SINIRI;

  protected readonly subeSecenekleri = computed<readonly SecenekOgesi<string>[]>(() => {
    const liste = [...(this.store.secenekler.veri()?.subeler ?? [])];
    const secili = this.liste.sorgu().filtreler.sube;
    if (secili !== undefined && !liste.includes(secili)) liste.unshift(secili);
    return liste.map((s) => ({ deger: s, etiket: s }));
  });
  protected readonly grupOnerileri = computed(() => this.store.secenekler.veri()?.gruplar ?? []);
  protected readonly filtreVar = computed(() => {
    const f = this.liste.sorgu().filtreler;
    return f.plaka !== undefined || f.grup !== undefined || f.sube !== undefined;
  });

  constructor() {
    const politika = inject(FetchPolicy);
    politika.baglan({
      parametre: computed(() => takvimParametreleri(this.liste.apiParametreleri()), {
        equal: (a, b) => JSON.stringify(a) === JSON.stringify(b),
      }),
      yukle: (p) => this.store.izgara.yukle(p),
      sifirla: () => this.store.izgara.sifirla(),
      // Başka sekmede kira/rezervasyon açılmış olabilir: dönüşte taze doluluk.
      sekmeyeDonunce: 'yenile',
    });
    politika.baglan({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.secenekler.yukle(),
      sifirla: () => this.store.secenekler.sifirla(),
    });
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() =>
        this.filtreFormu.reset({
          plaka: f.plaka ?? null,
          grup: f.grup ?? null,
          sube: f.sube ?? null,
        }),
      );
    });
  }

  protected filtrele(): void {
    const v = this.filtreFormu.getRawValue();
    void this.liste.degistir({
      filtreler: {
        ay: this.ay(),
        plaka: v.plaka ?? undefined,
        grup: v.grup ?? undefined,
        sube: v.sube ?? undefined,
      },
    });
  }

  /** Blazor "Temizle": süzgeçler kalkar, AY korunur. */
  protected temizle(): void {
    void this.liste.degistir({
      filtreler: { ay: this.ay(), plaka: undefined, grup: undefined, sube: undefined },
    });
  }

  /** Ay gezinmesi süzgeçleri KORUR (Blazor `AyUrl`). */
  protected ayaGit(ay: string): void {
    void this.liste.degistir({ filtreler: { ...this.liste.sorgu().filtreler, ay } });
  }

  protected hucre(arac: TakvimAraci, indeks: number) {
    return doluluk(arac.gunler[indeks]);
  }

  protected hucreBasligi(arac: TakvimAraci, indeks: number): string | null {
    const d = this.hucre(arac, indeks);
    return d === null
      ? null
      : this.t('takvimSayfasi.hucreBasligi', {
          plaka: arac.plaka,
          tur: this.t(`takvimSayfasi.doluluk.${d}`),
        });
  }

  protected kiralaSorgusu(arac: TakvimAraci): Record<string, string> {
    return kiralaSorgusu(arac.id);
  }
}
