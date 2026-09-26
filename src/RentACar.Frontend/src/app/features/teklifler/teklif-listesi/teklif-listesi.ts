import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';
import { KatlanirFiltre } from '@shared/katlanir-filtre/katlanir-filtre';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { TeklifIslemleri } from '../teklif-islemleri';
import {
  TEKLIF_DURUMLARI,
  teklifAcik,
  teklifDurumuMu,
  teklifRozeti,
  type TeklifDurumu,
  type TeklifListeSatiri,
} from '../teklif-modeli';
import { TEKLIF_LISTESI, TeklifListesiStore, teklifSutunlari } from './teklif-listesi.store';

/**
 * Teklif listesi (`/app/teklifler`) — Blazor `QuotationList.razor` paritesi (F5.2a): sütunlar, satır eylemleri
 * Gönder (Taslak) / Kabul → Rezervasyon / Reddet (Taslak ya da Gönderildi); Kabul edilmiş teklifin durumu oluşan
 * rezervasyona bağlanır (Blazor listeye gidiyordu; SPA doğrudan kaydı açar). Yeni teklif ayrı formda.
 */
@Component({
  selector: 'rc-teklif-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    SayfaBandi,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Ikon,
    KatlanirFiltre,
    Secim,
    Tablo,
    TabloHucre,
  ],
  providers: [FetchPolicy, TeklifListesiStore, TeklifIslemleri],
  templateUrl: './teklif-listesi.html',
  styleUrl: '../../rezervasyonlar/rezervasyon-listesi/rezervasyon-listesi.scss',
})
export class TeklifListesi {
  protected readonly store = inject(TeklifListesiStore);
  protected readonly islemler = inject(TeklifIslemleri);
  private readonly router = inject(Router);
  private readonly t = ceviriFonksiyonu();

  protected readonly liste = listeSorgusuUrlSenkronu(TEKLIF_LISTESI);
  protected readonly sutunlar = teklifSutunlari(this.t);
  protected readonly varsayilanSirala = TEKLIF_LISTESI.varsayilanSirala;
  protected readonly kimlik = (r: TeklifListeSatiri) => r.id;
  protected readonly filtreAcik = signal(false);

  protected readonly filtreFormu = new FormGroup({
    durum: new FormControl<TeklifDurumu | null>(null),
  });
  protected readonly durumSecenekleri: readonly SecenekOgesi<TeklifDurumu>[] = TEKLIF_DURUMLARI.map(
    (d) => ({ deger: d, etiket: this.t(`teklif.durumlar.${d}`) }),
  );

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.sifirla(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() => this.filtreFormu.reset({ durum: f.durum ?? null }));
    });
  }

  protected filtrele(): void {
    void this.liste.degistir({
      filtreler: { durum: this.filtreFormu.getRawValue().durum ?? undefined },
    });
  }

  protected temizle(): void {
    void this.liste.sifirla();
  }

  protected satiriAc(satir: TeklifListeSatiri): void {
    void this.router.navigate(['/teklifler', satir.id]);
  }

  protected rozet(durum: string): string {
    return teklifRozeti(durum);
  }

  protected durumEtiketi(durum: string): string {
    return teklifDurumuMu(durum) ? this.t(`teklif.durumlar.${durum}`) : durum;
  }

  protected acik(satir: TeklifListeSatiri): boolean {
    return teklifAcik(satir.durum);
  }

  private readonly yenile = () => this.store.liste.yenile();

  protected gonder(satir: TeklifListeSatiri): void {
    this.islemler.gonder({ id: satir.id, no: satir.no }, this.yenile);
  }

  protected kabul(satir: TeklifListeSatiri): void {
    void this.islemler.kabul({ id: satir.id, no: satir.no }, this.yenile);
  }

  protected reddet(satir: TeklifListeSatiri): void {
    void this.islemler.reddet({ id: satir.id, no: satir.no }, this.yenile);
  }
}
