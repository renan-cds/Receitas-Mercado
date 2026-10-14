using System.ComponentModel.DataAnnotations;
using ReceitasMercado.Api.Data;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Contracts;

public sealed class RegistrarRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 10, ErrorMessage = "A senha deve ter pelo menos 10 caracteres.")] public string Senha { get; init; } = "";
    [Required, StringLength(80, MinimumLength = 1)] public string Nome { get; init; } = "";
}

public sealed class EntrarRequest
{
    [Required, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128)] public string Senha { get; init; } = "";
}

public sealed class AtualizarPerfilRequest
{
    [Required, StringLength(80, MinimumLength = 1)] public string Nome { get; init; } = "";
    public Guid? DespensaAtivaId { get; init; }
}

public sealed class AlterarSenhaRequest
{
    [Required, StringLength(128)] public string Atual { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 10, ErrorMessage = "A nova senha deve ter pelo menos 10 caracteres.")] public string Nova { get; init; } = "";
}

public sealed class ExcluirContaRequest
{
    [Required, StringLength(128)] public string Senha { get; init; } = "";
}

public sealed class DespensaAtualizarRequest
{
    [Required, StringLength(80, MinimumLength = 1)] public string Nome { get; init; } = "";
    [Required, StringLength(64)] public string Fuso { get; init; } = "America/Sao_Paulo";
    [Range(1, 90)] public int AlertaValidadeDias { get; init; } = 7;
}

public sealed class ExcluirDespensaRequest
{
    [Required, StringLength(80)] public string ConfirmacaoNome { get; init; } = "";
}

public sealed class ConviteCriarRequest
{
    public Papel Papel { get; init; } = Papel.Editor;
    [Range(1, 30)] public int ValidadeDias { get; init; } = 7;
}

public sealed class MembroAtualizarRequest
{
    public Papel Papel { get; init; }
}

public sealed class NomeRequest
{
    [Required, StringLength(80, MinimumLength = 1)] public string Nome { get; init; } = "";
}

public sealed class IngredienteInput
{
    [Required, StringLength(120, MinimumLength = 1)] public string Nome { get; init; } = "";
    public Guid CategoriaId { get; init; }
    [Required, StringLength(20)] public string UnidadeReferencia { get; init; } = "UN";
    public Guid? GrupoEquivalenciaId { get; init; }
    [Range(typeof(decimal), "0", "10000000")] public decimal QuantidadeMinima { get; init; }
    [StringLength(80)] public string? LocalArmazenamento { get; init; }
    [StringLength(2000)] public string? Observacoes { get; init; }
}

public sealed class EntradaRequest
{
    public Guid IngredienteId { get; init; }
    [Range(typeof(decimal), "0.001", "10000000")] public decimal Quantidade { get; init; }
    [StringLength(20)] public string? Unidade { get; init; }
    public DateOnly? DataValidade { get; init; }
    public OrigemLote Origem { get; init; } = OrigemLote.Compra;
    [StringLength(500)] public string? Motivo { get; init; }
    [StringLength(500)] public string? Observacao { get; init; }
}

public sealed class SaidaRequest
{
    public Guid IngredienteId { get; init; }
    [Range(typeof(decimal), "0.001", "10000000")] public decimal Quantidade { get; init; }
    [StringLength(20)] public string? Unidade { get; init; }
    public Guid? LoteId { get; init; }
    [Required, StringLength(500, MinimumLength = 1)] public string Motivo { get; init; } = "";
}

public sealed class CorrecaoRequest
{
    public Guid IngredienteId { get; init; }
    public Guid? LoteId { get; init; }
    [Range(typeof(decimal), "-10000000", "10000000")] public decimal Delta { get; init; }
    [StringLength(20)] public string? Unidade { get; init; }
    [Required, StringLength(500, MinimumLength = 1)] public string Motivo { get; init; } = "";
}

public sealed class ReceitaIngredienteInput
{
    [Required, StringLength(200, MinimumLength = 1)] public string Nome { get; init; } = "";
    [StringLength(400)] public string? TextoOriginal { get; init; }
    public Guid? IngredienteId { get; init; }
    [Range(typeof(decimal), "0", "10000000")] public decimal? Quantidade { get; init; }
    [StringLength(20)] public string? Unidade { get; init; }
    public bool Opcional { get; init; }
    public Confidence? Confianca { get; init; }
}

public sealed class ReceitaInput
{
    [Required, StringLength(200, MinimumLength = 1)] public string Titulo { get; init; } = "";
    [StringLength(2000)] public string? Descricao { get; init; }
    [Range(typeof(decimal), "0.1", "500")] public decimal Porcoes { get; init; } = 4;
    [Range(1, 10080)] public int? TempoPreparoMin { get; init; }
    public Dificuldade? Dificuldade { get; init; }
    public StatusReceita Status { get; init; } = StatusReceita.Completa;
    public OrigemReceita Origem { get; init; } = OrigemReceita.Manual;
    [StringLength(2048)] public string? UrlOrigem { get; init; }
    [StringLength(120)] public string? AutorAtribuicao { get; init; }
    public Guid? ImagemArquivoId { get; init; }
    [StringLength(2048)] public string? ImagemUrl { get; init; }
    [StringLength(4000)] public string? Observacoes { get; init; }
    [MaxLength(10)] public List<string> Tags { get; init; } = [];
    [MaxLength(100)] public List<ReceitaIngredienteInput> Ingredientes { get; init; } = [];
    [MaxLength(100)] public List<string> Etapas { get; init; } = [];
}

