using Microsoft.EntityFrameworkCore;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Rezervasyon kaynağı repo — generic <see cref="MasterTanimRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class ReservationSourceRepository(IDbContextFactory<AppDbContext> factory)
    : MasterTanimRepository<ReservationSource>(factory, "rezervasyon kaynağı"), IReservationSourceRepository;
