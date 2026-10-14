using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Contracts;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Services;

public sealed record IngredientInfo(Guid Id, string Nome, string UnidadeReferencia, Guid? GrupoId);

public sealed record CompatPartDto(Guid IngredienteId, string Nome, decimal QuantidadeNaUnidadeDoEstoque, string UnidadeEstoque, bool Aproximado, bool Substituto);

public sealed record CompatLineDto(
    int Ordem, Guid LinhaId, string Nome, Guid? IngredienteId, string? IngredienteNome, decimal? Quantidade, string? Unidade,
    bool Opcional, Confidence Confianca, LineStatus Status, decimal Coberto, decimal Faltante, bool UsaEquivalente,
    IReadOnlyList<CompatPartDto> Partes);

public sealed record CompatDto(
    CompatibilityState Estado, decimal Percentual, int ObrigatoriosTotal, int ObrigatoriosOk, string Aviso, decimal Porcoes,
    IReadOnlyList<CompatLineDto> Linhas);

public sealed class RecipeService(AppDbContext db, StockService stock)
{
    public static string SourceCode(OrigemReceita origem) => origem switch
    {
        OrigemReceita.Busca => "themealdb",
        OrigemReceita.Url => "jsonld",
        OrigemReceita.Ia => "ia",
        _ => "manual",
    };

    public async Task<Receita> Create(Guid despensaId, Guid userId, ReceitaInput input, CancellationToken ct)
    {
        var receita = new Receita { DespensaId = despensaId, CriadoPorUsuarioId = userId };
        await Apply(receita, input, ct);
        db.Receitas.Add(receita);
        await db.SaveChangesAsync(ct);
        return receita;
    }

    public async Task<Receita> Update(Guid despensaId, Guid id, ReceitaInput input, CancellationToken ct)
    {
        var receita = await Load(despensaId, id, ct);
        db.ReceitaIngredientes.RemoveRange(receita.Ingredientes);
        db.EtapasReceita.RemoveRange(receita.Etapas);
        db.ReceitaTags.RemoveRange(receita.Tags);
        receita.Ingredientes = [];
        receita.Etapas = [];
        receita.Tags = [];
        await db.SaveChangesAsync(ct);
        await Apply(receita, input, ct);
        await db.SaveChangesAsync(ct);
        return receita;
    }

