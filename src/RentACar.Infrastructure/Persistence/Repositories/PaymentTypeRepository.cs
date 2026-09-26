using Microsoft.EntityFrameworkCore;
using RentACar.Application.PaymentTypes;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Ödeme tipi repo — generic <see cref="MasterDefinitionRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class PaymentTypeRepository(IDbContextFactory<AppDbContext> factory)
    : MasterDefinitionRepository<PaymentType>(factory, "ödeme tipi"), IPaymentTypeRepository;
