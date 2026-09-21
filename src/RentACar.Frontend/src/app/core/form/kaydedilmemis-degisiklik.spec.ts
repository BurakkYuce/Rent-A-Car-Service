import {
  ChangeDetectionStrategy,
  Component,
  EnvironmentInjector,
  runInInjectionContext,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { provideCeviri } from '../i18n/ceviri';
import {
  ONAY_ISTEMI,
  kaydedilmemisDegisiklikGuard,
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
});
