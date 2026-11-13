using Microsoft.AspNetCore.Identity;
using ReceitasMercado.Domain;

namespace ReceitasMercado.Api.Data;

public enum Papel { Leitor = 0, Editor = 1, Admin = 2 }
public enum StatusMembro { Ativo, Removido }
public enum OrigemLote { Compra, Ajuste, Lista, Outro }
public enum StatusLote { Aberto, Esgotado, Descartado }
public enum TipoMovimentacao { Entrada, Consumo, Perda, Correcao, BaixaPreparo, EntradaCompra }
public enum Dificuldade { Facil, Medio, Dificil }
public enum StatusReceita { Rascunho, Completa }
public enum OrigemReceita { Manual, Url, Busca, Ia }
public enum VisibilidadeCatalogo { Pessoal, Despensa }
public enum StatusPreparo { Iniciado, Confirmado, Cancelado }
public enum StatusLista { Aberta, Comprada, Arquivada }
public enum OrigemLista { Manual, Receitas }
public enum TipoExecucaoIa { Teste, SugerirReceitas, GerarReceita, NormalizarIngredientes, Substituicoes, AdaptarEtapas }

public class Usuario : IdentityUser<Guid>
{
    public string NomeExibicao { get; set; } = "";
    public Guid? DespensaAtivaId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class Despensa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    public string Fuso { get; set; } = "America/Sao_Paulo";
    public int AlertaValidadeDias { get; set; } = 7;
    public Guid? CriadoPorUsuarioId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class MembroDespensa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public Guid UsuarioId { get; set; }
    public Papel Papel { get; set; }
    public StatusMembro Status { get; set; } = StatusMembro.Ativo;
    public DateTimeOffset EntrouEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SaiuEm { get; set; }
    public Despensa? Despensa { get; set; }
    public Usuario? Usuario { get; set; }
}

public class ConviteDespensa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public Papel Papel { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? UsadoEm { get; set; }
    public Guid? UsadoPorUsuarioId { get; set; }
    public DateTimeOffset? RevogadoEm { get; set; }
    public Guid? CriadoPorUsuarioId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public Despensa? Despensa { get; set; }
}

public class CategoriaIngrediente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public string Nome { get; set; } = "";
    public string NomeNormalizado { get; set; } = "";
    public int Ordem { get; set; }
}

public class UnidadeMedida
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Abreviacao { get; set; } = "";
    public UnitDimension Dimensao { get; set; }
    public bool Aproximada { get; set; }
}

public class ConversaoUnidade
{
    public string DeCodigo { get; set; } = "";
    public string ParaCodigo { get; set; } = "";
    public decimal Fator { get; set; }
    public bool Aproximada { get; set; }
}

public class GrupoEquivalencia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public string Nome { get; set; } = "";
    public string NomeNormalizado { get; set; } = "";
}

public class Ingrediente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public string Nome { get; set; } = "";
    public string NomeNormalizado { get; set; } = "";
    public Guid CategoriaId { get; set; }
    public string UnidadeReferenciaCodigo { get; set; } = "UN";
    public Guid? GrupoEquivalenciaId { get; set; }
    public decimal QuantidadeMinima { get; set; }
    public string? LocalArmazenamento { get; set; }
    public string? Observacoes { get; set; }
    public DateTimeOffset? ArquivadoEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public CategoriaIngrediente? Categoria { get; set; }
    public GrupoEquivalencia? GrupoEquivalencia { get; set; }
    public ItemEstoque? ItemEstoque { get; set; }
}

public class ItemEstoque
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public Guid IngredienteId { get; set; }
    public decimal QuantidadeTotal { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
    public Ingrediente? Ingrediente { get; set; }
    public List<LoteEstoque> Lotes { get; set; } = [];
}

public class LoteEstoque
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemEstoqueId { get; set; }
    public decimal Quantidade { get; set; }
    public decimal QuantidadeInicial { get; set; }
    public DateOnly? DataValidade { get; set; }
    public DateTimeOffset DataEntrada { get; set; } = DateTimeOffset.UtcNow;
    public OrigemLote Origem { get; set; } = OrigemLote.Compra;
    public string? Observacao { get; set; }
    public StatusLote Status { get; set; } = StatusLote.Aberto;
}

public class Movimentacao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public Guid ItemEstoqueId { get; set; }
    public Guid? LoteEstoqueId { get; set; }
    public TipoMovimentacao Tipo { get; set; }
    public short Sentido { get; set; }
    public decimal Quantidade { get; set; }
    public string UnidadeCodigo { get; set; } = "";
    public string Motivo { get; set; } = "";
    public Guid? UsuarioId { get; set; }
    public Guid? PreparoId { get; set; }
    public Guid? ItemListaCompraId { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class FonteReceita
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? UrlTermos { get; set; }
    public bool Ativa { get; set; } = true;
}

