import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-client';
import type { Schema } from '@core/api/ui-types';
import { translationFunction } from '@core/i18n/translation';
import { TemelStore } from '@core/data/base-store';
import { DateTimePipe } from '@shared/format/format-pipe';
import { Alan } from '@shared/form/field/field';
import { TextInput } from '@shared/form/controls/text-input';
import { Selection } from '@shared/form/controls/selection';
import type { SecenekOgesi } from '@shared/form/controls/option';
import { PageBand } from '../../../shell/page-band/page-band';

type AuditPageDto = Schema<'SayfaOfAuditDto'>;

export const AUDIT_PAGE_SIZE = 30;

interface AuditQuery {
  readonly tablo: string | null;
  readonly kullanici: string | null;
  readonly islem: string | null;
  readonly sayfa: number;
}

/** Toplam sayfa (en az 1). */
export function pageCount(total: number | string, size: number | string): number {
  const t = Number(total);
  const s = Number(size) || AUDIT_PAGE_SIZE;
  return Math.max(1, Math.ceil(t / s));
}

/**
 * F11.2b denetim kaydı (Blazor `AuditList`, ManageUsers, SALT OKUR). Eski/yeni değer JSON'u sunucuda sır ve kişisel
 * veri anahtarlarından arındırılmış gelir (`***`); ekran yalnız DÜZ METİN basar (innerHTML yok). Süzgeçler bellekte,
 * tarayıcı deposuna yazılmaz.
 */
@Component({
  selector: 'rc-audit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, ReactiveFormsModule, TranslocoPipe, DateTimePipe, Alan, TextInput, Selection],
  styleUrl: '../system.scss',
  templateUrl: './audit-page.html',
})
export class AuditPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly t = translationFunction();

  protected readonly filters = new FormGroup({
    tablo: new FormControl<string | null>(null, Validators.maxLength(128)),
    kullanici: new FormControl<string | null>(null, Validators.maxLength(128)),
    islem: new FormControl<string | null>(null),
  });
  protected readonly actions: readonly SecenekOgesi<string>[] = ['Create', 'Update', 'Delete'].map(
    (a) => ({ deger: a, etiket: this.actionLabel(a) }),
  );
  private readonly query = signal<AuditQuery>({
    tablo: null,
    kullanici: null,
    islem: null,
    sayfa: 1,
  });
  protected readonly list = new TemelStore<AuditPageDto, AuditQuery>(
    (q) => {
      return this.api.get<AuditPageDto>('/api/ui/v1/denetim', {
        parametreler: {
          tablo: q.tablo,
          kullanici: q.kullanici,
          islem: q.islem,
          sayfa: q.sayfa,
          boyut: AUDIT_PAGE_SIZE,
        },
      });
    },
    { oncekiVeriyiKoru: true },
  );
  protected readonly page = computed(() => this.query().sayfa);
  protected readonly pages = computed(() => {
    const d = this.list.veri();
    return d ? pageCount(d.toplam, d.boyut) : 1;
  });

  constructor() {
    this.list.yukle(this.query());
  }

  protected actionLabel(a: string): string {
    return a === 'Create' || a === 'Update' || a === 'Delete'
      ? this.t(`sistem.denetim.islem.${a}`)
      : a;
  }

  protected search(): void {
    if (this.filters.invalid) return;
    const v = this.filters.getRawValue();
    this.query.set({
      tablo: v.tablo?.trim() || null,
      kullanici: v.kullanici?.trim() || null,
      islem: v.islem,
      sayfa: 1,
    });
    this.list.yukle(this.query());
  }

  protected clear(): void {
    this.filters.reset();
    this.search();
  }

  protected go(delta: number): void {
    const next = Math.min(this.pages(), Math.max(1, this.page() + delta));
    if (next === this.page()) return;
    this.query.update((q) => ({ ...q, sayfa: next }));
    this.list.yukle(this.query());
  }
}
