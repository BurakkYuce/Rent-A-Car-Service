import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { ParaPipe, SayiPipe, TarihPipe } from '@shared/bicim/bicim-pipe';
import { Ikon } from '@shared/ikon/ikon';

import { sunucuDegerleriniBirlestir } from '@features/planlama-ortak/form-yardimcilari';

import { FiloKunyeAlanlari, kunyeKontrolleri } from './filo-kunye-alanlari';
import { DURUM_ROZETI } from './filo-listesi';
import {
  type FiloKiralama,
  type FiloKunyeDegeri,
  filoDurumu,
  kunyeDegerleri,
  kunyeGovdesi,
  sayi,
} from './filo-modeli';
import { FiloDetayStore } from './filo.store';

/**
 * Filo sözleşmesi detayı (`/app/filo-kiralama/:id`): sözleşme özeti + SUNUCUNUN taksit planı (salt-hesap,
 * deftere yazmaz), künye düzenleme (Blazor "Künye" formu; para/süre alanları yok), Tamamla ve İptal
 * (OperationsDelete; sunucunun `yetkiler`'ine göre görünür). Künye PUT'u tam değiştirmedir (`surum`); bayat
 * sürüm 409 `cakisma` → güncel kayıt kirli forma birleştirilir, form silinmez.
 */
@Component({
  selector: 'rc-filo-detay',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    FiloKunyeAlanlari,
    FormHatalari,
    Ikon,
    ParaPipe,
    SayiPipe,
    TarihPipe,
  ],
  providers: [FetchPolicy, FiloDetayStore],
  templateUrl: './filo-detay.html',
  styleUrl: './filo.scss',
})
export class FiloDetay implements KaydedilmemisDegisiklikSahibi {
  protected readonly store = inject(FiloDetayStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly onay = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly bant = inject(UyariBandiServisi);
  private readonly yikim = inject(DestroyRef);
  private readonly sekme = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly form = new FormGroup(kunyeKontrolleri());
  protected readonly gonderim = formGonderimi();
  protected readonly islemde = signal(false);
  /** Son okunan sunucu hâli: `surum` PUT'a gider, birleştirmenin tabanıdır. */
  protected readonly taban = signal<FiloKiralama | null>(null);

  protected readonly kayit = computed(() => this.store.detay.veri() ?? null);
  protected readonly durum = computed(() => {
    const k = this.kayit();
    return k ? filoDurumu(k.durum) : null;
  });
  protected readonly sayi = sayi;

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detay.yukle(id),
      sifirla: () => this.store.detay.sifirla(),
      // Başka oturum künyeyi değiştirmiş ya da sözleşmeyi kapatmış olabilir: dönüşte taze kayıt (kirli form korunur).
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const d = this.store.detay.durum();
      if (d.tur === 'hazir') untracked(() => this.detayGeldi(d.veri));
    });
    sayfaTerkKorumasi(() => this.form.dirty);
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected rozet(): string {
    const d = this.durum();
    return `rc-rozet ${d ? DURUM_ROZETI[d] : ''}`;
  }

  protected durumEtiketi(): string {
    const d = this.durum();
    return d ? this.t(`filoKiralama.durumlar.${d}`) : (this.kayit()?.durum ?? '');
  }

  protected kdvYuzde(k: FiloKiralama): number | null {
    const o = sayi(k.kdvOrani);
    return o === null ? null : o * 100;
  }

  protected kaydet(): void {
    const taban = this.taban();
    if (taban === null || this.store.detay.yukleniyor()) return;
    const govde = kunyeGovdesi(this.form.getRawValue() as FiloKunyeDegeri, taban);
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        this.api.put<FiloKiralama>(
          `/api/ui/v1/filo-kiralama/${encodeURIComponent(this.id)}/kunye`,
          govde,
          { islemAnahtari: anahtar },
        ),
      {
        basarili: () => {
          this.toast.basari(this.t('filoKiralama.kunyeKaydedildi'));
          this.store.detay.yenile();
        },
        // Bayat sürüm: güncel kayıt okunur, kirli forma birleştirilir (yeniden gönderim YOK).
        hata: (h) => {
          if (h.kod === 'cakisma') this.store.detay.yenile();
        },
      },
    );
  }

  protected tamamla(): void {
    this.islem('tamamla', 'filoKiralama.tamamlandi');
  }

  protected async iptal(): Promise<void> {
    const k = this.kayit();
    if (!k || this.islemde()) return;
    const evet = await this.onay.sor({
      baslik: this.t('filoKiralama.iptalBaslik'),
      mesaj: this.t('filoKiralama.iptalMesaj', { no: k.no }),
      onayEtiketi: this.t('filoKiralama.iptal'),
      tehlikeli: true,
    });
    if (evet) this.islem('iptal', 'filoKiralama.iptalEdildi');
  }

  private islem(
    yol: 'tamamla' | 'iptal',
    bildirim: 'filoKiralama.tamamlandi' | 'filoKiralama.iptalEdildi',
  ): void {
    if (this.islemde()) return;
    this.islemde.set(true);
    this.api
      .post<FiloKiralama>(`/api/ui/v1/filo-kiralama/${encodeURIComponent(this.id)}/${yol}`, null)
      .pipe(
        finalize(() => this.islemde.set(false)),
        takeUntilDestroyed(this.yikim),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t(bildirim));
          this.store.detay.yenile();
        },
        error: (ham: unknown) => {
          const hata = apiHatasinaCevir(ham);
          if (!genelGosterilir(hata)) this.toast.hata(hata.detay);
          this.store.detay.yenile();
        },
      });
  }

  /** Temiz form sunucu hâline sıfırlanır; kirli formda dokunulan alanlar korunur (çakışan işaretlenir). */
  private detayGeldi(k: FiloKiralama): void {
    this.sekme.etiketAyarla(this.t('filoKiralama.sekmeEtiketi', { no: k.no }));
    const yeni = kunyeDegerleri(k);
    const eski = this.taban();
    if (!this.form.dirty || eski === null) {
      this.form.reset({ ...yeni });
    } else {
      const cakisan = sunucuDegerleriniBirlestir(
        this.form,
        { ...yeni },
        { ...kunyeDegerleri(eski) },
        this.t('filoKiralama.cakismaAlan'),
      );
      if (cakisan.length > 0) {
        this.bant.goster({
          tur: 'uyari',
          mesaj: this.t('filoKiralama.cakismaBant', { sayi: cakisan.length }),
          kod: 'cakisma',
        });
      }
    }
    this.taban.set(k);
    if (k.yetkiler.kunye) this.form.enable({ emitEvent: false });
    else this.form.disable({ emitEvent: false });
  }
}
