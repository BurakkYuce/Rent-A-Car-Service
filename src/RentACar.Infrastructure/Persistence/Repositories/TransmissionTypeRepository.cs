using Microsoft.EntityFrameworkCore;
using RentACar.Application.TransmissionTypes;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Vites türü repo — generic <see cref="MasterTanimRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class TransmissionTypeRepository(IDbContextFactory<AppDbContext> factory)
    : MasterTanimRepository<TransmissionType>(factory, "vites türü"), ITransmissionTypeRepository;
