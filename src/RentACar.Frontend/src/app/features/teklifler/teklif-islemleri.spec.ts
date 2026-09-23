import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { type Observable, firstValueFrom, of, throwError } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { provideCeviri } from '@core/i18n/ceviri';

import { TeklifIslemleri } from './teklif-islemleri';
import {
  gecerlilikDogrulayici,
  gecerlilikEnErken,
  teklifFormuOlustur,
  teklifGovdesi,
} from './teklif-modeli';

const TEKLIF_ID = '0b0e7c1a-8888-4aaa-8bbb-000000000008';

async function kur(cevap: () => Observable<unknown>, onay = true) {
  const postlar: string[] = [];
  const toast = { basari: vi.fn(), bilgi: vi.fn(), uyari: vi.fn(), hata: vi.fn() };
  const sor = vi.fn(async () => onay);
  TestBed.configureTestingModule({
    providers: [
      ...provideCeviri(),
      TeklifIslemleri,
      {
        provide: ApiIstemcisi,
        useValue: {
          post: (yol: string) => {
            postlar.push(yol);
            return cevap();
          },
        },
      },
      { provide: ToastServisi, useValue: toast },
      { provide: OnayServisi, useValue: { sor } },
    ],
  });
  await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  return { i: TestBed.inject(TeklifIslemleri), postlar, toast, sor };
}

describe('Teklif işlemleri', () => {
  it('kabul: onaydan sonra TEK istek; başarıda oluşan rezervasyon no bildirilir ve kayıt yenilenir', async () => {
    const { i, postlar, toast } = await kur(() =>
      of({ rezervasyonId: '0b0e7c1a-7777-4aaa-8bbb-000000000007', rezervasyonNo: 'RZ-000042' }),
    );
    const sonra = vi.fn();
    await i.kabul({ id: TEKLIF_ID, no: 'TK-000007' }, sonra);
    expect(postlar).toEqual([`/api/ui/v1/teklifler/${TEKLIF_ID}/kabul`]);
    expect(toast.basari).toHaveBeenCalledWith(
      'TK-000007 kabul edildi: rezervasyon RZ-000042 oluşturuldu.',
    );
    expect(sonra).toHaveBeenCalledTimes(1);
  });

  it('kabul TEKRARI (409 cakisma): otomatik yeniden gönderim YOK, teklif yeniden yüklenir + bilgi', async () => {
    const { i, postlar, toast } = await kur(() =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { status: 409, kod: 'cakisma', detail: 'Teklif zaten kabul edilmiş.' },
          }),
      ),
    );
    const sonra = vi.fn();
    await i.kabul({ id: TEKLIF_ID, no: 'TK-000007' }, sonra);
    expect(postlar).toHaveLength(1);
    expect(sonra).toHaveBeenCalledTimes(1);
    expect(toast.bilgi).toHaveBeenCalledWith('TK-000007 bu arada işlenmiş; güncel durum yüklendi.');
    expect(toast.basari).not.toHaveBeenCalled();
    expect(i.suruyor()).toBeNull();
  });

  it('reddet: onay reddedilirse istek gitmez', async () => {
    const { i, postlar, sor } = await kur(() => of({}), false);
    await i.reddet({ id: TEKLIF_ID, no: 'TK-000007' }, vi.fn());
    expect(sor).toHaveBeenCalledWith(expect.objectContaining({ tehlikeli: true }));
    expect(postlar).toEqual([]);
  });
});

describe('Teklif geçerlilik doğrulaması (sunucu: GecerlilikTarihi < BasTar → red)', () => {
  const dogrula = gecerlilikDogrulayici((e) => `en erken ${e}`);
  const alan = (basTar: string, gecerlilik: string | null) => {
    const f = teklifFormuOlustur();
    f.controls.gecerlilik.addValidators(dogrula);
    f.patchValue({ basTar, gecerlilik });
    return f.controls.gecerlilik;
  };

  it('başlangıç 09:00 İstanbul → aynı gün hata (gece yarısı < başlangıç), ertesi gün geçerli', () => {
    // 2026-10-01 09:00 İstanbul = 06:00Z.
    expect(alan('2026-10-01T06:00:00.000Z', '2026-10-01').errors).toEqual({
      gecerlilikErken: { mesaj: 'en erken 02.10.2026' },
    });
    expect(alan('2026-10-01T06:00:00.000Z', '2026-10-02').errors).toBeNull();
    expect(alan('2026-10-01T06:00:00.000Z', null).errors).toBeNull();
    expect(gecerlilikEnErken('2026-10-01T06:00:00.000Z')).toBe('2026-10-02');
  });

  it('başlangıç tam 00:00 İstanbul → aynı gün geçerli (eşitlik kabul)', () => {
    // 2026-10-01 00:00 İstanbul = 2026-09-30 21:00Z.
    expect(alan('2026-09-30T21:00:00.000Z', '2026-10-01').errors).toBeNull();
    expect(gecerlilikEnErken('2026-09-30T21:00:00.000Z')).toBe('2026-10-01');
  });
});

describe('Teklif gövdesi', () => {
  it('Blazor alanları; geçerlilik günü İstanbul gece yarısı, günlük ücret metni AYNEN, boş ofis null', () => {
    const form = teklifFormuOlustur();
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
    expect(teklifGovdesi(form.getRawValue())).toEqual({
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
