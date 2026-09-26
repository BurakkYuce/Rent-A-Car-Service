import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

import { ApiHatasi, SERVER_ERROR_CODES, toApiError, isServerErrorCode } from './api-hatasi';

/** Backend'in ürettiği biçimde elle yazılmış ProblemDetails örnekleri (UiHata.cs kod tablosu). */
function problem(status: number, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: body,
    url: '/api/ui/v1/deneme',
    headers: new HttpHeaders({ 'Content-Type': 'application/problem+json' }),
  });
}

describe('apiHatasinaCevir', () => {
  it('dogrulama + errors → alanlar dahil tipli hata', () => {
    const error = toApiError(
      problem(400, {
        title: 'Doğrulama hatası',
        status: 400,
        detail: 'Plaka zorunludur.',
        kod: 'dogrulama',
        errors: { Plaka: ['Plaka zorunludur.'] },
      }),
    );

    expect(error).toBeInstanceOf(ApiHatasi);
    expect(error).toBeInstanceOf(Error);
    expect({
      status: error.status,
      kod: error.kod,
      detay: error.detay,
      alanlar: error.alanlar,
    }).toEqual({
      status: 400,
      kod: 'dogrulama',
      detay: 'Plaka zorunludur.',
      alanlar: { Plaka: ['Plaka zorunludur.'] },
    });
    expect(error.message).toBe('Plaka zorunludur.');
  });

  it('409 mukerrer + mevcut (işlem zaten yazıldı) → tipli `mevcut`; biçimsiz mevcut yok sayılır', () => {
    const error = toApiError(
      problem(409, {
        status: 409,
        detail: 'Bu tahsilat zaten kaydedildi (No T-1, 500,00 TRY); yeni tahsilat yazılmadı.',
        kod: 'mukerrer',
        mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 500, doviz: 'TRY', ayniIcerik: true },
      }),
    );
    expect(error.mevcut).toEqual({
      id: 'c1',
      belgeNo: 'T-1',
      tutar: 500,
      doviz: 'TRY',
      ayniIcerik: true,
    });
    // ayniIcerik yok/biçimsiz → güvenli taraf: false (form silinmez, "YAZILMADI" uyarısı).
    const missing = toApiError(
      problem(409, {
        status: 409,
        detail: 'x',
        kod: 'mukerrer',
        mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 500, doviz: 'TRY', ayniIcerik: 'evet' },
      }),
    );
    expect(missing.mevcut?.ayniIcerik).toBe(false);
    const corrupt = toApiError(
      problem(409, { status: 409, detail: 'x', kod: 'mukerrer', mevcut: { id: 1 } }),
    );
    expect(corrupt.mevcut).toBeUndefined();
    // Başka kodda (ör. cakisma) mevcut okunmaz.
    const other = toApiError(
      problem(409, {
        status: 409,
        detail: 'x',
        kod: 'cakisma',
        mevcut: { id: 'c1', belgeNo: 'T-1', tutar: 500, doviz: 'TRY' },
      }),
    );
    expect(other.mevcut).toBeUndefined();
  });

  it('Idempotency-Key başlık hatası da alan olarak gelir', () => {
    const error = toApiError(
      problem(400, {
        title: 'Doğrulama hatası',
        status: 400,
        detail: 'İşlem anahtarı 16–128 görünür ASCII karakter olmalı.',
        kod: 'dogrulama',
        errors: { 'Idempotency-Key': ['İşlem anahtarı 16–128 görünür ASCII karakter olmalı.'] },
      }),
    );
    expect(error.alanlar).toEqual({
      'Idempotency-Key': ['İşlem anahtarı 16–128 görünür ASCII karakter olmalı.'],
    });
  });

  const codeTable: readonly [number, string, string][] = [
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

  it.each(codeTable)('%i %s → kod aynen, alanlar yok', (status, code, detail) => {
    const error = toApiError(problem(status, { title: 'x', status, detail: detail, kod: code }));
    expect(error.status).toBe(status);
    expect(error.kod).toBe(code);
    expect(error.detay).toBe(detail);
    expect(error.alanlar).toBeUndefined();
  });

  it('kod birliği backend kod tablosuyla birebir (dokuz kod)', () => {
    expect([...SERVER_ERROR_CODES].sort()).toEqual(
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
    expect(isServerErrorCode('cakisma')).toBe(true);
    expect(isServerErrorCode('conflict')).toBe(false);
    expect(isServerErrorCode(409)).toBe(false);
  });

  it('gövde JSON metni olarak gelse de ayrıştırılır', () => {
    const error = toApiError(
      problem(
        409,
        '{"title":"Çakışma","status":409,"detail":"Plaka zaten kayıtlı.","kod":"cakisma"}',
      ),
    );
    expect(error.kod).toBe('cakisma');
    expect(error.detay).toBe('Plaka zaten kayıtlı.');
  });

  it('status 0 → ag (yanıt yok)', () => {
    const error = toApiError(
      new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }),
    );
    expect(error.status).toBe(0);
    expect(error.kod).toBe('ag');
    expect(error.detay).toBe('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.');
  });

  it("kod'suz 500 → sunucu; detail varsa o", () => {
    const error = toApiError(
      problem(500, { title: 'Sunucu hatası', status: 500, detail: 'Beklenmeyen bir hata oluştu.' }),
    );
    expect(error.kod).toBe('sunucu');
    expect(error.detay).toBe('Beklenmeyen bir hata oluştu.');
  });

  it('gövdesiz 502 → sunucu + varsayılan metin', () => {
    const error = toApiError(problem(502, null));
    expect(error.kod).toBe('sunucu');
    expect(error.detay).toBe('Beklenmeyen bir sunucu hatası oluştu.');
  });

  it("kod'suz 404 → bilinmeyen, detail yoksa title", () => {
    const error = toApiError(problem(404, { title: 'Not Found', status: 404 }));
    expect(error.kod).toBe('bilinmeyen');
    expect(error.detay).toBe('Not Found');
  });

  it('tanınmayan kod → bilinmeyen (HTTP durumundan kod TAHMİN edilmez)', () => {
    const error = toApiError(problem(409, { status: 409, detail: 'x', kod: 'conflict' }));
    expect(error.kod).toBe('bilinmeyen');
    expect(error.status).toBe(409);
  });

  it('HTML hata sayfası (JSON değil) → bilinmeyen + varsayılan metin', () => {
    const error = toApiError(problem(400, '<html><body>Bad Request</body></html>'));
    expect(error.kod).toBe('bilinmeyen');
    expect(error.detay).toBe('Beklenmeyen bir hata oluştu.');
  });

  it('biçimsiz errors girdileri atılır, geçerliler kalır', () => {
    const error = toApiError(
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
    expect(error.alanlar).toEqual({
      Tutar: ['Tutar pozitif olmalı.'],
      Tarih: ['Tarih geçmişte olamaz.'],
    });
  });

  it('errors dizi ya da tamamen geçersizse alanlar undefined', () => {
    expect(
      toApiError(problem(400, { kod: 'dogrulama', detail: 'x', errors: ['a'] })).alanlar,
    ).toBeUndefined();
    expect(
      toApiError(problem(400, { kod: 'dogrulama', detail: 'x', errors: { A: [1] } })).alanlar,
    ).toBeUndefined();
  });

  it('HTTP dışı istisna → bilinmeyen, neden cause olarak korunur', () => {
    const reason = new TypeError('okunamadı');
    const error = toApiError(reason);
    expect(error.kod).toBe('bilinmeyen');
    expect(error.status).toBe(0);
    expect(error.cause).toBe(reason);
  });

  it('zaten ApiHatasi ise aynı nesne döner', () => {
    const first = toApiError(problem(403, { kod: 'yetki_yok', detail: 'Yetki yok.' }));
    expect(toApiError(first)).toBe(first);
  });
});
