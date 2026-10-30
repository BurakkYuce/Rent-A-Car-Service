import { PercentPipe } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { FORMAT_PIPES } from '@shared/format/format-pipe';
import { Alan } from '@shared/form/field/field';
import { SearchSelection } from '@shared/form/search-selection/search-selection';
import { FormErrors } from '@shared/form/form-errors';
import { TextArea } from '@shared/form/controls/text-area';
import { TextInput } from '@shared/form/controls/text-input';
import { MoneyInput } from '@shared/form/controls/money-input';
import { NumberInput } from '@shared/form/controls/number-input';
import { Checkbox } from '@shared/form/controls/checkbox';
import { Selection } from '@shared/form/controls/selection';
import { DateTimePicker } from '@shared/form/date/date-time-picker';
import { DatePicker } from '@shared/form/date/date-picker';
import { Icon } from '@shared/icon/icon';
import { PlateChipComponent } from '@shared/plate/plate';

import { PageBand } from '../../shell/page-band/page-band';

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
  Checkbox,
  Selection,
  DateTimePicker,
  DatePicker,
] as const;
