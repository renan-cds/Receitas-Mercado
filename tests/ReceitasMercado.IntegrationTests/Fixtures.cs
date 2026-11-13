namespace ReceitasMercado.IntegrationTests;

public sealed class FactoryFixture : IDisposable
{
    public AppFactory Factory { get; } = new();
    public void Dispose() => Factory.Dispose();
}
