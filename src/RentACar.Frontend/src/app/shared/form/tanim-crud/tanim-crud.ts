import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  type OnInit,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { invariantOndalik, ondalikBicimle } from '@core/form/ondalik';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import { TemelStore } from '@core/veri/temel-store';
import { Ikon } from '../../ikon/ikon';
import { Alan } from '../alan/alan';
import { tekilKimlik } from '../alan/alan-baglami';
import { formGonderimi } from '../form-gonderimi';
import { FormHatalari } from '../form-hatalari';
import { MetinGirdisi } from '../kontroller/metin-girdisi';
import { OnayKutusu } from '../kontroller/onay-kutusu';
import { ParaGirdisi } from '../kontroller/para-girdisi';
import { SayiGirdisi } from '../kontroller/sayi-girdisi';
import { Secim } from '../kontroller/secim';
import type { TanimAlani, TanimKaynagi, TanimSatiri } from './tanim-kaynagi';

type Duzenleme = { readonly tur: 'yeni' } | { readonly tur: 'satir'; readonly id: string };

/**
 * Genel tanım CRUD'u (F11'in basit tanım ekranları): liste + satır içi oluştur/düzenle + onaylı sil.
 * Alanlar `TanimAlani[]` ile tanımlanır; form her düzenlemede o alanlardan kurulur. Liste
 * `TemelStore` ile (hata boş liste gibi görünmez), kayıt `formGonderimi` ile (kilit + anahtar +
 * sunucu alan hatası), silme ayrı `GonderimKilidi` ile.
 */
@Component({
  selector: 'rc-tanim-crud',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    TranslocoPipe,
    Ikon,
    Alan,
    FormHatalari,
    MetinGirdisi,
    OnayKutusu,
    ParaGirdisi,
    SayiGirdisi,
    Secim,
  ],
  styleUrl: './tanim-crud.scss',
  templateUrl: './tanim-crud.html',
})
export class TanimCrud implements OnInit {
  readonly baslik = input.required<string>();
  readonly alanlar = input.required<readonly TanimAlani[]>();
  readonly kaynak = input.required<TanimKaynagi>();

  private readonly eleman = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly liste = new TemelStore<readonly TanimSatiri[]>(() => this.kaynak().listele(), {
    oncekiVeriyiKoru: true,
  });
  protected readonly gonderim = formGonderimi();
  private readonly silmeKilidi = new GonderimKilidi();
  protected readonly siliniyor = this.silmeKilidi.gonderiliyor;

  protected readonly duzenleme = signal<Duzenleme | null>(null);
  protected readonly silinecek = signal<string | null>(null);
  protected readonly silmeHatasi = signal<string | null>(null);
  protected form = new FormGroup<Record<string, FormControl<unknown>>>({});

  protected readonly satirlar = computed(() => this.liste.veri() ?? []);

  protected readonly baslikKimligi = tekilKimlik('rc-tanim');

  ngOnInit(): void {
    this.liste.yukle();
  }

  /** Sayfanın `canDeactivate`'i için: açık satır formu kirli mi? */
  kaydedilmemisDegisiklikVar(): boolean {
    return this.duzenleme() !== null && this.form.dirty;
  }

  protected yeni(): void {
    this.duzenlemeyiAc({ tur: 'yeni' }, null);
  }

  protected duzenle(satir: TanimSatiri): void {
    this.duzenlemeyiAc({ tur: 'satir', id: satir.id }, satir);
  }

  protected vazgec(): void {
    this.duzenleme.set(null);
  }

  protected kaydet(): void {
    const d = this.duzenleme();
    if (d === null) return;
    const deger = this.form.getRawValue();
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        d.tur === 'yeni'
          ? this.kaynak().olustur(deger, anahtar)
          : this.kaynak().guncelle(d.id, deger, anahtar),
      {
        gecersiz: () => this.ilkHatayaOdaklan(),
        basarili: () => {
          this.duzenleme.set(null);
          this.liste.yenile();
        },
      },
    );
  }

  protected silmeyiSor(id: string): void {
    this.silmeHatasi.set(null);
    this.silinecek.set(id);
  }

  protected sil(id: string): void {
    this.silmeKilidi
      .gonder((anahtar) => this.kaynak().sil(id, anahtar))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.silinecek.set(null);
          this.liste.yenile();
        },
        error: (hata: unknown) => this.silmeHatasi.set(apiHatasinaCevir(hata).detay),
      });
  }

  protected duzenleniyor(satir: TanimSatiri): boolean {
    const d = this.duzenleme();
    return d?.tur === 'satir' && d.id === satir.id;
  }

  protected goster(alan: TanimAlani, deger: unknown): string {
    if (deger === null || deger === undefined || deger === '') return '';
    switch (alan.tur) {
      case 'onay':
        return deger === true ? '✓' : '';
      case 'para':
        // Kayan noktaya girmeden (değer invariant metin ya da JSON sayısı).
        return `${ondalikBicimle(invariantOndalik(deger as string | number, { kesir: 2 }), 2)} ₺`;
      case 'secim':
        return alan.secenekler?.find((s) => s.deger === deger)?.etiket ?? String(deger);
      default:
        return String(deger);
    }
  }

  private duzenlemeyiAc(d: Duzenleme, satir: TanimSatiri | null): void {
    const kontroller: Record<string, FormControl<unknown>> = {};
    for (const alan of this.alanlar()) {
      const dogrulayicilar: ValidatorFn[] = [];
      if (alan.zorunlu) {
        dogrulayicilar.push(alan.tur === 'onay' ? Validators.requiredTrue : Validators.required);
      }
      if (alan.azamiUzunluk) dogrulayicilar.push(Validators.maxLength(alan.azamiUzunluk));
      const varsayilan = alan.tur === 'onay' ? false : null;
      kontroller[alan.ad] = new FormControl<unknown>(
        satir?.[alan.ad] ?? varsayilan,
        dogrulayicilar,
      );
    }
    this.form = new FormGroup(kontroller);
    this.gonderim.kilit.yenile();
    this.silinecek.set(null);
    this.duzenleme.set(d);
    afterNextRender(
      () =>
        this.eleman.nativeElement
          .querySelector<HTMLElement>('.duzenleme input, .duzenleme select')
          ?.focus(),
      { injector: this.injector },
    );
  }

  private ilkHatayaOdaklan(): void {
    afterNextRender(
      () =>
        this.eleman.nativeElement
          .querySelector<HTMLElement>('.duzenleme [aria-invalid="true"]')
          ?.focus(),
      { injector: this.injector },
    );
  }
}