public sealed class CatalogoInput
{
    [Required, StringLength(80, MinimumLength = 1)] public string Nome { get; init; } = "";
    [StringLength(1000)] public string? Descricao { get; init; }
    public VisibilidadeCatalogo Visibilidade { get; init; } = VisibilidadeCatalogo.Pessoal;
}

public sealed class CatalogoDuplicarRequest
{
    [StringLength(80)] public string? Nome { get; init; }
    public bool IncluirReceitas { get; init; }
    public VisibilidadeCatalogo? Visibilidade { get; init; }
}

public sealed class PreparoIniciarRequest
{
    public Guid ReceitaId { get; init; }
    [Range(typeof(decimal), "0.1", "500")] public decimal Porcoes { get; init; }
    public bool IncluirOpcionais { get; init; }
}

public sealed class PreparoConfirmarRequest
{
    public bool ConsumirDisponivel { get; init; }
}

public sealed class ListaCriarRequest
{
    [Required, StringLength(120, MinimumLength = 1)] public string Nome { get; init; } = "";
}

public sealed class ListaReceitaItem
{
    public Guid ReceitaId { get; init; }
    [Range(typeof(decimal), "0.1", "500")] public decimal Porcoes { get; init; }
}

public sealed class ListaDeReceitasRequest
{
    [Required, StringLength(120, MinimumLength = 1)] public string Nome { get; init; } = "";
    [Required, MinLength(1), MaxLength(20)] public List<ListaReceitaItem> Receitas { get; init; } = [];
    public bool IncluirOpcionais { get; init; }
}

public sealed class ListaAtualizarRequest
{
    [Required, StringLength(120, MinimumLength = 1)] public string Nome { get; init; } = "";
    public StatusLista Status { get; init; } = StatusLista.Aberta;
}

public sealed class ItemListaInput
{
    [Required, StringLength(200, MinimumLength = 1)] public string Texto { get; init; } = "";
    public Guid? IngredienteId { get; init; }
    [Range(typeof(decimal), "0", "10000000")] public decimal Quantidade { get; init; }
    [Required, StringLength(20)] public string Unidade { get; init; } = "UN";
}

public sealed class ItemListaAtualizarRequest
{
    [Required, StringLength(200, MinimumLength = 1)] public string Texto { get; init; } = "";
    [Range(typeof(decimal), "0", "10000000")] public decimal QuantidadeComprar { get; init; }
    [Required, StringLength(20)] public string Unidade { get; init; } = "UN";
    public bool Comprado { get; init; }
}

public sealed class LancarEntradaItem
{
    public Guid ItemId { get; init; }
    public DateOnly? DataValidade { get; init; }
}

public sealed class LancarEntradasRequest
{
    [MaxLength(300)] public List<LancarEntradaItem> Itens { get; init; } = [];
}

public sealed class ImportarUrlRequest
{
    [Required, StringLength(2048)] public string Url { get; init; } = "";
}

public sealed class IaConfigRequest
{
    [Required, StringLength(2048)] public string BaseUrl { get; init; } = "";
    [Required, StringLength(120)] public string Modelo { get; init; } = "";
    [StringLength(512)] public string? ApiKey { get; init; }
    public bool RemoverChave { get; init; }
    public bool Ativa { get; init; } = true;
    [Range(1, 60)] public int LimiteHora { get; init; } = 20;
}

public sealed class IaSugerirRequest
{
    public bool PriorizarValidade { get; init; } = true;
    [Range(1, 5)] public int Quantidade { get; init; } = 3;
    [StringLength(300)] public string? Observacoes { get; init; }
}

public sealed class IaGerarRequest
{
    [Required, MinLength(1), MaxLength(30)] public List<string> Ingredientes { get; init; } = [];
    [Range(typeof(decimal), "0.5", "100")] public decimal Porcoes { get; init; } = 4;
    [StringLength(300)] public string? Observacoes { get; init; }
}

public sealed class IaNormalizarRequest
{
    [Required, MinLength(1), MaxLength(60)] public List<string> Linhas { get; init; } = [];
}

public sealed class IaSubstituicoesRequest
{
    [Required, StringLength(120, MinimumLength = 1)] public string Ingrediente { get; init; } = "";
    public Guid? ReceitaId { get; init; }
}

public sealed class IaAdaptarRequest
{
    public Guid ReceitaId { get; init; }
    [Range(typeof(decimal), "0.5", "100")] public decimal Porcoes { get; init; }
    public bool ReescreverEtapas { get; init; }
}
