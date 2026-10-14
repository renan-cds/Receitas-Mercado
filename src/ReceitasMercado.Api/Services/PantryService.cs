using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Api.Infrastructure;
using ReceitasMercado.Api.Integrations;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Services;

public sealed class PantryService(AppDbContext db, IFileStorage files, TimeProvider clock)
{
    public const int MaxMembers = 10;

    private static readonly string[] DefaultCategories =
        ["Grãos e cereais", "Laticínios", "Proteínas", "Hortifrúti", "Temperos", "Mercearia", "Congelados", "Bebidas", "Outros"];

    public Despensa CreateWithAdmin(Guid userId, string nome)
    {
        var despensa = new Despensa { Nome = nome, CriadoPorUsuarioId = userId, CriadoEm = clock.GetUtcNow() };
        db.Despensas.Add(despensa);
        db.Membros.Add(new MembroDespensa { DespensaId = despensa.Id, UsuarioId = userId, Papel = Papel.Admin });
        var ordem = 0;
        foreach (var name in DefaultCategories)
        {
            db.Categorias.Add(new CategoriaIngrediente
            {
                DespensaId = despensa.Id, Nome = name, NomeNormalizado = TextNormalizer.Normalize(name), Ordem = ordem++,
            });
        }
        return despensa;
    }

    public async Task<int> ActiveMemberCount(Guid despensaId, CancellationToken ct) =>
        await db.Membros.CountAsync(m => m.DespensaId == despensaId && m.Status == StatusMembro.Ativo, ct);

    public async Task<int> ActiveAdminCount(Guid despensaId, CancellationToken ct) =>
        await db.Membros.CountAsync(m => m.DespensaId == despensaId && m.Status == StatusMembro.Ativo && m.Papel == Papel.Admin, ct);

    /// <summary>
    /// Encerra a participação de um usuário: dados compartilhados ficam na despensa (histórico preserva o usuário),
    /// dados pessoais ligados a ela (catálogos pessoais e favoritos) são apagados.
    /// </summary>
    public async Task RemoveMember(Guid despensaId, Guid userId, CancellationToken ct)
    {
        var membership = await db.Membros.FirstOrDefaultAsync(
            m => m.DespensaId == despensaId && m.UsuarioId == userId && m.Status == StatusMembro.Ativo, ct)
            ?? throw BusinessException.NotFound("Membro");

        if (membership.Papel == Papel.Admin && await ActiveAdminCount(despensaId, ct) <= 1)
            throw BusinessException.Conflict("ULTIMO_ADMIN",
                "Esta pessoa é a única administradora. Promova outro membro a administrador ou exclua a despensa.");

        membership.Status = StatusMembro.Removido;
        membership.SaiuEm = clock.GetUtcNow();

        await db.Catalogos
            .Where(c => c.DespensaId == despensaId && c.UsuarioId == userId && c.Visibilidade == VisibilidadeCatalogo.Pessoal)
            .ExecuteDeleteAsync(ct);
        await db.Favoritos
            .Where(f => f.UsuarioId == userId && db.Receitas.Any(r => r.Id == f.ReceitaId && r.DespensaId == despensaId))
            .ExecuteDeleteAsync(ct);

        var activeElsewhere = await db.Usuarios.Where(u => u.Id == userId && u.DespensaAtivaId == despensaId).AnyAsync(ct);
        if (activeElsewhere)
        {
            var next = await db.Membros
                .Where(m => m.UsuarioId == userId && m.Status == StatusMembro.Ativo && m.DespensaId != despensaId)
                .Select(m => (Guid?)m.DespensaId).FirstOrDefaultAsync(ct);
            await db.Usuarios.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DespensaAtivaId, next), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeletePantry(Guid despensaId, CancellationToken ct)
    {
        await db.Usuarios.Where(u => u.DespensaAtivaId == despensaId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DespensaAtivaId, (Guid?)null), ct);
        await db.Despensas.Where(d => d.Id == despensaId).ExecuteDeleteAsync(ct);
        files.DeleteDirectory(despensaId.ToString("N"));
    }
}
