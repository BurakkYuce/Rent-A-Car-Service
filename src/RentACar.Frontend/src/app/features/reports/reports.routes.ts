import type { Route, Routes } from '@angular/router';

import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

/**
 * F10.2 rapor ekranları — Blazor yollarıyla AYNI (`/raporlar/<kod>`; kesişte yönlendirme birebir). Hepsi tek
 * ortak bileşen (`ReportPage`), rapor `data.rapor` kodundan. İzinler uçlarla BİREBİR (`ReportApi`):
 * - `vr` → ViewReports (defter, cari, fatura, satış, filo raporları);
 * - `ops` → OperationsWrite ∨ ViewReports (Blazor'da dört rolün açtığı operasyon raporları).
 * Firma geneli rapor şube kapsamlı kullanıcıya 403 döner; ekran bunu açık mesajla gösterir (rota kapısı değil —
 * izin kullanıcı bazında verilebilir, kapsam kararı sunucuda). Tablo ↔ katalog eşliği `report-catalog.spec.ts`.
 */
type Access = 'vr' | 'ops';

export const REPORT_ROUTE_TABLE: readonly (readonly [
  code: string,
  title: string,
  access: Access,
])[] = [
  ['gelir-gider', 'Gelir-Gider Raporu', 'vr'],
  ['kasa-banka', 'Kasa/Banka Defteri', 'vr'],
  ['finans-analiz', 'Finans Analiz', 'vr'],
  ['virman-gecmisi', 'Virman Geçmişi', 'vr'],
  ['kdv-listesi', 'KDV Listesi', 'vr'],
  ['cari-bakiye', 'Cari Bakiye Raporu', 'vr'],
  ['extre-ozeti', 'Ekstre Özeti', 'vr'],
  ['tahsilat-fatura', 'Tahsilat–Fatura', 'vr'],
  ['fatura-donem', 'Dönem Faturaları', 'vr'],
  ['karlilik', 'Araç Kârlılığı', 'vr'],
  ['ek-hizmet', 'Ek Hizmet Raporu', 'vr'],
  ['gunluk', 'Günlük Faaliyet', 'vr'],
  ['arac-karne', 'Araç Karnesi', 'vr'],
  ['filo-analiz', 'Filo Analiz', 'vr'],
  ['filo', 'Filo Durumu', 'vr'],
  ['doluluk', 'Doluluk Raporu', 'vr'],
  ['arac-gunluk-durum', 'Araç Günlük Durum', 'vr'],
  ['servis-ozet', 'Servis Maliyet Özeti', 'vr'],
  ['rezervasyon-kaynak', 'Rezervasyon Kaynakları', 'vr'],
  ['otomatik-servisler', 'Otomatik Servisler', 'vr'],
  ['arac-durum-takip', 'Araç Durum Takip', 'ops'],
  ['km-detay', 'Km Detay', 'ops'],
  ['periyodik-servis', 'Periyodik Servis', 'ops'],
  ['sigorta-muayene', 'Sigorta / Muayene', 'ops'],
  ['karsilastirmali-analiz', 'Karşılaştırmalı Analiz', 'ops'],
  ['personel-calisma', 'Personel Çalışma Tablosu', 'ops'],
];

function route([code, title, access]: (typeof REPORT_ROUTE_TABLE)[number]): Route {
  return {
    path: code === 'arac-karne' ? 'raporlar/arac-karne/:id' : `raporlar/${code}`,
    title: `${title} — RentACar`,
    data: { rapor: code },
    canMatch: [
      access === 'vr'
        ? izinGuard('ViewReports')
        : anyPermissionGuard('OperationsWrite', 'ViewReports'),
    ],
    loadComponent: () => import('@features/reports/report-page').then((m) => m.ReportPage),
  };
}

export const REPORT_ROUTES: Routes = ceviriBloguyla('rapor', REPORT_ROUTE_TABLE.map(route));
