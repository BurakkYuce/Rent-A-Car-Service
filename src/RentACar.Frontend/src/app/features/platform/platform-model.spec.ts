import {
  documentStatus,
  documentUploadForm,
  kilobytes,
  logoInfo,
  tenantStatus,
  tenantStatusBadge,
  toggleTarget,
  toNumber,
  updateBody,
} from './platform-model';

describe('platform-model', () => {
  it('status: unknown value never reads as active; closed stamp has no toggle', () => {
    expect(tenantStatus('Aktif')).toBe('Aktif');
    expect(tenantStatus('Kapali')).toBe('Kapali');
    expect(tenantStatus('Kapalı')).toBe('Pasif');
    expect(tenantStatus('')).toBe('Pasif');
    expect(toggleTarget('Aktif')).toBe('Pasif');
    expect(toggleTarget('Pasif')).toBe('Aktif');
    expect(toggleTarget('Kapali')).toBeNull();
    expect(tenantStatusBadge('Kapali')).toBe('rc-rozet--hata');
    expect(tenantStatusBadge('Aktif')).toBe('rc-rozet--basari');
    expect(tenantStatusBadge('Pasif')).toBe('rc-rozet--uyari');
  });

  it('document status: only the two known non-draft values pass through', () => {
    expect(documentStatus('Yayinda')).toBe('Yayinda');
    expect(documentStatus('Arsiv')).toBe('Arsiv');
    expect(documentStatus('yayinda')).toBe('Taslak');
  });

  it('numbers: int fields may arrive as strings; invalid → null; KB floored', () => {
    expect(toNumber('12')).toBe(12);
    expect(toNumber(7)).toBe(7);
    expect(toNumber(null)).toBeNull();
    expect(toNumber('x')).toBeNull();
    expect(kilobytes(2047)).toBe(1);
    expect(kilobytes('3072')).toBe(3);
    expect(kilobytes(null)).toBe(0);
  });

  it('logo line: size + KB, unreadable dimensions, no logo', () => {
    const base = { baskiyaUygun: true, uyari: null };
    expect(logoInfo({ ...base, var: true, bayt: 12800, genislik: 640, yukseklik: 200 })).toBe(
      '640×200 px · 12 KB',
    );
    expect(logoInfo({ ...base, var: true, bayt: 900, genislik: null, yukseklik: null })).toBe(
      '900 bayt · ölçü okunamadı',
    );
    expect(
      logoInfo({ ...base, var: false, bayt: null, genislik: null, yukseklik: null }),
    ).toBeNull();
  });

  it('update body: all six fields + surum; blanks become null (full replace)', () => {
    expect(
      updateBody(
        {
          ad: '  Örnek Rent  ',
          yetkiliAd: '',
          eposta: ' a@b.co ',
          telefon: null,
          plan: 'Pro',
          notlar: '   ',
        },
        '638000000000000001',
      ),
    ).toEqual({
      ad: 'Örnek Rent',
      yetkiliAd: null,
      eposta: 'a@b.co',
      telefon: null,
      plan: 'Pro',
      notlar: null,
      surum: '638000000000000001',
    });
  });

  it('document upload: multipart fields; one hedef per target; none = all tenants', () => {
    const file = new File(['%PDF-1.4'], 'kvkk.pdf', { type: 'application/pdf' });
    const form = documentUploadForm({
      baslik: ' KVKK ',
      aciklama: '',
      file,
      onlyManagers: true,
      targets: ['t-1', 't-2'],
    });
    expect(form.get('baslik')).toBe('KVKK');
    expect(form.has('aciklama')).toBe(false);
    expect((form.get('dosya') as File).name).toBe('kvkk.pdf');
    expect(form.get('yalnizYoneticiler')).toBe('true');
    expect(form.getAll('hedef')).toEqual(['t-1', 't-2']);

    const all = documentUploadForm({
      baslik: 'x',
      aciklama: 'not',
      file,
      onlyManagers: false,
      targets: [],
    });
    expect(all.getAll('hedef')).toEqual([]);
    expect(all.get('yalnizYoneticiler')).toBe('false');
    expect(all.get('aciklama')).toBe('not');
  });
});
