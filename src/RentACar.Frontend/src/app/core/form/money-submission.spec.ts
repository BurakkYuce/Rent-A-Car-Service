import { HttpErrorResponse } from '@angular/common/http';
import {
  EnvironmentInjector,
  type WritableSignal,
  signal,
  createEnvironmentInjector,
  runInInjectionContext,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { TranslocoService } from '@jsverse/transloco';
import { Subject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type IstekSecenekleri } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { MUKERRER_CAGIRAN_GOSTERIR } from '@core/oturum/istek-baglami';
import { OturumServisi } from '@core/oturum/oturum-servisi';

import { PendingMoneyAttempts } from './money-attempts';
import {
  ConfirmGate,
  MoneySubmission,
  type MoneySubmissionConfig,
  mergeUntouched,
} from './money-submission';

/**
 * Para gönderim çekirdeği — bağımsız oracle: her senaryo elle kuruldu (anahtar sırası `k-1, k-2…`, gövdeler sabit);
 * beklenen değerler çekirdeğin kendi hesabından türetilmez.
 */
interface Call {
  readonly method: 'post' | 'put';
  readonly path: string;
  readonly body: unknown;
  readonly options: IstekSecenekleri | undefined;
  readonly reply: Subject<unknown>;
}

const PATH = '/api/ui/v1/finans/tahsilat' as const;
const problem = (status: number, kod: string, extra: object = {}) =>
  apiHatasinaCevir(
    new HttpErrorResponse({ status, error: { status, kod, detail: `${kod} detayı`, ...extra } }),
  );
const networkError = () => apiHatasinaCevir(new HttpErrorResponse({ status: 0 }));

let calls: Call[];
let confirmAnswer: boolean;
let session: WritableSignal<{ anahtar: string } | null>;
let toasts: { tone: string; text: string; title: string | undefined }[];

function record(method: 'post' | 'put') {
  return (path: string, body: unknown, options?: IstekSecenekleri) => {
    const reply = new Subject<unknown>();
    calls.push({ method, path, body, options, reply });
    return reply.asObservable();
  };
}

beforeEach(() => {
  calls = [];
  confirmAnswer = true;
  session = signal<{ anahtar: string } | null>({ anahtar: 'firma|kullanici-a|*' });
  toasts = [];
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiIstemcisi, useValue: { post: record('post'), put: record('put') } },
      { provide: OnayServisi, useValue: { sor: vi.fn(async () => confirmAnswer) } },
      { provide: TranslocoService, useValue: { translate: (k: string) => k } },
      {
        provide: ToastServisi,
        useValue: {
          bilgi: (text: string, o?: { baslik?: string }) =>
            toasts.push({ tone: 'bilgi', text, title: o?.baslik }),
          uyari: (text: string, o?: { baslik?: string }) =>
            toasts.push({ tone: 'uyari', text, title: o?.baslik }),
        },
      },
      {
        provide: OturumServisi,
        useValue: { temizlikKaydet: () => () => undefined, baglam: () => session() },
      },
    ],
  });
});

/** Bir "bileşen": kendi enjektörü (yok edilebilir) + form + çekirdek. */
function mount(config: Partial<MoneySubmissionConfig> = {}) {
  const injector = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
  let n = 0;
  const keys = config.newKey ?? (() => `k-${++n}`);
  const submission = runInInjectionContext(
    injector,
    () => new MoneySubmission<{ tutar: string }>({ ...config, newKey: keys }),
  );
  const form = new FormGroup({
    tutar: new FormControl<string | null>('500.00', Validators.required),
    aciklama: new FormControl<string | null>(null),
  });
  const hooks = {
    success: vi.fn(),
    settled: vi.fn(),
    afterDuplicate: vi.fn(),
    conflict: vi.fn(),
    rejected: vi.fn(),
  };
  const run = (tutar = form.controls.tutar.value ?? '') =>
    submission.run<{ id: string }>({
      form,
      build: () => ({ path: PATH, body: { tutar }, content: { tutar, doviz: 'TRY' } }),
      ...hooks,
    });
  return { injector, submission, form, hooks, run };
}

