import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup } from '@angular/forms';
import { TranslocoService } from '@jsverse/transloco';
import { Subject, firstValueFrom } from 'rxjs';

import { ApiHatasi } from '@core/api/api-error';
import { SubmitLock } from '@core/form/submit-lock';
import { provideTranslation } from '@core/i18n/translation';

import { formSubmission } from './form-submission';

describe('formSubmission — #417 L1 409 oturum_degisti', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...provideTranslation()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('yazılmadı notu formda; değerler ve işlem anahtarı KORUNUR (tekrar aynı anahtarla, yeni anahtar yok)', () => {
    let n = 0;
    const submission = TestBed.runInInjectionContext(() =>
      formSubmission(new SubmitLock(() => `anahtar-${++n}`)),
    );
    const form = new FormGroup({
      tutar: new FormControl<string | null>('500.00'),
      aciklama: new FormControl<string | null>('Ek hizmet'),
    });
    const keys: string[] = [];
    const responses: Subject<unknown>[] = [];
    const send = () =>
      submission.gonder(form, (key) => {
        keys.push(key);
        const response = new Subject<unknown>();
        responses.push(response);
        return response;
      });

    send();
    responses[0]?.error(
      new ApiHatasi({
        status: 409,
        kod: 'oturum_degisti',
        detay: 'Bu işlem başka bir oturumda başlatılmıştı.',
      }),
    );

    expect(submission.genelHatalar()).toEqual([
      TestBed.inject(TranslocoService).translate('form.oturumUyusmadi'),
    ]);
    expect(submission.genelHatalar()[0]).toContain('YAZMADI');
    expect(form.getRawValue()).toEqual({ tutar: '500.00', aciklama: 'Ek hizmet' });
    expect(submission.kilit.pendingKey).toBe('anahtar-1');

    send(); // aynı kullanıcıya dönüldü: tekrar AYNI anahtarla (çift kayıt yok)
    expect(keys).toEqual(['anahtar-1', 'anahtar-1']);
    expect(submission.genelHatalar()).toEqual([]);
  });
});