public class Receita
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descricao { get; set; }
    public decimal Porcoes { get; set; } = 4;
    public int? TempoPreparoMin { get; set; }
    public Dificuldade? Dificuldade { get; set; }
    public StatusReceita Status { get; set; } = StatusReceita.Rascunho;
    public OrigemReceita Origem { get; set; } = OrigemReceita.Manual;
    public string FonteCodigo { get; set; } = "manual";
    public string? UrlOrigem { get; set; }
    public string? AutorAtribuicao { get; set; }
    public Guid? ImagemArquivoId { get; set; }
    public string? ImagemUrl { get; set; }
    public string? Observacoes { get; set; }
    public string BuscaTexto { get; set; } = "";
    public Guid? CriadoPorUsuarioId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
    public List<ReceitaIngrediente> Ingredientes { get; set; } = [];
    public List<EtapaReceita> Etapas { get; set; } = [];
    public List<ReceitaTag> Tags { get; set; } = [];
}

public class ReceitaIngrediente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceitaId { get; set; }
    public int Ordem { get; set; }
    public string Nome { get; set; } = "";
    public string? TextoOriginal { get; set; }
    public Guid? IngredienteId { get; set; }
    public decimal? Quantidade { get; set; }
    public string? UnidadeCodigo { get; set; }
    public bool Opcional { get; set; }
    public Confidence Confianca { get; set; } = Confidence.Incerta;
}

public class EtapaReceita
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceitaId { get; set; }
    public int Ordem { get; set; }
    public string Instrucao { get; set; } = "";
}

public class ReceitaTag
{
    public Guid ReceitaId { get; set; }
    public string Tag { get; set; } = "";
}

public class Catalogo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public Guid? UsuarioId { get; set; }
    public string Nome { get; set; } = "";
    public string NomeNormalizado { get; set; } = "";
    public string? Descricao { get; set; }
    public VisibilidadeCatalogo Visibilidade { get; set; }
    public Guid? CriadoPorUsuarioId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public List<CatalogoReceita> Receitas { get; set; } = [];
}

public class CatalogoReceita
{
    public Guid CatalogoId { get; set; }
    public Guid ReceitaId { get; set; }
    public DateTimeOffset AdicionadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class Favorito
{
    public Guid UsuarioId { get; set; }
    public Guid ReceitaId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class Preparo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public Guid? ReceitaId { get; set; }
    public string ReceitaTitulo { get; set; } = "";
    public decimal Porcoes { get; set; }
    public StatusPreparo Status { get; set; } = StatusPreparo.Iniciado;
    public Guid? UsuarioId { get; set; }
    public string? IdempotencyKey { get; set; }
    public bool ConsumirDisponivel { get; set; }
    public bool IncluirOpcionais { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? ConfirmadoEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public List<PreparoItemSnapshot> Itens { get; set; } = [];
}

public class PreparoItemSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PreparoId { get; set; }
    public int Ordem { get; set; }
    public Guid? IngredienteId { get; set; }
    public string Texto { get; set; } = "";
    public decimal? QuantidadePrevista { get; set; }
    public string? UnidadeCodigo { get; set; }
    public bool Opcional { get; set; }
    public Confidence Confianca { get; set; }
    public decimal QuantidadeBaixada { get; set; }
    public decimal QuantidadeFaltante { get; set; }
    public bool NaoDescontado { get; set; }
    public string? Observacao { get; set; }
    public string? BaixasJson { get; set; }
}

public class ListaCompra
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public string Nome { get; set; } = "";
    public StatusLista Status { get; set; } = StatusLista.Aberta;
    public OrigemLista Origem { get; set; } = OrigemLista.Manual;
    public Guid? CriadoPorUsuarioId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public List<ItemListaCompra> Itens { get; set; } = [];
}

public class ItemListaCompra
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListaId { get; set; }
    public int Ordem { get; set; }
    public Guid? IngredienteId { get; set; }
    public string Texto { get; set; } = "";
    public decimal QuantidadeNecessaria { get; set; }
    public decimal QuantidadeDescontadaEstoque { get; set; }
    public decimal QuantidadeComprar { get; set; }
    public string UnidadeCodigo { get; set; } = "UN";
    public bool Comprado { get; set; }
    public bool EntradaRegistrada { get; set; }
    public bool AjusteManual { get; set; }
    public Guid? MovimentacaoId { get; set; }
}

public class ConfiguracaoIa
{
    public Guid DespensaId { get; set; }
    public string BaseUrl { get; set; } = "";
    public string Modelo { get; set; } = "";
    public byte[]? ApiKeyCifrada { get; set; }
    public bool Ativa { get; set; }
    public int LimiteHora { get; set; } = 20;
    public Guid? AtualizadoPorUsuarioId { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class ExecucaoIa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public Guid? UsuarioId { get; set; }
    public TipoExecucaoIa Tipo { get; set; }
    public string Modelo { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErroCodigo { get; set; }
    public string? PromptHash { get; set; }
    public int DuracaoMs { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class Arquivo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DespensaId { get; set; }
    public string NomeOriginal { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long TamanhoBytes { get; set; }
    public string CaminhoRelativo { get; set; } = "";
    public byte[] Conteudo { get; set; } = [];
    public Guid? CriadoPorUsuarioId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class Idempotencia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public string Rota { get; set; } = "";
    public string Chave { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = "";
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
