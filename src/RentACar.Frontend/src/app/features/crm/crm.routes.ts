import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';

/**
 * F7.2 CRM ekranları — Blazor yollarıyla AYNI (`/anketler`, `/sikayetler`, `/assistans`, `/hukuk`, `/crm`; kesişte
 * yönlendirme birebir). İzinler uçlarla aynı: anket/şikayet/assistans/hukuk OperationsWrite (okuma ve yazma aynı
 * kapı), CRM analiz ViewReports (şubeli kullanıcıya sunucu ayrıca 403 — firma geneli rapor).
 */
export const CRM_ROUTES: Routes = ceviriBloguyla('crm', [
  {
    path: 'anketler',
    title: 'Müşteri Anketleri — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('@features/crm/surveys/survey-list').then((m) => m.SurveyList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'sikayetler',
    title: 'Müşteri Şikayetleri — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/crm/complaints/complaint-list').then((m) => m.ComplaintList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'assistans',
    title: 'Assistans Talepleri — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/crm/assistance/assistance-list').then((m) => m.AssistanceList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'hukuk',
    title: 'Hukuk Dosyaları — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/crm/legal-files/legal-file-list').then((m) => m.LegalFileList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'crm',
    title: 'CRM Analiz — RentACar',
    canMatch: [izinGuard('ViewReports')],
    loadComponent: () =>
      import('@features/crm/crm-analysis/crm-analysis').then((m) => m.CrmAnalysis),
  },
]);
