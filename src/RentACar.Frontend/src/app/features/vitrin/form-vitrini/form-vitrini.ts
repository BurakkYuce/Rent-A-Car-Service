import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { invariantOndalik, ondalikBicimle } from '@core/form/ondalik';
import type { GunAraligi } from '@core/form/tarih-girdisi';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimUcuOgesi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinAlani } from '@shared/form/kontroller/metin-alani';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { Anahtar, OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { RadyoGrubu } from '@shared/form/kontroller/radyo-grubu';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { SekmePaneli, SekmeliForm, type SekmeTanimi } from '@shared/form/sekmeli-form/sekmeli-form';
import { TarihSaatSecici } from '@shared/form/tarih/tarih-saat-secici';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

type Durum = 'aktif' | 'pasif';
type OdemeTuru = 'nakit' | 'kart';

/**
 * Form seti vitrini: her CVA kontrolü, sekmeli yerleşim + sabit yan panel, alan hatası, gönderim
 * kilidi ve kaydedilmemiş değişiklik koruması tek sayfada. e2e (sahte arka uçla) bunu sürer; F4
 * kira formu aynı parçalarla kurulur.
 */
@Component({
  selector: 'rc-form-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    Alan,
    AramaSecim,
    FormHatalari,
    MetinAlani,
    MetinGirdisi,
    Anahtar,
    OnayKutusu,
    ParaGirdisi,
    RadyoGrubu,
    SayiGirdisi,
    Secim,
    SekmeliForm,
    SekmePaneli,
    TarihSaatSecici,
    TarihSecici,
  ],
  templateUrl: './form-vitrini.html',
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-4);
      min-width: 0;
      padding: var(--rc-bosluk-4);
    }
    .ozet {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--rc-bosluk-1) var(--rc-bosluk-3);
      margin: 0;
      padding: var(--rc-bosluk-3);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-yuzey);
    }
    .ozet dt {
      color: var(--rc-metin-ikincil);
    }
    .ozet dd {
      margin: 0;
      text-align: end;
    }
  `,
})
export class FormVitrini implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly sekmeli = viewChild.required(SekmeliForm);

  protected readonly sekmeler: readonly SekmeTanimi[] = [
    { kimlik: 'genel', etiket: 'Genel' },
    { kimlik: 'tarih-tutar', etiket: 'Tarih ve tutar' },
    { kimlik: 'secenekler', etiket: 'Seçenekler' },
  ];
  protected readonly durumlar: readonly SecenekOgesi<Durum>[] = [
    { deger: 'aktif', etiket: 'Aktif' },
    { deger: 'pasif', etiket: 'Pasif' },
  ];
  protected readonly odemeTurleri: readonly SecenekOgesi<OdemeTuru>[] = [
    { deger: 'nakit', etiket: 'Nakit' },
    { deger: 'kart', etiket: 'Kredi kartı' },
  ];
  protected readonly musteriler = sunucuSecimKaynagi('musteri');

  protected readonly form = new FormGroup({
    plaka: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(12)]),
    musteri: new FormControl<SecimUcuOgesi<'musteri'> | null>(null, Validators.required),
    durum: new FormControl<Durum | null>('aktif', Validators.required),
    adet: new FormControl<number | null>(1, [Validators.required, Validators.min(1)]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(500)),
    tutar: new FormControl<string | number | null>(null, Validators.required),
    cikisTarihi: new FormControl<string | null>(null, Validators.required),
    donusAni: new FormControl<string | null>(null),
    donem: new FormControl<GunAraligi | null>(null),
    kasko: new FormControl<boolean | null>(false),
    odemeTuru: new FormControl<OdemeTuru | null>('nakit'),
    otomatikFatura: new FormControl<boolean | null>(true),
    kosullarOnay: new FormControl<boolean | null>(false, Validators.requiredTrue),
  });

  protected readonly gonderim = formGonderimi();
  protected readonly kayitNo = signal<string | null>(null);

  constructor() {
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  /** Özet: değer invariant metin; kayan noktaya girmeden Türkçe yazım. */
  protected ozetTutar(): string {
    const tutar = ondalikBicimle(invariantOndalik(this.form.controls.tutar.value, { kesir: 2 }), 2);
    return tutar === '' ? '—' : `${tutar} ₺`;
  }

  protected kaydet(): void {
    this.kayitNo.set(null);
    const d = this.form.getRawValue();
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        this.api.post<{ no: string }>(
          '/api/ui/v1/vitrin/form',
          {
            plaka: d.plaka,
            musteriId: d.musteri?.id ?? null,
            durum: d.durum,
            adet: d.adet,
            aciklama: d.aciklama,
            tutar: d.tutar,
            cikisTarihi: d.cikisTarihi,
            donusAni: d.donusAni,
            donemBaslangic: d.donem?.baslangic ?? null,
            donemBitis: d.donem?.bitis ?? null,
            kasko: d.kasko,
            odemeTuru: d.odemeTuru,
            otomatikFatura: d.otomatikFatura,
          },
          { islemAnahtari: anahtar },
        ),
      {
        gecersiz: () => this.sekmeli().ilkGecersizeGit(),
        basarili: (sonuc) => this.kayitNo.set(sonuc.no),
      },
    );
  }
}
