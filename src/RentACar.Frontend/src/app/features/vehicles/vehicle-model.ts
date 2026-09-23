import type { Sema } from '@core/api/ui-tipleri';
import { listeTanimi } from '@core/veri/liste-sorgusu';

export type VehicleListRow = Sema<'AracListeSatiri'>;
export type VehicleSummary = Sema<'AracOzeti'>;
export type VehicleModelGroups = Sema<'AracModelGruplari'>;
export type VehicleModelGroup = Sema<'AracModelGrubu'>;
export type VehicleCard = Sema<'AracKartDto'>;
export type VehicleCreateRequest = Sema<'AracIstegi'>;
export type VehicleUpdateRequest = Sema<'AracGuncelleIstegi'>;
export type VehicleDetail = Sema<'AracDetayDto'>;
export type DetailedRow = Sema<'AracDetayliSatir'>;
export type StatusBoardResponse = Sema<'AracDurumYaniti'>;
export type StatusRow = Sema<'AracDurumSatiri'>;
export type VehiclePhoto = Sema<'AracFotoDto'>;
export type SuggestionValue = Sema<'AracSecimDegeri'>;

/** Sunucu enum ADLARI (`VehicleStatus`, `FiloStatus`, `Vites`, `FuelType`); tanımsız ad 400. */
export const VEHICLE_STATUSES = ['Musait', 'Kirada', 'Serviste', 'Pasif', 'Satildi'] as const;
export type VehicleStatus = (typeof VEHICLE_STATUSES)[number];
export const FLEET_STATUSES = [
  'SifirKmStok',
  'Havuz',
  'Tahsis',
  'Usk',
  'Ksk',
  'IkinciElSatis',
  'Siparis',
] as const;
export type FleetStatus = (typeof FLEET_STATUSES)[number];
export const GEARS = ['Manuel', 'Otomatik'] as const;
export type Gear = (typeof GEARS)[number];
export const FUELS = ['Benzin', 'Dizel', 'Lpg', 'Elektrik', 'Hibrit'] as const;
export type Fuel = (typeof FUELS)[number];
export const DATE_KINDS = ['FiloGiris', 'FiloCikis', 'Tescil'] as const;
export const GROUP_KINDS = ['Grup', 'Sipp'] as const;
/** "Sahibi girilmemiş" kovası (`sahiplik=Girilmemis`). */
export const OWNERSHIP_KINDS = ['Girilmemis'] as const;
export const VIEW_KINDS = ['grup'] as const;
export const TRI_STATE = ['true', 'false'] as const;

/** Blazor StatusBadge renkleri. */
export const STATUS_BADGE: Readonly<Record<VehicleStatus, string>> = {
  Musait: 'rc-rozet--basari',
  Kirada: 'rc-rozet--bilgi',
  Serviste: 'rc-rozet--uyari',
  Pasif: '',
  Satildi: 'rc-rozet--hata',
};

export function vehicleStatus(value: string | null | undefined): VehicleStatus | null {
  return (VEHICLE_STATUSES as readonly string[]).includes(value ?? '')
    ? (value as VehicleStatus)
    : null;
}

/** Araç listesi (`GET /araclar`): Blazor VehicleList süzgeçleri + "modele göre grupla" görünümü. */
export const VEHICLE_LIST = listeTanimi({
  filtreler: {
    q: { tur: 'metin', enFazla: 100 },
    grupTuru: { tur: 'secim', degerler: GROUP_KINDS },
    grup: { tur: 'metin', enFazla: 64 },
    durum: { tur: 'secim', degerler: VEHICLE_STATUSES },
    sube: { tur: 'metin', enFazla: 64 },
    sahiplik: { tur: 'secim', degerler: OWNERSHIP_KINDS },
    aracSahibi: { tur: 'metin', enFazla: 128 },
    tarihTuru: { tur: 'secim', degerler: DATE_KINDS },
    tarihBas: { tur: 'tarih' },
    tarihBit: { tur: 'tarih' },
    gorunum: { tur: 'secim', degerler: VIEW_KINDS },
  },
  siralanabilir: [
    'plaka',
    'marka',
    'tip',
    'detayTipi',
    'grup',
    'segment',
    'sipp',
    'modelYili',
    'renk',
    'km',
    'sube',
    'durum',
    'filoDurum',
    'aracSahibi',
    'sonBakimKm',
    'alimBedeli',
    'filoGirisTarih',
    'filoCikisTarih',
    'tescilTarihi',
    'ikinciElDeger',
    'tsbKaskoDegeri',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Detaylı araç listesi (`GET /araclar/detayli`, ViewReports). */
export const DETAILED_LIST = listeTanimi({
  filtreler: {
    ara: { tur: 'metin', enFazla: 100 },
    sube: { tur: 'metin', enFazla: 64 },
    durum: { tur: 'secim', degerler: VEHICLE_STATUSES },
  },
  siralanabilir: [
    'plaka',
    'marka',
    'grup',
    'sube',
    'durum',
    'alimTarihi',
    'alimBedeli',
    'muayeneBitis',
    'kaskoBitis',
    'trafikBitis',
    'aktifKiraBitis',
    'filoGirisTarih',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Araç güncel durum panosu (`GET /araclar/durum`, OperationsWrite). Üçlü bayraklar `true`/`false`/yok. */
export const STATUS_BOARD = listeTanimi({
  filtreler: {
    q: { tur: 'metin', enFazla: 100 },
    durum: { tur: 'secim', degerler: VEHICLE_STATUSES },
    filoDurum: { tur: 'secim', degerler: FLEET_STATUSES },
    sube: { tur: 'metin', enFazla: 64 },
    vites: { tur: 'secim', degerler: GEARS },
    yakit: { tur: 'secim', degerler: FUELS },
    grup: { tur: 'metin', enFazla: 64 },
    marka: { tur: 'metin', enFazla: 64 },
    kirada: { tur: 'secim', degerler: TRI_STATE },
    pasifSebep: { tur: 'metin', enFazla: 256 },
    hgs: { tur: 'metin', enFazla: 64 },
    gps: { tur: 'metin', enFazla: 64 },
    karLastigi: { tur: 'secim', degerler: TRI_STATE },
    webRezKapali: { tur: 'secim', degerler: TRI_STATE },
    ofisRezKapali: { tur: 'secim', degerler: TRI_STATE },
  },
  siralanabilir: [
    'plaka',
    'marka',
    'grup',
    'durum',
    'filoDurum',
    'kiraBitTar',
    'kiraKalanGun',
    'kiraBakiye',
    'rezBasTar',
    'km',
    'sube',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 100,
});

/** JSON sayısı (`number | string`) → gösterim sayısı. YALNIZ gösterim. */
export function toNumber(value: number | string | null | undefined): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}
