import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import type { ApiHatasi } from '@core/api/api-hatasi';
import { TAHSILAT_DENEME_KANALI, TahsilatDenemeKaydi } from '@core/form/tahsilat-denemesi';
import { OturumServisi } from '@core/oturum/oturum-servisi';

import type { LoanDetail } from '../finance-model';
import { InstallmentPayment } from './installment-payment';

const LOAN_ID = 'b1b1b1b1-0000-4000-8000-000000000001';

/** Elle kurulmuş kredi: 12 taksit, 3 ödenmiş → sonraki 4. taksit, 2.500,00 TRY. */
function loan(extra: Partial<Record<string, unknown>> = {}): LoanDetail {
  return {
    id: LOAN_ID,
    doviz: 'TRY',
    taksitSayisi: 12,
    sonrakiTaksit: { sira: 4, vade: '2026-10-14T21:00:00Z', tutar: 2500 },
    yetkiler: { taksitOde: true, iptal: true },
    ...extra,
  } as unknown as LoanDetail;
}

const error = (kod: ApiHatasi['kod'], mevcut?: { tutar: number; ayniIcerik: boolean }): ApiHatasi =>
  ({
    status: kod === 'mukerrer' || kod === 'cakisma' ? 409 : kod === 'dogrulama' ? 400 : 0,
    kod,
    detay: 'x',
    ...(mevcut ? { mevcut: { id: 'g', belgeNo: 'GD-1', doviz: 'TRY', ...mevcut } } : {}),
  }) as ApiHatasi;

function setup(): InstallmentPayment {
  TestBed.configureTestingModule({
    providers: [
      { provide: TAHSILAT_DENEME_KANALI, useValue: null },
      { provide: OturumServisi, useValue: { temizlikKaydet: () => () => undefined } },
    ],
  });
  let n = 0;
  return new InstallmentPayment(TestBed.inject(TahsilatDenemeKaydi), () => `anahtar-${++n}`);
}

describe('taksit ödemesi (para yaşam döngüsü)', () => {
  it('gövde sunucunun sonraki taksidinden: sıra 4, seçilen hesap; tutar gövdeye girmez', () => {
    const p = setup();
    const c = p.prepare(loan(), { hesap: 'Banka', hesapId: 'h-1' });
    expect(c).toEqual({
      loanId: LOAN_ID,
      key: 'anahtar-1',
      body: { sira: 4, hesap: 'Banka', hesapId: 'h-1' },
      amount: 2500,
      currency: 'TRY',
    });
  });

  it('sonucu bilinmeyen hata: kopya DONAR, tekrar AYNI anahtar + AYNI gövde (form değişse de)', () => {
    const p = setup();
    const first = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(first);
    expect(p.failed(first, error('ag'))).toEqual({ kind: 'uncertain' });
    // Kullanıcı hesabı değiştirdi — donmuş kopya yine birebir gider.
    const again = p.prepare(loan(), { hesap: 'Banka', hesapId: 'h-9' })!;
    expect(again.key).toBe('anahtar-1');
    expect(again.body).toEqual({ sira: 4, hesap: 'Kasa', hesapId: null });
  });

  it('kaybolan yanıt sonrası tekrar 409 mukerrer + ayniIcerik → "zaten kaydedildi", otomatik yeniden gönderim yok', () => {
    const p = setup();
    const first = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(first);
    p.failed(first, error('sunucu'));
    const again = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(again);
    expect(p.failed(again, error('mukerrer', { tutar: 2500, ayniIcerik: true }))).toEqual({
      kind: 'duplicate',
      type: 'zatenKaydedildi',
    });
    expect(p.frozen).toBeNull();
    // Sonraki işlem YENİ anahtarla (ikinci meşru ödeme mükerrer sayılmaz).
    expect(p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!.key).toBe('anahtar-2');
  });

  it('kesin red (doğrulama): kopya çözülür ama anahtar KORUNUR — düzeltilmiş gövde aynı işlem', () => {
    const p = setup();
    const first = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(first);
    expect(p.failed(first, error('dogrulama'))).toEqual({ kind: 'rejected' });
    expect(p.frozen).toBeNull();
    const fixed = p.prepare(loan(), { hesap: 'Banka', hesapId: null })!;
    expect(fixed.key).toBe('anahtar-1');
    expect(fixed.body.hesap).toBe('Banka');
  });

  it('409 cakisma (taksit bu arada ödendi): sonuç kesin, yeni anahtar', () => {
    const p = setup();
    const first = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(first);
    expect(p.failed(first, error('cakisma'))).toEqual({ kind: 'stale' });
    expect(p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!.key).toBe('anahtar-2');
  });

  it('2xx: anahtar yenilenir; belirsiz deneme kaydı kapanır', () => {
    const p = setup();
    const first = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(first);
    expect(p.succeeded()).toEqual({ kind: 'paid' });
    expect(p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!.key).toBe('anahtar-2');
  });

  it('başka krediye ait donmuş kopya o krediye gönderilmez', () => {
    const p = setup();
    const first = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(first);
    p.failed(first, error('ag'));
    const other = p.prepare(loan({ id: 'b1b1b1b1-0000-4000-8000-000000000002' }), {
      hesap: 'Kasa',
      hesapId: null,
    })!;
    expect(other.loanId).toBe('b1b1b1b1-0000-4000-8000-000000000002');
  });

  it('ödenecek taksit yoksa ya da sunucu izin vermiyorsa gönderim yok', () => {
    const p = setup();
    expect(p.prepare(loan({ sonrakiTaksit: null }), { hesap: 'Kasa', hesapId: null })).toBeNull();
    expect(
      p.prepare(loan({ yetkiler: { taksitOde: false, iptal: false } }), {
        hesap: 'Kasa',
        hesapId: null,
      }),
    ).toBeNull();
  });

  it('vazgeçiş: kopya ve anahtar bırakılır (yeni deneme yeni anahtarla; sıra kontrolü sunucuda)', () => {
    const p = setup();
    const first = p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!;
    p.started(first);
    p.failed(first, error('ag'));
    p.abandon();
    expect(p.frozen).toBeNull();
    expect(p.prepare(loan(), { hesap: 'Kasa', hesapId: null })!.key).toBe('anahtar-2');
  });
});
