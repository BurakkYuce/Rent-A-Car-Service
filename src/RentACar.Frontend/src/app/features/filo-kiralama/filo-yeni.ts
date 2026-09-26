import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';

import { SayfaBandi } from '../../kabuk/sayfa-bandi/sayfa-bandi';
import { FiloKunyeAlanlari, kunyeKontrolleri } from './filo-kunye-alanlari';
import {
  type FiloOlusturYaniti,
  type FiloYeniDegeri,
  olusturGovdesi,
  yeniDegerler,
} from './filo-modeli';

/**
 * Yeni filo sözleşmesi (`/app/filo-kiralama/yeni`) — Blazor "Yeni Sözleşme" formunun tüm alanları.
 * Taksit planı ve genel toplam SUNUCUDA hesaplanır (kayıttan sonra detayda görünür). Kayıt sonrası sekme
 * temiz forma döner, detay açılır.
 */
@Component({
  selector: 'rc-filo-yeni',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FiloKunyeAlanlari,
    FormHatalari,
    Ikon,
    ParaGirdisi,
    SayiGirdisi,
    TarihSecici,
    SayfaBandi,
  ],
  templateUrl: './filo-yeni.html',
  styleUrl: './filo.scss',
})
export class FiloYeni implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly musteriler = sunucuSecimKaynagi('musteri');
  protected readonly araclar = sunucuSecimKaynagi('arac');
  protected readonly gonderim = formGonderimi();

  protected readonly form = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    basTar: new FormControl<string | null>(null),
    sureAy: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.max(120),
    ]),
    aylikUcret: new FormControl<string | null>(null, Validators.required),
    kdvOrani: new FormControl<number | null>(0.2, [Validators.min(0), Validators.max(1)]),
    damgaVergisi: new FormControl<string | null>(null),
    ...kunyeKontrolleri(),
  });

  constructor() {
    this.form.reset(yeniDegerler());
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected kaydet(): void {
    const govde = olusturGovdesi(this.form.getRawValue() as FiloYeniDegeri);
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        this.api.post<FiloOlusturYaniti>('/api/ui/v1/filo-kiralama', govde, {
          islemAnahtari: anahtar,
        }),
      {
        esleme: { musteriId: 'musteri', vehicleId: 'arac' },
        basarili: (y) => {
          this.toast.basari(this.t('filoKiralama.olusturuldu', { no: y.no }));
          this.form.reset(yeniDegerler());
          void this.router.navigate(['/filo-kiralama', y.id]);
        },
      },
    );
  }
}
