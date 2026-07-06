using Microsoft.EntityFrameworkCore;
using RentACar.Application.Brands;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Marka repo — generic <see cref="MasterTanimRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class BrandRepository(IDbContextFactory<AppDbContext> factory)
    : MasterTanimRepository<Brand>(factory, "marka"), IBrandRepository;
