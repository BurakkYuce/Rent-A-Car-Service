namespace RentACar.Domain.Enums;

/// <summary>Marka-özel düzenlenebilir belge şablonunun uygulandığı PDF belge türü. Her tür kendi
/// bölüm kümesini kullanır (sözleşme: başlık+hukuki metin+ek koşullar+alt bilgi; fatura/makbuz:
/// başlık+alt bilgi). Şablon yoksa renderer koddaki varsayılanı basar (BelgeSablonVarsayilan).</summary>
public enum BelgeTuru
{
    KiraSozlesmesi = 0,
    Fatura = 1,
    Makbuz = 2
}
