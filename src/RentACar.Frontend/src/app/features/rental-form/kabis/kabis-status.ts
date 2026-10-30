import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Observable, EMPTY, catchError, filter, switchMap } from 'rxjs';

import { toApiError } from '@core/api/api-error';
import { ApiIstemcisi } from '@core/api/api-client';
import { SubmitLock } from '@core/form/submit-lock';
import { ConfirmService } from '@core/feedback/confirm-service';
import { ToastService } from '@core/feedback/toast-service';
import type { CeviriAnahtari } from '@core/i18n/translation-keys';
import { translationFunction } from '@core/i18n/translation';
import { DUGME_IZINLERI } from '@core/session/button-permissions';
import { SessionService } from '@core/session/session-service';
import { genelGosterilir } from '@core/session/session-interceptor';
import { FORMAT_PIPES } from '@shared/format/format-pipe';
import { RentalFormState } from '../rental-form-state';
import {
  type KabisNotice,
  type KabisStatus as KabisStatusDto,
  attemptCount,
  kabisActionable,
  kabisTone,
  knownStatus,
  knownType,
} from './kabis-model';

interface PendingAction {
  readonly tur: string;
  readonly tip: 'gerekli-degil' | 'elle-bildirildi';
}

/**
 * Kira ekranı KABİS kartı: kiralama/iade/iptal bildirimlerinin durum rozeti, bildirilmemiş ya da hatalı kirada
 * uyarı (sunucunun cümlesi) ve operatör işlemleri — şimdi yeniden dene, gerekli değil (gerekçe zorunlu), portaldan
 * elle bildirildi (referans isteğe bağlı). Kapatan iki işlem onay ister. Veri `GET /kiralar/{id}/kabis`; kiranın
 * sürümü değişince (teslim, dönüş, iptal) yeniden yüklenir. Kimlik numarası bu uçta hiç yoktur.
 */
@Component({
  selector: 'rc-kf-kabis',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, ...FORMAT_PIPES],
  // Kabuk kutu üretmez: kart çizilmediğinde (kapalı firma, yükleniyor) sayfa sütununa boş bir öğe ve ek boşluk
  // eklenmez — koordinatla tıklanan yan panel alanları kaymaz.
  styles: `
    :host {
      display: contents;
    }
  `,
  template: `
    <!-- Kart yalnız durum BİLİNİNCE ve otomatik bildirim AÇIKKEN çizilir: kapalı firmada (ve yüklenirken) sayfa
         düzeni hiç kaymaz (koordinatla tıklanan para alanları aynı yerde kalır). -->
    @if (!d.yeni && d.id && (loadError() || status()?.etkin)) {
      <section
        class="rc-bolum kf-kart"
        [attr.aria-label]="'kiraFormu.kabis.baslik' | transloco"
        data-testid="kabis-karti"
      >
        <h2 class="kf-kart__baslik">{{ 'kiraFormu.kabis.baslik' | transloco }}</h2>
        @if (loadError(); as e) {
          <p class="kf-not kf-not--uyari" role="alert">
            {{ 'kiraFormu.kabis.yuklenemedi' | transloco }} {{ e }}
          </p>
        } @else if (status(); as s) {
          @if (s.etkin) {
            @if (s.uyari) {
              <p
                class="rc-form-mesaji rc-form-mesaji--uyari"
                role="alert"
                data-testid="kabis-uyari"
              >
                {{ s.uyari }}
              </p>
            }
            @if (s.teslimiEngelle) {
              <p class="kf-not">{{ 'kiraFormu.kabis.teslimiEngeller' | transloco }}</p>
            }
            @if (s.bildirimler.length === 0) {
              <p class="kf-not">{{ 'kiraFormu.kabis.kayitYok' | transloco }}</p>
            }
            @for (b of s.bildirimler; track b.tur) {
              <div class="kf-bilgi" [attr.data-testid]="'kabis-' + b.tur">
                <span>{{ typeLabel(b.tur) }}</span>
                <strong>
                  <span [class]="badgeClass(b.durum)">{{ statusLabel(b.durum) }}</span>
                  @if (b.elle) {
                    ({{ 'kiraFormu.kabis.elle' | transloco }})
                  }
                </strong>
                <p class="kf-not">
                  {{ 'kiraFormu.kabis.kurum' | transloco: { kurum: b.kurum } }}
                  · {{ b.olayUtc | tarihSaat }}
                  @if (b.disReferans) {
                    · {{ 'kiraFormu.kabis.referans' | transloco: { ref: b.disReferans } }}
                  }
                  @if (attempts(b.denemeSayisi) > 0) {
                    ·
                    {{ 'kiraFormu.kabis.deneme' | transloco: { sayi: attempts(b.denemeSayisi) } }}
                  }
                  @if (b.sonrakiDenemeUtc) {
                    ·
                    {{
                      'kiraFormu.kabis.sonraki'
                        | transloco: { zaman: (b.sonrakiDenemeUtc | tarihSaat) }
                    }}
                  }
                </p>
                @if (b.sonHata && b.durum !== 'Bildirildi') {
                  <p class="kf-not kf-not--uyari">{{ b.sonHata }}</p>
                }
                @if (b.gerekce) {
                  <p class="kf-not">{{ b.gerekce }}</p>
                }
                @if (canAct() && actionable(b)) {
                  @if (pending()?.tur === b.tur) {
                    <div class="kf-eylemler">
                      <label class="rc-gorunmez" [for]="'kabis-metin-' + b.tur">{{
                        (pending()?.tip === 'gerekli-degil'
                          ? 'kiraFormu.kabis.gerekce'
                          : 'kiraFormu.kabis.referansAlan'
                        ) | transloco
                      }}</label>
                      <input
                        class="rc-girdi"
                        [id]="'kabis-metin-' + b.tur"
                        [formControl]="text"
                        [attr.maxlength]="pending()?.tip === 'gerekli-degil' ? 256 : 128"
                        [placeholder]="
                          (pending()?.tip === 'gerekli-degil'
                            ? 'kiraFormu.kabis.gerekce'
                            : 'kiraFormu.kabis.referansAlan'
                          ) | transloco
                        "
                      />
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
                        [disabled]="lock.gonderiliyor()"
                        (click)="confirmPending()"
                      >
                        {{ 'kiraFormu.kabis.onayla' | transloco }}
                      </button>
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--kucuk"
                        (click)="pending.set(null)"
                      >
                        {{ 'kiraFormu.kabis.vazgec' | transloco }}
                      </button>
                    </div>
                  } @else {
                    <div class="kf-eylemler">
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--kucuk"
                        [disabled]="lock.gonderiliyor()"
                        (click)="retry(b)"
                      >
                        {{ 'kiraFormu.kabis.yenidenDene' | transloco }}
                      </button>
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
                        [disabled]="lock.gonderiliyor()"
                        (click)="open(b, 'elle-bildirildi')"
                      >
                        {{ 'kiraFormu.kabis.elleBildirildi' | transloco }}
                      </button>
                      <button
                        type="button"
                        class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
                        [disabled]="lock.gonderiliyor()"
                        (click)="open(b, 'gerekli-degil')"
                      >
                        {{ 'kiraFormu.kabis.gerekliDegil' | transloco }}
                      </button>
                    </div>
                  }
                }
              </div>
            }
          }
        }
      </section>
    }
  `,
})
export class KabisStatus {
  protected readonly d = inject(RentalFormState);
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly status = signal<KabisStatusDto | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly pending = signal<PendingAction | null>(null);
  protected readonly text = new FormControl<string | null>(null, Validators.maxLength(256));
  protected readonly lock = new SubmitLock();
  /** Görünürlük kapısı — asıl kapı sunucuda (UiDugmeIzinTests üç işlemin de OperationsWrite olduğunu kilitler). */
  protected readonly canAct = computed(() =>
    this.session.hasPermissions(DUGME_IZINLERI.kabisYenidenDene.izinler),
  );
  protected readonly actionable = kabisActionable;
  protected readonly attempts = attemptCount;

