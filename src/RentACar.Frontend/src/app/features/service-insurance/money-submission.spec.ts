import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import type { ApiHatasi } from '@core/api/api-hatasi';
import { TAHSILAT_DENEME_KANALI, TahsilatDenemeKaydi } from '@core/form/tahsilat-denemesi';
import { OturumServisi } from '@core/oturum/oturum-servisi';

import { MoneySubmission, duplicateNotice, lineNetAmount, round2 } from './money-submission';

interface Body {
  readonly tutar: string | null;
}

const error = (kod: ApiHatasi['kod'], mevcut?: { tutar: number; ayniIcerik: boolean }): ApiHatasi =>
  ({
    status: kod === 'mukerrer' || kod === 'cakisma' ? 409 : kod === 'dogrulama' ? 400 : 0,
    kod,
    detay: 'Sunucu mesajı.',
    ...(mevcut ? { mevcut: { id: 'o', belgeNo: 'MTV 2026-1 #1', doviz: 'TRY', ...mevcut } } : {}),
  }) as ApiHatasi;

function setup(): MoneySubmission<Body> {
  TestBed.configureTestingModule({
    providers: [
      { provide: TAHSILAT_DENEME_KANALI, useValue: null },
      { provide: OturumServisi, useValue: { temizlikKaydet: () => () => undefined } },
    ],
  });
  let n = 0;
  return new MoneySubmission<Body>(TestBed.inject(TahsilatDenemeKaydi), () => `anahtar-${++n}`);
}

const content = (tutar: string) => ({ tutar, doviz: 'TRY', hesap: 'Kasa' });

describe('para gönderimi (F9 ödeme / kalem / yansıtma)', () => {
  it('bir işlem = bir anahtar; 2xx sonrası yeni anahtar', () => {
    const m = setup();
    const a = m.prepare('mtv:1', { tutar: '500.00' }, content('500.00'));
    expect(a.key).toBe('anahtar-1');
    m.started(a);
    expect(m.sending()).toBe(true);
    expect(m.succeeded()).toEqual({ kind: 'done' });
    expect(m.sending()).toBe(false);
    expect(m.prepare('mtv:1', { tutar: '100.00' }, content('100.00')).key).toBe('anahtar-2');
  });

  it('sonucu bilinmeyen hata: kopya DONAR — tekrar AYNI anahtar + AYNI gövde (form değişse de)', () => {
    const m = setup();
    const first = m.prepare('mtv:1', { tutar: '500.00' }, content('500.00'));
    m.started(first);
    expect(m.failed(first, error('ag'))).toEqual({ kind: 'uncertain' });
    const retry = m.prepare('mtv:1', { tutar: '600.00' }, content('600.00'));
    expect(retry).toBe(first);
    expect(retry.body).toEqual({ tutar: '500.00' });
    expect(m.frozen()).toBe(first);
  });

  it('kesin red (400): kopya çözülür, anahtar KORUNUR — düzeltilmiş gövde aynı işlemle gider', () => {
    const m = setup();
    const first = m.prepare('mtv:1', { tutar: '10.555' }, content('10.555'));
    m.started(first);
    expect(m.failed(first, error('dogrulama'))).toEqual({ kind: 'rejected' });
    expect(m.frozen()).toBeNull();
    const fixed = m.prepare('mtv:1', { tutar: '10.55' }, content('10.55'));
    expect(fixed.key).toBe('anahtar-1');
    expect(fixed.body).toEqual({ tutar: '10.55' });
  });

  it('başka kayda geçince anahtar yenilenir (anahtar kayda bağlı)', () => {
    const m = setup();
    const a = m.prepare('mtv:1', { tutar: '1.00' }, content('1.00'));
    const b = m.prepare('mtv:2', { tutar: '1.00' }, content('1.00'));
    expect(a.key).toBe('anahtar-1');
    expect(b.key).toBe('anahtar-2');
  });

  it('kaybolan yanıttan sonra tekrar → 409 mukerrer + ayniIcerik: "zaten kaydedildi", otomatik gönderim yok', () => {
    const m = setup();
    const first = m.prepare('mtv:1', { tutar: '500.00' }, content('500.00'));
    m.started(first);
    m.failed(first, error('sunucu'));
    const retry = m.prepare('mtv:1', { tutar: '500.00' }, content('500.00'));
    m.started(retry);
    const outcome = m.failed(retry, error('mukerrer', { tutar: 500, ayniIcerik: true }));
    expect(outcome).toEqual({ kind: 'duplicate', type: 'zatenKaydedildi' });
    expect(m.frozen()).toBeNull();
    expect(m.prepare('mtv:1', { tutar: null }, content('0')).key).toBe('anahtar-2');
  });

  it('donmuş denemeden bilinçli vazgeçiş: kopya bırakılır, yeni işlem YENİ anahtarla (kalan kontrolü sunucuda)', () => {
    const m = setup();
    const first = m.prepare('mtv:1', { tutar: '500.00' }, content('500.00'));
    m.started(first);
    m.failed(first, error('ag'));
    m.abandon();
    expect(m.frozen()).toBeNull();
    const second = m.prepare('mtv:1', { tutar: '600.00' }, content('600.00'));
    expect(second.key).toBe('anahtar-2');
    expect(second.body).toEqual({ tutar: '600.00' });
  });

  it('409 cakisma: "stale", anahtar yenilenir (form korunur — bileşen formu silmez)', () => {
    const m = setup();
    const first = m.prepare('muayene:1', { tutar: '100.00' }, content('100.00'));
    m.started(first);
    expect(m.failed(first, error('cakisma'))).toEqual({ kind: 'stale' });
    expect(m.prepare('muayene:1', { tutar: '100.00' }, content('100.00')).key).toBe('anahtar-2');
  });
});

