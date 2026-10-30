import { PercentPipe } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { FORMAT_PIPES } from '@shared/format/format-pipe';
import { Alan } from '@shared/form/field/field';
import { SearchSelection } from '@shared/form/search-selection/search-selection';
import { FormErrors } from '@shared/form/form-errors';
import { TextArea } from '@shared/form/controls/text-area';
import { TextInput } from '@shared/form/controls/text-input';
import { Checkbox } from '@shared/form/controls/checkbox';
import { MoneyInput } from '@shared/form/controls/money-input';
import { NumberInput } from '@shared/form/controls/number-input';
import { Selection } from '@shared/form/controls/selection';
import { DateTimePicker } from '@shared/form/date/date-time-picker';
import { DatePicker } from '@shared/form/date/date-picker';

/** Sekme bileşenlerinin ortak içe aktarımları (form seti + biçim pipe'ları). */
export const KF_SHARED = [
  ReactiveFormsModule,
  TranslocoPipe,
  PercentPipe,
  ...FORMAT_PIPES,
  Alan,
  SearchSelection,
  FormErrors,
  TextArea,
  TextInput,
  Checkbox,
  MoneyInput,
  NumberInput,
  Selection,
  DateTimePicker,
  DatePicker,
] as const;
