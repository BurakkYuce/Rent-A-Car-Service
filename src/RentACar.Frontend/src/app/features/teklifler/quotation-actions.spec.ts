import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, firstValueFrom, of, throwError } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';

import { QuotationActions } from './quotation-actions';
import {
  validityValidator,
  validityEarliest,
  createQuotationForm,
  quotationBody,
} from './teklif-modeli';

const QUOTATION_ID = '0b0e7c1a-8888-4aaa-8bbb-000000000008';

async function exchangeRate(answer: () => Observable<unknown>, approval = true) {
  const posts: string[] = [];
  const toast = { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() };
  const ask = vi.fn(async () => approval);
  TestBed.configureTestingModule({
    providers: [
      ...provideTranslation(),
      QuotationActions,
      {
        provide: ApiIstemcisi,
        useValue: {
          post: (path: string) => {
            posts.push(path);
            return answer();
          },
        },
      },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: { ask: ask } },
    ],
  });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  return { i: TestBed.inject(QuotationActions), postlar: posts, toast, sor: ask };
}

describe('Teklif işlemleri', () => {
  it('kabul: onaydan sonra TEK istek; başarıda oluşan rezervasyon no bildirilir ve kayıt yenilenir', async () => {
    const { i, postlar, toast } = await exchangeRate(() =>
      of({ rezervasyonId: '0b0e7c1a-7777-4aaa-8bbb-000000000007', rezervasyonNo: 'RZ-000042' }),
    );
    const after = vi.fn();
    await i.kabul({ id: QUOTATION_ID, no: 'TK-000007' }, after);
    expect(postlar).toEqual([`/api/ui/v1/teklifler/${QUOTATION_ID}/kabul`]);
    expect(toast.basari).toHaveBeenCalledWith(
      'TK-000007 kabul edildi: rezervasyon RZ-000042 oluşturuldu.',
    );
    expect(after).toHaveBeenCalledTimes(1);
  });

  it('kabul TEKRARI (409 cakisma): otomatik yeniden gönderim YOK, teklif yeniden yüklenir + bilgi', async () => {
    const { i, postlar, toast } = await exchangeRate(() =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { status: 409, kod: 'cakisma', detail: 'Teklif zaten kabul edilmiş.' },
          }),
      ),
    );
    const after = vi.fn();
    await i.kabul({ id: QUOTATION_ID, no: 'TK-000007' }, after);
    expect(postlar).toHaveLength(1);
    expect(after).toHaveBeenCalledTimes(1);
    expect(toast.bilgi).toHaveBeenCalledWith('TK-000007 bu arada işlenmiş; güncel durum yüklendi.');
    expect(toast.basari).not.toHaveBeenCalled();
    expect(i.inProgress()).toBeNull();
  });

  it('#271 L3: kabul TEKRARI 409 cakisma + mevcut → açılmış rezervasyonun numarası bildirilir, detay bağlantısına yazılır', async () => {
    const resId = '0b0e7c1a-7777-4aaa-8bbb-000000000009';
    const { i, postlar, toast } = await exchangeRate(() =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              status: 409,
              kod: 'cakisma',
              detail: 'Teklif zaten kabul edilmiş.',
              mevcut: { rezervasyonId: resId, rezervasyonNo: 'RZ-000099' },
            },
          }),
      ),
    );
    const after = vi.fn();
    await i.kabul({ id: QUOTATION_ID, no: 'TK-000007' }, after);
    expect(postlar).toHaveLength(1); // otomatik yeniden gönderim yok
    expect(after).toHaveBeenCalledTimes(1);
    expect(toast.bilgi).toHaveBeenCalledTimes(1);
    expect(toast.bilgi).toHaveBeenCalledWith(
      'TK-000007 zaten kabul edilmiş; açılan rezervasyon RZ-000099. İkinci rezervasyon açılmadı.',
    );
    expect(i.acceptExisting()).toEqual({
      teklifId: QUOTATION_ID,
      rezervasyonId: resId,
      rezervasyonNo: 'RZ-000099',
    });
  });

  it('#271 L3: biçimsiz mevcut (no yok) uydurma rezervasyon göstermez — genel bilgi', async () => {
    const { i, toast } = await exchangeRate(() =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { status: 409, kod: 'cakisma', detail: 'x', mevcut: { rezervasyonId: 'r' } },
          }),
      ),
    );
    await i.kabul({ id: QUOTATION_ID, no: 'TK-000007' }, vi.fn());
    expect(toast.bilgi).toHaveBeenCalledWith('TK-000007 bu arada işlenmiş; güncel durum yüklendi.');
    expect(i.acceptExisting()).toBeNull();
  });

  it('reddet: onay reddedilirse istek gitmez', async () => {
    const { i, postlar, sor } = await exchangeRate(() => of({}), false);
    await i.reddet({ id: QUOTATION_ID, no: 'TK-000007' }, vi.fn());
    expect(sor).toHaveBeenCalledWith(expect.objectContaining({ tehlikeli: true }));
    expect(postlar).toEqual([]);
  });
});

