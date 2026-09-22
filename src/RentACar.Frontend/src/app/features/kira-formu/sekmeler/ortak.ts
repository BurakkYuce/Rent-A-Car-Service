import { PercentPipe } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinAlani } from '@shared/form/kontroller/metin-alani';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSaatSecici } from '@shared/form/tarih/tarih-saat-secici';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

/** Sekme bileşenlerinin ortak içe aktarımları (form seti + biçim pipe'ları). */
export const KF_ORTAK = [
  ReactiveFormsModule,
  TranslocoPipe,
  PercentPipe,
  ...BICIM_PIPELARI,
  Alan,
  AramaSecim,
  FormHatalari,
  MetinAlani,
  MetinGirdisi,
  OnayKutusu,
  ParaGirdisi,
  SayiGirdisi,
  Secim,
  TarihSaatSecici,
  TarihSecici,
] as const;
