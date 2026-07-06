using Microsoft.EntityFrameworkCore;
using RentACar.Application.Departments;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Departman repo — generic <see cref="MasterTanimRepository{T}"/> ince alt sınıfı (O12d); gövde tabandan gelir.</summary>
public sealed class DepartmentRepository(IDbContextFactory<AppDbContext> factory)
    : MasterTanimRepository<Department>(factory, "departman"), IDepartmentRepository;
