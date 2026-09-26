import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { translationFunction } from '@core/i18n/ceviri';
import { tabContext } from '@core/sekme/tab-state';
import { TemelStore } from '@core/veri/temel-store';

import { RF_SHARED } from '../../rezervasyonlar/ortak';
import { toNumber } from '../../rezervasyonlar/rezervasyon-modeli';
import { QuotationActions } from '../quotation-actions';
import {
  QUOTATION_ROOT,
  isQuotationStatus,
  quotationBadge,
  type QuotationDetailResponse,
} from '../teklif-modeli';

/**
 * Teklif kaydı (`/app/teklifler/:id`) — salt okunur özet (Blazor'da teklif düzenleme yok) + durum eylemleri
 * (sunucunun `yetkiler`'ine göre). Kabul sonrası ve kabul TEKRARINDA (409 `cakisma`: teklif başka sekmede/oturumda
 * ya da yanıtı kaybolan istekte zaten kabul edildi) kayıt yeniden okunur ve OLUŞAN rezervasyonun bağlantısı
 * gösterilir — ikinci rezervasyon açılmaz, otomatik yeniden gönderim yok.
 */
@Component({
  selector: 'rc-teklif-detayi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...RF_SHARED, RouterLink],
  providers: [QuotationActions],
  templateUrl: './quotation-detail.html',
  styleUrl: '../../rezervasyonlar/rezervasyon-formu/rezervasyon-formu.scss',
})
export class QuotationDetail {
  private readonly api = inject(ApiIstemcisi);
  private readonly sekme = tabContext();
  private readonly t = translationFunction();
  protected readonly islemler = inject(QuotationActions);

  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  readonly detay = new TemelStore(
    (id: string) => this.api.get<QuotationDetailResponse>(`${QUOTATION_ROOT}/${id}`),
    { oncekiVeriyiKoru: true },
  );
  protected readonly teklif = computed(() => this.detay.veri()?.teklif ?? null);
  protected readonly permissions = computed(() => this.detay.veri()?.yetkiler ?? null);
  protected readonly notFound = computed(() => this.detay.hata()?.status === 404);

  constructor() {
    this.detay.yukle(this.id);
    effect(() => {
      const no = this.teklif()?.no;
      if (no) untracked(() => this.sekme.etiketAyarla(this.t('teklif.sekmeEtiketi', { no })));
    });
    let last: number | null = null;
    effect(() => {
      const n = this.sekme.onaGelme();
      untracked(() => {
        if (last !== null && n !== last) this.detay.yenile();
        last = n;
      });
    });
  }

  private readonly yenile = () => this.detay.yenile();

  private hedef() {
    const t = this.teklif();
    return t ? { id: t.id, no: t.no } : null;
  }

  protected gonder(): void {
    const h = this.hedef();
    if (h) this.islemler.gonder(h, this.yenile);
  }

  protected kabul(): void {
    const h = this.hedef();
    if (h) void this.islemler.kabul(h, this.yenile);
  }

  protected reddet(): void {
    const h = this.hedef();
    if (h) void this.islemler.reddet(h, this.yenile);
  }

  protected rozet(status: string): string {
    return quotationBadge(status);
  }

  protected statusLabel(status: string): string {
    return isQuotationStatus(status) ? this.t(`teklif.durumlar.${status}`) : status;
  }

  protected count(v: number | string | null | undefined): number | null {
    return toNumber(v);
  }
}
