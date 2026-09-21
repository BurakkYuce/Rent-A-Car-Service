import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { ApiHatasi } from '@core/api/api-hatasi';
import { provideCeviri } from '@core/i18n/ceviri';
import { TanimCrud } from './tanim-crud';
import type { TanimAlani, TanimDegeri, TanimKaynagi, TanimSatiri } from './tanim-kaynagi';

let satirlar: TanimSatiri[];
let islemler: string[];
let listeHatasi = false;

const kaynak: TanimKaynagi = {
  listele: (): Observable<readonly TanimSatiri[]> =>
    listeHatasi
      ? throwError(() => new ApiHatasi({ status: 500, kod: 'sunucu', detay: 'Sunucu hatası.' }))
      : of([...satirlar]),
  olustur: (d: TanimDegeri, anahtar: string) => {
    islemler.push(`olustur:${anahtar.length}`);
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
  guncelle: (id: string, d: TanimDegeri) => {
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
  imports: [TanimCrud],
  template: `<rc-tanim-crud baslik="Renkler" [alanlar]="alanlar" [kaynak]="kaynak" />`,
})
class DenemeTanim {
  readonly kaynak = kaynak;
  readonly alanlar: TanimAlani[] = [
    { ad: 'kod', etiket: 'Kod', tur: 'metin', zorunlu: true },
    { ad: 'ad', etiket: 'Ad', tur: 'metin' },
  ];
}

async function kur() {
  satirlar = [
    { id: '1', kod: 'BYZ', ad: 'Beyaz' },
    { id: '2', kod: 'SYH', ad: 'Siyah' },
  ];
  islemler = [];
  TestBed.configureTestingModule({ providers: [...provideCeviri()] });
  const fixture = TestBed.createComponent(DenemeTanim);
  await fixture.whenStable();
  const kok = fixture.nativeElement as HTMLElement;
  const dugme = (metin: string, kapsam: ParentNode = kok) =>
    [...kapsam.querySelectorAll<HTMLButtonElement>('button')].find((d) =>
      d.textContent?.includes(metin),
    );
  const satirMetinleri = () =>
    [...kok.querySelectorAll('tbody tr:not(.duzenleme)')].map((tr) =>
      [...tr.querySelectorAll('td')].slice(0, 2).map((td) => td.textContent?.trim()),
    );
  const yaz = async (sira: number, metin: string) => {
    const g = kok.querySelectorAll<HTMLInputElement>('.duzenleme input')[sira];
    if (!g) throw new Error('girdi');
    g.value = metin;
    g.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  return { fixture, kok, dugme, satirMetinleri, yaz, stabil: () => fixture.whenStable() };
}

describe('rc-tanim-crud', () => {
  it('listeler', async () => {
    const { satirMetinleri } = await kur();
    expect(satirMetinleri()).toEqual([
      ['BYZ', 'Beyaz'],
      ['SYH', 'Siyah'],
    ]);
  });

  it('yeni: zorunlu alan istemcide durur; sunucu alan hatası alana; düzeltince kaydeder', async () => {
    const { kok, dugme, yaz, stabil, satirMetinleri } = await kur();
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
    const { kok, dugme, yaz, stabil, satirMetinleri } = await kur();
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
    const { dugme, stabil, satirMetinleri, kok } = await kur();
    dugme('Sil')?.click();
    await stabil();
    expect(kok.textContent).toContain('Bu kayıt silinsin mi?');
    dugme('Vazgeç')?.click();
    await stabil();
    expect(islemler).toEqual([]);
    dugme('Sil')?.click();
    await stabil();
    dugme('Evet, sil')?.click();
    await stabil();
    expect(islemler).toEqual(['sil:1']);
    expect(satirMetinleri()).toEqual([['SYH', 'Siyah']]);
  });

  it('liste hatası boş liste gibi görünmez', async () => {
    listeHatasi = true;
    try {
      const { kok } = await kur();
      expect(kok.textContent).toContain('Kayıtlar yüklenemedi.');
      expect(kok.textContent).not.toContain('Henüz kayıt yok.');
    } finally {
      listeHatasi = false;
    }
  });
});
