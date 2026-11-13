using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Services;

public sealed class PreparoService(AppDbContext db, StockService stock, RecipeService recipes, TimeProvider clock)
{
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromMinutes(30);

    private sealed record WithdrawalLot(Guid LoteId, decimal Quantidade);
    private sealed record Withdrawal(Guid IngredienteId, string Nome, string Unidade, decimal Quantidade, bool Substituto, List<WithdrawalLot> Lotes);

    public async Task<object> Iniciar(TenantContext t, PreparoIniciarRequest req, CancellationToken ct)
    {
        var receita = await recipes.Load(t.DespensaId, req.ReceitaId, ct);
        if (receita.Status == StatusReceita.Rascunho)
            throw BusinessException.Conflict("RECEITA_RASCUNHO", "Complete a receita antes de iniciar um preparo.");

        var lines = receita.Ingredientes.OrderBy(l => l.Ordem).Where(l => req.IncluirOpcionais || !l.Opcional).ToList();
        if (lines.Count == 0)
            throw BusinessException.Conflict("RECEITA_SEM_INGREDIENTES", "A receita não tem ingredientes obrigatórios para baixar.");

        var now = clock.GetUtcNow();
        var preparo = new Preparo
        {
            DespensaId = t.DespensaId, ReceitaId = receita.Id, ReceitaTitulo = receita.Titulo, Porcoes = req.Porcoes,
            UsuarioId = t.UsuarioId, IncluirOpcionais = req.IncluirOpcionais, CriadoEm = now, ExpiraEm = now + DraftLifetime,
        };
        var ordem = 0;
        foreach (var line in lines)
        {
            preparo.Itens.Add(new PreparoItemSnapshot
            {
                PreparoId = preparo.Id, Ordem = ordem++, IngredienteId = line.IngredienteId, Texto = line.Nome,
                QuantidadePrevista = line.Quantidade.HasValue
                    ? RecipeScaler.Scale(line.Quantidade.Value, receita.Porcoes, req.Porcoes)
                    : null,
                UnidadeCodigo = line.UnidadeCodigo, Opcional = line.Opcional, Confianca = line.Confianca,
            });
        }
        db.Preparos.Add(preparo);
        await db.SaveChangesAsync(ct);
        return await Preview(preparo, ct);
    }

    private static List<Requirement> ToRequirements(IEnumerable<PreparoItemSnapshot> items) =>
        items.OrderBy(i => i.Ordem)
            .Select((i, idx) => new Requirement(idx, i.IngredienteId, i.QuantidadePrevista, i.UnidadeCodigo, false, i.Confianca))
            .ToList();

    private static string? NotDeductibleReason(LineStatus status) => status switch
    {
        LineStatus.Uncertain => "Correspondência com o estoque não confirmada: este item não será baixado.",
        LineStatus.NoQuantity => "Sem quantidade definida: este item não será baixado.",
        LineStatus.IncompatibleUnit => "Unidade incompatível com o estoque: este item não será baixado.",
        _ => null,
    };

