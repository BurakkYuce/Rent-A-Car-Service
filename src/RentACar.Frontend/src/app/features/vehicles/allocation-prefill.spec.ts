import { describe, expect, it } from 'vitest';

import { readAllocationPrefill } from './allocation-prefill';

describe('readAllocationPrefill', () => {
  const valid = {
    allocationPrefill: {
      vehicleId: '11111111-1111-1111-1111-111111111111',
      plaka: '34 ABC 01',
      km: 12500,
      sube: 'Merkez',
    },
  };

  it('reads a well-formed router state', () => {
    expect(readAllocationPrefill(valid)).toEqual({
      vehicleId: '11111111-1111-1111-1111-111111111111',
      plaka: '34 ABC 01',
      km: 12500,
      sube: 'Merkez',
    });
  });

  it('returns null when state is missing or malformed', () => {
    expect(readAllocationPrefill(undefined)).toBeNull();
    expect(readAllocationPrefill(null)).toBeNull();
    expect(readAllocationPrefill({})).toBeNull();
    expect(readAllocationPrefill({ allocationPrefill: 'x' })).toBeNull();
    expect(
      readAllocationPrefill({ allocationPrefill: { ...valid.allocationPrefill, vehicleId: '' } }),
    ).toBeNull();
    expect(
      readAllocationPrefill({ allocationPrefill: { ...valid.allocationPrefill, plaka: 5 } }),
    ).toBeNull();
  });

  it('drops negative/NaN km and blank branch instead of sending them', () => {
    expect(
      readAllocationPrefill({
        allocationPrefill: { ...valid.allocationPrefill, km: -1, sube: '  ' },
      }),
    ).toMatchObject({ km: null, sube: null });
    expect(
      readAllocationPrefill({ allocationPrefill: { ...valid.allocationPrefill, km: Number.NaN } }),
    ).toMatchObject({ km: null });
  });
});