describe('mükerrer bildirimi', () => {
  const t = (k: string, p?: Record<string, unknown>) => `${k}${p ? JSON.stringify(p) : ''}`;

  it('zaten kaydedildi: bilgi tonu, tutar temizlenmez; hiçbir sınıf ikinci ödemeye yönlendirmez', () => {
    const n = duplicateNotice(
      'zatenKaydedildi',
      error('mukerrer', { tutar: 500, ayniIcerik: true }),
      null,
      t,
    );
    expect(n).toMatchObject({ tone: 'bilgi', clearAmount: false });
    expect(n.message).toContain('servisSigorta.para.yenilendi');
  });

  it('önceki deneme kaydedilmiş: uyarı, tutar temizlenir, kayıtlı ve girilen tutar metinde', () => {
    const n = duplicateNotice(
      'oncekiDenemeKaydedilmis',
      error('mukerrer', { tutar: 500, ayniIcerik: false }),
      { anahtar: 'a', icerik: content('600.00'), onceki: [] },
      t,
    );
    expect(n).toMatchObject({ tone: 'uyari', clearAmount: true });
    expect(n.message).toContain('500,00');
    expect(n.message).toContain('600,00');
  });

  it('bayat anahtar: uyarı, tutar temizlenir', () => {
    expect(duplicateNotice('bayatAnahtar', error('mukerrer'), null, t)).toMatchObject({
      tone: 'uyari',
      clearAmount: true,
    });
  });
});

describe('servis kalemi net tutarı (mükerrer sınıflandırması — inceleme M3)', () => {
  it('elle kurulmuş: 100 × 2 − 0 = 200; 12,345 × 3 → 37,04 − 1,005 → 1,01 = 36,03; açık tutar önceliklidir', () => {
    expect(lineNetAmount({ tutar: null, birimFiyat: '100.00', miktar: 2, indirim: null })).toBe(
      200,
    );
    expect(lineNetAmount({ tutar: null, birimFiyat: 12.345, miktar: 3, indirim: '1.005' })).toBe(
      36.03,
    );
    expect(lineNetAmount({ tutar: '150.00', birimFiyat: '100.00', miktar: 2, indirim: null })).toBe(
      150,
    );
    expect(lineNetAmount({ tutar: null, birimFiyat: '99.99', miktar: null, indirim: null })).toBe(
      99.99,
    );
    expect(lineNetAmount({ tutar: null, birimFiyat: null, miktar: 2, indirim: null })).toBeNull();
  });

  it('yarım kuruş sıfırdan uzağa (kayan nokta tuzağı: 1,005 → 1,01; −2,675 → −2,68)', () => {
    expect(round2(1.005)).toBe(1.01);
    expect(round2(-2.675)).toBe(-2.68);
    expect(round2(0)).toBe(0);
  });

  it('kendi yazılmış denemesi: birim fiyat 100 × 2 (net 200) sonucu bilinmeden, sonra miktar 3 → mevcut 200 = "önceki deneme kaydedilmiş"', () => {
    const m = setup();
    const first = { tutar: null, birimFiyat: '100.00', miktar: 2, indirim: null };
    const a = m.prepare(
      'kalem:1',
      { tutar: null },
      { tutar: lineNetAmount(first), doviz: 'TRY', hesap: 'Yağ' },
    );
    m.started(a);
    m.failed(a, error('ag'));
    m.abandon(); // (429 kesin reddi gibi) kopya çözüldü; belirsiz deneme kayıtta kalır, aynı anahtar sürer
    const b = {
      ...a,
      content: { tutar: lineNetAmount({ ...first, miktar: 3 }), doviz: 'TRY', hesap: 'Yağ' },
    };
    m.started(b);
    const outcome = m.failed(b, error('mukerrer', { tutar: 200, ayniIcerik: false }));
    expect(outcome).toEqual({ kind: 'duplicate', type: 'oncekiDenemeKaydedilmis' });
    const n = duplicateNotice(
      'oncekiDenemeKaydedilmis',
      error('mukerrer', { tutar: 200, ayniIcerik: false }),
      m.lastSubmission,
      (k, p) => `${k}${JSON.stringify(p ?? {})}`,
    );
    expect(n.clearAmount).toBe(true);
    expect(n.message).toContain('300,00'); // "girilen" = satır toplamı, birim fiyat değil
  });

  it('eski davranışın tuzağı: birim fiyatla (100) karşılaştırmak kendi denemesini "başka işlem" sayardı', () => {
    const m = setup();
    const a = m.prepare(
      'kalem:1',
      { tutar: null },
      { tutar: '100.00', doviz: 'TRY', hesap: 'Yağ' },
    );
    m.started(a);
    m.failed(a, error('ag'));
    m.abandon();
    const b = { ...a, content: { tutar: '100.00', doviz: 'TRY', hesap: 'Yağ' } };
    m.started(b);
    expect(m.failed(b, error('mukerrer', { tutar: 200, ayniIcerik: false }))).toEqual({
      kind: 'duplicate',
      type: 'baskaIslemDenemeYazilmadi',
    });
  });
});