describe('MoneySubmission — anahtar ve kilit', () => {
  it('işlem başına anahtar; uçuşta form kilitli ve deneme ÖNCEDEN kayıtlı; 2xx sonrası yeni anahtar', async () => {
    const { submission, form, run, hooks } = mount({ scope: () => 'tahsilat:c1' });
    await run();
    expect(calls).toHaveLength(1);
    expect(calls[0]?.options?.islemAnahtari).toBe('k-1');
    expect(calls[0]?.options?.context?.get(MUKERRER_CAGIRAN_GOSTERIR)).toBe(true);
    expect(form.disabled).toBe(true);
    expect(TestBed.inject(PendingMoneyAttempts).get('tahsilat:c1')).toMatchObject({
      key: 'k-1',
      inFlight: true,
      body: { tutar: '500.00' },
    });
    await run(); // çift tık: uçuşta ikinci gönderim yok
    expect(calls).toHaveLength(1);
    calls[0]?.reply.next({ id: 't1' });
    expect(hooks.success).toHaveBeenCalledTimes(1);
    expect(hooks.settled).toHaveBeenCalledWith('done');
    expect(form.disabled).toBe(false);
    expect(TestBed.inject(PendingMoneyAttempts).count()).toBe(0);
    expect(submission.currentKey).toBeNull();
    form.controls.tutar.setValue('20.00');
    await run();
    expect(calls[1]?.options?.islemAnahtari).toBe('k-2');
  });

  it('kesin red (400): önce kilit açılır sonra alan hatası yazılır; anahtar KORUNUR (düzeltilmiş gövde aynı işlem)', async () => {
    const { form, run, hooks } = mount();
    await run();
    calls[0]?.reply.error(problem(400, 'dogrulama', { errors: { tutar: ['Tutar çok büyük.'] } }));
    expect(form.disabled).toBe(false);
    expect(form.controls.tutar.errors).toEqual({ sunucu: ['Tutar çok büyük.'] });
    expect(hooks.rejected).toHaveBeenCalledWith(
      expect.objectContaining({ kod: 'dogrulama' }),
      false,
    );
    form.controls.tutar.setValue('50.00');
    await run();
    expect(calls[1]?.options?.islemAnahtari).toBe('k-1');
    expect(calls[1]?.body).toEqual({ tutar: '50.00' });
  });

  it('cakisma: form SİLİNMEZ, kanca çağrılır, sonraki gönderim YENİ anahtar', async () => {
    const { form, run, hooks } = mount();
    await run();
    calls[0]?.reply.error(problem(409, 'cakisma'));
    expect(form.controls.tutar.value).toBe('500.00');
    expect(hooks.conflict).toHaveBeenCalledTimes(1);
    expect(hooks.settled).toHaveBeenCalledWith('conflict');
    await run();
    expect(calls[1]?.options?.islemAnahtari).toBe('k-2');
  });
});

