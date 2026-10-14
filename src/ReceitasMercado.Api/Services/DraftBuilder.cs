using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Services;

public sealed class DraftBuilder(AppDbContext db)
{
    public async Task<List<IngredientCandidate>> Candidates(Guid despensaId, CancellationToken ct) =>
        await db.Ingredientes.AsNoTracking()
            .Where(i => i.DespensaId == despensaId && i.ArquivadoEm == null)
            .Select(i => new IngredientCandidate(i.Id, i.Nome))
            .ToListAsync(ct);

    public static object Line(
        string nome, string? texto, decimal? qty, string? unit, bool optional, IReadOnlyList<IngredientCandidate> candidates,
        bool capAtInferred = false)
    {
        var match = IngredientMatcher.Match(nome, candidates);
        var confidence = capAtInferred && match.Confidence == Confidence.Confirmada ? Confidence.Inferida : match.Confidence;
        var names = candidates.ToDictionary(c => c.Id, c => c.Name);
        return new
        {
            nome, textoOriginal = texto, quantidade = qty, unidade = unit, opcional = optional,
            ingredienteId = match.BestId, confianca = confidence,
            candidatos = match.OtherCandidates.Take(5).Select(id => new { id, nome = names[id] }),
        };
    }

    public object FromImported(ImportedRecipe r, OrigemReceita origem, IReadOnlyList<IngredientCandidate> candidates)
    {
        var avisos = new List<string>();
        var lines = r.IngredientLines.Select(text =>
        {
            var parsed = IngredientTextParser.Parse(text);
            return Line(parsed.Name, text, parsed.Quantity, parsed.UnitCode, parsed.Optional, candidates, capAtInferred: true);
        }).ToList();

        if (r.Servings is null) avisos.Add("O rendimento não foi informado pela fonte; ajustamos para 4 porções. Revise antes de salvar.");
        if (r.Steps.Count == 0) avisos.Add("A fonte não trouxe o modo de preparo; a receita será salva como rascunho até você adicionar etapas.");
        avisos.Add("Confira os ingredientes vinculados ao estoque: correspondências automáticas precisam da sua confirmação.");

        return new
        {
            titulo = r.Title, descricao = r.Description, porcoes = r.Servings ?? 4m, tempoPreparoMin = r.PrepMinutes,
            imagemUrl = r.ImageUrl, autorAtribuicao = r.Author, urlOrigem = r.SourceUrl, origem,
            status = r.Steps.Count > 0 && lines.Count > 0 ? StatusReceita.Completa : StatusReceita.Rascunho,
            tags = r.Tags, etapas = r.Steps, ingredientes = lines, avisos,
        };
    }

    public object FromAi(AiRecipe r, IReadOnlyList<IngredientCandidate> candidates)
    {
        var lines = r.Ingredients.Select(i =>
        {
            var line = Line(i.Name, null, i.Quantity, i.UnitCode, false, candidates, capAtInferred: true);
            return line;
        }).ToList();

        return new
        {
            titulo = r.Title, descricao = r.Description, porcoes = r.Servings, tempoPreparoMin = r.PrepMinutes,
            imagemUrl = (string?)null, autorAtribuicao = "Sugestão de IA", urlOrigem = (string?)null,
            origem = OrigemReceita.Ia, status = StatusReceita.Completa, tags = r.Tags, etapas = r.Steps, ingredientes = lines,
            avisos = new[]
            {
                "Receita gerada por IA: quantidades, tempos e etapas são inferidos e podem conter erros. Revise tudo antes de salvar.",
                "Os vínculos com o estoque são sugestões e só valem depois da sua confirmação.",
            },
        };
    }
}
