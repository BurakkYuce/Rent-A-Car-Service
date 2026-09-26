import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { ApiHatasi } from '@core/api/api-hatasi';
import { provideTranslation } from '@core/i18n/ceviri';
import { DefinitionCrud } from './definition-crud';
import type {
  TanimAlani,
  DefinitionValue,
  DefinitionSource,
  DefinitionRow,
} from './definition-source';

let satirlar: DefinitionRow[];
let islemler: string[];
let listError = false;

const kaynak: DefinitionSource = {
  listele: (): Observable<readonly DefinitionRow[]> =>
    listError
      ? throwError(() => new ApiHatasi({ status: 500, kod: 'sunucu', detay: 'Sunucu hatası.' }))
      : of([...satirlar]),
  olustur: (d: DefinitionValue, key: string) => {
    islemler.push(`olustur:${key.length}`);
    if (satirlar.some((s) => s['kod'] === d['kod'])) {
      return throwError(
        () =>
          new ApiHatasi({
            status: 400,
            kod: 'dogrulama',
            detay: 'x',
            alanlar: { Kod: ['Bu kod zaten var.'] },
          }),
      );
    }
    satirlar.push({ ...d, id: `y${satirlar.length}` });
    return of(null);
  },
  guncelle: (id: string, d: DefinitionValue) => {
    islemler.push(`guncelle:${id}`);
    satirlar = satirlar.map((s) => (s.id === id ? { ...d, id } : s));
    return of(null);
  },
  sil: (id: string) => {
    islemler.push(`sil:${id}`);
    satirlar = satirlar.filter((s) => s.id !== id);
    return of(null);
  },
};

@Component({
  selector: 'rc-deneme-tanim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DefinitionCrud],
  template: `<rc-tanim-crud baslik="Renkler" [alanlar]="fields" [kaynak]="kaynak" />`,
})
class DefinitionTestHost {
  readonly kaynak = kaynak;
  readonly fields: TanimAlani[] = [
    { ad: 'kod', etiket: 'Kod', tur: 'metin', zorunlu: true },
    { ad: 'ad', etiket: 'Ad', tur: 'metin' },
  ];
}

async function exchangeRate() {
  satirlar = [
    { id: '1', kod: 'BYZ', ad: 'Beyaz' },
    { id: '2', kod: 'SYH', ad: 'Siyah' },
  ];
  islemler = [];
  TestBed.configureTestingModule({ providers: [...provideTranslation()] });
  const fixture = TestBed.createComponent(DefinitionTestHost);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const button = (text: string, scope: ParentNode = root) =>
    [...scope.querySelectorAll<HTMLButtonElement>('button')].find((d) =>
      d.textContent?.includes(text),
    );
  const rowTexts = () =>
    [...root.querySelectorAll('tbody tr:not(.duzenleme)')].map((tr) =>
      [...tr.querySelectorAll('td')].slice(0, 2).map((td) => td.textContent?.trim()),
    );
  const write = async (order: number, text: string) => {
    const g = root.querySelectorAll<HTMLInputElement>('.duzenleme input')[order];
    if (!g) throw new Error('girdi');
    g.value = text;
    g.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  return {
    fixture,
    kok: root,
    dugme: button,
    satirMetinleri: rowTexts,
    yaz: write,
    stabil: () => fixture.whenStable(),
  };
}

describe('rc-tanim-crud', () => {
  it('listeler', async () => {
    const { satirMetinleri } = await exchangeRate();
    expect(satirMetinleri()).toEqual([
      ['BYZ', 'Beyaz'],
      ['SYH', 'Siyah'],
    ]);
  });

  it('yeni: zorunlu alan istemcide durur; sunucu alan hatası alana; düzeltince kaydeder', async () => {
    const { kok, dugme, yaz, stabil, satirMetinleri } = await exchangeRate();
    dugme('Yeni kayıt')?.click();
    await stabil();
    dugme('Kaydet')?.click();
    await stabil();
    expect(islemler).toEqual([]);
    expect(kok.querySelector('.duzenleme')?.textContent).toContain('Bu alan zorunlu.');
    await yaz(0, 'BYZ');
    await yaz(1, 'Beyaz 2');
    dugme('Kaydet')?.click();
    await stabil();
    expect(kok.querySelector('.duzenleme')?.textContent).toContain('Bu kod zaten var.');
    expect(kok.querySelectorAll<HTMLInputElement>('.duzenleme input')[1]?.value).toBe('Beyaz 2');
    await yaz(0, 'KRM');
    dugme('Kaydet')?.click();
    await stabil();
    expect(kok.querySelector('.duzenleme')).toBeNull();
    expect(satirMetinleri()).toContainEqual(['KRM', 'Beyaz 2']);
    // Anahtar sunucunun 16–128 kuralına uygun uzunlukta (UUID); aynı gönderimin tekrarı aynı anahtar.
    expect(islemler).toEqual(['olustur:36', 'olustur:36']);
  });

  it('düzenle: satır içi form mevcut değerlerle; kaydedince güncellenir', async () => {
    const { kok, dugme, yaz, stabil, satirMetinleri } = await exchangeRate();
    dugme('Düzenle')?.click();
    await stabil();
    expect(kok.querySelectorAll<HTMLInputElement>('.duzenleme input')[0]?.value).toBe('BYZ');
    await yaz(1, 'Kar beyazı');
    dugme('Kaydet')?.click();
    await stabil();
    expect(islemler).toEqual(['guncelle:1']);
    expect(satirMetinleri()[0]).toEqual(['BYZ', 'Kar beyazı']);
  });

  it('sil: önce onay; vazgeç silmez, evet siler', async () => {
    const { dugme, stabil: stable, satirMetinleri, kok } = await exchangeRate();
    dugme('Sil')?.click();
    await stable();
    expect(kok.textContent).toContain('Bu kayıt silinsin mi?');
    dugme('Vazgeç')?.click();
    await stable();
    expect(islemler).toEqual([]);
    dugme('Sil')?.click();
    await stable();
    dugme('Evet, sil')?.click();
    await stable();
    expect(islemler).toEqual(['sil:1']);
    expect(satirMetinleri()).toEqual([['SYH', 'Siyah']]);
  });

  it('liste hatası boş liste gibi görünmez', async () => {
    listError = true;
    try {
      const { kok } = await exchangeRate();
      expect(kok.textContent).toContain('Kayıtlar yüklenemedi.');
      expect(kok.textContent).not.toContain('Henüz kayıt yok.');
    } finally {
      listError = false;
    }
  });
});
