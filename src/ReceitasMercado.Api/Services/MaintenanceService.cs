using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Data;

namespace ReceitasMercado.Api.Services;

/// <summary>Limpeza periódica de chaves de idempotência antigas e preparos abandonados.</summary>
public sealed class MaintenanceService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<MaintenanceService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await RunOnce(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Falha na rotina de manutenção.");
            }
        } while (await WaitNext(timer, stoppingToken));
    }

    private static async Task<bool> WaitNext(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    public async Task RunOnce(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var keys = now.AddHours(-48);
        var drafts = now.AddDays(-7);
        var removedKeys = await db.Idempotencias.Where(i => i.CriadoEm < keys).ExecuteDeleteAsync(ct);
        var removedDrafts = await db.Preparos
            .Where(p => p.Status != StatusPreparo.Confirmado && p.ExpiraEm < drafts).ExecuteDeleteAsync(ct);
        if (removedKeys + removedDrafts > 0)
            logger.LogInformation("Manutenção: {Chaves} chaves de idempotência e {Preparos} preparos abandonados removidos.", removedKeys, removedDrafts);
    }
}
