import {
  ChangeDetectionStrategy,
  Component,
  EnvironmentInjector,
  runInInjectionContext,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { provideCeviri } from '../i18n/ceviri';
import { SekmeRotaStratejisi } from '../sekme/sekme-stratejisi';
import {
  ONAY_ISTEMI,
  SayfaTerki,
  TAM_SAYFA_GEZINMESI,
  kaydedilmemisDegisiklikGuard,
  kirliBilesenMi,
  sayfaTerkKorumasi,
} from './kaydedilmemis-degisiklik';

describe('kaydedilmemisDegisiklikGuard', () => {
  let sorulan: string[];
  let yanit: boolean;

  beforeEach(() => {
    sorulan = [];
    yanit = false;
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        { provide: ONAY_ISTEMI, useValue: (m: string) => (sorulan.push(m), yanit) },
      ],
    });
  });

  function calistir(kirli: boolean): unknown {
    return runInInjectionContext(TestBed.inject(EnvironmentInjector), () =>
      kaydedilmemisDegisiklikGuard(
        { kaydedilmemisDegisiklikVar: () => kirli },
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        {} as RouterStateSnapshot,
      ),
    );
  }

  it('temiz formda sormadan geçer', () => {
    expect(calistir(false)).toBe(true);
    expect(sorulan).toEqual([]);
  });

  it('sekmesi açık kalan sayfa (başka sekmeye geçiş) sorulmaz: bileşen arka planda yaşar', () => {
    const rota = {} as ActivatedRouteSnapshot;
    vi.spyOn(TestBed.inject(SekmeRotaStratejisi), 'saklanacakMi').mockReturnValue(true);
    expect(
      runInInjectionContext(TestBed.inject(EnvironmentInjector), () =>
        kaydedilmemisDegisiklikGuard(
          { kaydedilmemisDegisiklikVar: () => true },
          rota,
          {} as RouterStateSnapshot,
          {} as RouterStateSnapshot,
        ),
      ),
    ).toBe(true);
    expect(sorulan).toEqual([]);
  });

  it('toplu onaylanmış işlem (çıkış) süresince sorulmaz, bitince yine sorar', async () => {
    const terk = TestBed.inject(SayfaTerki);
    await terk.onayliCalistir(async () => {
      expect(calistir(true)).toBe(true);
    });
    expect(sorulan).toEqual([]);
    expect(calistir(true)).toBe(false);
    expect(sorulan).toHaveLength(1);
  });

  it('kirliBilesenMi: yalnız kaydedilmemisDegisiklikVar() === true olan bileşen', () => {
    expect(kirliBilesenMi(null)).toBe(false);
    expect(kirliBilesenMi({})).toBe(false);
    expect(kirliBilesenMi({ kaydedilmemisDegisiklikVar: () => false })).toBe(false);
    expect(kirliBilesenMi({ kaydedilmemisDegisiklikVar: () => true })).toBe(true);
  });

  it('kirli formda sorar; cevap belirleyicidir', () => {
    expect(calistir(true)).toBe(false);
    yanit = true;
    expect(calistir(true)).toBe(true);
    expect(sorulan).toHaveLength(2);
    expect(sorulan[0]).toContain('Kaydedilmemiş değişiklikler var');
  });
});

@Component({
  selector: 'rc-deneme-terk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
class DenemeTerk {
  kirli = false;
  constructor() {
    sayfaTerkKorumasi(() => this.kirli);
  }
}

describe('sayfaTerkKorumasi (beforeunload)', () => {
  function beforeunload(): Event {
    const olay = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(olay);
    return olay;
  }

  it('kirliyken engeller, temizken engellemez, bileşen yok olunca dinleyici kalkar', () => {
    const fixture = TestBed.createComponent(DenemeTerk);
    expect(beforeunload().defaultPrevented).toBe(false);
    fixture.componentInstance.kirli = true;
    expect(beforeunload().defaultPrevented).toBe(true);
    fixture.destroy();
    expect(beforeunload().defaultPrevented).toBe(false);
  });

  it('onaylanmış tam sayfa terkte (Blazor ekranı) beforeunload ikinci kez sormaz', () => {
    const gezilen: string[] = [];
    TestBed.overrideProvider(TAM_SAYFA_GEZINMESI, { useValue: (a: string) => gezilen.push(a) });
    const fixture = TestBed.createComponent(DenemeTerk);
    fixture.componentInstance.kirli = true;
    TestBed.inject(SayfaTerki).tamSayfayaGit('/kiralar');
    expect(gezilen).toEqual(['/kiralar']);
    expect(beforeunload().defaultPrevented).toBe(false);
    // Geri/ileri önbelleğinden dönüş: koruma yeniden devrede.
    window.dispatchEvent(new Event('pageshow'));
    expect(beforeunload().defaultPrevented).toBe(true);
    fixture.destroy();
  });
});
