import { TestBed } from '@angular/core/testing';
import { GECIS_YOK, TEMA_ANAHTARI, TemaServisi } from './tema-servisi';

describe('TemaServisi', () => {
  const kok = document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    kok.removeAttribute('data-theme');
    kok.removeAttribute('style');
    TestBed.resetTestingModule();
  });

  it('varsayılan sistem: data-theme yok, etkin tema işletim sistemine göre', () => {
    const tema = TestBed.inject(TemaServisi);
    expect(tema.mod()).toBe('sistem');
    expect(kok.hasAttribute('data-theme')).toBe(false);
    expect(tema.etkinTema()).toBe('acik'); // jsdom matchMedia yok → açık
  });

  it('koyu/açık seçimi data-theme yazar ve tercihi saklar; sistem kaldırır', () => {
    const tema = TestBed.inject(TemaServisi);

    tema.modAyarla('koyu');
    expect(kok.getAttribute('data-theme')).toBe('dark');
    expect(tema.etkinTema()).toBe('koyu');
    expect(localStorage.getItem(TEMA_ANAHTARI)).toBe('koyu');

    tema.modAyarla('acik');
    expect(kok.getAttribute('data-theme')).toBe('light');
    expect(tema.etkinTema()).toBe('acik');

    tema.modAyarla('sistem');
    expect(kok.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem(TEMA_ANAHTARI)).toBe('sistem');
  });

  it('tema değişirken CSS geçişleri kısa süre bastırılır (ara karede kontrast düşmesin)', async () => {
    const tema = TestBed.inject(TemaServisi);
    await new Promise((bitti) => setTimeout(bitti, 5));
    tema.modAyarla('koyu');
    expect(kok.classList.contains(GECIS_YOK)).toBe(true);
    await new Promise((bitti) => setTimeout(bitti, 5));
    expect(kok.classList.contains(GECIS_YOK)).toBe(false);
  });

  it('saklı tercih açılışta uygulanır; bozuk değer sistem sayılır', () => {
    localStorage.setItem(TEMA_ANAHTARI, 'koyu');
    expect(TestBed.inject(TemaServisi).mod()).toBe('koyu');
    expect(kok.getAttribute('data-theme')).toBe('dark');

    TestBed.resetTestingModule();
    kok.removeAttribute('data-theme');
    localStorage.setItem(TEMA_ANAHTARI, '<script>');
    expect(TestBed.inject(TemaServisi).mod()).toBe('sistem');
    expect(kok.hasAttribute('data-theme')).toBe(false);
  });

  it('sistem modunda işletim sistemi koyuysa etkin tema koyu', () => {
    const matchMedia = vi.fn(() => ({
      matches: true,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));
    const pencere = document.defaultView as Window;
    const onceki = Object.getOwnPropertyDescriptor(pencere, 'matchMedia');
    Object.defineProperty(pencere, 'matchMedia', { value: matchMedia, configurable: true });
    try {
      expect(TestBed.inject(TemaServisi).etkinTema()).toBe('koyu');
      expect(matchMedia).toHaveBeenCalledWith('(prefers-color-scheme: dark)');
    } finally {
      if (onceki) Object.defineProperty(pencere, 'matchMedia', onceki);
      else Reflect.deleteProperty(pencere, 'matchMedia');
    }
  });

  it('kiracı vurgusu --rc-kiraci-* değişkenlerine yazılır; null ve geçersiz renk temizler', () => {
    const tema = TestBed.inject(TemaServisi);

    tema.kiraciVurgusuUygula('#D97706');
    expect(kok.style.getPropertyValue('--rc-kiraci-vurgu')).toBe('#d97706');
    expect(kok.style.getPropertyValue('--rc-kiraci-vurgu-uzeri')).toBe('#000000');

    tema.kiraciVurgusuUygula(null);
    expect(kok.style.getPropertyValue('--rc-kiraci-vurgu')).toBe('');

    tema.kiraciVurgusuUygula('#1d4ed8');
    tema.kiraciVurgusuUygula('geçersiz');
    expect(kok.style.getPropertyValue('--rc-kiraci-vurgu')).toBe('');
    expect(kok.style.getPropertyValue('--rc-kiraci-vurgu-metin-koyu')).toBe('');
  });
});
