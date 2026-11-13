using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Services;

public sealed class ShoppingService(AppDbContext db, StockService stock, RecipeService recipes)
{
    public async Task<(ListaCompra Lista, List<string> Ignorados)> FromRecipes(TenantContext t, ListaDeReceitasRequest req, CancellationToken ct)
    {
        var ids = req.Receitas.Select(r => r.ReceitaId).Distinct().ToArray();
        var found = await db.Receitas.Include(r => r.Ingredientes)
            .Where(r => r.DespensaId == t.DespensaId && ids.Contains(r.Id)).ToListAsync(ct);
        if (found.Count != ids.Length) throw BusinessException.NotFound("Receita");

        var needs = new List<NeedLine>();
        var ignorados = new List<string>();
        foreach (var item in req.Receitas)
        {
            var receita = found.First(r => r.Id == item.ReceitaId);
            foreach (var line in receita.Ingredientes.OrderBy(l => l.Ordem).Where(l => req.IncluirOpcionais || !l.Opcional))
            {
                if (line.Quantidade is not > 0)
                {
                    ignorados.Add($"{line.Nome} ({receita.Titulo}): sem quantidade definida");
                    continue;
                }
                var qty = RecipeScaler.Scale(line.Quantidade.Value, receita.Porcoes, item.Porcoes);
                if (qty <= 0) continue;
                var matched = line.IngredienteId is { } id && line.Confianca == Confidence.Confirmada ? id : (Guid?)null;
                needs.Add(new NeedLine(matched, line.Nome, qty, line.UnidadeCodigo ?? "UN"));
            }
        }

        var entries = await stock.LoadStock(t.DespensaId, ct);
        var names = (await recipes.IngredientInfo(t.DespensaId, ct)).ToDictionary(kv => kv.Key, kv => kv.Value.Nome);
        var consolidated = ShoppingConsolidator.Consolidate(needs, entries, names);

        var lista = new ListaCompra
        {
            DespensaId = t.DespensaId, Nome = req.Nome.Trim(), Origem = OrigemLista.Receitas, CriadoPorUsuarioId = t.UsuarioId,
        };
        var ordem = 0;
        foreach (var c in consolidated)
        {
            lista.Itens.Add(new ItemListaCompra
            {
                ListaId = lista.Id, Ordem = ordem++, IngredienteId = c.IngredientId, Texto = c.Text,
                QuantidadeNecessaria = c.Needed, QuantidadeDescontadaEstoque = c.FromStock, QuantidadeComprar = c.ToBuy,
                UnidadeCodigo = c.UnitCode,
            });
        }
        db.ListasCompra.Add(lista);
        await db.SaveChangesAsync(ct);
        return (lista, ignorados);
    }

    public async Task<ListaCompra> Load(Guid despensaId, Guid id, CancellationToken ct) =>
        await db.ListasCompra.Include(l => l.Itens)
            .FirstOrDefaultAsync(l => l.Id == id && l.DespensaId == despensaId, ct)
        ?? throw BusinessException.NotFound("Lista de compras");

    public async Task<object> LancarEntradas(TenantContext t, Guid listaId, LancarEntradasRequest req, string? key, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from listas_compra where id = {listaId} and despensa_id = {t.DespensaId} for update", ct);
        var lista = await Load(t.DespensaId, listaId, ct);

        var explicitItems = req.Itens.ToDictionary(i => i.ItemId);
        foreach (var itemId in explicitItems.Keys)
            if (lista.Itens.All(i => i.Id != itemId)) throw BusinessException.NotFound("Item da lista");

        var lancados = new List<object>();
        var ignorados = new List<object>();
        foreach (var item in lista.Itens.OrderBy(i => i.Ordem))
        {
            var chosen = explicitItems.TryGetValue(item.Id, out var choice);
            if (!chosen && !item.Comprado) continue;

            if (item.EntradaRegistrada) { ignorados.Add(new { itemId = item.Id, item.Texto, motivo = "ja_lancado" }); continue; }
            if (item.IngredienteId is null) { ignorados.Add(new { itemId = item.Id, item.Texto, motivo = "sem_ingrediente" }); continue; }
            if (item.QuantidadeComprar <= 0) { ignorados.Add(new { itemId = item.Id, item.Texto, motivo = "quantidade_zero" }); continue; }

            var result = await stock.Entrada(t.DespensaId, t.UsuarioId, item.IngredienteId.Value, item.QuantidadeComprar,
                item.UnidadeCodigo, choice?.DataValidade, OrigemLote.Lista, $"Compra: {lista.Nome}", null,
                TipoMovimentacao.EntradaCompra, item.Id, key, ct);
            item.Comprado = true;
            item.EntradaRegistrada = true;
            item.MovimentacaoId = result.MovimentacaoIds[0];
            lancados.Add(new { itemId = item.Id, item.Texto, quantidade = item.QuantidadeComprar, unidade = item.UnidadeCodigo });
        }

        if (lista.Itens.Count > 0 && lista.Itens.All(i => i.Comprado)) lista.Status = StatusLista.Comprada;
        await db.SaveChangesAsync(ct);
        return new { lancados, ignorados, status = lista.Status };
    }

    public static object ToDto(ListaCompra l, IReadOnlyDictionary<Guid, string>? names = null) => new
    {
        l.Id, l.Nome, l.Status, l.Origem, l.CriadoEm,
        itens = l.Itens.OrderBy(i => i.Ordem).Select(i => new
        {
            i.Id, i.Ordem, i.IngredienteId, ingredienteNome = i.IngredienteId is { } id && names is not null && names.TryGetValue(id, out var n) ? n : null,
            i.Texto, i.QuantidadeNecessaria, i.QuantidadeDescontadaEstoque, i.QuantidadeComprar, unidade = i.UnidadeCodigo,
            i.Comprado, i.EntradaRegistrada, i.AjusteManual,
        }),
    };
}