    public async Task<Receita> Load(Guid despensaId, Guid id, CancellationToken ct) =>
        await db.Receitas
            .Include(r => r.Ingredientes).Include(r => r.Etapas).Include(r => r.Tags)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id && r.DespensaId == despensaId, ct)
        ?? throw BusinessException.NotFound("Receita");

    private async Task Apply(Receita receita, ReceitaInput input, CancellationToken ct)
    {
        var titulo = input.Titulo.Trim();
        if (titulo.Length == 0) throw BusinessException.BadRequest("VALIDACAO", "Informe o título da receita.");
        var etapas = input.Etapas.Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
        if (etapas.Any(e => e.Length > 4000))
            throw BusinessException.BadRequest("VALIDACAO", "Cada etapa pode ter até 4000 caracteres.");

        if (input.Status == StatusReceita.Completa && (input.Ingredientes.Count == 0 || etapas.Count == 0))
            throw BusinessException.BadRequest("RECEITA_INCOMPLETA",
                "Uma receita completa precisa de ao menos um ingrediente e uma etapa. Salve como rascunho para continuar depois.");

        CheckHttpUrl(input.UrlOrigem, "A URL de origem");
        CheckHttpUrl(input.ImagemUrl, "A URL da imagem");

        var ingredientIds = input.Ingredientes.Where(i => i.IngredienteId.HasValue).Select(i => i.IngredienteId!.Value).Distinct().ToArray();
        if (ingredientIds.Length > 0)
        {
            var found = await db.Ingredientes.Where(i => i.DespensaId == receita.DespensaId && ingredientIds.Contains(i.Id))
                .Select(i => i.Id).ToListAsync(ct);
            if (found.Count != ingredientIds.Length)
                throw BusinessException.BadRequest("INGREDIENTE_INVALIDO", "Algum ingrediente vinculado não existe nesta despensa.");
        }

        if (input.ImagemArquivoId is { } fileId &&
            !await db.Arquivos.AnyAsync(a => a.Id == fileId && a.DespensaId == receita.DespensaId, ct))
            throw BusinessException.BadRequest("ARQUIVO_INVALIDO", "A imagem informada não existe nesta despensa.");

        var tags = input.Tags.Select(TextNormalizer.Normalize).Where(t => t.Length is > 0 and <= 40).Distinct().ToList();

        var ordem = 0;
        var lines = new List<ReceitaIngrediente>();
        foreach (var line in input.Ingredientes)
        {
            var unit = string.IsNullOrWhiteSpace(line.Unidade) ? null : line.Unidade.Trim().ToUpperInvariant();
            if (unit is not null && !UnitCatalog.Exists(unit))
                throw BusinessException.BadRequest("UNIDADE_INVALIDA", $"Unidade desconhecida: {line.Unidade}.");
            if (line.Quantidade is > 0 && unit is null) unit = "UN";
            if (line.Quantidade is null) unit = null;

            var confidence = line.IngredienteId is null
                ? Confidence.Incerta
                : line.Confianca ?? Confidence.Confirmada;
            lines.Add(new ReceitaIngrediente
            {
                ReceitaId = receita.Id, Ordem = ordem++, Nome = line.Nome.Trim(), TextoOriginal = line.TextoOriginal?.Trim(),
                IngredienteId = line.IngredienteId, Quantidade = line.Quantidade, UnidadeCodigo = unit,
                Opcional = line.Opcional, Confianca = confidence,
            });
        }

        receita.Titulo = titulo;
        receita.Descricao = input.Descricao?.Trim();
        receita.Porcoes = input.Porcoes;
        receita.TempoPreparoMin = input.TempoPreparoMin;
        receita.Dificuldade = input.Dificuldade;
        receita.Status = input.Status;
        receita.Origem = input.Origem;
        receita.FonteCodigo = SourceCode(input.Origem);
        receita.UrlOrigem = input.UrlOrigem?.Trim();
        receita.AutorAtribuicao = input.AutorAtribuicao?.Trim();
        receita.ImagemArquivoId = input.ImagemArquivoId;
        receita.ImagemUrl = input.ImagemUrl?.Trim();
        receita.Observacoes = input.Observacoes?.Trim();
        receita.AtualizadoEm = DateTimeOffset.UtcNow;
        receita.Ingredientes = lines;
        receita.Etapas = etapas.Select((e, i) => new EtapaReceita { ReceitaId = receita.Id, Ordem = i, Instrucao = e }).ToList();
        receita.Tags = tags.Select(t => new ReceitaTag { ReceitaId = receita.Id, Tag = t }).ToList();
        receita.BuscaTexto = TextNormalizer.Normalize(
            $"{titulo} {receita.Descricao} {string.Join(' ', tags)} {string.Join(' ', lines.Select(l => l.Nome))}");
    }

    private static void CheckHttpUrl(string? url, string label)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw BusinessException.BadRequest("URL_INVALIDA", $"{label} deve começar com http:// ou https://.");
    }

    public async Task<Dictionary<Guid, IngredientInfo>> IngredientInfo(Guid despensaId, CancellationToken ct) =>
        await db.Ingredientes.AsNoTracking().Where(i => i.DespensaId == despensaId)
            .Select(i => new IngredientInfo(i.Id, i.Nome, i.UnidadeReferenciaCodigo, i.GrupoEquivalenciaId))
            .ToDictionaryAsync(i => i.Id, ct);

    public static List<Requirement> BuildRequirements(
        IReadOnlyList<ReceitaIngrediente> lines, decimal originalServings, decimal desiredServings, bool includeOptionals = true)
    {
        var ordered = lines.OrderBy(l => l.Ordem).Where(l => includeOptionals || !l.Opcional).ToList();
        return ordered.Select((l, i) => new Requirement(
            i, l.IngredienteId,
            l.Quantidade.HasValue ? RecipeScaler.Scale(l.Quantidade.Value, originalServings, desiredServings) : null,
            l.UnidadeCodigo, l.Opcional, l.Confianca)).ToList();
    }

    public CompatDto Analyze(
        Receita receita, decimal porcoes, IReadOnlyList<StockEntry> stockEntries,
        IReadOnlyDictionary<Guid, IngredientInfo> info, bool includeOptionals = true)
    {
        var ordered = receita.Ingredientes.OrderBy(l => l.Ordem).Where(l => includeOptionals || !l.Opcional).ToList();
        var requirements = BuildRequirements(receita.Ingredientes, receita.Porcoes, porcoes, includeOptionals);
        var result = StockPlanner.Evaluate(requirements, stockEntries, receita.Status == StatusReceita.Rascunho);

        var lines = result.Lines.Select(plan =>
        {
            var line = ordered[plan.Index];
            return new CompatLineDto(
                line.Ordem, line.Id, line.Nome, line.IngredienteId,
                line.IngredienteId is { } id && info.TryGetValue(id, out var ing) ? ing.Nome : null,
                requirements[plan.Index].Quantity, line.UnidadeCodigo, line.Opcional, line.Confianca, plan.Status,
                UnitCatalog.Round(plan.Covered), UnitCatalog.Round(plan.Shortage), plan.UsedEquivalent,
                plan.Parts.Select(p => new CompatPartDto(
                    p.IngredientId, info.TryGetValue(p.IngredientId, out var pi) ? pi.Nome : "?",
                    p.QuantityInRefUnit, pi?.UnidadeReferencia ?? "", p.Approximate, p.IsSubstitute)).ToList());
        }).ToList();

        return new CompatDto(result.State, result.Percent, result.RequiredTotal, result.RequiredOk,
            StockPlanner.PercentDisclaimer, porcoes, lines);
    }

    public static object ToDetail(Receita r, bool favorito) => new
    {
        r.Id, r.Titulo, r.Descricao, r.Porcoes, r.TempoPreparoMin, r.Dificuldade, r.Status, r.Origem,
        fonteCodigo = r.FonteCodigo, r.UrlOrigem, r.AutorAtribuicao, r.ImagemArquivoId, r.ImagemUrl, r.Observacoes,
        r.CriadoEm, r.AtualizadoEm, favorito,
        tags = r.Tags.Select(t => t.Tag).OrderBy(t => t).ToList(),
        ingredientes = r.Ingredientes.OrderBy(i => i.Ordem).Select(i => new
        {
            i.Id, i.Ordem, i.Nome, i.TextoOriginal, i.IngredienteId, i.Quantidade, unidade = i.UnidadeCodigo, i.Opcional, i.Confianca,
        }),
        etapas = r.Etapas.OrderBy(e => e.Ordem).Select(e => new { e.Ordem, e.Instrucao }),
    };
}
