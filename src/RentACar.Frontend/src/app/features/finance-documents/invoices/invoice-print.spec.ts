import { FINANCE_DOCUMENT_ROUTES } from '../finance-documents.routes';
import { invoicePdfPath, invoiceRouteId } from './invoice-print';

const ID = 'f8f8f8f8-0000-4000-8000-000000000001';

describe('fatura rota kimliği', () => {
  it('yalnız UUID kabul edilir; başka metin istek üretmez', () => {
    expect(invoiceRouteId(ID)).toBe(ID);
    expect(invoiceRouteId('detay-listesi')).toBeNull();
    expect(invoiceRouteId('../pdf')).toBeNull();
    expect(invoiceRouteId(null)).toBeNull();
    expect(invoicePdfPath(ID)).toBe(`/faturalar/${ID}/pdf`);
    expect(invoicePdfPath('x')).toBeNull();
  });

  it('/faturalar/:id rotası var; statik detay-listesi ondan ÖNCE eşleşir', () => {
    const paths = FINANCE_DOCUMENT_ROUTES.flatMap((r) => (r.children ?? [r]).map((c) => c.path));
    const detail = paths.indexOf('faturalar/:id');
    expect(detail).toBeGreaterThan(-1);
    expect(paths.indexOf('faturalar/detay-listesi')).toBeLessThan(detail);
  });
});
