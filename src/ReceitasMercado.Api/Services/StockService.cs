using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Services;

public sealed record MovementResult(Guid IngredienteId, decimal QuantidadeTotal, string UnidadeCodigo, IReadOnlyList<Guid> MovimentacaoIds);

public sealed class StockService(AppDbContext db, TimeProvider clock)
{
    public static string NormalizeName(string name) => TextNormalizer.Normalize(name);

    public async Task<Ingrediente> RequireIngrediente(Guid despensaId, Guid id, CancellationToken ct = default) =>
        await db.Ingredientes.FirstOrDefaultAsync(i => i.Id == id && i.DespensaId == despensaId, ct)
        ?? throw BusinessException.NotFound("Ingrediente");

    /// <summary>
    /// Serializa alterações concorrentes de um ingrediente bloqueando a linha de ItemEstoque
    /// (ordem por id evita deadlock) e só então carrega os lotes abertos.
    /// </summary>
    public async Task<List<ItemEstoque>> LockItems(Guid despensaId, IEnumerable<Guid> ingredienteIds, CancellationToken ct = default)
    {
        var ids = ingredienteIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var items = await db.ItensEstoque
            .FromSqlInterpolated($"select * from itens_estoque where despensa_id = {despensaId} and ingrediente_id = any({ids}) order by id for update")
            .ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToArray();
        await db.Lotes.Where(l => itemIds.Contains(l.ItemEstoqueId) && l.Status == StatusLote.Aberto).LoadAsync(ct);
        return items;
    }

    public decimal ToReferenceUnit(Ingrediente ing, decimal quantity, string? unit)
    {
        unit ??= ing.UnidadeReferenciaCodigo;
        if (!UnitCatalog.Exists(unit))
            throw BusinessException.BadRequest("UNIDADE_INVALIDA", $"Unidade desconhecida: {unit}.");
        var converted = UnitCatalog.Convert(quantity, unit, ing.UnidadeReferenciaCodigo)
            ?? throw BusinessException.BadRequest("UNIDADE_INCOMPATIVEL",
                $"Não é possível converter {unit} para {ing.UnidadeReferenciaCodigo} ({ing.Nome}).");
        var rounded = UnitCatalog.Round(converted.Value);
        if (rounded <= 0) throw BusinessException.BadRequest("QUANTIDADE_INVALIDA", "A quantidade deve ser maior que zero.");
        return rounded;
    }

    private static void Recalculate(ItemEstoque item, DateTimeOffset now)
    {
        item.QuantidadeTotal = item.Lotes.Where(l => l.Status == StatusLote.Aberto).Sum(l => l.Quantidade);
        item.AtualizadoEm = now;
    }

    public async Task<MovementResult> Entrada(
        Guid despensaId, Guid? userId, Guid ingredienteId, decimal quantity, string? unit, DateOnly? validade,
        OrigemLote origem, string motivo, string? observacao, TipoMovimentacao tipo = TipoMovimentacao.Entrada,
        Guid? itemListaId = null, string? key = null, CancellationToken ct = default)
    {
        var ing = await RequireIngrediente(despensaId, ingredienteId, ct);
        if (ing.ArquivadoEm is not null)
            throw BusinessException.Conflict("INGREDIENTE_ARQUIVADO", "Reative o ingrediente antes de registrar novas entradas.");
        var qty = ToReferenceUnit(ing, quantity, unit);

        var item = (await LockItems(despensaId, [ingredienteId], ct)).Single();
        var now = clock.GetUtcNow();
        var lot = new LoteEstoque
        {
            ItemEstoqueId = item.Id, Quantidade = qty, QuantidadeInicial = qty, DataValidade = validade,
            DataEntrada = now, Origem = origem, Observacao = observacao,
        };
        db.Lotes.Add(lot);
        item.Lotes.Add(lot);
        var mov = NewMovement(despensaId, item.Id, lot.Id, tipo, 1, qty, ing.UnidadeReferenciaCodigo, motivo, userId, key, now);
        mov.ItemListaCompraId = itemListaId;
        db.Movimentacoes.Add(mov);
        Recalculate(item, now);
        await db.SaveChangesAsync(ct);
        return new MovementResult(ingredienteId, item.QuantidadeTotal, ing.UnidadeReferenciaCodigo, [mov.Id]);
    }

    public async Task<MovementResult> Saida(
        Guid despensaId, Guid? userId, Guid ingredienteId, decimal quantity, string? unit, Guid? loteId,
        TipoMovimentacao tipo, string motivo, string? key = null, CancellationToken ct = default)
    {
        var ing = await RequireIngrediente(despensaId, ingredienteId, ct);
        var qty = ToReferenceUnit(ing, quantity, unit);
        var item = (await LockItems(despensaId, [ingredienteId], ct)).Single();
        var movements = Withdraw(item, ing, qty, tipo, motivo, userId, loteId, null, key);
        await db.SaveChangesAsync(ct);
        return new MovementResult(ingredienteId, item.QuantidadeTotal, ing.UnidadeReferenciaCodigo, movements.Select(m => m.Id).ToList());
    }