    public async Task<object> Preview(Preparo preparo, CancellationToken ct)
    {
        var items = preparo.Itens.OrderBy(i => i.Ordem).ToList();
        var entries = await stock.LoadStock(preparo.DespensaId, ct);
        var plans = StockPlanner.Plan(ToRequirements(items), entries);
        var info = await recipes.IngredientInfo(preparo.DespensaId, ct);

        var touched = plans.SelectMany(p => p.Parts.Select(x => x.IngredientId)).Distinct().ToArray();
        var lots = await db.Lotes.AsNoTracking()
            .Where(l => l.Status == StatusLote.Aberto && db.ItensEstoque.Any(i => i.Id == l.ItemEstoqueId && touched.Contains(i.IngredienteId)))
            .Join(db.ItensEstoque, l => l.ItemEstoqueId, i => i.Id, (l, i) => new { i.IngredienteId, Lot = l })
            .ToListAsync(ct);
        var lotsByIngredient = lots.GroupBy(x => x.IngredienteId).ToDictionary(g => g.Key, g => g.Select(x => x.Lot).ToList());
        var remainingLots = lots.ToDictionary(x => x.Lot.Id, x => x.Lot.Quantidade);

        var result = new List<object>();
        foreach (var plan in plans)
        {
            var item = items[plan.Index];
            var withdrawals = new List<object>();
            foreach (var part in plan.Parts)
            {
                var available = (lotsByIngredient.GetValueOrDefault(part.IngredientId) ?? [])
                    .Select(l => new LotInfo(l.Id, remainingLots[l.Id], l.DataValidade, l.DataEntrada)).ToList();
                var fefo = Fefo.Allocate(available, part.QuantityInRefUnit);
                foreach (var a in fefo.Allocations) remainingLots[a.LotId] -= a.Quantity;
                var ing = info.GetValueOrDefault(part.IngredientId);
                withdrawals.Add(new
                {
                    ingredienteId = part.IngredientId, nome = ing?.Nome ?? "?", unidade = ing?.UnidadeReferencia ?? "",
                    quantidade = part.QuantityInRefUnit, aproximado = part.Approximate, substituto = part.IsSubstitute,
                    lotes = fefo.Allocations.Select(a => new
                    {
                        loteId = a.LotId,
                        validade = lotsByIngredient[part.IngredientId].First(l => l.Id == a.LotId).DataValidade,
                        quantidade = a.Quantity,
                    }),
                });
            }

            var reason = NotDeductibleReason(plan.Status);
            result.Add(new
            {
                item.Ordem, item.Texto, item.IngredienteId, quantidade = item.QuantidadePrevista, unidade = item.UnidadeCodigo,
                item.Opcional, status = plan.Status, aproximado = plan.Status == LineStatus.Approximate,
                naoDescontavel = reason is not null, motivoNaoDescontavel = reason,
                faltante = UnitCatalog.Round(plan.Shortage), usaEquivalente = plan.UsedEquivalent, baixas = withdrawals,
            });
        }

        var now = clock.GetUtcNow();
        return new
        {
            preparoId = preparo.Id, preparo.ReceitaId, preparo.ReceitaTitulo, preparo.Porcoes, preparo.Status,
            preparo.ExpiraEm, expirado = preparo.Status == StatusPreparo.Iniciado && preparo.ExpiraEm < now,
            temFaltantes = plans.Any(p => p.Status == LineStatus.Insufficient),
            temNaoDescontaveis = plans.Any(p => NotDeductibleReason(p.Status) is not null),
            itens = result,
        };
    }

    public async Task<object> Confirmar(TenantContext t, Guid id, PreparoConfirmarRequest req, string? key, CancellationToken ct)
    {
        // Bloqueia o preparo antes de ler o status: duas confirmações concorrentes se serializam aqui.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from preparos where id = {id} and despensa_id = {t.DespensaId} for update", ct);
        var preparo = await db.Preparos.Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Id == id && p.DespensaId == t.DespensaId, ct)
            ?? throw BusinessException.NotFound("Preparo");

        if (preparo.Status == StatusPreparo.Confirmado) return Result(preparo);
        if (preparo.Status == StatusPreparo.Cancelado)
            throw BusinessException.Conflict("PREPARO_CANCELADO", "Este preparo foi cancelado. Inicie um novo.");
        var now = clock.GetUtcNow();
        if (preparo.ExpiraEm < now)
            throw BusinessException.Conflict("PREPARO_EXPIRADO", "A confirmação expirou. Inicie o preparo novamente para revalidar o estoque.");

        var items = preparo.Itens.OrderBy(i => i.Ordem).ToList();
        var directIds = items.Where(i => i.IngredienteId.HasValue).Select(i => i.IngredienteId!.Value).Distinct().ToArray();
        var groupIds = await db.Ingredientes.Where(i => directIds.Contains(i.Id) && i.GrupoEquivalenciaId != null)
            .Select(i => i.GrupoEquivalenciaId!.Value).Distinct().ToArrayAsync(ct);
        var memberIds = groupIds.Length == 0
            ? []
            : await db.Ingredientes.Where(i => i.GrupoEquivalenciaId != null && groupIds.Contains(i.GrupoEquivalenciaId.Value))
                .Select(i => i.Id).ToArrayAsync(ct);
        var allIds = directIds.Union(memberIds).OrderBy(x => x).ToArray();

