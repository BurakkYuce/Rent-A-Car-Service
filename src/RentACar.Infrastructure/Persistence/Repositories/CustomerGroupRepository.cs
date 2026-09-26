using Microsoft.EntityFrameworkCore;
using RentACar.Application.CustomerGroups;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Müşteri grubu repo — generic <see cref="MasterDefinitionRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class CustomerGroupRepository(IDbContextFactory<AppDbContext> factory)
    : MasterDefinitionRepository<CustomerGroup>(factory, "müşteri grubu"), ICustomerGroupRepository;
