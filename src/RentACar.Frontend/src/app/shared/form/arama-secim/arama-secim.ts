import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  numberAttribute,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CdkListbox, CdkOption, type ListboxValueChangeEvent } from '@angular/cdk/listbox';
import { CdkConnectedOverlay, CdkOverlayOrigin } from '@angular/cdk/overlay';
import { TranslocoPipe } from '@jsverse/transloco';
import { Subject, catchError, debounce, map, of, startWith, switchMap, timer } from 'rxjs';
import { Ikon } from '../../ikon/ikon';
import { tekilKimlik } from '../alan/alan-baglami';
import { TemelKontrol, kontrolSaglayicilari } from '../kontroller/temel-kontrol';
import { SECIM_AZAMI_LIMIT, type SecimKaynagi, type SecimSecenegi } from './secim-kaynagi';

type AramaDurumu = 'bos' | 'kisa' | 'yukleniyor' | 'hazir' | 'hata';

interface AramaIstegi {
  readonly metin: string;
  readonly anlik: boolean;
}

/**
 * Aranabilir tekli seçim (combobox + CDK overlay + CDK listbox). Kaynak F1.6 `/secim/*` uçları
 * (`sunucuSecimKaynagi`): `q` + `limit ≤ 20`, yazarken gecikmeli (varsayılan 250 ms), önceki istek
 * iptal (switchMap). DOM odağı girdide kalır, etkin seçenek `aria-activedescendant` ile:
 * ↓/↑ gezin, Enter seç, Esc kapat (metin seçili öğeye döner), Tab kapatır.
 *
 * Değer seçilen öğenin kendisi (`{ id, etiket, … }`) — ön doldurmada etiket için ek istek gerekmez; gönderirken
 * `.id` alınır.
 */
@Component({
  selector: 'rc-arama-secim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, CdkListbox, CdkOption, TranslocoPipe, Ikon],
  providers: kontrolSaglayicilari(() => AramaSecim),
  styleUrl: './arama-secim.scss',
  templateUrl: './arama-secim.html',
})
export class AramaSecim extends TemelKontrol<SecimSecenegi> {
  readonly kaynak = input.required<SecimKaynagi>();
  readonly yerTutucu = input('');
  readonly limit = input(SECIM_AZAMI_LIMIT, { transform: numberAttribute });
  readonly enAzHarf = input(0, { transform: numberAttribute });
  readonly gecikme = input(250, { transform: numberAttribute });

  private readonly girdi = viewChild.required<ElementRef<HTMLInputElement>>('girdi');
  private readonly kutu = viewChild.required<ElementRef<HTMLElement>>('kutu');

  protected readonly listeKimligi = tekilKimlik('rc-liste');
  protected readonly metin = signal('');
  protected readonly acik = signal(false);
  protected readonly durum = signal<AramaDurumu>('bos');
  protected readonly sonuclar = signal<readonly SecimSecenegi[]>([]);
  protected readonly aktifSira = signal(-1);
  protected readonly panelGenisligi = signal(0);

  protected readonly aktifKimlik = computed(() =>
    this.acik() && this.aktifSira() >= 0 && this.aktifSira() < this.sonuclar().length
      ? this.secenekKimligi(this.aktifSira())
      : null,
  );

  private readonly aramalar = new Subject<AramaIstegi>();

