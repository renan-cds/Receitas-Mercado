namespace ReceitasMercado.Domain;

public sealed record IngredientCandidate(Guid Id, string Name);

public sealed record MatchResult(Guid? BestId, Confidence Confidence, IReadOnlyList<Guid> OtherCandidates);

public static class IngredientMatcher
{
    private static readonly HashSet<string> StopWords = ["de", "da", "do", "das", "dos", "e", "a", "o", "of", "the", "com"];

    private static List<string> Tokens(string value) =>
        TextNormalizer.Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !StopWords.Contains(t))
            .Select(Stem)
            .ToList();

    private static string Stem(string token)
    {
        if (token.Length > 4 && token.EndsWith("oes")) return token[..^3] + "ao";
        if (token.Length > 4 && token.EndsWith("es")) return token[..^2];
        if (token.Length > 3 && token.EndsWith('s')) return token[..^1];
        return token;
    }

    /// <summary>
    /// Correspondência determinística. Só igualdade de termos vira "inferida";
    /// inclusão parcial apenas lista candidatos e o item continua incerto até confirmação humana.
    /// </summary>
    public static MatchResult Match(string name, IEnumerable<IngredientCandidate> candidates)
    {
        var target = Tokens(name);
        if (target.Count == 0) return new MatchResult(null, Confidence.Incerta, []);

        var exact = new List<Guid>();
        var partial = new List<Guid>();
        foreach (var candidate in candidates)
        {
            var tokens = Tokens(candidate.Name);
            if (tokens.Count == 0) continue;
            if (tokens.Count == target.Count && tokens.OrderBy(t => t).SequenceEqual(target.OrderBy(t => t)))
                exact.Add(candidate.Id);
            else if (tokens.All(target.Contains) || target.All(tokens.Contains))
                partial.Add(candidate.Id);
        }

        if (exact.Count == 1) return new MatchResult(exact[0], Confidence.Inferida, partial);
        return new MatchResult(null, Confidence.Incerta, exact.Concat(partial).ToList());
    }
}
