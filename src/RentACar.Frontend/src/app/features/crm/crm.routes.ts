import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';

/**
 * F7.2 CRM ekranları — Blazor yollarıyla AYNI (`/anketler`, `/sikayetler`, `/assistans`, `/hukuk`, `/crm`; kesişte
 * yönlendirme birebir). İzinler uçlarla aynı: anket/şikayet/assistans/hukuk OperationsWrite (okuma ve yazma aynı
 * kapı), CRM analiz ViewReports (şubeli kullanıcıya sunucu ayrıca 403 — firma geneli rapor).
 */
export const CRM_ROUTES: Routes = withTranslationBlock('crm', [
  {
    path: 'anketler',
    title: 'Müşteri Anketleri — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('@features/crm/surveys/survey-list').then((m) => m.SurveyList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'sikayetler',
    title: 'Müşteri Şikayetleri — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/crm/complaints/complaint-list').then((m) => m.ComplaintList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'assistans',
    title: 'Assistans Talepleri — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/crm/assistance/assistance-list').then((m) => m.AssistanceList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'hukuk',
    title: 'Hukuk Dosyaları — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/crm/legal-files/legal-file-list').then((m) => m.LegalFileList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'crm',
    title: 'CRM Analiz — RentACar',
    canMatch: [permissionGuard('ViewReports')],
    loadComponent: () =>
      import('@features/crm/crm-analysis/crm-analysis').then((m) => m.CrmAnalysis),
  },
]);