        var locked = await stock.LockItems(t.DespensaId, allIds, ct);
        var ingredients = await db.Ingredientes.Where(i => allIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var entries = locked.Select(item =>
        {
            var ing = ingredients[item.IngredienteId];
            var next = item.Lotes.Where(l => l.Status == StatusLote.Aberto && l.DataValidade != null).Min(l => l.DataValidade);
            return new StockEntry(ing.Id, ing.UnidadeReferenciaCodigo, item.QuantidadeTotal, ing.GrupoEquivalenciaId, next);
        }).ToList();

        var plans = StockPlanner.Plan(ToRequirements(items), entries);
        var shortages = plans.Where(p => p.Status == LineStatus.Insufficient).ToList();
        if (shortages.Count > 0 && !req.ConsumirDisponivel)
        {
            throw BusinessException.Conflict("ESTOQUE_INSUFICIENTE",
                "Não há estoque suficiente para este preparo. Nenhuma baixa foi feita.",
                new
                {
                    faltantes = shortages.Select(p => new
                    {
                        texto = items[p.Index].Texto, ingredienteId = items[p.Index].IngredienteId,
                        necessario = UnitCatalog.Round(p.Needed), disponivel = UnitCatalog.Round(p.Covered),
                        faltante = UnitCatalog.Round(p.Shortage), unidade = p.UnitCode,
                    }),
                });
        }

        var motivo = $"Preparo: {preparo.ReceitaTitulo}";
        foreach (var plan in plans)
        {
            var snapshot = items[plan.Index];
            if (NotDeductibleReason(plan.Status) is { } reason)
            {
                snapshot.NaoDescontado = true;
                snapshot.Observacao = reason;
                continue;
            }

            var withdrawals = new List<Withdrawal>();
            foreach (var part in plan.Parts)
            {
                var item = locked.First(i => i.IngredienteId == part.IngredientId);
                var ing = ingredients[part.IngredientId];
                var movements = stock.Withdraw(item, ing, part.QuantityInRefUnit, TipoMovimentacao.BaixaPreparo, motivo,
                    t.UsuarioId, null, preparo.Id, key);
                withdrawals.Add(new Withdrawal(ing.Id, ing.Nome, ing.UnidadeReferenciaCodigo, part.QuantityInRefUnit, part.IsSubstitute,
                    movements.Where(m => m.LoteEstoqueId.HasValue).Select(m => new WithdrawalLot(m.LoteEstoqueId!.Value, m.Quantidade)).ToList()));
            }
            snapshot.QuantidadeBaixada = UnitCatalog.Round(plan.Covered);
            snapshot.QuantidadeFaltante = UnitCatalog.Round(plan.Shortage);
            snapshot.BaixasJson = JsonSerializer.Serialize(withdrawals);
        }

        preparo.Status = StatusPreparo.Confirmado;
        preparo.ConfirmadoEm = now;
        preparo.UsuarioId = t.UsuarioId;
        preparo.ConsumirDisponivel = req.ConsumirDisponivel;
        preparo.IdempotencyKey = key;
        await db.SaveChangesAsync(ct);
        return Result(preparo);
    }

    public async Task Cancelar(TenantContext t, Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from preparos where id = {id} and despensa_id = {t.DespensaId} for update", ct);
        var preparo = await db.Preparos.FirstOrDefaultAsync(p => p.Id == id && p.DespensaId == t.DespensaId, ct)
            ?? throw BusinessException.NotFound("Preparo");
        if (preparo.Status == StatusPreparo.Confirmado)
            throw BusinessException.Conflict("PREPARO_CONFIRMADO", "Um preparo confirmado não pode ser cancelado.");
        preparo.Status = StatusPreparo.Cancelado;
        await db.SaveChangesAsync(ct);
    }

    public static object Result(Preparo p) => new
    {
        preparoId = p.Id, p.ReceitaId, p.ReceitaTitulo, p.Porcoes, p.Status, p.ConfirmadoEm, p.ConsumirDisponivel, p.CriadoEm,
        itens = p.Itens.OrderBy(i => i.Ordem).Select(i => new
        {
            i.Ordem, i.Texto, i.IngredienteId, quantidadePrevista = i.QuantidadePrevista, unidade = i.UnidadeCodigo, i.Opcional,
            i.QuantidadeBaixada, i.QuantidadeFaltante, i.NaoDescontado, i.Observacao,
            baixas = i.BaixasJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(i.BaixasJson),
        }),
    };
}
