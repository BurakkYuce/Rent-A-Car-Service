import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';

import type { ApiHatasi } from '@core/api/api-hatasi';
import { TAHSILAT_DENEME_KANALI, TahsilatDenemeKaydi } from '@core/form/tahsilat-denemesi';
import { OturumServisi } from '@core/oturum/oturum-servisi';

import { rowErrorMap } from './finance-model';
import { ConfirmGate } from './finance-shared';
import { duplicateNotice } from './money-action';
import { MoneyOperation } from './money-operation';

/** Çeviri: anahtarın kendisi (metin kontrolü `finans.json`'daki gerçek metinle e2e'de). */
const T = ((k: string) => k) as Parameters<typeof duplicateNotice>[3];

const PATH = '/api/ui/v1/finans/kasa/virman' as const;
const CONTENT = { tutar: '500.00', doviz: 'TRY', hesap: 'Kasa' };

const error = (kod: ApiHatasi['kod'], mevcut?: { tutar: number; ayniIcerik: boolean }): ApiHatasi =>
  ({
    status: kod === 'mukerrer' || kod === 'cakisma' ? 409 : kod === 'dogrulama' ? 400 : 0,
    kod,
    detay: 'x',
    ...(mevcut ? { mevcut: { id: 'k', belgeNo: 'TH-1', doviz: 'TRY', ...mevcut } } : {}),
  }) as ApiHatasi;

function setup(): MoneyOperation<{ tutar: string }> {
  TestBed.configureTestingModule({
    providers: [
      { provide: TAHSILAT_DENEME_KANALI, useValue: null },
      { provide: OturumServisi, useValue: { temizlikKaydet: () => () => undefined } },
    ],
  });
  let n = 0;
  return new MoneyOperation(TestBed.inject(TahsilatDenemeKaydi), () => `anahtar-${++n}`);
}

