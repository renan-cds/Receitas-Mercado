using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<Usuario, Guid>(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Despensa> Despensas => Set<Despensa>();
    public DbSet<MembroDespensa> Membros => Set<MembroDespensa>();
    public DbSet<ConviteDespensa> Convites => Set<ConviteDespensa>();
    public DbSet<CategoriaIngrediente> Categorias => Set<CategoriaIngrediente>();
    public DbSet<UnidadeMedida> Unidades => Set<UnidadeMedida>();
    public DbSet<ConversaoUnidade> Conversoes => Set<ConversaoUnidade>();
    public DbSet<GrupoEquivalencia> GruposEquivalencia => Set<GrupoEquivalencia>();
    public DbSet<Ingrediente> Ingredientes => Set<Ingrediente>();
    public DbSet<ItemEstoque> ItensEstoque => Set<ItemEstoque>();
    public DbSet<LoteEstoque> Lotes => Set<LoteEstoque>();
    public DbSet<Movimentacao> Movimentacoes => Set<Movimentacao>();
    public DbSet<FonteReceita> FontesReceita => Set<FonteReceita>();
    public DbSet<Receita> Receitas => Set<Receita>();
    public DbSet<ReceitaIngrediente> ReceitaIngredientes => Set<ReceitaIngrediente>();
    public DbSet<EtapaReceita> EtapasReceita => Set<EtapaReceita>();
    public DbSet<ReceitaTag> ReceitaTags => Set<ReceitaTag>();
    public DbSet<Catalogo> Catalogos => Set<Catalogo>();
    public DbSet<CatalogoReceita> CatalogoReceitas => Set<CatalogoReceita>();
    public DbSet<Favorito> Favoritos => Set<Favorito>();
    public DbSet<Preparo> Preparos => Set<Preparo>();
    public DbSet<PreparoItemSnapshot> PreparoItens => Set<PreparoItemSnapshot>();
    public DbSet<ListaCompra> ListasCompra => Set<ListaCompra>();
    public DbSet<ItemListaCompra> ItensListaCompra => Set<ItemListaCompra>();
    public DbSet<ConfiguracaoIa> ConfiguracoesIa => Set<ConfiguracaoIa>();
    public DbSet<ExecucaoIa> ExecucoesIa => Set<ExecucaoIa>();
    public DbSet<Arquivo> Arquivos => Set<Arquivo>();
    public DbSet<Idempotencia> Idempotencias => Set<Idempotencia>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<decimal>().HavePrecision(14, 3);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.Property(x => x.NomeExibicao).HasMaxLength(80);
        });
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("usuario_claims");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("usuario_logins");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("usuario_tokens");

        b.Entity<Despensa>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(80);
            e.Property(x => x.Fuso).HasMaxLength(64);
            e.ToTable(t => t.HasCheckConstraint("ck_despensas_alerta", "alerta_validade_dias between 1 and 90"));
        });

        b.Entity<MembroDespensa>(e =>
        {
            e.HasOne(x => x.Despensa).WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DespensaId, x.UsuarioId }).IsUnique();
            e.HasIndex(x => new { x.UsuarioId, x.Status });
        });

        b.Entity<ConviteDespensa>(e =>
        {
            e.HasOne(x => x.Despensa).WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        b.Entity<CategoriaIngrediente>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(60);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DespensaId, x.NomeNormalizado }).IsUnique();
        });

        b.Entity<UnidadeMedida>(e =>
        {
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.HasData(UnitCatalog.All.Select(u => new UnidadeMedida
            {
                Codigo = u.Code, Nome = u.Name, Abreviacao = u.Abbreviation, Dimensao = u.Dimension, Aproximada = u.Approximate,
            }));
        });

        b.Entity<ConversaoUnidade>(e =>
        {
            e.HasKey(x => new { x.DeCodigo, x.ParaCodigo });
            e.Property(x => x.Fator).HasPrecision(18, 9);
            e.HasOne<UnidadeMedida>().WithMany().HasForeignKey(x => x.DeCodigo);
            e.HasOne<UnidadeMedida>().WithMany().HasForeignKey(x => x.ParaCodigo);
            e.HasData(UnitCatalog.Conversions().Select(c => new ConversaoUnidade
            {
                DeCodigo = c.From, ParaCodigo = c.To, Fator = decimal.Round(c.Factor, 9), Aproximada = c.Approximate,
            }));
        });

        b.Entity<GrupoEquivalencia>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(80);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DespensaId, x.NomeNormalizado }).IsUnique();
        });

        b.Entity<Ingrediente>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.UnidadeReferenciaCodigo).HasMaxLength(20);
            e.Property(x => x.LocalArmazenamento).HasMaxLength(80);
            e.Property(x => x.Observacoes).HasMaxLength(2000);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Categoria).WithMany().HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.GrupoEquivalencia).WithMany().HasForeignKey(x => x.GrupoEquivalenciaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<UnidadeMedida>().WithMany().HasForeignKey(x => x.UnidadeReferenciaCodigo).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.DespensaId, x.NomeNormalizado }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_ingredientes_minimo", "quantidade_minima >= 0"));
        });

        b.Entity<ItemEstoque>(e =>
        {
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Ingrediente).WithOne(x => x.ItemEstoque).HasForeignKey<ItemEstoque>(x => x.IngredienteId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.IngredienteId).IsUnique();
            e.HasIndex(x => new { x.DespensaId, x.QuantidadeTotal });
            e.ToTable(t => t.HasCheckConstraint("ck_itens_estoque_total", "quantidade_total >= 0"));
        });

        b.Entity<LoteEstoque>(e =>
        {
            e.HasOne<ItemEstoque>().WithMany(x => x.Lotes).HasForeignKey(x => x.ItemEstoqueId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.HasIndex(x => new { x.ItemEstoqueId, x.Status, x.DataValidade });
            e.HasIndex(x => x.DataValidade).HasFilter("status = 'Aberto' and data_validade is not null");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_lotes_quantidade", "quantidade >= 0");
                t.HasCheckConstraint("ck_lotes_inicial", "quantidade_inicial > 0");
            });
        });

        b.Entity<Movimentacao>(e =>
        {
            e.Property(x => x.Motivo).HasMaxLength(500);
            e.Property(x => x.UnidadeCodigo).HasMaxLength(20);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ItemEstoque>().WithMany().HasForeignKey(x => x.ItemEstoqueId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<LoteEstoque>().WithMany().HasForeignKey(x => x.LoteEstoqueId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.DespensaId, x.CriadoEm });
            e.HasIndex(x => new { x.ItemEstoqueId, x.CriadoEm });
            e.HasIndex(x => x.PreparoId);
            e.HasIndex(x => x.IdempotencyKey).HasFilter("idempotency_key is not null");
            e.ToTable(t => t.HasCheckConstraint("ck_movimentacoes_quantidade", "quantidade > 0 and sentido in (-1, 1)"));
        });

        b.Entity<FonteReceita>(e =>
        {
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.HasData(
                new FonteReceita { Codigo = "manual", Nome = "Cadastro manual" },
                new FonteReceita { Codigo = "themealdb", Nome = "TheMealDB", UrlTermos = "https://www.themealdb.com/api.php" },
                new FonteReceita { Codigo = "jsonld", Nome = "Importação por URL (schema.org/Recipe)" },
                new FonteReceita { Codigo = "ia", Nome = "Sugestão de IA" });
        });

        b.Entity<Receita>(e =>
        {
            e.Property(x => x.Titulo).HasMaxLength(200);
            e.Property(x => x.Descricao).HasMaxLength(2000);
            e.Property(x => x.FonteCodigo).HasMaxLength(20);
            e.Property(x => x.UrlOrigem).HasMaxLength(2048);
            e.Property(x => x.ImagemUrl).HasMaxLength(2048);
            e.Property(x => x.AutorAtribuicao).HasMaxLength(120);
            e.Property(x => x.Observacoes).HasMaxLength(4000);
            e.Property(x => x.Porcoes).HasPrecision(8, 2);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FonteReceita>().WithMany().HasForeignKey(x => x.FonteCodigo).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Arquivo>().WithMany().HasForeignKey(x => x.ImagemArquivoId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.DespensaId, x.Status });
            e.ToTable(t => t.HasCheckConstraint("ck_receitas_porcoes", "porcoes > 0"));
        });

        b.Entity<ReceitaIngrediente>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(200);
            e.Property(x => x.TextoOriginal).HasMaxLength(400);
            e.Property(x => x.UnidadeCodigo).HasMaxLength(20);
            e.HasOne<Receita>().WithMany(x => x.Ingredientes).HasForeignKey(x => x.ReceitaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Ingrediente>().WithMany().HasForeignKey(x => x.IngredienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<UnidadeMedida>().WithMany().HasForeignKey(x => x.UnidadeCodigo).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.ReceitaId);
            e.HasIndex(x => x.IngredienteId);
        });

        b.Entity<EtapaReceita>(e =>
        {
            e.Property(x => x.Instrucao).HasMaxLength(4000);
            e.HasOne<Receita>().WithMany(x => x.Etapas).HasForeignKey(x => x.ReceitaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ReceitaId, x.Ordem }).IsUnique();
        });

        b.Entity<ReceitaTag>(e =>
        {
            e.HasKey(x => new { x.ReceitaId, x.Tag });
            e.Property(x => x.Tag).HasMaxLength(40);
            e.HasOne<Receita>().WithMany(x => x.Tags).HasForeignKey(x => x.ReceitaId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Catalogo>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(80);
            e.Property(x => x.Descricao).HasMaxLength(1000);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DespensaId, x.UsuarioId, x.NomeNormalizado }).IsUnique().HasFilter("visibilidade = 'Pessoal'");
            e.HasIndex(x => new { x.DespensaId, x.NomeNormalizado }).IsUnique().HasFilter("visibilidade = 'Despensa'");
            e.ToTable(t => t.HasCheckConstraint("ck_catalogos_dono",
                "(visibilidade = 'Pessoal' and usuario_id is not null) or visibilidade = 'Despensa'"));
        });

        b.Entity<CatalogoReceita>(e =>
        {
            e.HasKey(x => new { x.CatalogoId, x.ReceitaId });
            e.HasOne<Catalogo>().WithMany(x => x.Receitas).HasForeignKey(x => x.CatalogoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Receita>().WithMany().HasForeignKey(x => x.ReceitaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ReceitaId);
        });

        b.Entity<Favorito>(e =>
        {
            e.HasKey(x => new { x.UsuarioId, x.ReceitaId });
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Receita>().WithMany().HasForeignKey(x => x.ReceitaId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Preparo>(e =>
        {
            e.Property(x => x.ReceitaTitulo).HasMaxLength(200);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Porcoes).HasPrecision(8, 2);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Receita>().WithMany().HasForeignKey(x => x.ReceitaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.DespensaId, x.CriadoEm });
            e.HasIndex(x => new { x.DespensaId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key is not null");
        });

        b.Entity<PreparoItemSnapshot>(e =>
        {
            e.Property(x => x.Texto).HasMaxLength(200);
            e.Property(x => x.UnidadeCodigo).HasMaxLength(20);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.Property(x => x.BaixasJson).HasColumnType("jsonb");
            e.HasOne<Preparo>().WithMany(x => x.Itens).HasForeignKey(x => x.PreparoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.PreparoId);
        });

        b.Entity<ListaCompra>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(120);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DespensaId, x.Status });
        });

        b.Entity<ItemListaCompra>(e =>
        {
            e.Property(x => x.Texto).HasMaxLength(200);
            e.Property(x => x.UnidadeCodigo).HasMaxLength(20);
            e.HasOne<ListaCompra>().WithMany(x => x.Itens).HasForeignKey(x => x.ListaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Ingrediente>().WithMany().HasForeignKey(x => x.IngredienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ListaId);
            e.ToTable(t => t.HasCheckConstraint("ck_itens_lista_quantidades",
                "quantidade_necessaria >= 0 and quantidade_comprar >= 0 and quantidade_descontada_estoque >= 0"));
        });

        b.Entity<ConfiguracaoIa>(e =>
        {
            e.HasKey(x => x.DespensaId);
            e.Property(x => x.BaseUrl).HasMaxLength(2048);
            e.Property(x => x.Modelo).HasMaxLength(120);
            e.HasOne<Despensa>().WithOne().HasForeignKey<ConfiguracaoIa>(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_config_ia_limite", "limite_hora between 1 and 60"));
        });

        b.Entity<ExecucaoIa>(e =>
        {
            e.Property(x => x.Modelo).HasMaxLength(120);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ErroCodigo).HasMaxLength(60);
            e.Property(x => x.PromptHash).HasMaxLength(64);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DespensaId, x.CriadoEm });
        });

        b.Entity<Arquivo>(e =>
        {
            e.Property(x => x.NomeOriginal).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(60);
            e.Property(x => x.CaminhoRelativo).HasMaxLength(300);
            e.HasOne<Despensa>().WithMany().HasForeignKey(x => x.DespensaId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Idempotencia>(e =>
        {
            e.Property(x => x.Rota).HasMaxLength(200);
            e.Property(x => x.Chave).HasMaxLength(100);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.HasIndex(x => new { x.UsuarioId, x.Rota, x.Chave }).IsUnique();
            e.HasIndex(x => x.CriadoEm);
        });

        foreach (var entityType in b.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid) && entityType.ClrType != typeof(Usuario))
                key.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }
}
