import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import {
  type AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { tarihSaatBicimle } from '@core/bicim/bicim';
import { saatCoz } from '@core/form/tarih-girdisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import type { StoreDurumu } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import {
  MUSAITLIK,
  type MusaitlikSatiri,
  aramaParametreleri,
  kiralaParametreleri,
} from './musaitlik-modeli';
import { musaitlikSutunlari } from './musaitlik-sutunlari';
import { MusaitlikStore } from './musaitlik.store';

/** Saat kutusu: boş serbest; doluysa `saatCoz` kabul etmeli (`25:00` sessizce kırpılmaz). */
function saatDogrulayici(k: AbstractControl<string | null>): ValidationErrors | null {
  const v = k.value?.trim() ?? '';
  return v === '' || saatCoz(v) !== 'gecersiz' ? null : { saatGecersiz: true };
}

const jsonEsit = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

/**
 * Müsait araç arama (`/app/musaitlik`) — Blazor `MusaitlikArama.razor` paritesi (FAZ-48/73): pencere
 * (gün sayısı bitişin yerine geçer, alış/dönüş saati İSTANBUL saatidir), grup/şube/kaynak/döviz/plaka
 * süzgeçleri, 25 sütun, broker çiti notu ve "Kirala" → kira formu (`?varac&vfrom&vto&vgrup`). Pencere
 * kuralı, fiyat, broker ve döviz süzgeci SUNUCUDA; ekran yalnız gösterir. Yanıttaki `pencereBas/Bit`
 * gerçek UTC anıdır ve İstanbul saatiyle yazılır.
 */
@Component({
  selector: 'rc-musaitlik-sayfasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Ikon,
    MetinGirdisi,
    SayiGirdisi,
    Secim,
    Tablo,
    TabloHucre,
    TarihSecici,
  ],
  providers: [FetchPolicy, MusaitlikStore],
  templateUrl: './musaitlik-sayfasi.html',
  styleUrl: './musaitlik-sayfasi.scss',
})
export class MusaitlikSayfasi {
  protected readonly store = inject(MusaitlikStore);
  private readonly t = ceviriFonksiyonu();
  protected readonly liste = listeSorgusuUrlSenkronu(MUSAITLIK);
  protected readonly sutunlar = musaitlikSutunlari(this.t);
  protected readonly kimlik = (r: MusaitlikSatiri) => r.id;

  protected readonly form = new FormGroup({
    basGun: new FormControl<string | null>(null, Validators.required),
    bitGun: new FormControl<string | null>(null),
    gun: new FormControl<number | null>(null, [Validators.min(1), Validators.max(365)]),
    basSaat: new FormControl<string | null>(null, saatDogrulayici),
    bitSaat: new FormControl<string | null>(null, saatDogrulayici),
    grup: new FormControl<string | null>(null),
    sube: new FormControl<string | null>(null),
    rezKaynak: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>(null),
    plaka: new FormControl<string | null>(null),
  });

  private readonly arama = computed(() => aramaParametreleri(this.liste.apiParametreleri()), {
    equal: jsonEsit,
  });
  protected readonly aramaVar = computed(() => this.arama() !== null);

  /** Tablo kaynağı: yanıtın araç listesi (dört durum korunur; hata ASLA boş liste değildir). */
  protected readonly tabloKaynagi = computed<StoreDurumu<readonly MusaitlikSatiri[]>>(() => {
    const d = this.store.sonuc.durum();
    switch (d.tur) {
      case 'hazir':
        return { tur: 'hazir', veri: d.veri.araclar };
      case 'yukleniyor':
        return { tur: 'yukleniyor', onceki: d.onceki?.araclar };
      default:
        return d;
    }
  });

  protected readonly ozet = computed(() => {
    const v = this.store.sonuc.veri();
    if (!v) return null;
    return this.t('musaitlikSayfasi.ozet', {
      bas: tarihSaatBicimle(v.pencereBas),
      bit: tarihSaatBicimle(v.pencereBit),
      sayi: v.araclar.length,
    });
  });
  protected readonly brokerNotu = computed(() => {
    const v = this.store.sonuc.veri();
    const elenen = Number(v?.brokerElenen ?? 0);
    if (!v || elenen <= 0) return null;
    return this.t('musaitlikSayfasi.brokerNotu', {
      sayi: elenen,
      kaynak: this.liste.sorgu().filtreler.rezKaynak ?? '',
      gerekce: v.brokerGerekce.join(', '),
    });
  });

  private readonly secenekler = computed(() => this.store.secenekler.veri());
  protected readonly grupOnerileri = computed(() => this.secenekler()?.gruplar ?? []);
  protected readonly subeOnerileri = computed(() => this.secenekler()?.subeler ?? []);
  protected readonly kaynakSecenekleri = computed(() =>
    this.metinSecenekleri(this.secenekler()?.kaynaklar, this.liste.sorgu().filtreler.rezKaynak),
  );
  protected readonly dovizSecenekleri = computed(() =>
    this.metinSecenekleri(this.secenekler()?.dovizler, this.liste.sorgu().filtreler.doviz),
  );

  constructor() {
    const politika = inject(FetchPolicy);
    politika.baglan({
      parametre: this.arama,
      yukle: (p) => (p === null ? this.store.sonuc.sifirla() : this.store.sonuc.yukle(p)),
      sifirla: () => this.store.sonuc.sifirla(),
      esit: jsonEsit,
    });
    politika.baglan({
      parametre: signal(0).asReadonly(),
      yukle: () => this.store.secenekler.yukle(),
      sifirla: () => this.store.secenekler.sifirla(),
    });
    // URL → form (geri/ileri, paylaşılan bağlantı).
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() =>
        this.form.reset({
          basGun: f.basGun ?? null,
          bitGun: f.bitGun ?? null,
          gun: f.gun ?? null,
          basSaat: f.basSaat ?? null,
          bitSaat: f.bitSaat ?? null,
          grup: f.grup ?? null,
          sube: f.sube ?? null,
          rezKaynak: f.rezKaynak ?? null,
          doviz: f.doviz ?? null,
          plaka: f.plaka ?? null,
        }),
      );
    });
  }

  protected ara(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const saat = (s: string | null) => {
      const c = saatCoz(s ?? '');
      return c === null || c === 'gecersiz' ? undefined : c;
    };
    const metin = (s: string | null) => (s === null || s.trim() === '' ? undefined : s.trim());
    void this.liste.degistir({
      filtreler: {
        basGun: v.basGun ?? undefined,
        bitGun: v.bitGun ?? undefined,
        gun: v.gun ?? undefined,
        basSaat: saat(v.basSaat),
        bitSaat: saat(v.bitSaat),
        grup: metin(v.grup),
        sube: metin(v.sube),
        rezKaynak: v.rezKaynak ?? undefined,
        doviz: v.doviz ?? undefined,
        plaka: metin(v.plaka),
      },
    });
  }

  protected kiralaSorgusu(satir: MusaitlikSatiri): Record<string, string> | null {
    const v = this.store.sonuc.veri();
    return v ? kiralaParametreleri(satir.id, v.kiralaSorgusu) : null;
  }

  private metinSecenekleri(
    liste: readonly string[] | undefined,
    secili: string | undefined,
  ): readonly SecenekOgesi<string>[] {
    const degerler = [...(liste ?? [])];
    if (secili !== undefined && !degerler.includes(secili)) degerler.unshift(secili);
    return degerler.map((d) => ({ deger: d, etiket: d }));
  }
}