describe('MoneyOperation (finans para yaşam döngüsü)', () => {
  it('ilk gönderim işlem anahtarı alır; 2xx sonrası sıradaki işlem YENİ anahtar', () => {
    const op = setup();
    const a = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    expect(a.key).toBe('anahtar-1');
    op.started(a);
    expect(op.succeeded()).toEqual({ kind: 'done' });
    expect(op.prepare(PATH, { tutar: '700.00' }, CONTENT).key).toBe('anahtar-2');
  });

  it('kayıp yanıt: kopya DONAR; tekrar AYNI yol + anahtar + gövde (form değişse de)', () => {
    const op = setup();
    const first = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    op.started(first);
    expect(op.failed(first, error('ag'))).toEqual({ kind: 'uncertain' });
    const again = op.prepare('/api/ui/v1/finans/odeme', { tutar: '9999.00' }, CONTENT);
    expect(again).toBe(first);
    expect(again.body).toEqual({ tutar: '500.00' });
    expect(again.key).toBe('anahtar-1');
  });

  it('kayıp yanıt sonrası tekrar 409 mukerrer + ayniIcerik → zatenKaydedildi; yeni anahtar, kopya çözülür', () => {
    const op = setup();
    const first = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    op.started(first);
    op.failed(first, error('sunucu'));
    const retry = op.prepare(PATH, { tutar: '1.00' }, CONTENT);
    op.started(retry);
    expect(op.failed(retry, error('mukerrer', { tutar: 500, ayniIcerik: true }))).toEqual({
      kind: 'duplicate',
      type: 'zatenKaydedildi',
    });
    expect(op.frozen).toBeNull();
    expect(op.prepare(PATH, { tutar: '1.00' }, CONTENT).key).toBe('anahtar-2');
  });

  it('kesin red (400): yazılmadı → kopya çözülür, anahtar KORUNUR (düzeltilmiş gövde aynı işlem)', () => {
    const op = setup();
    const first = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    op.started(first);
    expect(op.failed(first, error('dogrulama'))).toEqual({ kind: 'rejected' });
    expect(op.frozen).toBeNull();
    const fixed = op.prepare(PATH, { tutar: '450.00' }, CONTENT);
    expect(fixed.key).toBe('anahtar-1');
    expect(fixed.body).toEqual({ tutar: '450.00' });
  });

  it('bilinçli vazgeçiş: yeni anahtar; sonraki kesin red o anahtarı korur', () => {
    const op = setup();
    const first = op.prepare(PATH, { tutar: '500.00' }, { ...CONTENT, tutar: '500.00' });
    op.started(first);
    op.failed(first, error('ag'));
    op.abandon(); // bilinçli vazgeçiş → yeni anahtar; belirsiz iz kayıtta kalır
    const second = op.prepare(PATH, { tutar: '600.00' }, { ...CONTENT, tutar: '600.00' });
    expect(second.key).toBe('anahtar-2');
    op.started(second);
    op.failed(second, error('dogrulama'));
    const third = op.prepare(PATH, { tutar: '600.00' }, { ...CONTENT, tutar: '600.00' });
    expect(third.key).toBe('anahtar-2');
  });

  it('409 cakisma: stale, yeni anahtar; otomatik tekrar yok', () => {
    const op = setup();
    const first = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    op.started(first);
    expect(op.failed(first, error('cakisma'))).toEqual({ kind: 'stale' });
    expect(op.frozen).toBeNull();
    expect(op.prepare(PATH, { tutar: '500.00' }, CONTENT).key).toBe('anahtar-2');
  });

  it('aynı anahtarla belirsiz deneme 500 kayıtlı; mevcut 500 ama ayniIcerik=false → oncekiDenemeKaydedilmis', () => {
    const op = setup();
    const first = op.prepare(PATH, { tutar: '500.00' }, { ...CONTENT, tutar: '500.00' });
    op.started(first);
    op.failed(first, error('ag'));
    const retry = op.prepare(PATH, { tutar: '500.00' }, { ...CONTENT, tutar: '500.00' });
    op.started(retry);
    const out = op.failed(retry, error('mukerrer', { tutar: 500, ayniIcerik: false }));
    expect(out).toEqual({ kind: 'duplicate', type: 'oncekiDenemeKaydedilmis' });
  });

  // r299 HIGH-1: tahsilat/ödeme/toplu gider tekrarı ve FarkliIcerik 409'u `mevcut`SUZ döner — kayıt VARDIR.
  it('kayıp yanıt → tekrar → mevcut YOK 409: dahaOnceKaydedildi; metin "yazılmadı/değişti" DEMEZ', () => {
    const op = setup();
    const first = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    op.started(first);
    op.failed(first, error('ag'));
    const retry = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    op.started(retry);
    const err = error('mukerrer');
    const out = op.failed(retry, err);
    expect(out).toEqual({ kind: 'duplicate', type: 'dahaOnceKaydedildi' });
    const n = duplicateNotice('dahaOnceKaydedildi', err, CONTENT, T);
    expect(n).toEqual({
      tone: 'bilgi',
      title: 'finans.islem.dahaOnceKaydedildiBaslik',
      message: 'finans.islem.dahaOnceKaydedildi',
    });
  });

  it('belirsiz deneme yokken mevcut farklı içerikli 409 (FarkliIcerik mevcut ile) de dahaOnceKaydedildi', () => {
    const op = setup();
    const a = op.prepare(PATH, { tutar: '500.00' }, CONTENT);
    op.started(a);
    expect(op.failed(a, error('mukerrer', { tutar: 700, ayniIcerik: false }))).toEqual({
      kind: 'duplicate',
      type: 'dahaOnceKaydedildi',
    });
  });
});

describe('r299 düzeltme yardımcıları', () => {
  it('satır hataları GÖNDERİLEN satırın kimliğine göre güncel sıraya eşlenir; silinen satır eşlenmez', () => {
    // Gönderilen: [A, B]; ekranda (kilitten önceki eski davranışla) [B, C] olsaydı: B hatası 0. satıra gider.
    expect(rowErrorMap(['A', 'B'], ['B', 'C'], { tutar: 'tutar', cariId: 'cari' })).toEqual({
      'satirlar[1].tutar': 'satirlar.0.tutar',
      'satirlar[1].cariId': 'satirlar.0.cari',
    });
  });

  it('onay kapısı: pencere açıkken ikinci soru SORULMAZ (false)', async () => {
    const gate = new ConfirmGate();
    let resolve: (v: boolean) => void = () => undefined;
    let asked = 0;
    const first = gate.ask(() => {
      asked++;
      return new Promise<boolean>((r) => (resolve = r));
    });
    expect(await gate.ask(() => Promise.resolve(true))).toBe(false);
    resolve(true);
    expect(await first).toBe(true);
    expect(asked).toBe(1);
    expect(await gate.ask(() => Promise.resolve(true))).toBe(true); // kapı yeniden açılır
  });
});
