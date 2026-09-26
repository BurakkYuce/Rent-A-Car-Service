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
import { formatMoney, formatDateTime } from '@core/bicim/bicim';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { translationFunction } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { FormErrors } from '@shared/form/form-errors';
import { TabPanel, TabbedForm, type SekmeTanimi } from '@shared/form/sekmeli-form/tabbed-form';
import { Icon } from '@shared/ikon/icon';
import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import { RentalFinanceSlot } from './finans-paneli/rental-finance-slot';
import { RentalFormState } from './rental-form-state';
import { TABS, isSubTab, parseHash, isoCurrency, toNumber, isTab } from './kira-formu-modeli';
import { contractPdfUrl } from './kira-yazdir';
import type { ServerNumber } from './kira-tipleri';
import { Vehicle } from './sekmeler/arac';
import { Details } from './sekmeler/details';
import { Return } from './sekmeler/return';
import { AddOn } from './sekmeler/add-on';
import { Price } from './sekmeler/price';
import { QuickEntry } from './sekmeler/quick-entry';
import { RentalInfo } from './sekmeler/rental-info';
import { Customer } from './sekmeler/customer';
import { ShareBar } from './sekmeler/share-bar';
import { Share } from './sekmeler/share';

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
  providers: [RentalFormState],
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    FormErrors,
    Icon,
    PageBand,
    TabbedForm,
    TabPanel,
    RentalFinanceSlot,
    QuickEntry,
    RentalInfo,
    Customer,
    Vehicle,
    Price,
    AddOn,
    Details,
    Return,
    ShareBar,
    Share,
  ],
  host: { class: 'kira-formu' },
  templateUrl: './kira-formu.html',
  styleUrl: './kira-formu.scss',
})
export class RentalFormPage implements UnsavedChangesOwner {
  protected readonly d = inject(RentalFormState);
  private readonly belge = inject(DOCUMENT);
  private readonly t = translationFunction();
  private readonly sekmeli = viewChild(TabbedForm);
  private readonly ayrintilar = viewChild(Details);
  /** Sabit finans paneli (tembel): yazılmış tutar ya da sonuçlanmamış para gönderimi de "kaydedilmemiş" sayılır. */
  private readonly finans = viewChild(RentalFinanceSlot);

  protected readonly tabs: readonly SekmeTanimi[] = TABS.map((k) => ({
    kimlik: k,
    etiket: this.t(`kiraFormu.sekme.${k}` as CeviriAnahtari),
  }));

  protected readonly pdfUrl = computed(() => contractPdfUrl(this.d.kira()?.id));
  /** Bant ikincil metni: çıkış ofisi · başlangıç – bitiş (İstanbul saati). */
  protected readonly bannerSubtext = computed(() => {
    const k = this.d.kira();
    if (!k) return null;
    const range = `${formatDateTime(k.basTar)} – ${formatDateTime(k.bitTar)}`;
    return k.cikisOfisi ? `${k.cikisOfisi} · ${range}` : range;
  });
  protected readonly notFound = computed(() => this.d.detay.hata()?.status === 404);

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    // Açık sekmeye `#sekme=` ile gelindiğinde (bileşen yaşıyor; `hashchange` tetiklenmez) sekme seçilir.
    inject(ActivatedRoute)
      .fragment.pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((part) => {
        if (!part) return;
        const h = parseHash(part);
        if (isTab(h['sekme'])) this.sekmeli()?.select(h['sekme'], { adreseYaz: false });
        if (isSubTab(h['alt'])) this.ayrintilar()?.select(h['alt'], { adreseYaz: false });
      });
  }

  /** Sabit panelde sonucu bilinmeyen para gönderimi varsa terk sorusu bunu söyler (3. tur, L3 metni). */
  unsavedChangesMessage(): string | null {
    return this.finans()?.hasUnknownOutcome() ? this.t('kiraFinans.sonucuBilinmeyen') : null;
  }

  hasUnsavedChanges(): boolean {
    return this.d.isDirty() || (this.finans()?.isDirty() ?? false);
  }

  protected kaydet(): void {
    this.d.kaydet(() => this.goToInvalid());
  }

  private goToInvalid(): void {
    this.ayrintilar()?.goToFirstInvalid();
    this.sekmeli()?.goToFirstInvalid();
  }

  /** Müşteri sekmesinden "yeni müşteri": blok Hızlı Giriş'te (formda tek) — oraya geçip açılır. */
  protected goToNewCustomer(): void {
    this.sekmeli()?.select('hizli');
    this.d.isNewCustomerOpen.set(true);
    queueMicrotask(() =>
      this.belge.getElementById('kf-yeni-musteri')?.scrollIntoView({ block: 'center' }),
    );
  }

  /** Sözleşme PDF'ini gizli çerçevede açıp yazdırır (indirme klasörüne dokunmadan; aynı köken). */
  protected yazdir(): void {
    const address = this.pdfUrl();
    if (!address) return;
    const frame = this.belge.createElement('iframe');
    frame.hidden = true;
    frame.src = address;
    frame.addEventListener('load', () => {
      try {
        frame.contentWindow?.focus();
        frame.contentWindow?.print();
      } catch {
        this.belge.defaultView?.open(address, '_blank', 'noopener');
      }
    });
    this.belge.body.appendChild(frame);
  }

  protected money(v: ServerNumber, currency: string | null | undefined): string {
    return formatMoney(toNumber(v), isoCurrency(currency)) || '—';
  }
}