  /** Renk = durum; nötr (gerekli değil) değiştiricisiz rozet. */
  protected badgeClass(status: string): string {
    const tone = kabisTone(status);
    return tone === 'notr' ? 'rc-rozet' : `rc-rozet rc-rozet--${tone}`;
  }

  /** Kiranın sürümü değişince (teslim/dönüş/iptal/kayıt) durum yeniden okunur; son istek kazanır. */
  private readonly version = computed(() => this.d.kira()?.surum ?? null);

  constructor() {
    toObservable(this.version)
      .pipe(
        // Kira okunmadan (sürüm yok) istenmez: sayfa açılışında tek istek, her işlemden sonra bir yenisi.
        filter((v) => v !== null && !this.d.yeni && this.d.id !== null),
        switchMap(() =>
          this.api.get<KabisStatusDto>(this.root).pipe(
            catchError((raw: unknown) => {
              this.loadError.set(toApiError(raw).detay);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((s) => {
        this.loadError.set(null);
        this.status.set(s);
      });
  }

  private get root(): `/api/ui/v1/${string}` {
    return `/api/ui/v1/kiralar/${this.d.id ?? ''}/kabis`;
  }

  protected typeLabel(type: string): string {
    return knownType(type) ? this.t(`kiraFormu.kabis.tur.${type}` as CeviriAnahtari) : type;
  }

  protected statusLabel(status: string): string {
    return knownStatus(status)
      ? this.t(`kiraFormu.kabis.durum.${status}` as CeviriAnahtari)
      : status;
  }

  protected open(n: KabisNotice, tip: PendingAction['tip']): void {
    this.text.reset(null);
    this.pending.set({ tur: n.tur, tip });
  }

  protected retry(n: KabisNotice): void {
    this.run(
      this.api.post<KabisStatusDto>(`${this.root}/${encodeURIComponent(n.tur)}/yeniden-dene`, {}),
    );
  }

  protected async confirmPending(): Promise<void> {
    const p = this.pending();
    if (p === null) return;
    const value = this.text.value?.trim() || null;
    if (p.tip === 'gerekli-degil' && value === null) {
      this.toast.uyari(this.t('kiraFormu.kabis.gerekceZorunlu'));
      return;
    }
    const notRequired = p.tip === 'gerekli-degil';
    const ok = await this.approval.ask({
      baslik: this.t(
        notRequired ? 'kiraFormu.kabis.gerekliDegilBaslik' : 'kiraFormu.kabis.elleBaslik',
      ),
      mesaj: this.t(
        notRequired ? 'kiraFormu.kabis.gerekliDegilMesaj' : 'kiraFormu.kabis.elleMesaj',
      ),
      tehlikeli: true,
    });
    if (!ok) return;
    const url = `${this.root}/${encodeURIComponent(p.tur)}/${p.tip}` as const;
    this.run(
      this.api.post<KabisStatusDto>(url, notRequired ? { gerekce: value } : { referans: value }),
    );
  }

  private run(request: Observable<KabisStatusDto>): void {
    this.lock
      .gonder(() => request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.pending.set(null);
          this.status.set(s);
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }
}
