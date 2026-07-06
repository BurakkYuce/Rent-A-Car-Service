using Microsoft.EntityFrameworkCore;
using RentACar.Application.VehicleColors;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Renk repo — generic <see cref="MasterTanimRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class VehicleColorRepository(IDbContextFactory<AppDbContext> factory)
    : MasterTanimRepository<VehicleColor>(factory, "renk"), IVehicleColorRepository;