describe('MoneySubmission — sonucu bilinmeyen deneme ve 409 mukerrer', () => {
  it('ağ hatası: gövde DONAR, form kilitli kalır; tekrar AYNI yol + anahtar + gövdeyle (form değişse de)', async () => {
    const { submission, form, run } = mount({ scope: () => 's1' });
    await run();
    calls[0]?.reply.error(networkError());
    expect(form.disabled).toBe(true);
    expect(submission.frozen()?.key).toBe('k-1');
    expect(submission.notice()?.message).toBe('paraIslemi.sonucBilinmiyor');
    expect(TestBed.inject(PendingMoneyAttempts).get('s1')?.inFlight).toBe(false);
    await run('9999.00'); // build çağrılmaz: donmuş kopya gider
    expect(calls[1]?.path).toBe(PATH);
    expect(calls[1]?.options?.islemAnahtari).toBe('k-1');
    expect(calls[1]?.body).toEqual({ tutar: '500.00' });
  });

  it('tekrar → 409 mevcut + aynı içerik: "zaten kaydedildi" notu, form sıfırlama kancası, YENİ anahtar', async () => {
    const { submission, form, run, hooks } = mount();
    await run();
    calls[0]?.reply.error(networkError());
    await run();
    calls[1]?.reply.error(
      problem(409, 'mukerrer', {
        mevcut: { id: 't1', belgeNo: 'TH-1', tutar: 500, doviz: 'TRY', ayniIcerik: true },
      }),
    );
    expect(submission.notice()).toMatchObject({
      message: 'paraIslemi.oncekiKaydedildi',
      params: { no: 'TH-1', tutar: '500,00 ₺' },
    });
    expect(hooks.afterDuplicate).toHaveBeenCalledWith('recorded');
    expect(hooks.settled).toHaveBeenCalledWith('duplicate');
    expect(submission.frozen()).toBeNull();
    expect(form.disabled).toBe(false);
    await run();
    expect(calls[2]?.options?.islemAnahtari).toBe('k-2');
  });

  it('409 mevcut YOK: "daha önce kaydedildi", anahtar YENİLENMEZ, gövde donar, sıfırlama kancası çağrılmaz', async () => {
    const { submission, form, run, hooks } = mount({ scope: () => 's2' });
    await run();
    calls[0]?.reply.error(problem(409, 'mukerrer'));
    expect(submission.notice()?.message).toBe('paraIslemi.dahaOnceKaydedildi');
    expect(submission.frozen()?.key).toBe('k-1');
    expect(form.disabled).toBe(true);
    expect(hooks.afterDuplicate).not.toHaveBeenCalled();
    expect(TestBed.inject(PendingMoneyAttempts).get('s2')?.notice.message).toBe(
      'paraIslemi.dahaOnceKaydedildi',
    );
    await run();
    expect(calls[1]?.options?.islemAnahtari).toBe('k-1');
  });

  it('bilinçli vazgeçiş onaylıdır: reddedilirse donmuş kalır; onaylanırsa kayıt düşer, form açılır, YENİ anahtar', async () => {
    const { submission, form, run } = mount({ scope: () => 's3' });
    await run();
    calls[0]?.reply.error(networkError());
    confirmAnswer = false;
    expect(await submission.abandon()).toBe(false);
    expect(submission.frozen()).not.toBeNull();
    confirmAnswer = true;
    expect(await submission.abandon()).toBe(true);
    expect(form.disabled).toBe(false);
    expect(TestBed.inject(PendingMoneyAttempts).get('s3')).toBeUndefined();
    await run();
    expect(calls[1]?.options?.islemAnahtari).toBe('k-2');
  });

  it('kesin redden sonra başka kayda (hedef) geçilince eski kaydın anahtarı taşınmaz: YENİ anahtar', async () => {
    const { submission, form } = mount();
    const send = (target: string) =>
      submission.run({
        form,
        build: () => ({ path: PATH, body: { tutar: '1.00' }, target }),
        success: () => undefined,
      });
    await send('mtv:1');
    calls[0]?.reply.error(problem(400, 'dogrulama'));
    await send('mtv:2');
    expect(calls[1]?.options?.islemAnahtari).toBe('k-2');
  });
});

