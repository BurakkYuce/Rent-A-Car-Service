import {
  ChangeDetectionStrategy,
  Component,
  EnvironmentInjector,
  runInInjectionContext,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { provideTranslation } from '../i18n/ceviri';
import { TabRouteStrategy } from '../sekme/sekme-stratejisi';
import {
  CONFIRM_PROMPT,
  PageLeave,
  FULL_PAGE_NAVIGATION,
  unsavedChangesGuard,
  isDirtyComponent,
  pageLeaveGuard,
} from './kaydedilmemis-degisiklik';

describe('kaydedilmemisDegisiklikGuard', () => {
  let asked: string[];
  let response: boolean;

  beforeEach(() => {
    asked = [];
    response = false;
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        { provide: CONFIRM_PROMPT, useValue: (m: string) => (asked.push(m), response) },
      ],
    });
  });

  function calistir(dirty: boolean): unknown {
    return runInInjectionContext(TestBed.inject(EnvironmentInjector), () =>
      unsavedChangesGuard(
        { hasUnsavedChanges: () => dirty },
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        {} as RouterStateSnapshot,
      ),
    );
  }

  it('temiz formda sormadan geçer', () => {
    expect(calistir(false)).toBe(true);
    expect(asked).toEqual([]);
  });

  it('sekmesi açık kalan sayfa (başka sekmeye geçiş) sorulmaz: bileşen arka planda yaşar', () => {
    const route = {} as ActivatedRouteSnapshot;
    vi.spyOn(TestBed.inject(TabRouteStrategy), 'shouldStore').mockReturnValue(true);
    expect(
      runInInjectionContext(TestBed.inject(EnvironmentInjector), () =>
        unsavedChangesGuard(
          { hasUnsavedChanges: () => true },
          route,
          {} as RouterStateSnapshot,
          {} as RouterStateSnapshot,
        ),
      ),
    ).toBe(true);
    expect(asked).toEqual([]);
  });

  it('toplu onaylanmış işlem (çıkış) süresince sorulmaz, bitince yine sorar', async () => {
    const leave = TestBed.inject(PageLeave);
    await leave.runConfirmed(async () => {
      expect(calistir(true)).toBe(true);
    });
    expect(asked).toEqual([]);
    expect(calistir(true)).toBe(false);
    expect(asked).toHaveLength(1);
  });

  it('kirliBilesenMi: yalnız kaydedilmemisDegisiklikVar() === true olan bileşen', () => {
    expect(isDirtyComponent(null)).toBe(false);
    expect(isDirtyComponent({})).toBe(false);
    expect(isDirtyComponent({ hasUnsavedChanges: () => false })).toBe(false);
    expect(isDirtyComponent({ hasUnsavedChanges: () => true })).toBe(true);
  });

  it('kirli formda sorar; cevap belirleyicidir', () => {
    expect(calistir(true)).toBe(false);
    response = true;
    expect(calistir(true)).toBe(true);
    expect(asked).toHaveLength(2);
    expect(asked[0]).toContain('Kaydedilmemiş değişiklikler var');
  });
});

@Component({
  selector: 'rc-deneme-terk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
class LeaveTestHost {
  dirty = false;
  constructor() {
    pageLeaveGuard(() => this.dirty);
  }
}

describe('sayfaTerkKorumasi (beforeunload)', () => {
  function beforeunload(): Event {
    const evt = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(evt);
    return evt;
  }

  it('kirliyken engeller, temizken engellemez, bileşen yok olunca dinleyici kalkar', () => {
    const fixture = TestBed.createComponent(LeaveTestHost);
    expect(beforeunload().defaultPrevented).toBe(false);
    fixture.componentInstance.dirty = true;
    expect(beforeunload().defaultPrevented).toBe(true);
    fixture.destroy();
    expect(beforeunload().defaultPrevented).toBe(false);
  });

  it('onaylanmış tam sayfa terkte (Blazor ekranı) beforeunload ikinci kez sormaz', () => {
    const visited: string[] = [];
    TestBed.overrideProvider(FULL_PAGE_NAVIGATION, { useValue: (a: string) => visited.push(a) });
    const fixture = TestBed.createComponent(LeaveTestHost);
    fixture.componentInstance.dirty = true;
    TestBed.inject(PageLeave).goToFullPage('/kiralar');
    expect(visited).toEqual(['/kiralar']);
    expect(beforeunload().defaultPrevented).toBe(false);
    // Geri/ileri önbelleğinden dönüş: koruma yeniden devrede.
    window.dispatchEvent(new Event('pageshow'));
    expect(beforeunload().defaultPrevented).toBe(true);
    fixture.destroy();
  });
});
