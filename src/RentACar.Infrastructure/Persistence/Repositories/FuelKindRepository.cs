using Microsoft.EntityFrameworkCore;
using RentACar.Application.FuelKinds;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Yakıt türü repo — generic <see cref="MasterDefinitionRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class FuelKindRepository(IDbContextFactory<AppDbContext> factory)
    : MasterDefinitionRepository<FuelKind>(factory, "yakıt türü"), IFuelKindRepository;
