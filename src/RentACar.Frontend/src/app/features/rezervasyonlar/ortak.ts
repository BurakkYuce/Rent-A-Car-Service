import { PercentPipe } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import { FormErrors } from '@shared/form/form-errors';
import { TextArea } from '@shared/form/kontroller/text-area';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import { Selection } from '@shared/form/kontroller/selection';
import { DateTimePicker } from '@shared/form/tarih/date-time-picker';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { PlateChipComponent } from '@shared/plaka/plaka';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';

/** Rezervasyon ve teklif formlarının ortak içe aktarımları (form seti + biçim pipe'ları + sayfa bandı, plaka). */
export const RF_SHARED = [
  ReactiveFormsModule,
  TranslocoPipe,
  PercentPipe,
  ...FORMAT_PIPES,
  Alan,
  SearchSelection,
  FormErrors,
  Icon,
  PlateChipComponent,
  PageBand,
  TextArea,
  TextInput,
  MoneyInput,
  NumberInput,
  Selection,
  DateTimePicker,
  DatePicker,
] as const;