describe('Teklif geçerlilik doğrulaması (sunucu: GecerlilikTarihi < BasTar → red)', () => {
  const validate = validityValidator((e) => `en erken ${e}`);
  const alan = (startDate: string, validity: string | null) => {
    const f = createQuotationForm();
    f.controls.gecerlilik.addValidators(validate);
    f.patchValue({ basTar: startDate, gecerlilik: validity });
    return f.controls.gecerlilik;
  };

  it('başlangıç 09:00 İstanbul → aynı gün hata (gece yarısı < başlangıç), ertesi gün geçerli', () => {
    // 2026-10-01 09:00 İstanbul = 06:00Z.
    expect(alan('2026-10-01T06:00:00.000Z', '2026-10-01').errors).toEqual({
      gecerlilikErken: { mesaj: 'en erken 02.10.2026' },
    });
    expect(alan('2026-10-01T06:00:00.000Z', '2026-10-02').errors).toBeNull();
    expect(alan('2026-10-01T06:00:00.000Z', null).errors).toBeNull();
    expect(validityEarliest('2026-10-01T06:00:00.000Z')).toBe('2026-10-02');
  });

  it('başlangıç tam 00:00 İstanbul → aynı gün geçerli (eşitlik kabul)', () => {
    // 2026-10-01 00:00 İstanbul = 2026-09-30 21:00Z.
    expect(alan('2026-09-30T21:00:00.000Z', '2026-10-01').errors).toBeNull();
    expect(validityEarliest('2026-09-30T21:00:00.000Z')).toBe('2026-10-01');
  });
});

describe('Teklif gövdesi', () => {
  it('Blazor alanları; geçerlilik günü İstanbul gece yarısı, günlük ücret metni AYNEN, boş ofis null', () => {
    const form = createQuotationForm();
    form.reset({
      musteri: { id: '0b0e7c1a-2222-4aaa-8bbb-000000000002', etiket: 'Ayşe' },
      arac: { id: '0b0e7c1a-3333-4aaa-8bbb-000000000003', etiket: '34 ABC 123' },
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
      fiyatTuru: 'Otomatik',
      gunlukUcret: '',
      gecerlilik: '2026-10-05',
      cikisOfisi: { id: '0b0e7c1a-9999-4aaa-8bbb-000000000009', etiket: 'Merkez' },
    });
    expect(quotationBody(form.getRawValue())).toEqual({
      musteriId: '0b0e7c1a-2222-4aaa-8bbb-000000000002',
      vehicleId: '0b0e7c1a-3333-4aaa-8bbb-000000000003',
      basTar: '2026-10-01T06:00:00.000Z',
      bitTar: '2026-10-04T06:00:00.000Z',
      gunlukUcret: null,
      fiyatTuru: 'Otomatik',
      cikisOfisi: 'Merkez',
      donusOfisi: null,
      gecerlilikTarihi: '2026-10-04T21:00:00.000Z',
    });
  });
});