  constructor() {
    super();
    this.aramalar
      .pipe(
        debounce((istek) => timer(istek.anlik ? 0 : this.gecikme())),
        switchMap((istek) => this.getir(istek.metin)),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(({ durum, sonuclar }) => {
        this.durum.set(durum);
        this.sonuclar.set(sonuclar);
        this.aktifSira.set(sonuclar.length > 0 ? 0 : -1);
      });
  }

  protected override disaridanYazildi(deger: SecimSecenegi | null): void {
    this.metin.set(deger?.etiket ?? '');
  }

  protected secenekKimligi(sira: number): string {
    return `${this.listeKimligi}-${sira}`;
  }

  protected ac(anlik = true): void {
    if (this.pasif()) return;
    if (!this.acik()) {
      this.panelGenisligi.set(this.kutu().nativeElement.getBoundingClientRect().width);
      this.acik.set(true);
      // Açılışta metin seçili öğenin etiketiyse filtre değil — ilk sayfa gelir.
      const secili = this.deger();
      const arama = secili && this.metin() === secili.etiket ? '' : this.metin();
      this.aramalar.next({ metin: arama, anlik });
    }
  }

  protected kapat(): void {
    this.acik.set(false);
    this.aktifSira.set(-1);
  }

  protected yazildi(olay: Event): void {
    const metin = (olay.target as HTMLInputElement).value;
    this.metin.set(metin);
    if (!this.acik()) {
      this.panelGenisligi.set(this.kutu().nativeElement.getBoundingClientRect().width);
      this.acik.set(true);
    }
    this.aramalar.next({ metin, anlik: false });
  }

  protected tus(olay: KeyboardEvent): void {
    const adet = this.sonuclar().length;
    switch (olay.key) {
      case 'ArrowDown':
        olay.preventDefault();
        if (!this.acik()) this.ac();
        else if (adet > 0) this.aktifYap(Math.min(this.aktifSira() + 1, adet - 1));
        break;
      case 'ArrowUp':
        olay.preventDefault();
        if (adet > 0) this.aktifYap(Math.max(this.aktifSira() - 1, 0));
        break;
      case 'Enter': {
        const oge = this.acik() ? this.sonuclar()[this.aktifSira()] : undefined;
        if (oge) {
          olay.preventDefault();
          this.sec(oge);
        }
        break;
      }
      case 'Escape':
        if (this.acik()) {
          olay.preventDefault();
          olay.stopPropagation();
          this.metniGeriYukle();
          this.kapat();
        }
        break;
      case 'Tab':
        this.kapat();
        break;
    }
  }

  protected birakildi(): void {
    this.kapat();
    if (this.metin().trim() === '') {
      if (this.deger() !== null) this.bildir(null);
      this.metin.set('');
    } else {
      // Seçilmeden bırakılan arama metni değer değildir; seçili öğeye dön.
      this.metniGeriYukle();
    }
    this.dokun();
  }

  protected listedenSecildi(olay: ListboxValueChangeEvent<unknown>): void {
    // Seçenek değerleri bu bileşenin `sonuclar()` öğeleri; kimliğiyle eşlenir.
    const oge = this.sonuclar().find((o) => o === olay.option?.value);
    if (oge) this.sec(oge);
  }

  protected temizle(): void {
    this.bildir(null);
    this.metin.set('');
    this.dokun();
    this.girdi().nativeElement.focus();
  }

  protected disTiklama(olay: MouseEvent): void {
    if (!this.kutu().nativeElement.contains(olay.target as Node)) this.kapat();
  }

  /** Uca özgü kısa kod (lokasyon/ek hizmet `kod`, araç `plaka`) varsa ikinci sütunda. */
  protected kodu(oge: SecimSecenegi): string | null {
    const ek = oge as Partial<Record<'kod' | 'plaka', unknown>>;
    const kod = ek.kod ?? ek.plaka;
    return typeof kod === 'string' && kod !== '' && kod !== oge.etiket ? kod : null;
  }

  protected secili(oge: SecimSecenegi): boolean {
    return this.deger()?.id === oge.id;
  }

  private sec(oge: SecimSecenegi): void {
    this.bildir(oge);
    this.metin.set(oge.etiket);
    this.kapat();
    this.girdi().nativeElement.focus();
  }

  /**
   * Metni seçili öğenin etiketine döndürür. DOM da doğrudan yazılır: yazma ile bırakma arasında
   * çizim olmadıysa bağlamanın son değeri etiketle aynı kalır ve `[value]` güncellenmez.
   */
  private metniGeriYukle(): void {
    const etiket = this.deger()?.etiket ?? '';
    this.metin.set(etiket);
    this.girdi().nativeElement.value = etiket;
  }

  private aktifYap(sira: number): void {
    this.aktifSira.set(sira);
    const oge = this.girdi().nativeElement.ownerDocument.getElementById(this.secenekKimligi(sira));
    oge?.scrollIntoView?.({ block: 'nearest' });
  }

  private getir(metin: string) {
    const arama = metin.trim();
    if (arama.length < this.enAzHarf()) {
      return of({ durum: 'kisa' as AramaDurumu, sonuclar: [] as readonly SecimSecenegi[] });
    }
    const limit = Math.min(Math.max(1, this.limit()), SECIM_AZAMI_LIMIT);
    return this.kaynak()(arama, limit).pipe(
      map((sonuclar) => ({ durum: 'hazir' as AramaDurumu, sonuclar: sonuclar.slice(0, limit) })),
      catchError(() =>
        of({ durum: 'hata' as AramaDurumu, sonuclar: [] as readonly SecimSecenegi[] }),
      ),
      startWith({ durum: 'yukleniyor' as AramaDurumu, sonuclar: [] as readonly SecimSecenegi[] }),
    );
  }
}
