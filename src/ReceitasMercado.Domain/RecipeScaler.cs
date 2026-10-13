namespace ReceitasMercado.Domain;

public static class RecipeScaler
{
    public static decimal Scale(decimal quantity, decimal originalServings, decimal desiredServings)
    {
        if (originalServings <= 0) throw new ArgumentOutOfRangeException(nameof(originalServings));
        if (desiredServings <= 0) throw new ArgumentOutOfRangeException(nameof(desiredServings));
        return UnitCatalog.Round(quantity * desiredServings / originalServings);
    }
}
