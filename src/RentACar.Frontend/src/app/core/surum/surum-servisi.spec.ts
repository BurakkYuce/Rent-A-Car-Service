import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';

import { PARCA_YENILEME_ANAHTARI, ParcaHatasiServisi, parcaYuklemeHatasiMi } from './parca-hatasi';
import { SAYFA_YENILEYICI, SURUM_KAYNAGI, SurumServisi, surumImzasi } from './surum-servisi';

const INDEX = (ana: string) =>
  `<!doctype html><html><head><base href="/app/"></head><body><rc-root></rc-root>` +
  `<link rel="modulepreload" href="chunk-AAAA1111.js"><script src="${ana}" type="module"></script></body></html>`;

describe('surumImzasi', () => {
  it('ana paket adını bulur; yoksa null', () => {
    expect(surumImzasi(INDEX('main-Q73SSRX2.js'))).toBe('main-Q73SSRX2.js');
    expect(surumImzasi('<html><body></body></html>')).toBeNull();
  });
});

describe('SurumServisi', () => {
  const sayfa = { yenile: vi.fn(), git: vi.fn() };
  let yayindaki: string | null;

  beforeEach(async () => {
    sayfa.yenile.mockReset();
    // Bu sekmenin belgesi main-ESKI1234.js'i yüklemiş gibi.
    const betik = document.createElement('script');
    betik.src = 'main-ESKI1234.js';
    betik.type = 'module';
    betik.id = 'test-ana-paket';
    document.body.append(betik);
    yayindaki = INDEX('main-ESKI1234.js');
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        { provide: SAYFA_YENILEYICI, useValue: sayfa },
        { provide: SURUM_KAYNAGI, useValue: () => Promise.resolve(yayindaki) },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => {
    document.getElementById('test-ana-paket')?.remove();
    TestBed.inject(ToastServisi).temizle();
  });

  it('aynı sürümde bildirim yok; yayında farklı ana paket → kalıcı "Yenile" bildirimi, bir kez', async () => {
    const servis = TestBed.inject(SurumServisi);
    expect(servis.mevcut).toBe('main-ESKI1234.js');
    await expect(servis.denetle()).resolves.toBe(false);
    expect(TestBed.inject(ToastServisi).toastlar()).toEqual([]);

    yayindaki = INDEX('main-YENI5678.js');
    await expect(servis.denetle()).resolves.toBe(true);
    await servis.denetle();
    const toastlar = TestBed.inject(ToastServisi).toastlar();
    expect(toastlar).toHaveLength(1);
    expect(toastlar[0]?.mesaj).toContain('Yeni sürüm var');
    expect(servis.yeniSurumVar()).toBe(true);

    TestBed.inject(ToastServisi).eylemCalistir(toastlar[0]?.id ?? -1);
    expect(sayfa.yenile).toHaveBeenCalledTimes(1);
  });

  it('kaynak okunamazsa sessiz (sonraki denetim)', async () => {
    yayindaki = null;
    await expect(TestBed.inject(SurumServisi).denetle()).resolves.toBe(false);
    expect(TestBed.inject(ToastServisi).toastlar()).toEqual([]);
  });
});

describe('ChunkLoadError → kontrollü yenileme', () => {
  const sayfa = { yenile: vi.fn(), git: vi.fn() };

  beforeEach(async () => {
    sessionStorage.clear();
    sayfa.yenile.mockReset();
    sayfa.git.mockReset();
    TestBed.configureTestingModule({
      providers: [...provideCeviri(), { provide: SAYFA_YENILEYICI, useValue: sayfa }],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => TestBed.inject(ToastServisi).temizle());

  it('tarayıcı mesajlarını tanır', () => {
    expect(
      parcaYuklemeHatasiMi(
        new TypeError('Failed to fetch dynamically imported module: /app/chunk-X.js'),
      ),
    ).toBe(true);
    expect(parcaYuklemeHatasiMi(new TypeError('error loading dynamically imported module'))).toBe(
      true,
    );
    expect(parcaYuklemeHatasiMi(new TypeError('Importing a module script failed.'))).toBe(true);
    const chunk = new Error('Loading chunk 12 failed.');
    chunk.name = 'ChunkLoadError';
    expect(parcaYuklemeHatasiMi(chunk)).toBe(true);
    expect(parcaYuklemeHatasiMi(new TypeError('x is undefined'))).toBe(false);
  });

  it('bir kez hedef adrese yeniler; aynı sekmede kısa sürede ikinci hata döngüye girmez', () => {
    expect(TestBed.inject(ParcaHatasiServisi).isle('/app/kiralar')).toBe(true);
    expect(sayfa.git).toHaveBeenCalledWith('/app/kiralar');
    expect(sessionStorage.getItem(PARCA_YENILEME_ANAHTARI)).not.toBeNull();

    // Yeni sayfa yüklemesi (yeni servis örneği), parça yine yüklenemedi:
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [...provideCeviri(), { provide: SAYFA_YENILEYICI, useValue: sayfa }],
    });
    expect(TestBed.inject(ParcaHatasiServisi).isle('/app/kiralar')).toBe(false);
    expect(sayfa.git).toHaveBeenCalledTimes(1);
    expect(TestBed.inject(ToastServisi).toastlar()[0]).toMatchObject({ durum: 'hata' });
  });
});
