import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { listeTanimi } from './liste-sorgusu';
import { listeSorgusuUrlSenkronu } from './liste-sorgusu-url';

const ARACLAR = listeTanimi({
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
class DenemeListe {
  readonly liste = listeSorgusuUrlSenkronu(ARACLAR);
}

describe('listeSorgusuUrlSenkronu', () => {
  let harness: RouterTestingHarness;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'araclar', component: DenemeListe }])],
    });
    harness = await RouterTestingHarness.create();
    router = TestBed.inject(Router);
  });

  it('URL → sorgu (bozuk parametreler varsayılana düşer, Türkçe metin korunur)', async () => {
    const sayfa = await harness.navigateByUrl(
      '/araclar?sayfa=abc&boyut=900&sirala=bilinmeyen&durum=kirada&arama=%C4%B0zmir%20%C5%9Eube',
      DenemeListe,
    );
    expect(sayfa.liste.sorgu()).toEqual({
      sayfa: 1,
      boyut: 200,
      sirala: null,
      filtreler: { durum: 'kirada', arama: 'İzmir Şube' },
    });
    expect(sayfa.liste.apiParametreleri()).toEqual({
      sayfa: 1,
      boyut: 200,
      arama: 'İzmir Şube',
      durum: 'kirada',
    });
    expect(sayfa.liste.etkinFiltreSayisi()).toBe(2);
  });

  it('sorgu → URL: filtre değişince sayfa 1, varsayılanlar silinir, yabancı parametre ve fragment korunur', async () => {
    const sayfa = await harness.navigateByUrl(
      '/araclar?sayfa=4&bilgi=Kaydedildi#sekme=genel',
      DenemeListe,
    );

    await sayfa.liste.degistir({ filtreler: { arama: 'Çorum' } }, { yaziyor: true });

    expect(router.url).toBe('/araclar?bilgi=Kaydedildi&arama=%C3%87orum#sekme=genel');
    expect(sayfa.liste.sorgu()).toMatchObject({ sayfa: 1, filtreler: { arama: 'Çorum' } });
  });

  it('yaziyor → replaceUrl; ayrık adım → geçmişe girer', async () => {
    const sayfa = await harness.navigateByUrl('/araclar', DenemeListe);
    const gezinme = vi.spyOn(router, 'navigate');

    await sayfa.liste.degistir({ filtreler: { arama: 'a' } }, { yaziyor: true });
    await sayfa.liste.degistir({ sayfa: 2 });

    expect(gezinme.mock.calls.map(([, ekstra]) => ekstra?.replaceUrl)).toEqual([true, false]);
  });

  it('aynı tikteki iki değişiklik birbirini ezmez', async () => {
    const sayfa = await harness.navigateByUrl('/araclar', DenemeListe);

    const ilk = sayfa.liste.degistir({ filtreler: { durum: 'musait' } });
    const ikinci = sayfa.liste.degistir({ sirala: '-plaka' });
    await Promise.all([ilk, ikinci]);

    expect(sayfa.liste.sorgu()).toEqual({
      sayfa: 1,
      boyut: 50,
      sirala: '-plaka',
      filtreler: { durum: 'musait' },
    });
  });

  it('anlamca aynı URL (yabancı parametre değişti) sorgu sinyalini tetiklemez', async () => {
    const sayfa = await harness.navigateByUrl('/araclar?arama=x', DenemeListe);
    const once = sayfa.liste.sorgu();

    await router.navigateByUrl('/araclar?arama=x&bilgi=Tamam');

    expect(sayfa.liste.sorgu()).toBe(once);
  });

  it('sifirla tüm yönetilen parametreleri kaldırır', async () => {
    const sayfa = await harness.navigateByUrl(
      '/araclar?sayfa=3&arama=x&sirala=plaka&bilgi=1',
      DenemeListe,
    );
    await sayfa.liste.sifirla();
    expect(router.url).toBe('/araclar?bilgi=1');
  });
});
