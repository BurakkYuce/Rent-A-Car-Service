import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

import { ApiHatasi, SUNUCU_HATA_KODLARI, apiHatasinaCevir, sunucuHataKoduMu } from './api-hatasi';

/** Backend'in ürettiği biçimde elle yazılmış ProblemDetails örnekleri (UiHata.cs kod tablosu). */
function problem(status: number, govde: unknown): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: govde,
    url: '/api/ui/v1/deneme',
    headers: new HttpHeaders({ 'Content-Type': 'application/problem+json' }),
  });
}

describe('apiHatasinaCevir', () => {
  it('dogrulama + errors → alanlar dahil tipli hata', () => {
    const hata = apiHatasinaCevir(
      problem(400, {
        title: 'Doğrulama hatası',
        status: 400,
        detail: 'Plaka zorunludur.',
        kod: 'dogrulama',
        errors: { Plaka: ['Plaka zorunludur.'] },
      }),
    );

    expect(hata).toBeInstanceOf(ApiHatasi);
    expect(hata).toBeInstanceOf(Error);
    expect({
      status: hata.status,
      kod: hata.kod,
      detay: hata.detay,
      alanlar: hata.alanlar,
    }).toEqual({
      status: 400,
      kod: 'dogrulama',
      detay: 'Plaka zorunludur.',
      alanlar: { Plaka: ['Plaka zorunludur.'] },
    });
    expect(hata.message).toBe('Plaka zorunludur.');
  });

  it('409 mukerrer + mevcut (işlem zaten yazıldı) → tipli `mevcut`; biçimsiz mevcut yok sayılır', () => {
    const hata = apiHatasinaCevir(
      problem(409, {
        status: 409,
        detail: 'Bu tahsilat zaten kaydedildi (No T-1, 500,00 TRY); yeni tahsilat yazılmadı.',
        kod: 'mukerrer',
        mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 500, doviz: 'TRY' },
      }),
    );
    expect(hata.mevcut).toEqual({ id: 'c1', belgeNo: 'T-1', tutar: 500, doviz: 'TRY' });
    const bozuk = apiHatasinaCevir(
      problem(409, { status: 409, detail: 'x', kod: 'mukerrer', mevcut: { id: 1 } }),
    );
    expect(bozuk.mevcut).toBeUndefined();
    // Başka kodda (ör. cakisma) mevcut okunmaz.
    const baska = apiHatasinaCevir(
      problem(409, {
        status: 409,
        detail: 'x',
        kod: 'cakisma',
        mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 500, doviz: 'TRY' },
      }),
    );
    expect(baska.mevcut).toBeUndefined();
  });

  it('Idempotency-Key başlık hatası da alan olarak gelir', () => {
    const hata = apiHatasinaCevir(
      problem(400, {
        title: 'Doğrulama hatası',
        status: 400,
        detail: 'İşlem anahtarı 16–128 görünür ASCII karakter olmalı.',
        kod: 'dogrulama',
        errors: { 'Idempotency-Key': ['İşlem anahtarı 16–128 görünür ASCII karakter olmalı.'] },
      }),
    );
    expect(hata.alanlar).toEqual({
      'Idempotency-Key': ['İşlem anahtarı 16–128 görünür ASCII karakter olmalı.'],
    });
  });

  const kodTablosu: readonly [number, string, string][] = [
    [403, 'yetki_yok', 'Bu işlem için yetkiniz yok.'],
    [403, 'pilot_degil', 'Yeni arayüz bu firmada açık değil.'],
    [409, 'cakisma', 'Araç bu tarihlerde müsait değil.'],
    [
      409,
      'mukerrer',
      'Bu işlem anahtarı farklı içerikle zaten kullanılmış; işlem daha önce kaydedilmiş olabilir. Yeniden göndermeden önce kayıtları kontrol edin.',
    ],
    [401, 'oturum_yok', 'Oturum yok.'],
    [401, 'kiraci_kapali', 'Firma hesabı kapalı.'],
    [429, 'cok_istek', 'Çok fazla istek.'],
    [400, 'xsrf_gecersiz', 'Güvenlik belirteci (X-XSRF-TOKEN) eksik.'],
  ];

  it.each(kodTablosu)('%i %s → kod aynen, alanlar yok', (status, kod, detay) => {
    const hata = apiHatasinaCevir(problem(status, { title: 'x', status, detail: detay, kod }));
    expect(hata.status).toBe(status);
    expect(hata.kod).toBe(kod);
    expect(hata.detay).toBe(detay);
    expect(hata.alanlar).toBeUndefined();
  });

  it('kod birliği backend kod tablosuyla birebir (dokuz kod)', () => {
    expect([...SUNUCU_HATA_KODLARI].sort()).toEqual(
      [
        'cakisma',
        'cok_istek',
        'dogrulama',
        'kiraci_kapali',
        'mukerrer',
        'oturum_yok',
        'pilot_degil',
        'xsrf_gecersiz',
        'yetki_yok',
      ].sort(),
    );
    expect(sunucuHataKoduMu('cakisma')).toBe(true);
    expect(sunucuHataKoduMu('conflict')).toBe(false);
    expect(sunucuHataKoduMu(409)).toBe(false);
  });

  it('gövde JSON metni olarak gelse de ayrıştırılır', () => {
    const hata = apiHatasinaCevir(
      problem(
        409,
        '{"title":"Çakışma","status":409,"detail":"Plaka zaten kayıtlı.","kod":"cakisma"}',
      ),
    );
    expect(hata.kod).toBe('cakisma');
    expect(hata.detay).toBe('Plaka zaten kayıtlı.');
  });

  it('status 0 → ag (yanıt yok)', () => {
    const hata = apiHatasinaCevir(
      new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }),
    );
    expect(hata.status).toBe(0);
    expect(hata.kod).toBe('ag');
    expect(hata.detay).toBe('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.');
  });

  it("kod'suz 500 → sunucu; detail varsa o", () => {
    const hata = apiHatasinaCevir(
      problem(500, { title: 'Sunucu hatası', status: 500, detail: 'Beklenmeyen bir hata oluştu.' }),
    );
    expect(hata.kod).toBe('sunucu');
    expect(hata.detay).toBe('Beklenmeyen bir hata oluştu.');
  });

  it('gövdesiz 502 → sunucu + varsayılan metin', () => {
    const hata = apiHatasinaCevir(problem(502, null));
    expect(hata.kod).toBe('sunucu');
    expect(hata.detay).toBe('Beklenmeyen bir sunucu hatası oluştu.');
  });

  it("kod'suz 404 → bilinmeyen, detail yoksa title", () => {
    const hata = apiHatasinaCevir(problem(404, { title: 'Not Found', status: 404 }));
    expect(hata.kod).toBe('bilinmeyen');
    expect(hata.detay).toBe('Not Found');
  });

  it('tanınmayan kod → bilinmeyen (HTTP durumundan kod TAHMİN edilmez)', () => {
    const hata = apiHatasinaCevir(problem(409, { status: 409, detail: 'x', kod: 'conflict' }));
    expect(hata.kod).toBe('bilinmeyen');
    expect(hata.status).toBe(409);
  });

  it('HTML hata sayfası (JSON değil) → bilinmeyen + varsayılan metin', () => {
    const hata = apiHatasinaCevir(problem(400, '<html><body>Bad Request</body></html>'));
    expect(hata.kod).toBe('bilinmeyen');
    expect(hata.detay).toBe('Beklenmeyen bir hata oluştu.');
  });

  it('biçimsiz errors girdileri atılır, geçerliler kalır', () => {
    const hata = apiHatasinaCevir(
      problem(400, {
        status: 400,
        detail: 'Hatalı alanlar var.',
        kod: 'dogrulama',
        errors: {
          Tutar: ['Tutar pozitif olmalı.', 42, '', null],
          Tarih: 'Tarih geçmişte olamaz.',
          Bos: [],
          Sayi: 7,
        },
      }),
    );
    expect(hata.alanlar).toEqual({
      Tutar: ['Tutar pozitif olmalı.'],
      Tarih: ['Tarih geçmişte olamaz.'],
    });
  });

  it('errors dizi ya da tamamen geçersizse alanlar undefined', () => {
    expect(
      apiHatasinaCevir(problem(400, { kod: 'dogrulama', detail: 'x', errors: ['a'] })).alanlar,
    ).toBeUndefined();
    expect(
      apiHatasinaCevir(problem(400, { kod: 'dogrulama', detail: 'x', errors: { A: [1] } })).alanlar,
    ).toBeUndefined();
  });

  it('HTTP dışı istisna → bilinmeyen, neden cause olarak korunur', () => {
    const neden = new TypeError('okunamadı');
    const hata = apiHatasinaCevir(neden);
    expect(hata.kod).toBe('bilinmeyen');
    expect(hata.status).toBe(0);
    expect(hata.cause).toBe(neden);
  });

  it('zaten ApiHatasi ise aynı nesne döner', () => {
    const ilk = apiHatasinaCevir(problem(403, { kod: 'yetki_yok', detail: 'Yetki yok.' }));
    expect(apiHatasinaCevir(ilk)).toBe(ilk);
  });
});
