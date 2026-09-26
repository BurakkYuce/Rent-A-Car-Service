using Microsoft.EntityFrameworkCore;
using RentACar.Application.Countries;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Ülke repo — generic <see cref="MasterDefinitionRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class CountryRepository(IDbContextFactory<AppDbContext> factory)
    : MasterDefinitionRepository<Country>(factory, "ülke"), ICountryRepository;
