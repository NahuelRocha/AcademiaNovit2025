using AcademiaNovit;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

#region configuracion del Serilog
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());
#endregion

#region leer variables de entorno
builder.Configuration.AddEnvironmentVariables();
#endregion

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddOpenApi();
builder.Services.AddControllers();

var app = builder.Build();

// CAMBIADO: Migración con retry y manejo de errores
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    var maxRetries = 30; // 30 intentos = 5 minutos máximo
    var delay = TimeSpan.FromSeconds(10);
    
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            logger.LogInformation("Intentando conectar a la base de datos (intento {Attempt}/{MaxRetries})", i + 1, maxRetries);
            await dbContext.Database.CanConnectAsync();
            logger.LogInformation("Conexión a la base de datos exitosa. Ejecutando migraciones...");
            dbContext.Database.Migrate();
            logger.LogInformation("Migraciones ejecutadas correctamente.");
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error al conectar con la base de datos (intento {Attempt}/{MaxRetries}). Reintentando en {Delay} segundos...", 
                i + 1, maxRetries, delay.TotalSeconds);
            
            if (i == maxRetries - 1)
            {
                logger.LogError(ex, "No se pudo conectar a la base de datos después de {MaxRetries} intentos. La aplicación se cerrará.", maxRetries);
                throw;
            }
            
            await Task.Delay(delay);
        }
    }
}

app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

#region health endpoint con verificación de DB
app.MapGet("/health", async (IServiceProvider serviceProvider) =>
{
    try 
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.CanConnectAsync();
        
        return Results.Ok(new { 
            status = "healthy", 
            database = "connected",
            timestamp = DateTime.UtcNow 
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { 
            status = "unhealthy", 
            database = "disconnected",
            error = ex.Message,
            timestamp = DateTime.UtcNow 
        }, statusCode: 503);
    }
});
#endregion

app.Run();

public partial class Program { } // This partial class is required for the WebApplicationFactory to work properly in tests.