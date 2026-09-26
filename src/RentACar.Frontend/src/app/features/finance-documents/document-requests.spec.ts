import { type IncomingInvoiceRow, invoiceLineExportParameters } from './document-model';
import { currencyMismatch } from './expenses/currency-rules';
import {
  type ExpenseForm,
  type ManualInvoiceForm,
  type PenaltyForm,
  expensePaymentRequest,
  expenseRequest,
  incomingLinkRequest,
  incomingToLinkForm,
  manualInvoiceRequest,
  penaltyPaymentRequest,
  penaltyRequest,
  vatBreakdownText,
} from './document-requests';

/** Beklenen değerler elle yazıldı (bağımsız oracle); gövde kurucularının kendisinden türetilmez. */
const ACCOUNT = { id: 'c0c0c0c0-0000-4000-8000-000000000001', etiket: 'Ayşe Yılmaz' };
const VEHICLE = { id: 'a1a1a1a1-0000-4000-8000-000000000001', etiket: '34ABC123' };

describe('finans belge gövdeleri', () => {
  it('manuel fatura: tutar invariant metin AYNEN, KDV istemcide hesaplanmaz, gün İstanbul gece yarısı', () => {
    const v: ManualInvoiceForm = {
      cari: ACCOUNT,
      netTutar: '1500.50',
      kdvOrani: '0.20',
      tarih: '2026-09-01',
      vadeTarihi: null,
      aciklama: '  Hasar bedeli  ',
      islemSube: '',
      evrakNo: null,
      faturaOzelKod: null,
      odemeTuru: 'Havale',
      gonderimSekli: null,
      kdvSifirSebep: null,
      otv: '',
      tevkifatOran: null,
      tevkifatTutar: null,
      damgaVergisi: '12.30',
    };
    const body = manualInvoiceRequest(v);
    expect(body).toEqual({
      cariId: ACCOUNT.id,
      netTutar: '1500.50',
      kdvOrani: '0.20',
      aciklama: 'Hasar bedeli',
      tarih: '2026-08-31T21:00:00.000Z',
      vadeTarihi: null,
      islemSube: null,
      evrakNo: null,
      faturaOzelKod: null,
      odemeTuru: 'Havale',
      gonderimSekli: null,
      kdvSifirSebep: null,
      otv: null,
      tevkifatOran: null,
      tevkifatTutar: null,
      damgaVergisi: '12.30',
    });
    expect(Object.keys(body)).not.toContain('kdvTutar');
    expect(Object.keys(body)).not.toContain('genelToplam');
  });

  it('ceza: boş tutarlı yuva atlanır, sıra korunur', () => {
    const v: PenaltyForm = {
      cezaTuru: ' Hız ',
      tebligTarihi: null,
      saat: '14:30',
      vadeGun: 15,
      yer: null,
      cepTel: null,
      makbuzNo: null,
      islemSube: null,
      arac: VEHICLE,
      cari: null,
      sebep: null,
      kalemler: [
        { tutar: '900', sebep: 'Hız' },
        { tutar: '', sebep: 'boş yuva' },
        { tutar: '250.75', sebep: null },
        { tutar: null, sebep: null },
      ],
    };
    const body = penaltyRequest(v);
    expect(body.kalemler).toEqual([
      { tutar: '900', sebep: 'Hız' },
      { tutar: '250.75', sebep: null },
    ]);
    expect(body).toMatchObject({ cezaTuru: 'Hız', aracId: VEHICLE.id, cariId: null, kiraId: null });
  });

  it('ceza ödemesi: boş tutar null gider (kalemin kalanı SUNUCUDA)', () => {
    expect(
      penaltyPaymentRequest({
        satirId: 's1',
        tutar: '',
        hesap: 'Banka',
        tarih: null,
        makbuzNo: ' M-7 ',
        islemYapan: null,
        aciklama: null,
      }),
    ).toEqual({
      satirId: 's1',
      hesap: 'Banka',
      tutar: null,
      tarih: null,
      makbuzNo: 'M-7',
      islemYapan: null,
      aciklama: null,
    });
  });

  it('gider: boş kur null (sunucu çözer), açık kur aynen', () => {
    const base: ExpenseForm = {
      tip: 'Arac',
      arac: VEHICLE,
      cari: ACCOUNT,
      kira: null,
      netTutar: '1000',
      kdvOrani: '0.20',
      odemeYontemi: 'AcikHesap',
      doviz: 'EUR',
      kur: null,
      hesapId: null,
      tarih: null,
      odemeTarihi: null,
      vade: '2026-10-15',
      hazirAciklama: 'Lastik',
      sube: ' Merkez ',
      evrakNo: null,
      aciklama: null,
    };
    expect(expenseRequest(base)).toMatchObject({
      tip: 'Arac',
      netTutar: '1000',
      kdvOrani: '0.20',
      odemeYontemi: 'AcikHesap',
      doviz: 'EUR',
      kur: null,
      aracId: VEHICLE.id,
      cariId: ACCOUNT.id,
      kiraId: null,
      sube: 'Merkez',
      vade: '2026-10-14T21:00:00.000Z',
    });
    expect(expenseRequest({ ...base, kur: '35.1234' }).kur).toBe('35.1234');
    // Sözleşme alanı (#300): seçilen kiranın kimliği gider; etiket gövdeye girmez.
    const rental = { id: 'b2b2b2b2-0000-4000-8000-000000000001', etiket: 'K-100 — 34ABC123' };
    expect(expenseRequest({ ...base, kira: rental }).kiraId).toBe(
      'b2b2b2b2-0000-4000-8000-000000000001',
    );
    expect(
      expensePaymentRequest({ tutar: null, tarih: null, makbuzNo: null, aciklama: null }),
    ).toEqual({ tutar: null, tarih: null, makbuzNo: null, aciklama: null });
  });

  it('gelen e-fatura bağlama: tam değiştirme + surum; boş kademe null (temizler)', () => {
    const form = incomingToLinkForm({
      id: 'g1',
      ettn: 'E-1',
      gonderenVkn: '1234567890',
      gonderenUnvan: 'Tedarikçi A.Ş.',
      tarih: '2026-09-01T00:00:00Z',
      netTutar: 1000,
      kdvTutar: 200,
      genelToplam: 1200,
      doviz: 'TRY',
      durum: 'Onaylandi',
      redNedeni: null,
      aciklama: null,
      kdv20Matrah: 1000,
      kdv20: 200,
      kdv10Matrah: null,
      kdv10: null,
      kdv1Matrah: null,
      kdv1: null,
      kdv0Matrah: null,
      aracId: null,
      plaka: null,
      giderKategoriId: 'k0k0k0k0-0000-4000-8000-000000000001',
      cariId: ACCOUNT.id,
      cariAd: ACCOUNT.etiket,
      giderTipi: null,
      giderlestirildi: false,
      giderlestirilmeTarihi: null,
      giderKategoriAd: 'Yakıt',
    });
    // Kategori aranabilir seçim: etiket kaydın adından (#300); gövdeye yalnız kimlik gider.
    expect(form.kategori).toEqual({ id: 'k0k0k0k0-0000-4000-8000-000000000001', etiket: 'Yakıt' });
    expect(incomingLinkRequest(form, 'v-7').giderKategoriId).toBe(
      'k0k0k0k0-0000-4000-8000-000000000001',
    );
    expect(incomingLinkRequest({ ...form, kdv20: '', kategori: null }, 'v-7')).toEqual({
      surum: 'v-7',
      kdv20Matrah: '1000',
      kdv20: null,
      kdv10Matrah: null,
      kdv10: null,
      kdv1Matrah: null,
      kdv1: null,
      kdv0Matrah: null,
      aracId: null,
      giderKategoriId: null,
      cariId: ACCOUNT.id,
      giderTipi: null,
    });
  });

  it('giderleştirme onayı: kayıtlı kırılım okunur özet, boş kademe atlanır', () => {
    const row = {
      doviz: 'TRY',
      kdv20Matrah: 1000,
      kdv20: 200,
      kdv10Matrah: null,
      kdv10: null,
      kdv1Matrah: null,
      kdv1: null,
      kdv0Matrah: '150.25',
    } as unknown as IncomingInvoiceRow;
    expect(vatBreakdownText(row, 'yok')).toBe('%20: 1.000,00 ₺ + KDV 200,00 ₺; %0: 150,25 ₺');
    expect(
      vatBreakdownText({ ...row, kdv20Matrah: null, kdv20: null, kdv0Matrah: null }, 'yok'),
    ).toBe('yok');
  });

  it('döviz uyuşmazlığı: TL = TRY, hesap dövizi bilinmiyorsa uyuşmazlık yok', () => {
    expect(currencyMismatch('TL', 'TRY')).toBe(false);
    expect(currencyMismatch('USD', 'EUR')).toBe(true);
    expect(currencyMismatch(null, 'EUR')).toBe(false);
    expect(currencyMismatch('eur', 'EUR')).toBe(false);
  });

  it('fatura detay listesi dışa aktarma: Blazor adları (ara, iptal=gizle)', () => {
    expect(
      invoiceLineExportParameters({
        q: 'RNT',
        plaka: '34ABC123',
        iptalleriGizle: true,
        bas: '2026-09-01',
      }),
    ).toEqual({ ara: 'RNT', plaka: '34ABC123', bas: '2026-09-01', iptal: 'gizle' });
  });
});
