import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { listDefinition } from './liste-sorgusu';
import { listQueryUrlSync } from './liste-sorgusu-url';

const VEHICLES = listDefinition({
  filtreler: {
    arama: { tur: 'metin' },
    durum: { tur: 'secim', degerler: ['musait', 'kirada'] },
  },
  siralanabilir: ['plaka'],
});

@Component({
  selector: 'rc-deneme-liste',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
class ListTestHost {
  readonly liste = listQueryUrlSync(VEHICLES);
}

describe('listeSorgusuUrlSenkronu', () => {
  let harness: RouterTestingHarness;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'araclar', component: ListTestHost }])],
    });
    harness = await RouterTestingHarness.create();
    router = TestBed.inject(Router);
  });

  it('URL → sorgu (bozuk parametreler varsayılana düşer, Türkçe metin korunur)', async () => {
    const page = await harness.navigateByUrl(
      '/araclar?sayfa=abc&boyut=900&sirala=bilinmeyen&durum=kirada&arama=%C4%B0zmir%20%C5%9Eube',
      ListTestHost,
    );
    expect(page.liste.sorgu()).toEqual({
      sayfa: 1,
      boyut: 200,
      sirala: null,
      filtreler: { durum: 'kirada', arama: 'İzmir Şube' },
    });
    expect(page.liste.apiParametreleri()).toEqual({
      sayfa: 1,
      boyut: 200,
      arama: 'İzmir Şube',
      durum: 'kirada',
    });
    expect(page.liste.etkinFiltreSayisi()).toBe(2);
  });

  it('sorgu → URL: filtre değişince sayfa 1, varsayılanlar silinir, yabancı parametre ve fragment korunur', async () => {
    const page = await harness.navigateByUrl(
      '/araclar?sayfa=4&bilgi=Kaydedildi#sekme=genel',
      ListTestHost,
    );

    await page.liste.degistir({ filtreler: { arama: 'Çorum' } }, { yaziyor: true });

    expect(router.url).toBe('/araclar?bilgi=Kaydedildi&arama=%C3%87orum#sekme=genel');
    expect(page.liste.sorgu()).toMatchObject({ sayfa: 1, filtreler: { arama: 'Çorum' } });
  });

  it('yaziyor → replaceUrl; ayrık adım → geçmişe girer', async () => {
    const page = await harness.navigateByUrl('/araclar', ListTestHost);
    const navigation = vi.spyOn(router, 'navigate');

    await page.liste.degistir({ filtreler: { arama: 'a' } }, { yaziyor: true });
    await page.liste.degistir({ sayfa: 2 });

    expect(navigation.mock.calls.map(([, extra]) => extra?.replaceUrl)).toEqual([true, false]);
  });

  it('aynı tikteki iki değişiklik birbirini ezmez', async () => {
    const page = await harness.navigateByUrl('/araclar', ListTestHost);

    const first = page.liste.degistir({ filtreler: { durum: 'musait' } });
    const second = page.liste.degistir({ sirala: '-plaka' });
    await Promise.all([first, second]);

    expect(page.liste.sorgu()).toEqual({
      sayfa: 1,
      boyut: 50,
      sirala: '-plaka',
      filtreler: { durum: 'musait' },
    });
  });

  it('anlamca aynı URL (yabancı parametre değişti) sorgu sinyalini tetiklemez', async () => {
    const page = await harness.navigateByUrl('/araclar?arama=x', ListTestHost);
    const once = page.liste.sorgu();

    await router.navigateByUrl('/araclar?arama=x&bilgi=Tamam');

    expect(page.liste.sorgu()).toBe(once);
  });

  it('sifirla tüm yönetilen parametreleri kaldırır', async () => {
    const page = await harness.navigateByUrl(
      '/araclar?sayfa=3&arama=x&sirala=plaka&bilgi=1',
      ListTestHost,
    );
    await page.liste.sifirla();
    expect(router.url).toBe('/araclar?bilgi=1');
  });
});
