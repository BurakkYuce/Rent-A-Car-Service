import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Translation, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from './ceviri';
import { TRANSLATION_BLOCK_SOURCES, ceviriBlogu, withTranslationBlock } from './ceviri-blogu';
import { CEVIRI_BLOKLARI } from './ceviri-bloklari';
import { ONYUKLU_CEVIRI_BLOKLARI } from './onyuklu-ceviri';

@Component({
  selector: 'rc-panel-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `<h1>{{ 'panel.baslik' | transloco }}</h1>`,
})
class PanelTest {}

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Empty {}

// Bağımsız oracle: beklenen metinler elle yazıldı (blok dosyasından okunmadı).
const FAKE_PANEL: Translation = { panel: { baslik: 'Panel (sahte blok)' } };

describe('rota bazlı tembel çeviri (ceviriBlogu)', () => {
  let loadPanel: ReturnType<typeof vi.fn<() => Promise<Translation>>>;

  beforeEach(async () => {
    loadPanel = vi.fn(() => Promise.resolve(FAKE_PANEL));
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        // Üretimdeki gibi: testlerin önyüklü blokları YOK, blok yalnız rotayla gelir.
        { provide: ONYUKLU_CEVIRI_BLOKLARI, useValue: [] },
        {
          provide: TRANSLATION_BLOCK_SOURCES,
          useValue: { ...CEVIRI_BLOKLARI, panel: () => loadPanel() },
        },
        provideRouter([
          { path: 'panel', component: PanelTest, canActivate: [ceviriBlogu('panel')] },
          { path: '', component: Empty },
        ]),
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('çekirdek sözlükte özellik bloğu yok; eksik anahtar davranışı aynı', () => {
    const transloco = TestBed.inject(TranslocoService);
    expect(transloco.translate('panel.baslik')).toBe('EKSİK: panel.baslik');
    expect(transloco.translate('kiraFormu.yeniBaslik')).toBe('EKSİK: kiraFormu.yeniBaslik');
    expect(transloco.translate('tema.koyu')).toBe('Koyu');
  });

  it('blok rota çözülmeden yüklenir: ilk çizimde metin hazır (anahtar yanıp sönmez)', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/panel');
    expect(harness.routeNativeElement?.textContent).toContain('Panel (sahte blok)');
    // Çekirdek korunur (birleştirme ezmez).
    expect(TestBed.inject(TranslocoService).translate('tema.koyu')).toBe('Koyu');
  });

  it('blok bir kez yüklenir; eşzamanlı ve sonraki gezinmeler aynı yüklemeyi kullanır', async () => {
    const guard = ceviriBlogu('panel');
    const run = () =>
      TestBed.runInInjectionContext(() => guard(null as never, null as never)) as Promise<boolean>;
    const [a, b] = await Promise.all([run(), run()]);
    await run();
    expect([a, b]).toEqual([true, true]);
    expect(loadPanel).toHaveBeenCalledTimes(1);
  });

  it('yükleme hatası gezinmeyi düşürür, sonraki deneme yeniden yükler', async () => {
    loadPanel.mockImplementationOnce(() =>
      Promise.reject(new TypeError('Failed to fetch dynamically imported module')),
    );
    const router = TestBed.inject(Router);
    await expect(router.navigateByUrl('/panel')).rejects.toThrow('dynamically imported module');
    expect(await router.navigateByUrl('/panel')).toBe(true);
    expect(TestBed.inject(TranslocoService).translate('panel.baslik')).toBe('Panel (sahte blok)');
    expect(loadPanel).toHaveBeenCalledTimes(2);
  });

  it('gerçek blok kaydı: her blok ayrı dinamik parça, kendi üst düzey anahtarını taşır', async () => {
    expect(Object.keys(CEVIRI_BLOKLARI)).toEqual(
      expect.arrayContaining(['kira-formu', 'kiralar', 'panel']),
    );
    expect(Object.keys(await CEVIRI_BLOKLARI.panel())).toEqual(['panel']);
    expect(Object.keys(await CEVIRI_BLOKLARI['kira-formu']())).toContain('kiraFormu');
  });

  it('ceviriBloguyla her rotaya bloğu ilk koruyucu olarak ekler, mevcutları korur', () => {
    const existing = () => true;
    const routes = withTranslationBlock('vitrin', [
      { path: 'a', component: Empty },
      { path: 'b', component: Empty, canActivate: [existing] },
    ]);
    expect(routes.map((r) => r.canActivate?.length)).toEqual([1, 2]);
    expect(routes[1].canActivate?.[1]).toBe(existing);
  });
});
