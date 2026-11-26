using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace backend_dickifier.Data;

/** Solo para generar SQL/migraciones: no carga credenciales ni abre conexiones. */
public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<DickifierDbContext>
{
    public DickifierDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DickifierDbContext>()
        .UseNpgsql("Host=127.0.0.1;Database=dickifier_design_only;Username=design_only;Timeout=1").Options);
}
