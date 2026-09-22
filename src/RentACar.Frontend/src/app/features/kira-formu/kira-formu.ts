import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ViewEncapsulation,
  computed,
  inject,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { paraBicimle } from '@core/bicim/bicim';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { FormHatalari } from '@shared/form/form-hatalari';
import { SekmePaneli, SekmeliForm, type SekmeTanimi } from '@shared/form/sekmeli-form/sekmeli-form';
import { KiraFinansYuvasi } from './finans-paneli/kira-finans-yuvasi';
import { KiraFormuDurumu } from './kira-formu-durumu';
import {
  SEKMELER,
  altSekmeMi,
  hashParcala,
  isoParaBirimi,
  sayiya,
  sekmeMi,
} from './kira-formu-modeli';
import { sozlesmePdfAdresi } from './kira-yazdir';
import type { SunucuSayisi } from './kira-tipleri';
import { Arac } from './sekmeler/arac';
import { Ayrintilar } from './sekmeler/ayrintilar';
import { Donus } from './sekmeler/donus';
import { EkHizmet } from './sekmeler/ek-hizmet';
import { Fiyat } from './sekmeler/fiyat';
import { HizliGiris } from './sekmeler/hizli-giris';
import { KiraBilgisi } from './sekmeler/kira-bilgisi';
import { Musteri } from './sekmeler/musteri';
import { Paylasim } from './sekmeler/paylasim';

/**
 * Kira sözleşmesi — TEK form: `/app/kiralar/yeni` (oluştur) ve `/app/kiralar/:id` (düzenle + operasyon),
 * Blazor `KiraForm.razor` paritesi (8 ana sekme, Ayrıntılar'da 8 alt sekme, sabit yan panel).
 *
 * Form verisi kaybolmaz: doğrulama/çakışma/oturum hatasında değerlere dokunulmaz (`formGonderimi` +
 * F3.3 interceptor), sekme değişince bileşen yaşar, kaydedilmemiş değişiklikte ayrılmadan önce sorulur.
 * Sayfada `<form>` öğesi YOK: ikincil işlemler (teslim, dönüş, ek hizmet…) iç içe form olmadan kendi
 * düğmeleriyle gönderilir; Enter ana kaydı tetiklemez.
 */
@Component({
  selector: 'rc-kira-formu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  providers: [KiraFormuDurumu],
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    FormHatalari,
    SekmeliForm,
    SekmePaneli,
    KiraFinansYuvasi,
    HizliGiris,
    KiraBilgisi,
    Musteri,
    Arac,
    Fiyat,
    EkHizmet,
    Ayrintilar,
    Donus,
    Paylasim,
  ],
  host: { class: 'rc-kira-formu' },
  templateUrl: './kira-formu.html',
  styleUrl: './kira-formu.scss',
})
export class KiraFormuSayfasi implements KaydedilmemisDegisiklikSahibi {
  protected readonly d = inject(KiraFormuDurumu);
  private readonly belge = inject(DOCUMENT);
  private readonly t = ceviriFonksiyonu();
  private readonly sekmeli = viewChild(SekmeliForm);
  private readonly ayrintilar = viewChild(Ayrintilar);
  /** Sabit finans paneli (tembel): yazılmış tutar ya da sonuçlanmamış para gönderimi de "kaydedilmemiş" sayılır. */
  private readonly finans = viewChild(KiraFinansYuvasi);

  protected readonly sekmeler: readonly SekmeTanimi[] = SEKMELER.map((k) => ({
    kimlik: k,
    etiket: this.t(`kiraFormu.sekme.${k}` as CeviriAnahtari),
  }));

  protected readonly pdfAdresi = computed(() => sozlesmePdfAdresi(this.d.kira()?.id));
  protected readonly bulunamadi = computed(() => this.d.detay.hata()?.status === 404);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    // Açık sekmeye `#sekme=` ile gelindiğinde (bileşen yaşıyor; `hashchange` tetiklenmez) sekme seçilir.
    inject(ActivatedRoute)
      .fragment.pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((parca) => {
        if (!parca) return;
        const h = hashParcala(parca);
        if (sekmeMi(h['sekme'])) this.sekmeli()?.sec(h['sekme'], { adreseYaz: false });
        if (altSekmeMi(h['alt'])) this.ayrintilar()?.sec(h['alt'], { adreseYaz: false });
      });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.d.kirliMi() || (this.finans()?.kirliMi() ?? false);
  }

  protected kaydet(): void {
    this.d.kaydet(() => this.gecersizeGit());
  }

  private gecersizeGit(): void {
    this.ayrintilar()?.ilkGecersizeGit();
    this.sekmeli()?.ilkGecersizeGit();
  }

  /** Müşteri sekmesinden "yeni müşteri": blok Hızlı Giriş'te (formda tek) — oraya geçip açılır. */
  protected yeniMusteriyeGit(): void {
    this.sekmeli()?.sec('hizli');
    this.d.yeniMusteriAcik.set(true);
    queueMicrotask(() =>
      this.belge.getElementById('kf-yeni-musteri')?.scrollIntoView({ block: 'center' }),
    );
  }

  /** Sözleşme PDF'ini gizli çerçevede açıp yazdırır (indirme klasörüne dokunmadan; aynı köken). */
  protected yazdir(): void {
    const adres = this.pdfAdresi();
    if (!adres) return;
    const cerceve = this.belge.createElement('iframe');
    cerceve.hidden = true;
    cerceve.src = adres;
    cerceve.addEventListener('load', () => {
      try {
        cerceve.contentWindow?.focus();
        cerceve.contentWindow?.print();
      } catch {
        this.belge.defaultView?.open(adres, '_blank', 'noopener');
      }
    });
    this.belge.body.appendChild(cerceve);
  }

  protected para(v: SunucuSayisi, doviz: string | null | undefined): string {
    return paraBicimle(sayiya(v), isoParaBirimi(doviz)) || '—';
  }
}
