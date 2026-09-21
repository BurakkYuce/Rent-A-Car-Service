import { inject, Injectable, Injector } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { OnayIstemi } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';

/** Onay diyaloğu içeriği. Etiketler verilmezse "Onayla" / "Vazgeç". */
export interface OnaySecenekleri {
  readonly baslik: string;
  readonly mesaj: string;
  readonly onayEtiketi?: string;
  readonly iptalEtiketi?: string;
  /** Geri alınamaz işlem (silme, iptal): onay düğmesi tehlike görünümünde, ilk odak "Vazgeç"te. */
  readonly tehlikeli?: boolean;
}

/**
 * CDK onay diyaloğu (Revlo `ModalHelperService.confirm` karşılığı): odak diyaloğa kilitlenir, Esc ve
 * perde tıklaması "vazgeç" sayılır, kapanınca odak açan düğmeye döner. Sonuç yalnız açık "Onayla"
 * tıklamasında `true`. Diyalog ve CDK tembel yüklenir.
 *
 * ```ts
 * if (await inject(OnayServisi).sor({ baslik: 'Kira iptal edilsin mi?', mesaj: '…', tehlikeli: true })) { … }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class OnayServisi {
  private readonly enjektor = inject(Injector);

  async sor(secenek: OnaySecenekleri): Promise<boolean> {
    const [{ Dialog }, { OnayDiyalogu }] = await Promise.all([
      import('@angular/cdk/dialog'),
      import('@shared/onay-diyalogu/onay-diyalogu'),
    ]);
    const ref = this.enjektor.get(Dialog).open<boolean, OnaySecenekleri>(OnayDiyalogu, {
      data: secenek,
      role: 'alertdialog',
      ariaLabelledBy: 'rc-onay-baslik',
      ariaDescribedBy: 'rc-onay-mesaj',
      ariaModal: true,
      panelClass: 'rc-diyalog-paneli',
      backdropClass: 'rc-diyalog-perdesi',
      autoFocus: secenek.tehlikeli ? '.rc-onay-iptal' : '.rc-onay-onayla',
      restoreFocus: true,
    });
    return (await firstValueFrom(ref.closed)) === true;
  }
}

/**
 * F3.6 `ONAY_ISTEMI` (kaydedilmemiş değişiklik sorusu) için CDK diyaloğu: tarayıcı `confirm`'ü yerine
 * erişilebilir, temalı onay. Varsayılan odak "Sayfada kal" (veri kaybı geri alınamaz).
 * `{ provide: ONAY_ISTEMI, useFactory: cdkOnayIstemi }`.
 */
export function cdkOnayIstemi(): OnayIstemi {
  const onay = inject(OnayServisi);
  const t = ceviriFonksiyonu();
  return (mesaj) =>
    onay.sor({
      baslik: t('geriBildirim.kaydedilmemis.baslik'),
      mesaj,
      onayEtiketi: t('geriBildirim.kaydedilmemis.ayril'),
      iptalEtiketi: t('geriBildirim.kaydedilmemis.kal'),
      tehlikeli: true,
    });
}
