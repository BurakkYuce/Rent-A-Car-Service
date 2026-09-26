import { TestBed } from '@angular/core/testing';
import { NO_TRANSITION, THEME_KEY, TemaServisi } from './tema-servisi';

describe('TemaServisi', () => {
  const root = document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
    root.removeAttribute('style');
    TestBed.resetTestingModule();
  });

  it('varsayılan sistem: data-theme yok, etkin tema işletim sistemine göre', () => {
    const theme = TestBed.inject(TemaServisi);
    expect(theme.mod()).toBe('sistem');
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(theme.activeTheme()).toBe('acik'); // jsdom matchMedia yok → açık
  });

  it('koyu/açık seçimi data-theme yazar ve tercihi saklar; sistem kaldırır', () => {
    const theme = TestBed.inject(TemaServisi);

    theme.setMode('koyu');
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(theme.activeTheme()).toBe('koyu');
    expect(localStorage.getItem(THEME_KEY)).toBe('koyu');

    theme.setMode('acik');
    expect(root.getAttribute('data-theme')).toBe('light');
    expect(theme.activeTheme()).toBe('acik');

    theme.setMode('sistem');
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem(THEME_KEY)).toBe('sistem');
  });

  it('tema değişirken CSS geçişleri kısa süre bastırılır (ara karede kontrast düşmesin)', async () => {
    const theme = TestBed.inject(TemaServisi);
    await new Promise((done) => setTimeout(done, 5));
    theme.setMode('koyu');
    expect(root.classList.contains(NO_TRANSITION)).toBe(true);
    await new Promise((done) => setTimeout(done, 5));
    expect(root.classList.contains(NO_TRANSITION)).toBe(false);
  });

  it('saklı tercih açılışta uygulanır; bozuk değer sistem sayılır', () => {
    localStorage.setItem(THEME_KEY, 'koyu');
    expect(TestBed.inject(TemaServisi).mod()).toBe('koyu');
    expect(root.getAttribute('data-theme')).toBe('dark');

    TestBed.resetTestingModule();
    root.removeAttribute('data-theme');
    localStorage.setItem(THEME_KEY, '<script>');
    expect(TestBed.inject(TemaServisi).mod()).toBe('sistem');
    expect(root.hasAttribute('data-theme')).toBe(false);
  });

  it('sistem modunda işletim sistemi koyuysa etkin tema koyu', () => {
    const matchMedia = vi.fn(() => ({
      matches: true,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));
    const window = document.defaultView as Window;
    const previous = Object.getOwnPropertyDescriptor(window, 'matchMedia');
    Object.defineProperty(window, 'matchMedia', { value: matchMedia, configurable: true });
    try {
      expect(TestBed.inject(TemaServisi).activeTheme()).toBe('koyu');
      expect(matchMedia).toHaveBeenCalledWith('(prefers-color-scheme: dark)');
    } finally {
      if (previous) Object.defineProperty(window, 'matchMedia', previous);
      else Reflect.deleteProperty(window, 'matchMedia');
    }
  });

  it('kiracı vurgusu --rc-kiraci-* değişkenlerine yazılır; null ve geçersiz renk temizler', () => {
    const theme = TestBed.inject(TemaServisi);

    theme.applyTenantAccent('#D97706');
    expect(root.style.getPropertyValue('--rc-kiraci-vurgu')).toBe('#d97706');
    expect(root.style.getPropertyValue('--rc-kiraci-vurgu-uzeri')).toBe('#000000');

    theme.applyTenantAccent(null);
    expect(root.style.getPropertyValue('--rc-kiraci-vurgu')).toBe('');

    theme.applyTenantAccent('#1d4ed8');
    theme.applyTenantAccent('geçersiz');
    expect(root.style.getPropertyValue('--rc-kiraci-vurgu')).toBe('');
    expect(root.style.getPropertyValue('--rc-kiraci-vurgu-metin-koyu')).toBe('');
  });
});
