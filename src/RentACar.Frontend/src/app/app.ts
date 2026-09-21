import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** Kök bileşen: yalnız rota çıkışı. Kabuk (menü, üst çubuk, sekmeler) F3.2'de. */
@Component({
  selector: 'rc-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App {}