describe('MoneySubmission — bileşen yok olup geri gelince', () => {
  it('uçuştayken yok edilen bileşen isteği İPTAL ETMEZ; belirsiz sonuç kayıtta kalır, yeni bileşen form kilitli + aynı anahtarla açılır', async () => {
    const first = mount({ scope: () => 'ceza-odeme:1' });
    first.form.controls.tutar.setValue('300.00');
    await first.run();
    first.injector.destroy();
    expect(calls[0]?.reply.observed).toBe(true);
    calls[0]?.reply.error(networkError());
    const stored = TestBed.inject(PendingMoneyAttempts).get('ceza-odeme:1');
    expect(stored).toMatchObject({ key: 'k-1', inFlight: false, body: { tutar: '300.00' } });

    const again = mount({ scope: () => 'ceza-odeme:1' });
    again.form.controls.tutar.setValue(null);
    expect(again.submission.restore(again.form)).toBe(true);
    expect(again.form.disabled).toBe(true);
    expect(again.form.controls.tutar.value).toBe('300.00');
    expect(again.submission.notice()?.message).toBe('paraIslemi.sonucBilinmiyor');
    await again.run('1.00');
    expect(calls[1]?.options?.islemAnahtari).toBe('k-1');
    expect(calls[1]?.body).toEqual({ tutar: '300.00' });
  });

  it('yanıt hâlâ beklenirken geri gelinirse "sonucu bilinmiyor" sayılır; yanıt 2xx gelince kayıt düşer', async () => {
    const first = mount({ scope: () => 'gider-odeme:1' });
    await first.run();
    first.injector.destroy();
    const again = mount({ scope: () => 'gider-odeme:1' });
    expect(again.submission.restore(again.form)).toBe(true);
    expect(again.submission.notice()?.message).toBe('paraIslemi.sonucBilinmiyor');
    calls[0]?.reply.next({ id: 'x' });
    expect(TestBed.inject(PendingMoneyAttempts).count()).toBe(0);
  });

  it('kapsamı verilmemiş (geri getirilemeyen) bileşen yok olunca kesinleşmemiş deneme kayıtta asılı kalmaz', async () => {
    const m = mount();
    await m.run();
    calls[0]?.reply.error(networkError());
    expect(TestBed.inject(PendingMoneyAttempts).count()).toBe(1);
    m.injector.destroy();
    expect(TestBed.inject(PendingMoneyAttempts).count()).toBe(0);
  });
});

describe('MoneySubmission — kancalar ve yardımcılar', () => {
  it('açık tutar kancası: mukerrer sonrası boş tutarla istek GİTMEZ, yazıp silmek açmaz; başarılı ödeme kilidi kaldırır', async () => {
    const m = mount({ amountControl: () => m.form.controls.tutar });
    await m.run();
    calls[0]?.reply.error(
      problem(409, 'mukerrer', {
        mevcut: { id: 'o', belgeNo: 'MTV 1', tutar: 400, doviz: 'TRY', ayniIcerik: true },
      }),
    );
    expect(m.submission.amountRequired()).toBe(true);
    m.form.controls.tutar.clearValidators();
    m.form.controls.tutar.setValue('5');
    m.form.controls.tutar.setValue('');
    await m.run();
    expect(calls).toHaveLength(1);
    m.form.controls.tutar.setValue('150.00');
    await m.run();
    calls[1]?.reply.next({ id: 'o2' });
    expect(m.submission.amountRequired()).toBe(false);
  });

  it('onay açıkken ikinci tık yok sayılır (tek onay, tek istek)', async () => {
    const m = mount();
    let answer: (v: boolean) => void = () => undefined;
    const confirm = vi.fn(() => new Promise<boolean>((ok) => (answer = ok)));
    const send = () =>
      m.submission.run({
        form: m.form,
        build: () => ({ path: PATH, body: { tutar: '1.00' } }),
        confirm,
        success: () => undefined,
      });
    const a = send();
    await send();
    answer(true);
    await a;
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(calls).toHaveLength(1);
  });

  it('ConfirmGate: pencere açıkken ikinci soru false; kapanınca yeniden açılır', async () => {
    const gate = new ConfirmGate();
    let answer: (v: boolean) => void = () => undefined;
    const first = gate.ask(() => new Promise<boolean>((ok) => (answer = ok)));
    expect(await gate.ask(() => Promise.resolve(true))).toBe(false);
    answer(true);
    expect(await first).toBe(true);
    expect(await gate.ask(() => Promise.resolve(true))).toBe(true);
  });

  it('mergeUntouched: güncel değer yalnız dokunulmamış alana; kullanıcının yazdığı korunur', () => {
    const form = new FormGroup({
      kur: new FormControl<string | null>('37.25'),
      bitTar: new FormControl<string | null>(null),
    });
    form.controls.kur.markAsDirty();
    mergeUntouched(form, { kur: '36.00', bitTar: '2026-12-31', yok: 'x' });
    expect(form.getRawValue()).toEqual({ kur: '37.25', bitTar: '2026-12-31' });
  });
});

