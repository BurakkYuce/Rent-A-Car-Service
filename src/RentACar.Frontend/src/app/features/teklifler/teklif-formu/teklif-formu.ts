import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';
import { sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';

import { RF_ORTAK } from '../../rezervasyonlar/ortak';
import {
  REZERVASYON_KOKU,
  secenekListesi,
  varsayilanTarihler,
  type RezervasyonFormSecenekleri,
} from '../../rezervasyonlar/rezervasyon-modeli';
import type { GunMetni } from '@core/form/tarih-girdisi';
import {
  TEKLIF_KOKU,
  gecerlilikDogrulayici,
  gecerlilikEnErken,
  teklifFormuOlustur,
  teklifGovdesi,
  type TeklifOlusturYaniti,
} from '../teklif-modeli';

/**
 * Yeni teklif (`/app/teklifler/yeni`) — Blazor QuotationList "+ Yeni Teklif" formu. Tutar/gün/KDV sunucuda (fiyat
 * motoru); hata formu silmez (`formGonderimi`). Fiyat türü seçenekleri rezervasyon formuyla ortak uçtan
 * (`/rezervasyonlar/form-secenekleri`); ön-seçim tenant varsayılanı, yoksa listenin ilki ("Otomatik") — Blazor
 * davranışı. Kayıttan sonra teklifin kaydına gidilir; sekme sonraki teklif için temiz forma döner.
 */
@Component({
  selector: 'rc-teklif-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...RF_ORTAK, RouterLink],
  templateUrl: './teklif-formu.html',
  styleUrl: '../../rezervasyonlar/rezervasyon-formu/rezervasyon-formu.scss',
})
export class TeklifFormuSayfasi implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly form = teklifFormuOlustur();
  protected readonly kayit = formGonderimi();

  private readonly secenekler = new TemelStore(() =>
    this.api.get<RezervasyonFormSecenekleri>(`${REZERVASYON_KOKU}/form-secenekleri`, {
      context: istekBaglami({ sessiz: true }),
    }),
  );
  protected readonly fiyatTurleri = computed(() =>
    secenekListesi(this.secenekler.veri()?.fiyatTurleri, null),
  );

  protected readonly musteriKaynagi = sunucuSecimKaynagi('musteri');
  protected readonly aracKaynagi = sunucuSecimKaynagi('arac');
  protected readonly lokasyonKaynagi = sunucuSecimKaynagi('lokasyon');

  /** Takvimde seçilebilecek en erken geçerlilik günü (başlangıca göre; sunucu kuralıyla aynı). */
  protected readonly gecerlilikEnAz = signal<GunMetni | null>(null);

  constructor() {
    sayfaTerkKorumasi(() => this.form.dirty);
    const { basTar, gecerlilik } = this.form.controls;
    gecerlilik.addValidators(
      gecerlilikDogrulayici((enErken) => this.t('teklif.alan.gecerlilikErken', { enErken })),
    );
    // Başlangıç değişince geçerlilik yeniden doğrulanır ve takvimin alt sınırı güncellenir.
    basTar.valueChanges.pipe(takeUntilDestroyed()).subscribe((b) => {
      this.gecerlilikEnAz.set(gecerlilikEnErken(b));
      gecerlilik.updateValueAndValidity({ emitEvent: false });
    });
    this.form.reset(varsayilanTarihler());
    this.secenekler.yukle();
    effect(() => {
      const s = this.secenekler.veri();
      untracked(() => {
        const k = this.form.controls.fiyatTuru;
        const ilk = s?.varsayilanFiyatTuru ?? s?.fiyatTurleri[0] ?? null;
        if (ilk && k.value === null && k.pristine) k.setValue(ilk);
      });
    });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected kaydet(): void {
    this.kayit.gonder(
      this.form,
      () => this.api.post<TeklifOlusturYaniti>(TEKLIF_KOKU, teklifGovdesi(this.form.getRawValue())),
      {
        basarili: (y) => {
          this.toast.basari(this.t('teklif.bildirim.olusturuldu', { no: y.no }));
          const s = this.secenekler.veri();
          this.form.reset({
            ...varsayilanTarihler(),
            fiyatTuru: s?.varsayilanFiyatTuru ?? s?.fiyatTurleri[0] ?? null,
          });
          this.kayit.kilit.yenile();
          void this.router.navigate(['/teklifler', y.id]);
        },
      },
    );
  }
}
