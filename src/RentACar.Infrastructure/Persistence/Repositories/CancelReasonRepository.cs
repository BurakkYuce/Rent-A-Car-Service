using Microsoft.EntityFrameworkCore;
using RentACar.Application.CancelReasons;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>İptal sebebi repo — generic <see cref="MasterDefinitionRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class CancelReasonRepository(IDbContextFactory<AppDbContext> factory)
    : MasterDefinitionRepository<CancelReason>(factory, "iptal sebebi"), ICancelReasonRepository;
