namespace ReceitasMercado.Api.Integrations;

public interface IFileStorage
{
    Task SaveAsync(string relativePath, Stream content, CancellationToken ct);
    Stream? Open(string relativePath);
    void Delete(string relativePath);
    void DeleteDirectory(string relativeDirectory);
}

public sealed class DiskFileStorage(string root) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(root);

    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Caminho fora do diretório de arquivos.");
        return full;
    }

    public async Task SaveAsync(string relativePath, Stream content, CancellationToken ct)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Stream? Open(string relativePath)
    {
        var path = Resolve(relativePath);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(string relativePath)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path)) File.Delete(path);
    }

    public void DeleteDirectory(string relativeDirectory)
    {
        var path = Resolve(relativeDirectory + Path.DirectorySeparatorChar + "x");
        var dir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
