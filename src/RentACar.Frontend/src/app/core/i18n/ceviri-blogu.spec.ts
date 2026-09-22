import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Translation, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from './ceviri';
import { CEVIRI_BLOK_KAYNAKLARI, ceviriBlogu, ceviriBloguyla } from './ceviri-blogu';
import { CEVIRI_BLOKLARI } from './ceviri-bloklari';
import { ONYUKLU_CEVIRI_BLOKLARI } from './onyuklu-ceviri';

@Component({
  selector: 'rc-panel-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `<h1>{{ 'panel.baslik' | transloco }}</h1>`,
})
class PanelDeneme {}

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Bos {}

// Bağımsız oracle: beklenen metinler elle yazıldı (blok dosyasından okunmadı).
const SAHTE_PANEL: Translation = { panel: { baslik: 'Panel (sahte blok)' } };

describe('rota bazlı tembel çeviri (ceviriBlogu)', () => {
  let panelYukle: ReturnType<typeof vi.fn<() => Promise<Translation>>>;

  beforeEach(async () => {
    panelYukle = vi.fn(() => Promise.resolve(SAHTE_PANEL));
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        // Üretimdeki gibi: testlerin önyüklü blokları YOK, blok yalnız rotayla gelir.
        { provide: ONYUKLU_CEVIRI_BLOKLARI, useValue: [] },
        {
          provide: CEVIRI_BLOK_KAYNAKLARI,
          useValue: { ...CEVIRI_BLOKLARI, panel: () => panelYukle() },
        },
        provideRouter([
          { path: 'panel', component: PanelDeneme, canActivate: [ceviriBlogu('panel')] },
          { path: '', component: Bos },
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
    const calistir = () =>
      TestBed.runInInjectionContext(() => guard(null as never, null as never)) as Promise<boolean>;
    const [a, b] = await Promise.all([calistir(), calistir()]);
    await calistir();
    expect([a, b]).toEqual([true, true]);
    expect(panelYukle).toHaveBeenCalledTimes(1);
  });

  it('yükleme hatası gezinmeyi düşürür, sonraki deneme yeniden yükler', async () => {
    panelYukle.mockImplementationOnce(() =>
      Promise.reject(new TypeError('Failed to fetch dynamically imported module')),
    );
    const router = TestBed.inject(Router);
    await expect(router.navigateByUrl('/panel')).rejects.toThrow('dynamically imported module');
    expect(await router.navigateByUrl('/panel')).toBe(true);
    expect(TestBed.inject(TranslocoService).translate('panel.baslik')).toBe('Panel (sahte blok)');
    expect(panelYukle).toHaveBeenCalledTimes(2);
  });

  it('gerçek blok kaydı: her blok ayrı dinamik parça, kendi üst düzey anahtarını taşır', async () => {
    expect(Object.keys(CEVIRI_BLOKLARI)).toEqual(
      expect.arrayContaining(['kira-formu', 'kiralar', 'panel']),
    );
    expect(Object.keys(await CEVIRI_BLOKLARI.panel())).toEqual(['panel']);
    expect(Object.keys(await CEVIRI_BLOKLARI['kira-formu']())).toContain('kiraFormu');
  });

  it('ceviriBloguyla her rotaya bloğu ilk koruyucu olarak ekler, mevcutları korur', () => {
    const mevcut = () => true;
    const rotalar = ceviriBloguyla('vitrin', [
      { path: 'a', component: Bos },
      { path: 'b', component: Bos, canActivate: [mevcut] },
    ]);
    expect(rotalar.map((r) => r.canActivate?.length)).toEqual([1, 2]);
    expect(rotalar[1].canActivate?.[1]).toBe(mevcut);
  });
});
