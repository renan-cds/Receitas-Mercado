using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReceitasMercado.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "despensas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    fuso = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    alerta_validade_dias = table.Column<int>(type: "integer", nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_despensas", x => x.id);
                    table.CheckConstraint("ck_despensas_alerta", "alerta_validade_dias between 1 and 90");
                });

            migrationBuilder.CreateTable(
                name: "fontes_receita",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    url_termos = table.Column<string>(type: "text", nullable: true),
                    ativa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fontes_receita", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "idempotencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "jsonb", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotencias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unidades",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    abreviacao = table.Column<string>(type: "text", nullable: false),
                    dimensao = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    aproximada = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidades", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_exibicao = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    despensa_ativa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "arquivos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_original = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tamanho_bytes = table.Column<long>(type: "bigint", nullable: false),
                    caminho_relativo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquivos", x => x.id);
                    table.ForeignKey(
                        name: "fk_arquivos_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nome_normalizado = table.Column<string>(type: "text", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categorias", x => x.id);
                    table.ForeignKey(
                        name: "fk_categorias_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "configuracoes_ia",
                columns: table => new
                {
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    modelo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    api_key_cifrada = table.Column<byte[]>(type: "bytea", nullable: true),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    limite_hora = table.Column<int>(type: "integer", nullable: false),
                    atualizado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracoes_ia", x => x.despensa_id);
                    table.CheckConstraint("ck_config_ia_limite", "limite_hora between 1 and 60");
                    table.ForeignKey(
                        name: "fk_configuracoes_ia_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "convites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    papel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    usado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revogado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_convites", x => x.id);
                    table.ForeignKey(
                        name: "fk_convites_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "execucoes_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    modelo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    erro_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    prompt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    duracao_ms = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execucoes_ia", x => x.id);
                    table.ForeignKey(
                        name: "fk_execucoes_ia_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "grupos_equivalencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nome_normalizado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grupos_equivalencia", x => x.id);
                    table.ForeignKey(
                        name: "fk_grupos_equivalencia_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listas_compra",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    origem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listas_compra", x => x.id);
                    table.ForeignKey(
                        name: "fk_listas_compra_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversoes",
                columns: table => new
                {
                    de_codigo = table.Column<string>(type: "character varying(20)", nullable: false),
                    para_codigo = table.Column<string>(type: "character varying(20)", nullable: false),
                    fator = table.Column<decimal>(type: "numeric(18,9)", precision: 18, scale: 9, nullable: false),
                    aproximada = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversoes", x => new { x.de_codigo, x.para_codigo });
                    table.ForeignKey(
                        name: "fk_conversoes_unidades_de_codigo",
                        column: x => x.de_codigo,
                        principalTable: "unidades",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversoes_unidades_para_codigo",
                        column: x => x.para_codigo,
                        principalTable: "unidades",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nome_normalizado = table.Column<string>(type: "text", nullable: false),
                    descricao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    visibilidade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogos", x => x.id);
                    table.CheckConstraint("ck_catalogos_dono", "(visibilidade = 'Pessoal' and usuario_id is not null) or visibilidade = 'Despensa'");
                    table.ForeignKey(
                        name: "fk_catalogos_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_catalogos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    papel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entrou_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    saiu_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membros", x => x.id);
                    table.ForeignKey(
                        name: "fk_membros_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_membros_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_claims_usuarios_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_usuario_logins_usuarios_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_usuario_tokens_usuarios_user_id",
                        column: x => x.user_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receitas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    porcoes = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    tempo_preparo_min = table.Column<int>(type: "integer", nullable: true),
                    dificuldade = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    origem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fonte_codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    url_origem = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    autor_atribuicao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    imagem_arquivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    imagem_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    busca_texto = table.Column<string>(type: "text", nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receitas", x => x.id);
                    table.CheckConstraint("ck_receitas_porcoes", "porcoes > 0");
                    table.ForeignKey(
                        name: "fk_receitas_arquivos_imagem_arquivo_id",
                        column: x => x.imagem_arquivo_id,
                        principalTable: "arquivos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receitas_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receitas_fontes_receita_fonte_codigo",
                        column: x => x.fonte_codigo,
                        principalTable: "fontes_receita",
                        principalColumn: "codigo");
                });

            migrationBuilder.CreateTable(
                name: "ingredientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nome_normalizado = table.Column<string>(type: "text", nullable: false),
                    categoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidade_referencia_codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    grupo_equivalencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantidade_minima = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    local_armazenamento = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    arquivado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingredientes", x => x.id);
                    table.CheckConstraint("ck_ingredientes_minimo", "quantidade_minima >= 0");
                    table.ForeignKey(
                        name: "fk_ingredientes_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "categorias",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_ingredientes_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ingredientes_grupos_equivalencia_grupo_equivalencia_id",
                        column: x => x.grupo_equivalencia_id,
                        principalTable: "grupos_equivalencia",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ingredientes_unidades_unidade_referencia_codigo",
                        column: x => x.unidade_referencia_codigo,
                        principalTable: "unidades",
                        principalColumn: "codigo");
                });

            migrationBuilder.CreateTable(
                name: "catalogo_receitas",
                columns: table => new
                {
                    catalogo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adicionado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo_receitas", x => new { x.catalogo_id, x.receita_id });
                    table.ForeignKey(
                        name: "fk_catalogo_receitas_catalogos_catalogo_id",
                        column: x => x.catalogo_id,
                        principalTable: "catalogos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_catalogo_receitas_receitas_receita_id",
                        column: x => x.receita_id,
                        principalTable: "receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "etapas_receita",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    instrucao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etapas_receita", x => x.id);
                    table.ForeignKey(
                        name: "fk_etapas_receita_receitas_receita_id",
                        column: x => x.receita_id,
                        principalTable: "receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "favoritos",
                columns: table => new
                {
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favoritos", x => new { x.usuario_id, x.receita_id });
                    table.ForeignKey(
                        name: "fk_favoritos_receitas_receita_id",
                        column: x => x.receita_id,
                        principalTable: "receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_favoritos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preparos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receita_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receita_titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    porcoes = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consumir_disponivel = table.Column<bool>(type: "boolean", nullable: false),
                    incluir_opcionais = table.Column<bool>(type: "boolean", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preparos", x => x.id);
                    table.ForeignKey(
                        name: "fk_preparos_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_preparos_receitas_receita_id",
                        column: x => x.receita_id,
                        principalTable: "receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_preparos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "receita_tags",
                columns: table => new
                {
                    receita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receita_tags", x => new { x.receita_id, x.tag });
                    table.ForeignKey(
                        name: "fk_receita_tags_receitas_receita_id",
                        column: x => x.receita_id,
                        principalTable: "receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "itens_estoque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingrediente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade_total = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_estoque", x => x.id);
                    table.CheckConstraint("ck_itens_estoque_total", "quantidade_total >= 0");
                    table.ForeignKey(
                        name: "fk_itens_estoque_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_itens_estoque_ingredientes_ingrediente_id",
                        column: x => x.ingrediente_id,
                        principalTable: "ingredientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "itens_lista_compra",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lista_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    ingrediente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    texto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantidade_necessaria = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    quantidade_descontada_estoque = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    quantidade_comprar = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    unidade_codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comprado = table.Column<bool>(type: "boolean", nullable: false),
                    entrada_registrada = table.Column<bool>(type: "boolean", nullable: false),
                    ajuste_manual = table.Column<bool>(type: "boolean", nullable: false),
                    movimentacao_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_lista_compra", x => x.id);
                    table.CheckConstraint("ck_itens_lista_quantidades", "quantidade_necessaria >= 0 and quantidade_comprar >= 0 and quantidade_descontada_estoque >= 0");
                    table.ForeignKey(
                        name: "fk_itens_lista_compra_ingredientes_ingrediente_id",
                        column: x => x.ingrediente_id,
                        principalTable: "ingredientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_itens_lista_compra_listas_compra_lista_id",
                        column: x => x.lista_id,
                        principalTable: "listas_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receita_ingredientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    texto_original = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ingrediente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantidade = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    unidade_codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    opcional = table.Column<bool>(type: "boolean", nullable: false),
                    confianca = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receita_ingredientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_receita_ingredientes_ingredientes_ingrediente_id",
                        column: x => x.ingrediente_id,
                        principalTable: "ingredientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_receita_ingredientes_receitas_receita_id",
                        column: x => x.receita_id,
                        principalTable: "receitas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receita_ingredientes_unidades_unidade_codigo",
                        column: x => x.unidade_codigo,
                        principalTable: "unidades",
                        principalColumn: "codigo");
                });

            migrationBuilder.CreateTable(
                name: "preparo_itens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preparo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    ingrediente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    texto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantidade_prevista = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    unidade_codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    opcional = table.Column<bool>(type: "boolean", nullable: false),
                    confianca = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantidade_baixada = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    quantidade_faltante = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    nao_descontado = table.Column<bool>(type: "boolean", nullable: false),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    baixas_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preparo_itens", x => x.id);
                    table.ForeignKey(
                        name: "fk_preparo_itens_preparos_preparo_id",
                        column: x => x.preparo_id,
                        principalTable: "preparos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_estoque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    quantidade_inicial = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    data_validade = table.Column<DateOnly>(type: "date", nullable: true),
                    data_entrada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    origem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotes", x => x.id);
                    table.CheckConstraint("ck_lotes_inicial", "quantidade_inicial > 0");
                    table.CheckConstraint("ck_lotes_quantidade", "quantidade >= 0");
                    table.ForeignKey(
                        name: "fk_lotes_itens_estoque_item_estoque_id",
                        column: x => x.item_estoque_id,
                        principalTable: "itens_estoque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movimentacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    despensa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_estoque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_estoque_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sentido = table.Column<short>(type: "smallint", nullable: false),
                    quantidade = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    unidade_codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    preparo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_lista_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimentacoes", x => x.id);
                    table.CheckConstraint("ck_movimentacoes_quantidade", "quantidade > 0 and sentido in (-1, 1)");
                    table.ForeignKey(
                        name: "fk_movimentacoes_despensas_despensa_id",
                        column: x => x.despensa_id,
                        principalTable: "despensas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_movimentacoes_itens_estoque_item_estoque_id",
                        column: x => x.item_estoque_id,
                        principalTable: "itens_estoque",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_movimentacoes_lotes_lote_estoque_id",
                        column: x => x.lote_estoque_id,
                        principalTable: "lotes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_movimentacoes_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "fontes_receita",
                columns: new[] { "codigo", "ativa", "nome", "url_termos" },
                values: new object[,]
                {
                    { "ia", true, "Sugestão de IA", null },
                    { "jsonld", true, "Importação por URL (schema.org/Recipe)", null },
                    { "manual", true, "Cadastro manual", null },
                    { "themealdb", true, "TheMealDB", "https://www.themealdb.com/api.php" }
                });

            migrationBuilder.InsertData(
                table: "unidades",
                columns: new[] { "codigo", "abreviacao", "aproximada", "dimensao", "nome" },
                values: new object[,]
                {
                    { "COLHER_CHA", "col. chá", true, "Culinaria", "colher de chá" },
                    { "COLHER_SOPA", "col. sopa", true, "Culinaria", "colher de sopa" },
                    { "G", "g", false, "Massa", "grama" },
                    { "KG", "kg", false, "Massa", "quilograma" },
                    { "L", "L", false, "Volume", "litro" },
                    { "ML", "ml", false, "Volume", "mililitro" },
                    { "UN", "un", false, "Unidade", "unidade" },
                    { "XICARA", "xíc", true, "Culinaria", "xícara (240 ml)" }
                });

            migrationBuilder.InsertData(
                table: "conversoes",
                columns: new[] { "de_codigo", "para_codigo", "aproximada", "fator" },
                values: new object[,]
                {
                    { "COLHER_CHA", "ML", true, 5m },
                    { "COLHER_SOPA", "ML", true, 15m },
                    { "G", "KG", false, 0.001m },
                    { "KG", "G", false, 1000m },
                    { "L", "ML", false, 1000m },
                    { "ML", "COLHER_CHA", true, 0.2m },
                    { "ML", "COLHER_SOPA", true, 0.066666667m },
                    { "ML", "L", false, 0.001m },
                    { "ML", "XICARA", true, 0.004166667m },
                    { "XICARA", "ML", true, 240m }
                });

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_despensa_id",
                table: "arquivos",
                column: "despensa_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_receitas_receita_id",
                table: "catalogo_receitas",
                column: "receita_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalogos_despensa_id_nome_normalizado",
                table: "catalogos",
                columns: new[] { "despensa_id", "nome_normalizado" },
                unique: true,
                filter: "visibilidade = 'Despensa'");

            migrationBuilder.CreateIndex(
                name: "ix_catalogos_despensa_id_usuario_id_nome_normalizado",
                table: "catalogos",
                columns: new[] { "despensa_id", "usuario_id", "nome_normalizado" },
                unique: true,
                filter: "visibilidade = 'Pessoal'");

            migrationBuilder.CreateIndex(
                name: "ix_catalogos_usuario_id",
                table: "catalogos",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_categorias_despensa_id_nome_normalizado",
                table: "categorias",
                columns: new[] { "despensa_id", "nome_normalizado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversoes_para_codigo",
                table: "conversoes",
                column: "para_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_convites_despensa_id",
                table: "convites",
                column: "despensa_id");

            migrationBuilder.CreateIndex(
                name: "ix_convites_token_hash",
                table: "convites",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_etapas_receita_receita_id_ordem",
                table: "etapas_receita",
                columns: new[] { "receita_id", "ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execucoes_ia_despensa_id_criado_em",
                table: "execucoes_ia",
                columns: new[] { "despensa_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_favoritos_receita_id",
                table: "favoritos",
                column: "receita_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupos_equivalencia_despensa_id_nome_normalizado",
                table: "grupos_equivalencia",
                columns: new[] { "despensa_id", "nome_normalizado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotencias_criado_em",
                table: "idempotencias",
                column: "criado_em");

            migrationBuilder.CreateIndex(
                name: "ix_idempotencias_usuario_id_rota_chave",
                table: "idempotencias",
                columns: new[] { "usuario_id", "rota", "chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingredientes_categoria_id",
                table: "ingredientes",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredientes_despensa_id_nome_normalizado",
                table: "ingredientes",
                columns: new[] { "despensa_id", "nome_normalizado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingredientes_grupo_equivalencia_id",
                table: "ingredientes",
                column: "grupo_equivalencia_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredientes_unidade_referencia_codigo",
                table: "ingredientes",
                column: "unidade_referencia_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_itens_estoque_despensa_id_quantidade_total",
                table: "itens_estoque",
                columns: new[] { "despensa_id", "quantidade_total" });

            migrationBuilder.CreateIndex(
                name: "ix_itens_estoque_ingrediente_id",
                table: "itens_estoque",
                column: "ingrediente_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_itens_lista_compra_ingrediente_id",
                table: "itens_lista_compra",
                column: "ingrediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_lista_compra_lista_id",
                table: "itens_lista_compra",
                column: "lista_id");

            migrationBuilder.CreateIndex(
                name: "ix_listas_compra_despensa_id_status",
                table: "listas_compra",
                columns: new[] { "despensa_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_lotes_data_validade",
                table: "lotes",
                column: "data_validade",
                filter: "status = 'Aberto' and data_validade is not null");

            migrationBuilder.CreateIndex(
                name: "ix_lotes_item_estoque_id_status_data_validade",
                table: "lotes",
                columns: new[] { "item_estoque_id", "status", "data_validade" });

            migrationBuilder.CreateIndex(
                name: "ix_membros_despensa_id_usuario_id",
                table: "membros",
                columns: new[] { "despensa_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_membros_usuario_id_status",
                table: "membros",
                columns: new[] { "usuario_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_despensa_id_criado_em",
                table: "movimentacoes",
                columns: new[] { "despensa_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_idempotency_key",
                table: "movimentacoes",
                column: "idempotency_key",
                filter: "idempotency_key is not null");

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_item_estoque_id_criado_em",
                table: "movimentacoes",
                columns: new[] { "item_estoque_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_lote_estoque_id",
                table: "movimentacoes",
                column: "lote_estoque_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_preparo_id",
                table: "movimentacoes",
                column: "preparo_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimentacoes_usuario_id",
                table: "movimentacoes",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_preparo_itens_preparo_id",
                table: "preparo_itens",
                column: "preparo_id");

            migrationBuilder.CreateIndex(
                name: "ix_preparos_despensa_id_criado_em",
                table: "preparos",
                columns: new[] { "despensa_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_preparos_despensa_id_idempotency_key",
                table: "preparos",
                columns: new[] { "despensa_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key is not null");

            migrationBuilder.CreateIndex(
                name: "ix_preparos_receita_id",
                table: "preparos",
                column: "receita_id");

            migrationBuilder.CreateIndex(
                name: "ix_preparos_usuario_id",
                table: "preparos",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_receita_ingredientes_ingrediente_id",
                table: "receita_ingredientes",
                column: "ingrediente_id");

            migrationBuilder.CreateIndex(
                name: "ix_receita_ingredientes_receita_id",
                table: "receita_ingredientes",
                column: "receita_id");

            migrationBuilder.CreateIndex(
                name: "ix_receita_ingredientes_unidade_codigo",
                table: "receita_ingredientes",
                column: "unidade_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_receitas_despensa_id_status",
                table: "receitas",
                columns: new[] { "despensa_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_receitas_fonte_codigo",
                table: "receitas",
                column: "fonte_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_receitas_imagem_arquivo_id",
                table: "receitas",
                column: "imagem_arquivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_claims_user_id",
                table: "usuario_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_logins_user_id",
                table: "usuario_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "usuarios",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "usuarios",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogo_receitas");

            migrationBuilder.DropTable(
                name: "configuracoes_ia");

            migrationBuilder.DropTable(
                name: "conversoes");

            migrationBuilder.DropTable(
                name: "convites");

            migrationBuilder.DropTable(
                name: "etapas_receita");

            migrationBuilder.DropTable(
                name: "execucoes_ia");

            migrationBuilder.DropTable(
                name: "favoritos");

            migrationBuilder.DropTable(
                name: "idempotencias");

            migrationBuilder.DropTable(
                name: "itens_lista_compra");

            migrationBuilder.DropTable(
                name: "membros");

            migrationBuilder.DropTable(
                name: "movimentacoes");

            migrationBuilder.DropTable(
                name: "preparo_itens");

            migrationBuilder.DropTable(
                name: "receita_ingredientes");

            migrationBuilder.DropTable(
                name: "receita_tags");

            migrationBuilder.DropTable(
                name: "usuario_claims");

            migrationBuilder.DropTable(
                name: "usuario_logins");

            migrationBuilder.DropTable(
                name: "usuario_tokens");

            migrationBuilder.DropTable(
                name: "catalogos");

            migrationBuilder.DropTable(
                name: "listas_compra");

            migrationBuilder.DropTable(
                name: "lotes");

            migrationBuilder.DropTable(
                name: "preparos");

            migrationBuilder.DropTable(
                name: "itens_estoque");

            migrationBuilder.DropTable(
                name: "receitas");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.DropTable(
                name: "ingredientes");

            migrationBuilder.DropTable(
                name: "arquivos");

            migrationBuilder.DropTable(
                name: "fontes_receita");

            migrationBuilder.DropTable(
                name: "categorias");

            migrationBuilder.DropTable(
                name: "grupos_equivalencia");

            migrationBuilder.DropTable(
                name: "unidades");

            migrationBuilder.DropTable(
                name: "despensas");
        }
    }
}