    public async Task<MovementResult> Correcao(
        Guid despensaId, Guid? userId, Guid ingredienteId, Guid? loteId, decimal delta, string? unit, string motivo,
        string? key = null, CancellationToken ct = default)
    {
        if (delta == 0) throw BusinessException.BadRequest("QUANTIDADE_INVALIDA", "A correção não pode ser zero.");
        var ing = await RequireIngrediente(despensaId, ingredienteId, ct);
        var qty = ToReferenceUnit(ing, Math.Abs(delta), unit);
        var item = (await LockItems(despensaId, [ingredienteId], ct)).Single();
        var now = clock.GetUtcNow();

        if (delta < 0)
        {
            var movements = Withdraw(item, ing, qty, TipoMovimentacao.Correcao, motivo, userId, loteId, null, key);
            await db.SaveChangesAsync(ct);
            return new MovementResult(ingredienteId, item.QuantidadeTotal, ing.UnidadeReferenciaCodigo, movements.Select(m => m.Id).ToList());
        }

        LoteEstoque lot;
        if (loteId is { } id)
        {
            lot = item.Lotes.FirstOrDefault(l => l.Id == id && l.Status == StatusLote.Aberto)
                ?? throw BusinessException.NotFound("Lote");
            lot.Quantidade += qty;
            if (lot.Quantidade > lot.QuantidadeInicial) lot.QuantidadeInicial = lot.Quantidade;
        }
        else
        {
            lot = new LoteEstoque
            {
                ItemEstoqueId = item.Id, Quantidade = qty, QuantidadeInicial = qty, DataEntrada = now,
                Origem = OrigemLote.Ajuste, Observacao = motivo,
            };
            db.Lotes.Add(lot);
            item.Lotes.Add(lot);
        }
        var mov = NewMovement(despensaId, item.Id, lot.Id, TipoMovimentacao.Correcao, 1, qty, ing.UnidadeReferenciaCodigo, motivo, userId, key, now);
        db.Movimentacoes.Add(mov);
        Recalculate(item, now);
        await db.SaveChangesAsync(ct);
        return new MovementResult(ingredienteId, item.QuantidadeTotal, ing.UnidadeReferenciaCodigo, [mov.Id]);
    }

    /// <summary>Baixa FEFO (ou de um lote específico) sobre um item já bloqueado. Nunca deixa saldo negativo.</summary>
    public List<Movimentacao> Withdraw(
        ItemEstoque item, Ingrediente ing, decimal qtyRef, TipoMovimentacao tipo, string motivo, Guid? userId,
        Guid? loteId, Guid? preparoId, string? key)
    {
        var now = clock.GetUtcNow();
        var open = item.Lotes.Where(l => l.Status == StatusLote.Aberto).ToList();
        IReadOnlyList<LotAllocation> allocations;
        if (loteId is { } id)
        {
            var lot = open.FirstOrDefault(l => l.Id == id) ?? throw BusinessException.NotFound("Lote");
            if (lot.Quantidade < qtyRef) throw Insufficient(ing, qtyRef, lot.Quantidade);
            allocations = [new LotAllocation(lot.Id, qtyRef)];
        }
        else
        {
            var fefo = Fefo.Allocate(open.Select(l => new LotInfo(l.Id, l.Quantidade, l.DataValidade, l.DataEntrada)), qtyRef);
            if (fefo.Shortage > 0) throw Insufficient(ing, qtyRef, qtyRef - fefo.Shortage);
            allocations = fefo.Allocations;
        }

        var movements = new List<Movimentacao>();
        foreach (var allocation in allocations)
        {
            var lot = open.First(l => l.Id == allocation.LotId);
            lot.Quantidade -= allocation.Quantity;
            if (lot.Quantidade <= 0) lot.Status = StatusLote.Esgotado;
            var mov = NewMovement(item.DespensaId, item.Id, lot.Id, tipo, -1, allocation.Quantity,
                ing.UnidadeReferenciaCodigo, motivo, userId, key, now);
            mov.PreparoId = preparoId;
            db.Movimentacoes.Add(mov);
            movements.Add(mov);
        }
        Recalculate(item, now);
        return movements;
    }

    private static BusinessException Insufficient(Ingrediente ing, decimal needed, decimal available) =>
        BusinessException.Conflict("ESTOQUE_INSUFICIENTE",
            $"Estoque insuficiente de {ing.Nome}: necessário {needed} {ing.UnidadeReferenciaCodigo}, disponível {available}.",
            new
            {
                faltantes = new[]
                {
                    new { ingredienteId = ing.Id, nome = ing.Nome, necessario = needed, disponivel = available, unidade = ing.UnidadeReferenciaCodigo },
                },
            });

    private static Movimentacao NewMovement(
        Guid despensaId, Guid itemId, Guid? loteId, TipoMovimentacao tipo, short sentido, decimal qty, string unit,
        string motivo, Guid? userId, string? key, DateTimeOffset now) => new()
    {
        DespensaId = despensaId, ItemEstoqueId = itemId, LoteEstoqueId = loteId, Tipo = tipo, Sentido = sentido,
        Quantidade = qty, UnidadeCodigo = unit, Motivo = string.IsNullOrWhiteSpace(motivo) ? tipo.ToString() : motivo,
        UsuarioId = userId, IdempotencyKey = key, CriadoEm = now,
    };

    public async Task<List<StockEntry>> LoadStock(Guid despensaId, CancellationToken ct = default) =>
        (await db.ItensEstoque.AsNoTracking()
            .Where(i => i.DespensaId == despensaId)
            .Select(i => new
            {
                i.IngredienteId,
                Unit = i.Ingrediente!.UnidadeReferenciaCodigo,
                i.QuantidadeTotal,
                Group = i.Ingrediente.GrupoEquivalenciaId,
                Next = i.Lotes.Where(l => l.Status == StatusLote.Aberto && l.DataValidade != null).Min(l => (DateOnly?)l.DataValidade),
            })
            .ToListAsync(ct))
        .Select(x => new StockEntry(x.IngredienteId, x.Unit, x.QuantidadeTotal, x.Group, x.Next))
        .ToList();
}