describe('MoneySubmission — bildirim yeri', () => {
  it('toast kipinde kayıtlı deneme bildirimi toast içinde (form notu boş); mevcutsuz 409 notu formda kalır', async () => {
    const m = mount({ duplicateDisplay: 'toast' });
    await m.run();
    calls[0]?.reply.error(
      problem(409, 'mukerrer', {
        mevcut: { id: 'g', belgeNo: 'GD-10', tutar: 2500, doviz: 'TRY', ayniIcerik: true },
      }),
    );
    expect(m.submission.notice()).toBeNull();
    expect(toasts).toEqual([
      {
        tone: 'bilgi',
        text: 'paraIslemi.oncekiKaydedildi mukerrer detayı',
        title: 'paraIslemi.zatenKaydedildi',
      },
    ]);
    await m.run();
    calls[1]?.reply.error(problem(409, 'mukerrer'));
    expect(m.submission.notice()?.message).toBe('paraIslemi.dahaOnceKaydedildi');
    expect(toasts).toHaveLength(1);
  });
});

describe('MoneySubmission — oturum bağlamı (r316 M1)', () => {
  it('çıkış sırasında uçan isteğin belirsiz yanıtı kayda YAZILMAZ; yeni kullanıcı eski kilitli formu görmez', async () => {
    const a = mount({ scope: () => 'cari-virman' });
    a.form.controls.tutar.setValue('300.00');
    await a.run();
    session.set(null); // çıkış
    TestBed.tick();
    calls[0]?.reply.error(networkError()); // yanıt çıkıştan SONRA
    session.set({ anahtar: 'firma|kullanici-b|*' });
    TestBed.tick();
    expect(TestBed.inject(PendingMoneyAttempts).count()).toBe(0);
    expect(a.submission.frozen()).toBeNull();
    expect(a.submission.sending()).toBe(false);
    const b = mount({ scope: () => 'cari-virman' });
    expect(b.submission.restore(b.form)).toBe(false);
    expect(b.form.disabled).toBe(false);
  });

  it('bağlam değişince donmuş deneme, not ve form temizlenir; yeni gönderim YENİ anahtarla', async () => {
    const m = mount({ scope: () => 'depozito' });
    await m.run();
    calls[0]?.reply.error(networkError());
    expect(m.submission.frozen()).not.toBeNull();
    session.set({ anahtar: 'firma|kullanici-b|*' });
    TestBed.tick();
    expect(m.submission.frozen()).toBeNull();
    expect(m.submission.notice()).toBeNull();
    expect(m.form.disabled).toBe(false);
    expect(m.form.controls.tutar.value).toBeNull();
    expect(TestBed.inject(PendingMoneyAttempts).get('depozito')).toBeUndefined();
    m.form.controls.tutar.setValue('10.00');
    await m.run();
    expect(calls[1]?.options?.islemAnahtari).toBe('k-2');
  });

  it('aynı kullanıcı yeniden girişte (bağlam aynı) donmuş deneme KORUNUR', async () => {
    const m = mount({ scope: () => 's' });
    await m.run();
    calls[0]?.reply.error(networkError());
    session.set({ anahtar: 'firma|kullanici-a|*' });
    TestBed.tick();
    expect(m.submission.frozen()?.key).toBe('k-1');
  });

  it('yapısal uç: mevcut bildirimi nötr metinle ("önceki denemeniz" denmez)', async () => {
    const m = mount({ recordedMessage: 'servisSigorta.para.policeZatenOdendi' });
    await m.run();
    calls[0]?.reply.error(
      problem(409, 'mukerrer', {
        mevcut: { id: 'p', belgeNo: 'P-1', tutar: 900, doviz: 'TRY', ayniIcerik: false },
      }),
    );
    expect(m.submission.notice()).toMatchObject({
      title: 'paraIslemi.kayitZatenIslenmis',
      message: 'servisSigorta.para.policeZatenOdendi',
      params: { no: 'P-1', tutar: '900,00 ₺' },
    });
  });
});
