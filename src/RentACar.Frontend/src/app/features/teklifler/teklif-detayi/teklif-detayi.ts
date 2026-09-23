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
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { TemelStore } from '@core/veri/temel-store';

import { RF_ORTAK } from '../../rezervasyonlar/ortak';
import { sayiya } from '../../rezervasyonlar/rezervasyon-modeli';
import { TeklifIslemleri } from '../teklif-islemleri';
import {
  TEKLIF_KOKU,
  teklifDurumuMu,
  teklifRozeti,
  type TeklifDetayYaniti,
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
  imports: [...RF_ORTAK, RouterLink],
  providers: [TeklifIslemleri],
  templateUrl: './teklif-detayi.html',
  styleUrl: '../../rezervasyonlar/rezervasyon-formu/rezervasyon-formu.scss',
})
export class TeklifDetayi {
  private readonly api = inject(ApiIstemcisi);
  private readonly sekme = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();
  protected readonly islemler = inject(TeklifIslemleri);

  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  readonly detay = new TemelStore(
    (id: string) => this.api.get<TeklifDetayYaniti>(`${TEKLIF_KOKU}/${id}`),
    { oncekiVeriyiKoru: true },
  );
  protected readonly teklif = computed(() => this.detay.veri()?.teklif ?? null);
  protected readonly yetkiler = computed(() => this.detay.veri()?.yetkiler ?? null);
  protected readonly bulunamadi = computed(() => this.detay.hata()?.status === 404);

  constructor() {
    this.detay.yukle(this.id);
    effect(() => {
      const no = this.teklif()?.no;
      if (no) untracked(() => this.sekme.etiketAyarla(this.t('teklif.sekmeEtiketi', { no })));
    });
    let son: number | null = null;
    effect(() => {
      const n = this.sekme.onaGelme();
      untracked(() => {
        if (son !== null && n !== son) this.detay.yenile();
        son = n;
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

  protected rozet(durum: string): string {
    return teklifRozeti(durum);
  }

  protected durumEtiketi(durum: string): string {
    return teklifDurumuMu(durum) ? this.t(`teklif.durumlar.${durum}`) : durum;
  }

  protected sayi(v: number | string | null | undefined): number | null {
    return sayiya(v);
  }
}
