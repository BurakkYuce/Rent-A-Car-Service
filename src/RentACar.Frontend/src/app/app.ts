import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * Yeni arayüzün kabuğu. F2.1'de yalnız yer tutucu: rota, API çağrısı ve tasarım sistemi F3'te.
 */
@Component({
  selector: 'rc-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
