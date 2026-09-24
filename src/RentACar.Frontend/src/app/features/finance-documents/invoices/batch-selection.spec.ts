import { pruneSelection } from './batch-selection';

describe('toplu faturalama seçimi', () => {
  it('görünmeyen kiralar seçimden düşer, görünenler kalır', () => {
    const next = pruneSelection(new Set(['k1', 'k2', 'k3']), ['k1', 'k3', 'k9']);
    expect([...next].sort()).toEqual(['k1', 'k3']);
  });

  it('boş aday listesi seçimi tamamen boşaltır', () => {
    expect(pruneSelection(new Set(['k1']), []).size).toBe(0);
  });

  it('değişiklik yoksa aynı küme döner', () => {
    const current = new Set(['k1', 'k2']);
    expect(pruneSelection(current, ['k2', 'k1', 'k5'])).toBe(current);
  });
});
