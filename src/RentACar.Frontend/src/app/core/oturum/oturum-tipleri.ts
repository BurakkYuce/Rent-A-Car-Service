import type { BenYaniti, GirisIstegi } from '@core/api/ui-tipleri';

/**
 * `GET /api/ui/v1/oturum/ben` yanıtı — OpenAPI'den ÜRETİLEN tip (`@core/api/ui-tipleri`, F2.2). API'de alan
 * değişirse `npm run tipler` sonrası kullanan kod `typecheck`'te kırılır.
 */
export type Ben = BenYaniti;

/** Backend `Permission` enum adları (claim'e ve `ben.izinler`'e adıyla yazılır). */
export const IZINLER = [
  'ManageUsers',
  'OperationsWrite',
  'FinanceWrite',
  'ViewReports',
  'OperationsDelete',
  'FinanceReverse',
] as const;

export type Izin = (typeof IZINLER)[number];

/** `POST /api/ui/v1/oturum/giris` gövdesi; form üç alanı da DOLU gönderir (sözleşmede null kabul edilir). */
export type GirisBilgileri = { readonly [A in keyof GirisIstegi]: NonNullable<GirisIstegi[A]> };
