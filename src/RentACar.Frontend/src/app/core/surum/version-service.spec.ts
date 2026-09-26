import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';

import { CHUNK_RELOAD_KEY, ChunkErrorService, isChunkLoadError } from './parca-hatasi';
import { PAGE_RELOADER, VERSION_SOURCE, VersionService, versionSignature } from './version-service';

const INDEX = (main: string) =>
  `<!doctype html><html><head><base href="/app/"></head><body><rc-root></rc-root>` +
  `<link rel="modulepreload" href="chunk-AAAA1111.js"><script src="${main}" type="module"></script></body></html>`;

describe('surumImzasi', () => {
  it('ana paket adını bulur; yoksa null', () => {
    expect(versionSignature(INDEX('main-Q73SSRX2.js'))).toBe('main-Q73SSRX2.js');
    expect(versionSignature('<html><body></body></html>')).toBeNull();
  });
});

describe('SurumServisi', () => {
  const page = { yenile: vi.fn(), git: vi.fn() };
  let published: string | null;

  beforeEach(async () => {
    page.yenile.mockReset();
    // Bu sekmenin belgesi main-ESKI1234.js'i yüklemiş gibi.
    const script = document.createElement('script');
    script.src = 'main-ESKI1234.js';
    script.type = 'module';
    script.id = 'test-ana-paket';
    document.body.append(script);
    published = INDEX('main-ESKI1234.js');
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        { provide: PAGE_RELOADER, useValue: page },
        { provide: VERSION_SOURCE, useValue: () => Promise.resolve(published) },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => {
    document.getElementById('test-ana-paket')?.remove();
    TestBed.inject(ToastService).clear();
  });

  it('aynı sürümde bildirim yok; yayında farklı ana paket → kalıcı "Yenile" bildirimi, bir kez', async () => {
    const service = TestBed.inject(VersionService);
    expect(service.mevcut).toBe('main-ESKI1234.js');
    await expect(service.check()).resolves.toBe(false);
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);

    published = INDEX('main-YENI5678.js');
    await expect(service.check()).resolves.toBe(true);
    await service.check();
    const toasts = TestBed.inject(ToastService).toasts();
    expect(toasts).toHaveLength(1);
    expect(toasts[0]?.mesaj).toContain('Yeni sürüm var');
    expect(service.hasNewVersion()).toBe(true);

    TestBed.inject(ToastService).runAction(toasts[0]?.id ?? -1);
    expect(page.yenile).toHaveBeenCalledTimes(1);
  });

  it('kaynak okunamazsa sessiz (sonraki denetim)', async () => {
    published = null;
    await expect(TestBed.inject(VersionService).check()).resolves.toBe(false);
    expect(TestBed.inject(ToastService).toasts()).toEqual([]);
  });
});

describe('ChunkLoadError → kontrollü yenileme', () => {
  const page = { yenile: vi.fn(), git: vi.fn() };

  beforeEach(async () => {
    sessionStorage.clear();
    page.yenile.mockReset();
    page.git.mockReset();
    TestBed.configureTestingModule({
      providers: [...provideTranslation(), { provide: PAGE_RELOADER, useValue: page }],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => TestBed.inject(ToastService).clear());

  it('tarayıcı mesajlarını tanır', () => {
    expect(
      isChunkLoadError(
        new TypeError('Failed to fetch dynamically imported module: /app/chunk-X.js'),
      ),
    ).toBe(true);
    expect(isChunkLoadError(new TypeError('error loading dynamically imported module'))).toBe(true);
    expect(isChunkLoadError(new TypeError('Importing a module script failed.'))).toBe(true);
    const chunk = new Error('Loading chunk 12 failed.');
    chunk.name = 'ChunkLoadError';
    expect(isChunkLoadError(chunk)).toBe(true);
    expect(isChunkLoadError(new TypeError('x is undefined'))).toBe(false);
  });

  it('bir kez hedef adrese yeniler; aynı sekmede kısa sürede ikinci hata döngüye girmez', () => {
    expect(TestBed.inject(ChunkErrorService).isle('/app/kiralar')).toBe(true);
    expect(page.git).toHaveBeenCalledWith('/app/kiralar');
    expect(sessionStorage.getItem(CHUNK_RELOAD_KEY)).not.toBeNull();

    // Yeni sayfa yüklemesi (yeni servis örneği), parça yine yüklenemedi:
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [...provideTranslation(), { provide: PAGE_RELOADER, useValue: page }],
    });
    expect(TestBed.inject(ChunkErrorService).isle('/app/kiralar')).toBe(false);
    expect(page.git).toHaveBeenCalledTimes(1);
    expect(TestBed.inject(ToastService).toasts()[0]).toMatchObject({ durum: 'hata' });
  });
});
