import { ChangeDetectionStrategy, Component, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Observable, delay, of, throwError } from 'rxjs';
import { ApiHatasi } from '@core/api/api-hatasi';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { TanimCrud } from '@shared/form/tanim-crud/tanim-crud';
import type {
  TanimAlani,
  TanimDegeri,
  TanimKaynagi,
  TanimSatiri,
} from '@shared/form/tanim-crud/tanim-kaynagi';

/**
 * Tanım CRUD vitrini, bellek içi kaynakla (F11'de `restTanimKaynagi('/api/ui/v1/...')`). Aynı kod
 * ikinci kez girilirse kaynak 400 `dogrulama` + `alanlar.Kod` döner: sunucu alan hatası yolu görünür.
 */
@Component({
  selector: 'rc-tanim-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TanimCrud],
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-4);
      max-width: 60rem;
      min-width: 0;
      padding: var(--rc-bosluk-4);
    }
  `,
  template: `
    <div class="rc-sayfa-basligi">
      <h1>Tanım vitrini</h1>
      <a routerLink="/" class="rc-yazdirma-gizle">Ana sayfa</a>
    </div>
    <rc-tanim-crud baslik="Araç renkleri" [alanlar]="alanlar" [kaynak]="kaynak" />
  `,
})
export class TanimVitrini {
  private readonly crud = viewChild.required(TanimCrud);

  protected readonly alanlar: readonly TanimAlani[] = [
    { ad: 'kod', etiket: 'Kod', tur: 'metin', zorunlu: true, azamiUzunluk: 10 },
    { ad: 'ad', etiket: 'Ad', tur: 'metin', zorunlu: true, azamiUzunluk: 60 },
    { ad: 'sira', etiket: 'Sıra', tur: 'sayi' },
    { ad: 'aktif', etiket: 'Aktif', tur: 'onay' },
  ];
  protected readonly kaynak = bellekKaynagi([
    { id: '1', kod: 'BYZ', ad: 'Beyaz', sira: 1, aktif: true },
    { id: '2', kod: 'SYH', ad: 'Siyah', sira: 2, aktif: true },
    { id: '3', kod: 'GRI', ad: 'Gri', sira: 3, aktif: false },
  ]);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.crud().kaydedilmemisDegisiklikVar();
  }
}

function bellekKaynagi(baslangic: TanimSatiri[]): TanimKaynagi {
  let satirlar = [...baslangic];
  let sayac = satirlar.length;
  const gecikmeli = <T>(deger: T): Observable<T> => of(deger).pipe(delay(150));
  const kodCakisiyor = (deger: TanimDegeri, haricId?: string): boolean =>
    satirlar.some((s) => s['kod'] === deger['kod'] && s.id !== haricId);
  const cakisma = (): Observable<never> =>
    throwError(
      () =>
        new ApiHatasi({
          status: 400,
          kod: 'dogrulama',
          detay: 'Bu kod zaten kullanılıyor.',
          alanlar: { Kod: ['Bu kod zaten kullanılıyor.'] },
        }),
    ).pipe(delay(150));
  return {
    listele: () => gecikmeli([...satirlar]),
    olustur: (deger) => {
      if (kodCakisiyor(deger)) return cakisma();
      sayac += 1;
      satirlar = [...satirlar, { ...deger, id: String(sayac) }];
      return gecikmeli(null);
    },
    guncelle: (id, deger) => {
      if (kodCakisiyor(deger, id)) return cakisma();
      satirlar = satirlar.map((s) => (s.id === id ? { ...deger, id } : s));
      return gecikmeli(null);
    },
    sil: (id) => {
      satirlar = satirlar.filter((s) => s.id !== id);
      return gecikmeli(null);
    },
  };
}
