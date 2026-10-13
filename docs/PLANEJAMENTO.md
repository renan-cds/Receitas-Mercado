# Planejamento — Receitas-Mercado

**Status:** aguardando aprovação. Não iniciar implementação de produção até a seção 14 ser decidida.

**Produto:** aplicativo web de estoque doméstico de mantimentos integrado a receitas culinárias.

**Restrição guia desta versão:** custo operacional zero. Toda a stack, hospedagem de referência, APIs de receitas e o caminho de IA são gratuitos (software livre + auto-hospedagem + provedores opcionais BYOK). Nenhum serviço pago é obrigatório no MVP.

---

## 1. Resumo executivo e visão do produto

Receitas-Mercado ajuda uma pessoa ou uma família a responder três perguntas com confiança:

1. O que há na despensa, em que quantidade e até quando vale.
2. O que dá para cozinhar agora, o que falta e o que está incerto.
3. O que comprar, o que foi preparado e o que foi efetivamente consumido.

A unidade de colaboração é a **despensa**. Estoque, lotes, receitas da casa, catálogos compartilhados, preparos e listas de compras pertencem à despensa. Favoritos, preferências e catálogos pessoais pertencem ao usuário. A IA é um assistente opcional: sugere, nunca altera estoque sozinha, e a aplicação permanece íntegra se estiver desligada.

A primeira versão é um **PWA online**. Dados vivem no servidor. A interface informa falha de rede e nunca declara sucesso de uma operação que não foi persistida. Offline completo fica fora do MVP.

### 1.1 Premissas adotadas para este documento

Estas premissas foram usadas para fechar o desenho. Itens marcados como **decisão pendente** na seção 14 podem revertê-las.

| ID | Premissa |
| --- | --- |
| P01 | Idioma da interface e dos dados de semente: português do Brasil. |
| P02 | Sistema métrico + unidades culinárias brasileiras (g, kg, ml, L, un, xícara, colher de sopa, colher de chá). Conversões culinárias são aproximadas e sinalizadas. |
| P03 | Cadastro cria automaticamente uma despensa pessoal. O usuário pode participar de outras via convite. |
| P04 | Autenticação local (e-mail + senha) com ASP.NET Identity. Sem OAuth obrigatório no MVP. Sem envio de e-mail no MVP: convites são link copiável. |
| P05 | Receitas da casa pertencem à despensa. Favoritos são pessoais. Catálogos podem ser pessoais ou da despensa. |
| P06 | Ingredientes são cadastro da despensa (não há catálogo global compartilhado entre despensas no MVP). |
| P07 | IA desligada por padrão. Ativa só com endpoint OpenAI-compatível e chave (ou Ollama local) informados pelo administrador da despensa. |
| P08 | Busca online no MVP usa TheMealDB (API gratuita) e importação de URL via JSON-LD `schema.org/Recipe`. |
| P09 | Imagens: URL externa opcional ou arquivo local no disco do servidor (`/data/uploads`). Sem object storage pago. |
| P10 | Sem fila offline de escrita. Sem Redis. Sem provedor de e-mail. Sem CDN pago. |
| P11 | Deploy de referência: Docker Compose (API + PostgreSQL) em máquina do usuário. A API serve o frontend estático. |
| P12 | Quantidades com até 3 casas decimais. Estoque negativo é impossível (restrição de banco + regra de aplicação). |
| P13 | Baixa de lote por FEFO (validade mais próxima primeiro; nulos por último; empate pela data de entrada). |
| P14 | Catálogo de receitas ≠ coleção de ingredientes. Coleções de ingredientes ficam fora do MVP. |
| P15 | Conteúdo importado é rascunho editável. A aplicação não copia sites cujo `robots.txt`, termos ou marcação indiquem vedação; falha vai para cadastro manual. |

### 1.2 Visão de qualidade

Simplicidade > recursos. Confiabilidade do estoque > sugestão inteligente. A despensa precisa ser auditável: toda alteração de quantidade tem movimentação, usuário, data, motivo e, quando houver, chave de idempotência.

---

## 2. Escopo do MVP e funcionalidades futuras

### 2.1 MVP (entrar em produção utilizável)

- Conta local, sessão, uma ou mais despensas, papéis Admin / Editor / Leitor.
- Convite por link, entrada, alteração de papel, remoção e saída.
- Cadastro de ingredientes, categorias, equivalências, unidade de referência, mínimo, local, observações.
- Lotes com quantidade e validade; total derivado; movimentações (entrada, consumo, perda, correção).
- Alertas de validade e abaixo do mínimo.
- Busca, filtro e ordenação de ingredientes e receitas.
- CRUD de receitas manuais (ingredientes, etapas, porções, tempos, tags, imagem opcional).
- Busca TheMealDB e importação por URL (JSON-LD), com revisão obrigatória antes de salvar.
- Correspondência de ingrediente da receita com o cadastro da despensa (confirmada / inferida / incerta).
- Análise “pode preparar / faltam / verificar”, com porções ajustáveis.
- Preparo com preview, confirmação explícita, revalidação, FEFO, recusa de baixa parcial silenciosa.
- Favoritos pessoais; catálogos pessoais e da despensa; duplicar vazio ou com associações.
- Listas de compras manuais ou a partir de receitas; consolidação; desconto do estoque; marcar comprado; gerar entradas.
- PWA instalável (manifesto + service worker da casca); aviso de conexão.
- IA opcional BYOK: sugestão, normalização, substituição, geração e adaptação de porções, sempre revisável.
- Proteção SSRF na importação; idempotência nas mutações de estoque; autorização no backend.

### 2.2 Fora do MVP (backlog explícito)

| Código | Item | Motivo de adiamento |
| --- | --- | --- |
| FUT-01 | Offline com fila de escrita e conflito | Risco alto de estoque divergente |
| FUT-02 | OAuth (Google/GitHub) e recuperação de senha por e-mail | Exige provedor de e-mail ou IdP |
| FUT-03 | Nutricional, calorias, dietas e alérgenos estruturados | Escopo e fontes de dados |
| FUT-04 | Código de barras / Open Food Facts | Hardware e matching |
| FUT-05 | Planejamento semanal de cardápio | Depende do núcleo estável |
| FUT-06 | Coleções de ingredientes (despensa organizada por “kits”) | Entidade distinta; não misturar com catálogos |
| FUT-07 | Marketplace global de receitas entre despensas | Moderação e licença |
| FUT-08 | App nativo iOS/Android | PWA cobre o MVP |
| FUT-09 | Multi-moeda, preços e orçamento de compras | Não é controle de estoque |
| FUT-10 | Impressão avançada / PDF de receitas | Custo de layout |
| FUT-11 | Notificações push de validade | Requer serviço push |
| FUT-12 | Densidades por ingrediente (massa ↔ volume automático) | Dados e responsabilidade |
| FUT-13 | Vários idiomas de UI | Depois do PT-BR estável |
| FUT-14 | Importação em lote (CSV de estoque) | Qualidade do núcleo primeiro |

### 2.3 Critério de “MVP concluído”

Uma família de até 6 pessoas consegue, só com o MVP: cadastrar a despensa, lançar compras em lotes, importar ou criar 10 receitas, ver o que dá para fazer, gerar lista do que falta, confirmar um preparo e ver o estoque baixar certo, inclusive com dois editores não gerando quantidade negativa.

---

## 3. Requisitos funcionais

Cada requisito é verificável. Papéis: **A** administrador da despensa, **E** editor, **L** leitor, **U** usuário autenticado, **P** público (não autenticado).

### 3.1 Autenticação e sessão

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-AUTH-01 | Cadastrar conta com e-mail, senha forte e nome de exibição. E-mail único. | P |
| RF-AUTH-02 | Autenticar com e-mail e senha. Falha genérica (não revelar se o e-mail existe). | P |
| RF-AUTH-03 | Encerrar sessão no servidor (cookie invalidado). | U |
| RF-AUTH-04 | Sessão persistente via cookie HTTP-only, Secure, SameSite=Lax. | U |
| RF-AUTH-05 | No cadastro, criar despensa pessoal e tornar o usuário Admin. | U |
| RF-AUTH-06 | Alterar nome e senha (senha atual exigida). Sem recuperação por e-mail no MVP. | U |

### 3.2 Despensa e membros

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-PAN-01 | Renomear despensa e fuso horário. | A |
| RF-PAN-02 | Listar despensas do usuário e escolher a ativa. | U |
| RF-PAN-03 | Gerar convite com papel, validade (padrão 7 dias) e token de uso único. | A |
| RF-PAN-04 | Aceitar convite autenticado. Recusar token inválido, expirado, revogado ou já usado. | U |
| RF-PAN-05 | Alterar papel de membro. Impedir remover o último Admin. | A |
| RF-PAN-06 | Remover membro. Histórico (movimentações/preparos) preserva o `UsuarioId`. | A |
| RF-PAN-07 | Sair da despensa. Último Admin não sai sem transferir. | U |
| RF-PAN-08 | Listar membros e convites pendentes; revogar convite. | A; L/E veem membros |

### 3.3 Ingredientes

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-ING-01 | CRUD de categorias da despensa (semente inicial: grãos, laticínios, proteínas, hortifruti, temperos, mercearia, congelados, bebidas, outros). | A/E escrevem; L lê |
| RF-ING-02 | CRUD de ingrediente: nome, categoria, unidade de referência, mínimo, local, observações, grupo de equivalência. | A/E |
| RF-ING-03 | Unicidade de nome normalizado na despensa. | sistema |
| RF-ING-04 | Arquivar ingrediente (não aparece em buscas padrão; histórico permanece). Exclusão física só se nunca houve lote/movimentação. | A/E |
| RF-ING-05 | Definir equivalentes via grupo. Equivalentes consolidam-se em listas e podem substituir-se na análise se a unidade for compatível. | A/E |
| RF-ING-06 | Busca por nome, filtro por categoria/local/alerta, ordenação por nome, validade mais próxima, quantidade. | A/E/L |

### 3.4 Estoque, lotes e movimentações

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-STK-01 | Consultar quantidade total e lotes abertos de cada ingrediente. | A/E/L |
| RF-STK-02 | Registrar entrada: cria lote com quantidade, validade opcional e origem. | A/E |
| RF-STK-03 | Registrar consumo, perda ou correção, informando motivo. | A/E |
| RF-STK-04 | Correção não pode resultar em total negativo. | sistema |
| RF-STK-05 | Histórico com usuário, data, quantidade, unidade, tipo, motivo, lote, preparo ou item de lista quando houver. | A/E/L |
| RF-STK-06 | Alertas: lote vence em N dias (padrão 7, configurável na despensa) e total < mínimo. | A/E/L |
| RF-STK-07 | Conversão entre unidades da mesma dimensão (massa ou volume). Recusar conversão massa↔volume e un↔contínuo. | sistema |
| RF-STK-08 | Quantidades fracionadas (3 decimais). | sistema |
| RF-STK-09 | Toda mutação de estoque aceita `Idempotency-Key`. Reexecução devolve o mesmo resultado. | sistema |

### 3.5 Receitas

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-REC-01 | Cadastrar receita manual completa ou rascunho incompleto. | A/E |
| RF-REC-02 | Editar e excluir receita da despensa. Exclusão remove associações de catálogo/favorito, não movimentações históricas de preparo (preparo guarda snapshot). | A/E |
| RF-REC-03 | Ingredientes da receita: texto original, quantidade, unidade, opcional, ordem, vínculo opcional ao ingrediente da despensa, confiança da correspondência. | A/E |
| RF-REC-04 | Etapas ordenadas. | A/E |
| RF-REC-05 | Metadados: descrição, porções, tempo, dificuldade, categorias/tags, observações, imagem, fonte, URL, atribuição. | A/E |
| RF-REC-06 | Buscar receitas da despensa por texto, tag, estado de compatibilidade. | A/E/L |
| RF-REC-07 | Buscar TheMealDB, pré-visualizar, editar e então salvar na despensa. | A/E |
| RF-REC-08 | Importar por URL: fetch seguro, extrair JSON-LD Recipe, revisar, salvar. | A/E |
| RF-REC-09 | Falha de importação/busca mostra mensagem clara e oferece cadastro manual. | A/E |
| RF-REC-10 | Não persistir HTML integral de páginas de terceiros. Persistir apenas campos estruturados da receita + URL + atribuição. | sistema |

### 3.6 Inteligência artificial (opcional)

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-IA-01 | Configurar base URL OpenAI-compatível e API key (armazenada cifrada). Testar conexão. Remover credencial. | A |
| RF-IA-02 | Sugerir receitas a partir do estoque, opcionalmente priorizando validade próxima. | A/E |
| RF-IA-03 | Gerar receita a partir de ingredientes informados. | A/E |
| RF-IA-04 | Sugerir correspondência/normalização de ingredientes importados. | A/E |
| RF-IA-05 | Sugerir substituições culinárias. | A/E |
| RF-IA-06 | Adaptar quantidades ao rendimento desejado (cálculo determinístico no backend; IA só se o usuário pedir reescrita de etapas). | A/E |
| RF-IA-07 | Toda saída passa por validador de esquema antes de virar rascunho. | sistema |
| RF-IA-08 | UI distingue dado conhecido, inferido pela IA e correspondência incerta. | sistema |
| RF-IA-09 | IA nunca grava estoque. Aplicar sugestão exige ação explícita. | sistema |
| RF-IA-10 | Sem credencial ou provedor indisponível: demais módulos inalterados; ações de IA retornam 503 de negócio com fallback. | sistema |

### 3.7 Favoritos e catálogos

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-FAV-01 | Favoritar/desfavoritar receita visível. Favorito é por usuário. | U |
| RF-CAT-01 | Criar catálogo vazio (nome, descrição, visibilidade pessoal ou despensa). | A/E; pessoal: qualquer U membro |
| RF-CAT-02 | Editar, renomear, alterar descrição, excluir catálogo. Excluir não exclui receitas. | dono (pessoal) ou A/E (despensa) |
| RF-CAT-03 | Adicionar receita a N catálogos; remover de um catálogo sem apagar a receita. | conforme visibilidade |
| RF-CAT-04 | Duplicar catálogo vazio (só configurações). | conforme visibilidade |
| RF-CAT-05 | Duplicar catálogo com associações (não duplica `Receita`). | conforme visibilidade |
| RF-CAT-06 | Pesquisar e filtrar receitas dentro do catálogo. | leitores do catálogo |
| RF-CAT-07 | Leitor não cria/edita catálogos da despensa; vê os da despensa e os pessoais. | L |

### 3.8 Correlação estoque × receita

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-CMP-01 | Comparar receita × estoque para N porções. | A/E/L |
| RF-CMP-02 | Estados: `pode_preparar`, `faltam_ingredientes`, `verificar_ingredientes`. | sistema |
| RF-CMP-03 | Percentual de compatibilidade opcional, com texto de limitação. Não substitui a lista. | sistema |
| RF-CMP-04 | Opcional não bloqueia `pode_preparar`. | sistema |
| RF-CMP-05 | Correspondência incerta = não disponível. Força `verificar_ingredientes` se o item for obrigatório. | sistema |
| RF-CMP-06 | Conversão aproximada (xícara/colher) marca o item como aproximado e pode elevar o estado para `verificar_ingredientes`. | sistema |

### 3.9 Preparo e baixa

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-PREP-01 | Iniciar preparo: receita + porções → snapshot do que seria consumido. | A/E |
| RF-PREP-02 | Tela de confirmação lista lote(s) previstos, quantidades e alertas. | A/E |
| RF-PREP-03 | Confirmar: revalida, registra preparo, movimentações, atualiza lotes, usuário e data. | A/E |
| RF-PREP-04 | Se insuficiente: nenhum lote é alterado. Usuário escolhe abortar, reduzir porções ou **consumir apenas o disponível e registrar faltas** (opção explícita, nunca padrão). | A/E |
| RF-PREP-05 | Idempotência na confirmação. | sistema |
| RF-PREP-06 | Histórico de preparos da despensa. | A/E/L |

### 3.10 Lista de compras

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-SHP-01 | Criar lista manual. | A/E |
| RF-SHP-02 | Criar lista a partir de uma ou mais receitas e porções. | A/E |
| RF-SHP-03 | Consolidar equivalentes e somar na unidade de referência quando convertível. | sistema |
| RF-SHP-04 | Descontar quantidade em estoque. Comprar = max(0, necessário − disponível). | sistema |
| RF-SHP-05 | Ajuste manual de item (quantidade, nome livre, unidade). | A/E |
| RF-SHP-06 | Marcar item comprado (não entra estoque ainda). | A/E |
| RF-SHP-07 | Registrar entradas dos itens comprados (validade opcional por item). Pular itens já lançados. | A/E |
| RF-SHP-08 | Idempotência no lançamento de entradas. | sistema |

### 3.11 Plataforma

| Código | Requisito | Papéis |
| --- | --- | --- |
| RF-PWA-01 | Manifesto, ícones, `display=standalone`. | P |
| RF-PWA-02 | Service worker cacheia casca estática; APIs de dados não são cacheadas como sucesso. | sistema |
| RF-PWA-03 | Banner/instrução de instalação nos navegadores compatíveis. | U |
| RF-UX-01 | Layout utilizável em 320 px, tablet e desktop. | todos |
| RF-UX-02 | Falha de rede visível; botões de persistência entram em estado de erro, não de sucesso. | todos |
| RF-UX-03 | Leitor nunca vê botões de mutação (UI); backend recusa com 403. | L |

---

## 4. Regras de negócio

| Código | Regra |
| --- | --- |
| RN-STK-01 | `LoteEstoque.Quantidade >= 0` e `ItemEstoque.QuantidadeTotal >= 0`. Violação aborta a transação. |
| RN-STK-02 | `QuantidadeTotal` é a soma dos lotes abertos e é atualizada na mesma transação da movimentação. |
| RN-STK-03 | FEFO: baixa consome lotes ordenados por `DataValidade ASC NULLS LAST`, depois `DataEntrada ASC`, depois `Id`. |
| RN-STK-04 | Movimentação é imutável. Estorno é nova movimentação de correção, nunca UPDATE da original. |
| RN-STK-05 | Correção para baixo não pode ultrapassar o lote. Correção para cima cria ajuste no lote escolhido ou novo lote. |
| RN-UNI-01 | Conversão exata só na mesma dimensão (`massa`: g↔kg; `volume`: ml↔L). Fator cadastrado em `ConversaoUnidade`. |
| RN-UNI-02 | Unidades culinárias (xícara=240 ml, colher sopa=15 ml, colher chá=5 ml) são `Aproximada=true`. |
| RN-UNI-03 | Sem densidade, massa↔volume é recusado. O item cai em `verificar_ingredientes`. |
| RN-ING-01 | Nome normalizado: minúsculas, sem acento, espaços colapsados, trim. Unicidade `(DespensaId, NomeNormalizado)`. |
| RN-ING-02 | Grupo de equivalência só agrupa ingredientes da mesma despensa. Substituição automática exige unidade convertível. |
| RN-ING-03 | Ingrediente arquivado não recebe novas entradas até ser reativado; não entra em busca padrão de receitas. |
| RN-REC-01 | Receita pode ser salva incompleta (`Status=Rascunho`). Rascunho não entra em `pode_preparar`. |
| RN-REC-02 | `ReceitaIngrediente` sem `IngredienteId` ou com `Confianca=Incerta` não conta como disponível. |
| RN-REC-03 | Adicionar a catálogo não copia a receita. PK de associação `(CatalogoId, ReceitaId)`. |
| RN-REC-04 | Importação grava `FonteReceita`, URL, autor e data. Corpo HTML bruto não é armazenado. |
| RN-CMP-01 | `pode_preparar`: todo obrigatório com correspondência confirmada (ou equivalente confirmado) e quantidade suficiente após conversão exata. |
| RN-CMP-02 | `faltam_ingredientes`: ao menos um obrigatório ausente ou insuficiente, todos com correspondência confirmada. |
| RN-CMP-03 | `verificar_ingredientes`: qualquer obrigatório incerto, inferido não confirmado, ou conversão apenas aproximada. |
| RN-CMP-04 | Percentual = 100 × (obrigatórios ok) / (obrigatórios). Texto fixo: “não considera correspondências incertas nem conversões aproximadas como disponíveis”. |
| RN-PREP-01 | Baixa só em `Status=Confirmado` após revalidação. Rascunho de preparo expira em 30 min sem confirmar. |
| RN-PREP-02 | Insuficiência → HTTP 409 com corpo `faltantes[]`. Nenhuma escrita de lote. |
| RN-PREP-03 | Opção explícita `consumirDisponivel=true` consome o que houver, registra faltas no snapshot, não cria quantidade negativa. |
| RN-PREP-04 | Confirmação usa transação única + `SELECT … FOR UPDATE` nos lotes envolvidos. |
| RN-SHP-01 | Consolidação: mesma `IngredienteId` ou mesmo `GrupoEquivalenciaId` com unidade convertível. Senão, linhas separadas. |
| RN-SHP-02 | Item já `EntradaRegistrada=true` é ignorado em novo lançamento da mesma lista. |
| RN-ACL-01 | Toda query de dado da despensa filtra `DespensaId` + membership ativo. IDs de outras despensas retornam 404 (não 403) para não vazar existência. |
| RN-ACL-02 | Leitor: GET apenas. Editor: mutações de estoque/receitas/listas/preparos. Admin: + membros, convites, config IA, exclusão da despensa. |
| RN-ACL-03 | Favorito e catálogo pessoal: só o dono lê/escreve. Catálogo da despensa: membros leem; A/E escrevem. |
| RN-ACL-04 | Ao sair/remover: perde acesso aos dados da despensa. Favoritos para receitas dela ficam órfãos e deixam de aparecer. Associações de catálogos pessoais a essas receitas são removidas. Catálogos da despensa permanecem. |
| RN-ACL-05 | Exclusão de conta: anonimiza `UsuarioId` em históricos da despensa (`UsuarioId=null`, nome “Conta removida”) e apaga credenciais, favoritos e catálogos pessoais. |
| RN-IA-01 | Chave nunca retorna em GET (apenas `configurada=true` + `baseUrl` + `modelo`). |
| RN-IA-02 | Rate limit por despensa (padrão 20 req/hora). Estouro → 429. |
| RN-IA-03 | Resposta fora do JSON Schema é descartada; usuário vê erro de validação, não texto bruto como receita. |
| RN-IDM-01 | Chave de idempotência: header `Idempotency-Key` (UUID). Escopo `(UsuarioId, Rota, Chave)`. Mesmo corpo → mesma resposta. Corpo diferente → 409. TTL 24 h. |
| RN-PWA-01 | Mutação sem rede: erro, estado local inalterado como “salvo”. GET falho: dados anteriores só se ainda visíveis na sessão memória, com selo “pode estar desatualizado”. |

---

## 5. Mapa de navegação e telas

Informação persistente no topo (app autenticado): despensa ativa, busca global, alertas, status de conexão, menu da conta.

```
Público
├── /entrar
├── /cadastrar
└── /convite/:token          (aceitar, exige login)

App
├── /                        painel
├── /estoque
│   ├── /                    lista + alertas
│   ├── /novo
│   └── /:ingredienteId      detalhe, lotes, movimentar, histórico
├── /receitas
│   ├── /                    lista + filtros de compatibilidade
│   ├── /nova
│   ├── /buscar-online
│   ├── /importar
│   ├── /:id                 detalhe + análise + ações
│   ├── /:id/editar
│   └── /:id/preparar        preview e confirmação
├── /favoritos
├── /catalogos
│   ├── /                    lista
│   └── /:id                 receitas do catálogo
├── /listas
│   ├── /                    lista de listas
│   └── /:id                 itens, consolidação, lançar entradas
├── /preparos                histórico
├── /despensa
│   ├── /membros
│   └── /convites
└── /configuracoes
    ├── /conta
    ├── /despensa
    └── /ia
```

### 5.1 Descrição das telas

| Tela | Objetivo | Pontos críticos |
| --- | --- | --- |
| Entrar | Autenticar | Erro genérico; foco no primeiro campo; senha revelável. |
| Cadastrar | Criar conta + despensa | Política de senha visível; não logar se persistência falhar. |
| Convite | Mostrar despensa e papel; aceitar | Token inválido explicado; se já membro, ir ao painel. |
| Painel | Resumo: alertas, “pode cozinhar”, listas abertas, atalhos | Cards clicáveis; vazio com CTA. |
| Estoque (lista) | Inventário | Busca, chips de alerta, FAB “entrada”, linha com total e validade mais próxima. |
| Novo ingrediente | Cadastro | Unidade de referência obrigatória; categoria; mínimo opcional. |
| Detalhe do ingrediente | Lotes + ações | Tabela de lotes; botões entrada/consumo/perda/correção; não permitir negativo na UI e no servidor. |
| Dialogo de movimentação | Quantidade, lote opcional, validade (entrada), motivo | Preview do total resultante. |
| Receitas (lista) | Biblioteca da despensa | Filtro estado; selo pode/falta/verificar; favorito. |
| Nova / editar receita | Formulário | Ingredientes com correspondência; etapas reordenáveis; salvar rascunho. |
| Buscar online | Consulta TheMealDB | Resultados com atribuição; “revisar para salvar”; termos de uso visíveis. |
| Importar URL | Campo URL + resultado | Erros SSRF/timeout/sem JSON-LD; fallback manual. |
| Revisão de importação/IA | Diff entre origem e o que será salvo | Campos inferidos marcados; usuário confirma correspondências. |
| Detalhe da receita | Modo de preparo + análise | Seletor de porções; lista de faltas; CTA preparar / lista de compras / catálogo. |
| Preparar | Preview FEFO | Confirmar desabilitado até checkbox “li as quantidades”; 409 tratado na tela. |
| Favoritos | Lista pessoal | Vazio; remove. |
| Catálogos | Coleções | Visibilidade visível; duplicar (vazio / com receitas). |
| Catálogo | Receitas da coleção | Busca interna; adicionar existente (não cria receita nova). |
| Listas | Listas da despensa | Status aberta/comprada. |
| Lista | Itens | Consolidado vs original; marcar comprado; lançar no estoque com validade. |
| Preparos | Histórico | Snapshot somente leitura. |
| Membros | Papéis | Transferir admin; remover com confirmação. |
| Convites | Gerar e copiar link | Expiração; revogar. |
| Conta | Nome, senha, sair, excluir conta | Exclusão com digitação do e-mail. |
| Despensa | Nome, fuso, janela de alerta de validade | Só admin. |
| IA | Base URL, modelo, chave, teste, limites | Chave write-only; aviso de BYOK e dados enviados ao provedor. |
| Offline/erro | Faixa persistente | Não usar toast de sucesso em falha. |

Padrões de UI: React Aria Components (foco, teclado, SR); alvos de toque ≥ 44 px; formulários com `label` visível; erros junto ao campo.

---

## 6. Fluxos principais

### 6.1 Cadastro e primeira despensa

1. Usuário envia e-mail, senha, nome.
2. Backend valida, cria `Usuario`, `Despensa`, `MembroDespensa(Admin)`, categorias e unidades semente (global).
3. Cookie de sessão. Redirect ao painel vazio com CTA “adicionar ingredientes”.

### 6.2 Convite

1. Admin gera convite (papel, validade) → token aleatório, guarda hash.
2. Admin copia URL `/convite/{token}`.
3. Convidado loga ou cadastra, abre o link, vê nome da despensa e papel, aceita.
4. Membership ativo; token marcado usado. Se já era membro, no-op idempotente.

### 6.3 Entrada de estoque (compra)

1. Editor escolhe ingrediente (ou cria) → quantidade, unidade, validade, local.
2. Cliente envia `Idempotency-Key`.
3. Transação: cria/atualiza lote, soma total, grava movimentação `Entrada`.
4. Só então a UI mostra sucesso.

### 6.4 Importar receita por URL

1. Editor cola URL. Cliente não busca a URL (só o servidor).
2. Backend: allowlist de esquema `https`, DNS público, bloqueio de IPs privados/link-local/metadata, limite de redirects (2), timeout 5 s, corpo ≤ 1,5 MB, content-type HTML/JSON.
3. Parser JSON-LD `Recipe`. Se vazio, erro + CTA manual. Sem armazenamento do HTML.
4. Normalização determinística de nomes; IA opcional para sugerir matches (inferidos).
5. Tela de revisão. Salvar cria `Receita` + filhos na despensa.

### 6.5 Análise e preparo

1. Detalhe da receita: usuário ajusta porções (cálculo linear no backend).
2. Serviço de compatibilidade devolve estado, itens, faltas, aproximações.
3. Iniciar preparo cria `Preparo(Iniciado)` + snapshot.
4. Confirmação revalida FEFO. Sucesso → movimentações `BaixaPreparo`. Insuficiência → 409.
5. Segunda confirmação com a mesma chave não duplica baixa.

### 6.6 Lista a partir de receitas

1. Usuário marca receitas + porções.
2. Backend consolida, converte, desconta estoque.
3. Editor ajusta, marca comprados, dispara “lançar no estoque” (uma chave de idempotência para o lote de entradas).

### 6.7 IA desligada ou indisponível

Qualquer endpoint `/ia/*` responde 503 de negócio `{ codigo: "IA_INDISPONIVEL" }`. Telas escondem CTAs ou mostram “configure na despensa”. Estoque, receitas salvas, catálogos e listas seguem.

### 6.8 Saída de membro

1. Confirmação. Se último admin, bloqueio.
2. `Status=Removido`. Convites não usados permanecem (admin que restou gerencia).
3. Job imediato (na mesma transação): apaga `CatalogoReceita` de catálogos pessoais do usuário para receitas da despensa; favoritos deixam de ser visíveis por join.

---

## 7. Modelo conceitual e lógico de dados

### 7.1 Conceitual (agrupado)

- **Identidade:** Usuario
- **Tenancy:** Despensa, MembroDespensa, ConviteDespensa
- **Catálogo de unidades (global):** UnidadeMedida, ConversaoUnidade
- **Ingredientes da despensa:** CategoriaIngrediente, GrupoEquivalencia, Ingrediente, ItemEstoque, LoteEstoque, Movimentacao
- **Receitas da despensa:** FonteReceita, Receita, ReceitaIngrediente, EtapaReceita, ReceitaTag
- **Coleções:** Catalogo, CatalogoReceita, Favorito
- **Operações:** Preparo, PreparoItemSnapshot, ListaCompra, ItemListaCompra
- **IA e plataforma:** ConfiguracaoIA, ExecucaoIA, Arquivo, Idempotencia, AuditoriaMinima

Coleção de ingredientes **não existe** neste modelo. Se no futuro houver, será `ColecaoIngrediente` + `ColecaoIngredienteItem`, nunca reuso de `Catalogo`.

### 7.2 Convenções

- PK `uuid` (`gen_random_uuid()`).
- `created_at`/`updated_at` timestamptz.
- Soft delete só em `Ingrediente.arquivado_em` e `MembroDespensa.status`. Demais exclusões: físicas quando sem histórico; senão bloquear.
- Nomes de tabela no plural snake_case. EF Core com snake_case.

### 7.3 Entidades

#### Usuario

Finalidade: conta de login.

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| email | citext | unique not null |
| password_hash | text | not null |
| nome_exibicao | text | not null, 1–80 |
| despensa_ativa_id | uuid FK nullable | |
| criado_em | timestamptz | |

Relacionamentos: 1:N membros, favoritos, catálogos pessoais, execuções IA.

#### Despensa

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| nome | text | not null, 1–80 |
| fuso | text | default `America/Sao_Paulo` |
| alerta_validade_dias | int | default 7, 1–90 |
| criado_por_usuario_id | uuid FK | |
| criado_em | timestamptz | |

#### MembroDespensa

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| despensa_id | uuid FK | on delete cascade |
| usuario_id | uuid FK | |
| papel | enum | admin, editor, leitor |
| status | enum | ativo, removido |
| entrou_em / saiu_em | timestamptz | |

Unique `(despensa_id, usuario_id)`. Índice `(usuario_id, status)`.

#### ConviteDespensa

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| despensa_id | uuid FK | |
| papel | enum | editor ou leitor (não admin pelo link, para reduzir sequestro) |
| token_hash | bytea | unique not null |
| email_hint | citext nullable | só lembrete visual |
| expira_em | timestamptz | |
| usado_em | timestamptz nullable | |
| revogado_em | timestamptz nullable | |
| criado_por_usuario_id | uuid FK | |

Admin inicial é o criador da despensa; promover a admin só internamente (RF-PAN-05).

#### CategoriaIngrediente

`id`, `despensa_id` FK, `nome`, `ordem`. Unique `(despensa_id, nome_normalizado)`.

#### UnidadeMedida (global, seed)

`id`, `codigo` unique (`G`,`KG`,`ML`,`L`,`UN`,`XICARA`,`COLHER_SOPA`,`COLHER_CHA`), `nome`, `dimensao` (massa, volume, unidade, culinaria), `aproximada` bool.

#### ConversaoUnidade (global)

`de_unidade_id`, `para_unidade_id`, `fator` numeric, `aproximada` bool. PK composta. Fator: 1 kg = 1000 g; 1 L = 1000 ml; 1 xícara = 240 ml (aprox); etc.

#### GrupoEquivalencia

`id`, `despensa_id`, `nome`. Unique `(despensa_id, nome_normalizado)`.

#### Ingrediente

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| despensa_id | uuid FK | |
| nome | text | |
| nome_normalizado | text | unique com despensa |
| categoria_id | uuid FK | |
| unidade_referencia_id | uuid FK | |
| grupo_equivalencia_id | uuid FK nullable | |
| quantidade_minima | numeric(12,3) | >= 0 default 0 |
| local_armazenamento | text nullable | 0–80 |
| observacoes | text nullable | 0–2000 |
| arquivado_em | timestamptz nullable | |

Não apagar se existir lote ou movimentação.

#### ItemEstoque

1:1 com ingrediente. `id`, `despensa_id`, `ingrediente_id` unique, `quantidade_total` numeric(12,3) >= 0, `atualizado_em`.

Pode ser mantido por trigger ou pela aplicação na transação (preferência: aplicação, testável).

#### LoteEstoque

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| item_estoque_id | uuid FK | |
| quantidade | numeric(12,3) | >= 0 |
| quantidade_inicial | numeric(12,3) | > 0 |
| data_validade | date nullable | |
| data_entrada | timestamptz | |
| origem | enum | compra, ajuste, importacao_lista, outro |
| observacao | text nullable | |
| status | enum | aberto, esgotado, descartado |

Índice `(item_estoque_id, status, data_validade)`.

#### Movimentacao

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| despensa_id | uuid FK | |
| item_estoque_id | uuid FK | |
| lote_estoque_id | uuid FK nullable | |
| tipo | enum | entrada, consumo, perda, correcao, baixa_preparo, entrada_compra |
| quantidade | numeric(12,3) | > 0 (sinal dado pelo tipo) |
| unidade_id | uuid FK | |
| motivo | text | 1–500 |
| usuario_id | uuid FK nullable | |
| preparo_id | uuid FK nullable | |
| item_lista_compra_id | uuid FK nullable | |
| idempotency_key | text nullable | |
| criado_em | timestamptz | |

Índices: `(despensa_id, criado_em desc)`, `(item_estoque_id, criado_em desc)`, `(preparo_id)`. Insert-only.

#### FonteReceita (global seed)

`id`, `codigo` (`manual`,`themealdb`,`jsonld`,`ia`), `nome`, `url_termos` nullable, `ativa` bool.

#### Receita

| Atributo | Tipo | Restrições |
| --- | --- | --- |
| id | uuid PK | |
| despensa_id | uuid FK | |
| titulo | text | 1–200 |
| descricao | text nullable | |
| porcoes | numeric(8,2) | > 0 |
| tempo_preparo_min | int nullable | |
| dificuldade | enum nullable | facil, medio, dificil |
| status | enum | rascunho, completa |
| origem | enum | manual, url, busca, ia |
| fonte_receita_id | uuid FK | |
| url_origem | text nullable | |
| autor_atribuicao | text nullable | |
| imagem_arquivo_id | uuid FK nullable | |
| imagem_url | text nullable | |
| observacoes | text nullable | |
| criado_por_usuario_id | uuid FK nullable | |

Índice texto: `GIN` em `to_tsvector('portuguese', titulo || ' ' || coalesce(descricao,''))`.

#### ReceitaIngrediente

`id`, `receita_id` FK cascade, `ordem`, `texto_original`, `ingrediente_id` nullable, `quantidade` numeric nullable, `unidade_id` nullable, `opcional` bool, `confianca` enum (`confirmada`,`inferida`,`incerta`), `grupo_equivalencia_id` nullable.

Receita incompleta é válida: quantidade nula ⇒ não entra em `pode_preparar`.

#### EtapaReceita

`id`, `receita_id` cascade, `ordem` unique por receita, `instrucao` text.

#### ReceitaTag

`receita_id`, `tag` (normalizada). PK composta.

#### Catalogo

`id`, `despensa_id` FK, `usuario_id` nullable (obrigatório se `visibilidade=pessoal`), `nome`, `descricao`, `visibilidade` enum (`pessoal`,`despensa`), `criado_por_usuario_id`, timestamps.

Check: pessoal ⇒ `usuario_id is not null`. Unique `(despensa_id, usuario_id, nome_normalizado)` para pessoais; unique `(despensa_id, nome_normalizado) where visibilidade=despensa`.

#### CatalogoReceita

`catalogo_id`, `receita_id`, `adicionado_em`. PK composta. FK receita da **mesma** despensa (validar na aplicação + trigger).

#### Favorito

`usuario_id`, `receita_id`, `criado_em`. PK composta. Não duplica receita.

#### Preparo

`id`, `despensa_id`, `receita_id` (sem cascade delete; RESTRICT ou SET snapshot-only), `porcoes`, `status` (iniciado, confirmado, cancelado), `usuario_id`, `idempotency_key` unique nullable, `confirmado_em`, `consumir_disponivel` bool default false, `criado_em`.

#### PreparoItemSnapshot

`preparo_id`, `ingrediente_id` nullable, `texto`, `quantidade_prevista`, `quantidade_baixada`, `unidade_id`, `lote_ids` jsonb, `faltou` bool. Preserva o que foi efetivamente consumido.

#### ListaCompra

`id`, `despensa_id`, `nome`, `status` (aberta, comprada, arquivada), `origem` (manual, receitas), `criado_por_usuario_id`.

#### ItemListaCompra

`id`, `lista_id` cascade, `ingrediente_id` nullable, `texto_livre`, `quantidade_necessaria`, `quantidade_descontada_estoque`, `quantidade_comprar`, `unidade_id`, `comprado` bool, `entrada_registrada` bool, `movimentacao_id` nullable.

#### ConfiguracaoIA

`despensa_id` PK FK, `base_url`, `modelo`, `api_key_cifrada` bytea nullable, `ativa` bool, `limite_hora` int default 20, `atualizado_por_usuario_id`.

Cifra: AES-256-GCM com chave de ambiente `DATA_PROTECTION_KEY` (ASP.NET Data Protection em volume persistente).

#### ExecucaoIA

`id`, `despensa_id`, `usuario_id`, `tipo` enum, `provedor`, `modelo`, `status`, `tokens_in/out` int, `erro_codigo` nullable, `resultado_validado` jsonb nullable, `duracao_ms`, `criado_em`. Sem API key, sem prompt completo se contiver dados demais — guardar hash do prompt.

#### Arquivo

`id`, `despensa_id`, `nome_original`, `content_type` (allowlist jpeg/png/webp), `tamanho_bytes` (máx 2 MB), `caminho_relativo`, `criado_por_usuario_id`. Arquivos no volume `/data/uploads/{despensaId}/{id}`.

#### Idempotencia

`id`, `usuario_id`, `despensa_id`, `rota`, `chave`, `request_hash`, `status_code`, `response_body` jsonb, `criado_em`. Unique `(usuario_id, rota, chave)`.

### 7.4 Índices adicionais

- `lote_estoque (data_validade) where status='aberto' and data_validade is not null`
- `item_estoque (despensa_id, quantidade_total)`
- `receita (despensa_id, status)`
- `movimentacao (idempotency_key) where idempotency_key is not null`

### 7.5 Migrações

- EF Core migrations no projeto `Infrastructure`.
- Seed em migração de dados: unidades, conversões, fontes.
- Categorias: seed **por despensa** no use case de criação (não global).
- Política: never expand/contract na mesma migração destrutiva; colunas novas nullable ou com default.
- Backup antes de migrar em produção auto-hospedada (script `scripts/backup.sh`).

### 7.6 Regras de exclusão (resumo)

| Entidade | Exclusão |
| --- | --- |
| Usuario | Anonimização + delete credencial (RN-ACL-05) |
| Despensa | Admin; cascade membros, ingredientes, receitas, listas; arquivos apagados do disco |
| Ingrediente | Arquiva; delete físico só sem histórico |
| Receita | Delete físico; cascade etapas/ings/tags/associações; preparos mantêm snapshot |
| Catalogo | Delete físico; cascade associações |
| Lote / Movimentacao | Sem delete; lote esgota |
| Preparo | Sem delete |

---

## 8. Arquitetura técnica e justificativa

### 8.1 Decisão de stack (MVP gratuito)

| Camada | Escolha | Por que | Alternativa rejeitada no MVP |
| --- | --- | --- | --- |
| Frontend | React 18 + TypeScript + Vite | Padrão pedido, OSS, PWA maduro | Next.js (SSR desnecessário e mais hospedagem) |
| UI | Tailwind CSS + React Aria Components | Acessibilidade de verdade, zero licença | MUI (mais peso); Chakra pago em partes |
| PWA | `vite-plugin-pwa` (Workbox) | Manifesto + SW sem serviço extra | |
| Backend | ASP.NET Core 8 Web API (C#) | Pedido, excelente para transações e Identity | Node/Nest (duplicaria stack se o time é .NET) |
| ORM | EF Core 8 + Npgsql | Migrações sólidas | Dapper puro (mais trabalho em MVP) |
| Banco | PostgreSQL 16 | Pedido, OSS, constraints reais de estoque | SQLite (pior concorrência de baixa FEFO) |
| Auth | ASP.NET Identity + cookie | Zero custo, sem Auth0/Clerk | JWT bearer sem cookie (pior para web same-site) |
| Imagens | Disco local | Zero custo | S3/R2 (conta e cartão) |
| Busca receitas | TheMealDB + JSON-LD | Gratuito, termos claros | Spoonacular (pago) |
| IA | Cliente OpenAI-compatível no backend | Ollama local = R$ 0; Groq/Gemini BYOK | Chave da aplicação (custo e vazamento) |
| Deploy | Docker Compose, API serve `wwwroot` | Um domínio, zero PaaS | Azure/AWS pagos; Render free instável |
| Observabilidade | Serilog → stdout + `/health` | Sem SaaS | Datadog/App Insights |

A stack sugerida no briefing **é adequada** e permanece. O que muda em relação a um desenho “cloud nativo pago” é: Identity próprio, arquivos em disco, IA BYOK, um único processo servindo SPA+API, nenhum Redis.

### 8.2 Diagrama lógico

```
[Navegador PWA]
    | HTTPS cookie
[ASP.NET Core]
    ├── UI estática (React)
    ├── Módulos de aplicação (Despensa, Estoque, Receitas, Compatibilidade, Preparo, Listas, IA)
    ├── Identity
    ├── Clientes: TheMealDB, HttpImport (SSRF-safe), OpenAI-compatible
    └── EF Core
          └── PostgreSQL
/data/uploads  (volume)
/data/keys     (Data Protection)
```

Regras de estoque, autorização e idempotência **somente no backend**. O frontend replica validações para UX, nunca como garantia.

### 8.3 Estrutura de repositório proposta (após aprovação)

```
/src/ReceitasMercado.Api          # host ASP.NET
/src/ReceitasMercado.Application  # casos de uso
/src/ReceitasMercado.Domain       # entidades, RN
/src/ReceitasMercado.Infrastructure
/src/ReceitasMercado.Web          # Vite React
/tests/Unit
/tests/Integration
/tests/E2E                        # Playwright
/docker-compose.yml
/docs/PLANEJAMENTO.md
```

Clean Architecture leve: Domain sem EF; Application com interfaces; Infrastructure implementa. Evitar microserviços.

### 8.4 Integrações encapsuladas

Interfaces no Application:

- `IRecipeSearchClient` → TheMealDB
- `IRecipeUrlImporter` → fetch + JSON-LD
- `IAiCompletionClient` → OpenAI compatible
- `IFileStorage` → disco
- `IClock`, `IIdGenerator`

Trocar TheMealDB ou o provedor de IA não altera Domain.

### 8.5 PWA

- Precache de `/`, JS/CSS, ícones.
- Network-first para `/api/*`; sem fallback de POST.
- `offline.html` mínimo só para a casca.
- Sem Background Sync no MVP.

---

## 9. Contratos e responsabilidades dos módulos e APIs

Prefixo `/api/v1`. JSON camelCase. Erros: `{ codigo, mensagem, detalhes? }`. Autenticação por cookie. Header `X-Despensa-Id` obrigatório nas rotas de tenancy (além do cookie). CSRF: SameSite=Lax + header `X-Requested-With` exigido em mutações.

### 9.1 Módulos

| Módulo | Responsabilidade | Não faz |
| --- | --- | --- |
| Identity | Conta, sessão, senha | Autorização de despensa |
| Despensa | Membros, convites, config | Estoque |
| Estoque | Ingredientes, lotes, movimentos, alertas | Receitas |
| Receitas | CRUD, tags, revisão | Baixa de estoque |
| Importacao | TheMealDB, URL, SSRF | Persistência final (devolve DTO de revisão) |
| Compatibilidade | Comparação e % | Mutação |
| Preparo | Snapshot, confirmação FEFO | IA |
| Catalogos | Favoritos e catálogos | Duplicar entidade Receita |
| Listas | Consolidar e lançar compra | Precificação |
| IA | Proxy + schema + rate limit | Escrever estoque |
| Arquivos | Upload/download autorizado | Processamento pesado de imagem além de limite |

### 9.2 Endpoints (MVP)

**Auth**

- `POST /auth/registrar`
- `POST /auth/entrar`
- `POST /auth/sair`
- `GET /auth/me`
- `PUT /auth/me`
- `PUT /auth/senha`
- `DELETE /auth/me`

**Despensas**

- `GET /despensas`
- `PATCH /despensas/{id}`
- `POST /despensas/{id}/convites`
- `GET /despensas/{id}/convites`
- `DELETE /despensas/{id}/convites/{conviteId}`
- `POST /convites/{token}/aceitar`
- `GET /despensas/{id}/membros`
- `PATCH /despensas/{id}/membros/{usuarioId}`
- `DELETE /despensas/{id}/membros/{usuarioId}`
- `POST /despensas/{id}/sair`

**Estoque**

- `GET /categorias` / `POST` / `PATCH /{id}` / `DELETE /{id}`
- `GET /ingredientes` (query: q, categoria, alerta, ordem)
- `POST /ingredientes`
- `GET|PATCH /ingredientes/{id}`
- `POST /ingredientes/{id}/arquivar`
- `GET /ingredientes/{id}/lotes`
- `GET /ingredientes/{id}/movimentacoes`
- `POST /estoque/entradas` (Idempotency-Key)
- `POST /estoque/consumos`
- `POST /estoque/perdas`
- `POST /estoque/correcoes`
- `GET /alertas`
- `GET /unidades`

**Receitas**

- `GET /receitas` (inclui `estadoCompatibilidade` calculado sob demanda ou em batch)
- `POST /receitas`
- `GET|PATCH|DELETE /receitas/{id}`
- `GET /receitas/{id}/compatibilidade?porcoes=`
- `GET /receitas/busca-online?q=`
- `POST /receitas/importar-url` body `{ url }` → DTO revisão
- `POST /receitas/from-revisao`

**Favoritos e catálogos**

- `PUT|DELETE /receitas/{id}/favorito`
- `GET /favoritos`
- `GET|POST /catalogos`
- `PATCH|DELETE /catalogos/{id}`
- `POST /catalogos/{id}/duplicar` body `{ incluirReceitas: bool }`
- `PUT|DELETE /catalogos/{id}/receitas/{receitaId}`

**Preparo**

- `POST /preparos` body `{ receitaId, porcoes }`
- `POST /preparos/{id}/confirmar` (+ Idempotency-Key, `{ consumirDisponivel? }`)
- `POST /preparos/{id}/cancelar`
- `GET /preparos`

**Listas**

- `GET|POST /listas-compra`
- `GET|PATCH /listas-compra/{id}`
- `POST /listas-compra/from-receitas`
- `PATCH /listas-compra/{id}/itens/{itemId}`
- `POST /listas-compra/{id}/lancar-entradas` (+ Idempotency-Key)

**IA**

- `GET|PUT /despensas/{id}/ia`
- `POST /despensas/{id}/ia/testar`
- `POST /ia/sugerir-receitas`
- `POST /ia/gerar-receita`
- `POST /ia/normalizar-ingredientes`
- `POST /ia/substituicoes`
- `POST /ia/adaptar-etapas`

**Arquivos / saúde**

- `POST /arquivos` multipart
- `GET /arquivos/{id}`
- `GET /health` (db ping)
- `GET /health/ready`

### 9.3 Exemplo de contrato — confirmar preparo

Pedido:

```json
{ "consumirDisponivel": false }
```

Sucesso 200: preparo + movimentações ids + totais novos.

Conflito 409:

```json
{
  "codigo": "ESTOQUE_INSUFICIENTE",
  "faltantes": [
    { "ingredienteId": "...", "nome": "Farinha", "necessario": 500, "disponivel": 200, "unidade": "G" }
  ]
}
```

### 9.4 Paginação

Listas: `cursor` opaco + `limit` (max 50). Evitar `offset` em movimentações.

---

## 10. Estratégia de importação, busca online e IA

### 10.1 Busca online (TheMealDB)

- Endpoint público `https://www.themealdb.com/api/json/v1/1/search.php?s=`
- Mapear: título, instruções (fatiar em etapas por linha), ingredientes `strIngredientN` + `strMeasureN`, imagem `strMealThumb` (guardar URL, não rehost obrigatório), atribuição “TheMealDB”, fonte id.
- Cache HTTP curto em memória (5 min) por query para não abusar.
- Não apresentar como receita da casa até “Salvar na despensa”.
- Respeitar [termos TheMealDB](https://www.themealdb.com/); uso gratuito com atribuição.

### 10.2 Importação por URL

1. Validar URL (`https` only).
2. Resolver DNS; rejeitar se qualquer IP ∈ redes privadas, loopback, link-local, ULA IPv6, metadata `169.254.169.254`.
3. Conectar com `HttpClient` dedicado (sem proxy aberto), `AllowAutoRedirect=false`, seguir no máximo 2 redirects revalidando IP a cada hop.
4. Timeout 5 s; `MaxResponseContentBufferSize` 1,5 MB.
5. Aceitar `text/html`, `application/ld+json`, `application/json`.
6. Extrair `<script type="application/ld+json">` e achar `@type=Recipe`.
7. Se o site enviar `X-Robots-Tag: noindex` + vedação explícita de scraping em termos conhecidos, ainda assim só extraímos JSON-LD público (não o artigo). Se não houver JSON-LD: falha educada.
8. Nunca executar JS (sem headless browser no MVP — reduz custo e superfície).
9. Campos extraídos viram DTO de revisão, `origem=url`, `confianca` inicial `incerta` até o usuário confirmar matches.

Sites sem JSON-LD: mensagem “não foi possível ler uma receita estruturada nesta página” + cadastro manual. Não implementar parser visual genérico no MVP (frágil e juridicamente mais arriscado).

### 10.3 Normalização de ingredientes

Pipeline determinístico primeiro:

1. lowercase, remover acentos, unidades do texto (“200 g de farinha” → qtd 200, un G, nome farinha).
2. alias locais da despensa (nome exato / normalizado).
3. grupo de equivalência.
4. Se IA ligada e ainda incerto: sugerir top-3 matches `inferida`. Usuário confirma → `confirmada`.

### 10.4 IA

**Provedor:** qualquer endpoint compatível com `POST /v1/chat/completions` (Ollama `http://localhost:11434/v1`, Groq, Gemini OpenAI-compat, etc.).

**Custo:** R$ 0 para o produto. Quem paga (ou não) é o dono da chave/Ollama.

**Segurança da chave:** Data Protection; nunca no frontend após salvar; nunca em logs; volume `/data/keys` fora do git.

**Prompts:** system prompt fixo versionado no código; user payload com ingredientes (nome, qtd, validade em dias), sem e-mail de membros. JSON Schema strict (`response_format`).

**Validação:** schema (título, porções, ingredientes[], etapas[]). Quantidades numéricas. Unidades no enum conhecido. Recusar alucinadas (unidade `tonelada`, 10^9 g). Marcar todos os ingredientes gerados como `inferida`.

**Adaptação de porções:** default **não usa IA** — regra `q * porcoesDesejadas / porcoesOriginais`. IA só para reescrever etapas se pedido.

**Substituição:** IA devolve sugestões; aplicação verifica se o substituto existe no estoque; usuário aplica.

**Indisponibilidade:** timeout 20 s, 429/5xx → `IA_INDISPONIVEL`. Circuit breaker simples (3 falhas / 5 min).

### 10.5 Limites

| Recurso | Limite MVP |
| --- | --- |
| Busca TheMealDB | 30/hora/despensa (além do cache) |
| Import URL | 20/hora/despensa |
| IA | 20/hora/despensa (configurável, máx 60) |
| Upload | 2 MB, 50 imagens/despensa no MVP gratuito |

---

## 11. Requisitos não funcionais

Metas verificáveis. Carga de referência: 1 despensa, 200 ingredientes, 500 lotes, 300 receitas, 10 membros, 5 req/s de leitura.

| ID | Área | Meta |
| --- | --- | --- |
| RNF-01 | Responsividade | Layout utilizável 320–1280+ px; sem scroll horizontal em fluxos principais. |
| RNF-02 | PWA | Instalável em Chrome/Edge Android e desktop Chromium; iOS Safari adiciona à tela inicial (limitações Safari documentadas). |
| RNF-03 | Desempenho leitura | P95 ≤ 500 ms em `GET /ingredientes`, `GET /receitas`, `GET /receitas/{id}/compatibilidade` na carga de referência, **excluindo** TheMealDB/IA, medido no servidor. |
| RNF-04 | Desempenho escrita | P95 ≤ 800 ms em entrada de estoque e confirmação de preparo com ≤ 20 lotes tocados. |
| RNF-05 | Frontend | First load da casca ≤ 200 KB gzip de JS inicial (orçamento; code split rotas). |
| RNF-06 | Segurança | Ver seção 12. Headers: HTTPS, HSTS (quando TLS), CSP restritiva, nosniff, frame-ancestors `'none'`. |
| RNF-07 | Autorização | Testes de integração comprovam 404 cross-despensa e 403 leitor em POST. |
| RNF-08 | Transação | Preparo confirmado é atômico (teste de kill no meio não deixa total ≠ soma lotes). |
| RNF-09 | Idempotência | Replay de POST com mesma chave não duplica lote (teste). |
| RNF-10 | Acessibilidade | WCAG 2.2 AA como objetivo. MVP gate: teclado, foco visível, labels, contraste 4.5:1, `prefers-reduced-motion`, erros anunciados. Auditoria axe nas telas críticas. |
| RNF-11 | Navegadores | Últimas 2 versões Chrome, Firefox, Edge; Safari 16+. Sem IE. |
| RNF-12 | Persistência | Dados sobrevivem logout, fechamento, reinstalação PWA (estão no Postgres). |
| RNF-13 | Observabilidade | Serilog: request id, user id, despensa id, latência, código de erro. Sem senha, cookie, Authorization, API key. `/health` e `/health/ready`. |
| RNF-14 | Manutenibilidade | Arquitetura em módulos; 1 PR ≈ 1 módulo; `.editorconfig` + `dotnet format`. |
| RNF-15 | Testabilidade | Domain sem I/O; use cases com fakes; Testcontainers Postgres nas integrações. |
| RNF-16 | Custo | R$ 0 de SaaS obrigatório. CPU/disco da máquina anfitriã apenas. |
| RNF-17 | Backup | `pg_dump` diário + retenção 7 cópias em `/data/backups` (cron no compose). Restore documentado. |
| RNF-18 | Disponibilidade | Sem SLA de nuvem. Objetivo local: API sobe se Postgres healthy (restart policies). |

---

## 12. Estratégia de segurança, privacidade e backup

### 12.1 Credenciais e sessão

- Senha: Identity hasher (PBKDF2 ou Argon2id se disponível na versão).
- Cookie: `HttpOnly`, `Secure` (configurável em dev), `SameSite=Lax`, nome `__Host-rm-auth` quando HTTPS.
- Lockout após 10 tentativas / 15 min.
- Data Protection keys no volume, não em repositório.

### 12.2 Validação e autorização

- FluentValidation no Application.
- Autorização: policy `DespensaPapel`. Nunca confiar em `despensaId` do body sem membership.
- Uploads: magic-byte check, não só extensão.

### 12.3 SSRF e conteúdo importado

Ver 10.2. Adicional: User-Agent identificável `ReceitasMercado/0.1`; não enviar cookies de usuário a terceiros.

### 12.4 IA

- Chave cifrada.
- Opt-in explícito com texto: “ingredientes e títulos serão enviados ao provedor que você configurou”.
- Sem treino nosso (não temos modelo).
- Sanitizar saída (sem HTML cru nas etapas; escape na UI).

### 12.5 Idempotência e concorrência

- Isolation: transação com `FOR UPDATE` nos lotes.
- Dois editores confirmando o mesmo ingrediente: o segundo revalida; se faltou, 409.
- Idempotency store.

### 12.6 Privacidade e retenção

- Dados da despensa visíveis só a membros ativos.
- Logs 14 dias no stdout/arquivo rotacionado.
- Backup 7 dias, acesso só ao host.
- Exclusão de conta: 30 dias para purge completo de backups (documentar; no MVP o cron local não expurga backups antigos automaticamente além da janela de 7).
- Sem analytics de terceiros no MVP.

### 12.7 Backup e recuperação

1. `pg_dump -Fc` diário.
2. Cópia do volume `/data/uploads` (rsync).
3. Restore: `pg_restore` em instância vazia + volume de arquivos.
4. Drill manual documentado no README (após aprovação, na implementação).
5. Falha de disco: perda máxima = 24 h + transações do dia (aceitável para app doméstico gratuito). WAL contínuo fica para FUT.

### 12.8 Conexão

- Axios/fetch com timeout.
- Interceptor: se `navigator.onLine===false` ou TypeError de rede, faixa “sem conexão” e rejeita a Promise da mutação.
- Não marcar favorito localmente como salvo.

### 12.9 Segredos

- `.env` no host: `ConnectionStrings__Postgres`, `DATA_PROTECTION_KEY` path, `ASPNETCORE_ENVIRONMENT`.
- Compose lê env; exemplo `.env.example` sem segredos.

---

## 13. Plano de testes e critérios de aceitação

Pirâmide: unitários de domínio (conversão, FEFO, compatibilidade, consolidação) → integração API+Postgres → e2e Playwright dos fluxos felizes → segurança (authz, SSRF) → PWA smoke.

### 13.1 Unitários (Domain/Application)

- Conversão g/kg, ml/L; recusa massa↔volume; flag aproximada xícara.
- FEFO com validades mistas e nulas.
- Baixa que esgota dois lotes e não negativa.
- Compatibilidade: pode / falta / verificar (incerto, aproximado, opcional).
- Percentual ignora incertos.
- Consolidação de lista com equivalentes.
- Normalização de nome.
- Idempotência: segundo comando no-op.
- Convite expirado/revogado.
- Último admin não sai.

### 13.2 Integração

- Registrar → cookie → CRUD ingrediente → entrada → consumo.
- Dois POSTs paralelos de consumo (mesmo lote) → um 200 e um 409 ou ambos consistentes, total ≥ 0 e = soma lotes.
- Replay `Idempotency-Key` na entrada.
- Leitor POST `/estoque/entradas` → 403.
- Header de outra despensa → 404.
- Import URL `http://127.0.0.1/` → 400 `SSRF`.
- TheMealDB mockado → revisão → salvar.
- IA mockada JSON inválido → 422, estoque intacto.
- IA down → 503, GET receitas 200.
- Confirmar preparo reduz lotes FEFO e grava snapshot.
- `consumirDisponivel` explícito.
- Lista from receitas desconta estoque; segundo lançar-entradas não duplica.
- Duplicar catálogo não duplica `receitas` count.
- Sair da despensa perde GET da receita (404) e some favorito na listagem.

### 13.3 E2E

- Cadastro, login, logout, reinstalação (storage persistente no servidor: login de novo vê dados).
- Fluxo compra → receita manual → análise → lista → marcar comprado → lançar → preparar.
- Redimensionar 375 px: navegação utilizável.
- Desligar rede no DevTools: mutação mostra erro, sem toast de sucesso.
- Axe nas telas: entrar, estoque, detalhe receita, preparar.

### 13.4 PWA

- Manifest `standalone`.
- SW registra. Offline: casca ou aviso, nunca 200 falso de API.
- Lighthouse PWA installable (Chromium).

### 13.5 Segurança

- SQL injection tentada em busca (parametrizado).
- XSS: título de receita com `<script>` renderiza texto.
- Upload `.exe` disfarçado → 400.
- Rate limit IA.

### 13.6 Critérios de aceitação por RF (formato Given/When/Then — amostra normativa)

Os RFs da seção 3 herdam este formato. Amostras obrigatórias no CI:

**RF-STK-11 / RN-STK-01**  
Dado lote com 1 un, quando duas confirmações paralelas de consumo 1 un, então uma falha e o total final é 0, nunca −1.

**RF-CMP-05**  
Dado ingrediente obrigatório com `confianca=incerta` e estoque cheio, quando analiso, então estado `verificar_ingredientes` e o item não entra no percentual como ok.

**RF-PREP-04**  
Dado falta de 50 g, quando confirmo sem `consumirDisponivel`, então 409 e lotes inalterados.

**RF-SHP-08**  
Dado lista lançada, quando relanço com a mesma Idempotency-Key, então nenhum lote extra.

**RF-IA-10**  
Dado IA não configurada, quando abro estoque e listas, então 200 e nenhuma dependência de rede de IA.

**RF-PWA-02**  
Dado SW ativo e API morta, quando salvo ingrediente, então mensagem de falha e recarregar autenticado não mostra o ingrediente.

**RF-PAN-07**  
Dado único admin, quando tenta sair, então erro de negócio e membership permanece.

**RF-REC-10**  
Dado import OK, então tabela `receitas` tem url e atribuição, e nenhum HTML bruto > 10 kB no banco.

Cobertura mínima de CI: unitários > 80% no Domain de estoque/compatibilidade; integração dos fluxos acima green; e2e smoke em PR.

---

## 14. Riscos, dependências e decisões pendentes

### 14.1 Riscos

| ID | Risco | Impacto | Mitigação |
| --- | --- | --- | --- |
| R01 | Correspondência ruim de ingredientes derruba confiança da análise | Alto | Estados `verificar`; nunca contar incerto; revisão humana na importação |
| R02 | SSRF na importação | Alto | Checklist de rede na seção 10.2 + testes |
| R03 | TheMealDB fora do ar ou limite | Médio | Cache, cadastro manual, IA opcional |
| R04 | Conversão culinária engana o usuário | Médio | Flag aproximada → `verificar` |
| R05 | Concorrência de baixa | Alto | Transação + FOR UPDATE + testes paralelos |
| R06 | Chave de IA vazada no frontend/logs | Alto | BYOK cifrada, nunca GET da chave |
| R07 | Conteúdo importado com restrição de copyright | Médio | Só JSON-LD/API licenciada; atribuição; não espelhar site |
| R08 | Auto-hospedagem sem backup | Alto | Script e compose documentados |
| R09 | Safari PWA limitado | Baixo | Documentar; app usável no browser |
| R10 | Escopo de catálogos pessoais vs despensa gerar confusão | Médio | Labels “só eu” / “despensa”; decisão D05 |
| R11 | Parser de medidas em PT-BR incompleto | Médio | Tabela de regex + revisão |
| R12 | Volume de disco de imagens | Baixo | Cota 50 × 2 MB |

### 14.2 Dependências externas

- PostgreSQL 16
- TheMealDB (opcional em runtime)
- DNS/rede para import URL (opcional)
- Endpoint de IA do usuário (opcional)
- Navegador Chromium para PWA completo

### 14.3 Premissas (já adotadas) versus decisões pendentes

**Premissas P01–P15 (seção 1.1)** valem até serem rejeitadas.

**Decisões que precisam de aprovação explícita** (bloqueiam ou alteram implementação):

| ID | Pergunta | Recomendação | Impacto se mudar |
| --- | --- | --- | --- |
| D01 | Nome de produto visível (Receitas-Mercado vs outro)? | Manter Receitas-Mercado | Só copy/ícones |
| D02 | Convite promove a **admin** pelo link? | Não; só editor/leitor; admin só por membro existente | ACL |
| D03 | Recuperação de senha no MVP sem e-mail? | Não; usuário cria outra conta ou SQL local | Auth |
| D04 | Uma despensa criada no cadastro basta, ou o usuário cria N despensas vazias? | Só a pessoal automática + convites; criar extra é FUT | API despensa |
| D05 | Catálogos da despensa no MVP além dos pessoais? | Sim, ambos | Modelo Catalogo |
| D06 | Opção “consumir apenas o disponível” no preparo? | Sim, desmarcada por padrão | RN-PREP-03 |
| D07 | Hospedagem alvo: só Docker local, ou também tentar Neon free + Cloudflare Pages? | Só Docker Compose no MVP para garantir R$ 0 | Deploy, CORS, cookies Secure |
| D08 | Rehost de imagens TheMealDB ou só hotlink? | Só URL original | Storage, copyright |
| D09 | Permitir Ollama como default documentado (`llama3.2` local)? | Sim, doc; app não instala modelo sozinha | Docs IA |
| D10 | Exclusão definitiva da despensa no MVP? | Sim, com digitação do nome | Admin API |
| D11 | Unidades americanas (cup/oz) no seed? | Não no MVP | Seed |
| D12 | Múltiplas despensas ativas em abas vs seletor único? | Seletor único + header `X-Despensa-Id` | Cliente |
| D13 | Leitor vê quantidades exatas? | Sim (é a despensa da família) | ACL |
| D14 | Limite de membros? | 10 no MVP (família) | Convites |
| D15 | Testes e2e no CI da nuvem (sem display) são obrigatórios no primeiro PR de código? | Sim Playwright no compose | Pipeline |

---

## 15. Roadmap de desenvolvimento (etapas técnicas)

Ordem pensada para um agente de implementação. Cada etapa termina com testes daquela fatia e a aplicação “andando” até ali.

| Etapa | Entrega | Critério de saída |
| --- | --- | --- |
| E0 | Repositório, Docker Compose (Postgres), solution .NET, Vite, CI format/test vazio, Serilog, health | `compose up` + `/health` 200 |
| E1 | Identity, cookie, registrar cria despensa + seed categorias | Cadastro/login e2e |
| E2 | Membros, convites por link, papéis, header despensa | Testes ACL |
| E3 | Unidades, ingredientes, lotes, movimentações, FEFO, idempotência, alertas | Testes de concorrência de estoque |
| E4 | Telas de estoque responsivas + PWA casca | Lista/detalhe/movimentar no browser |
| E5 | CRUD receitas manuais, etapas, tags, correspondência | Receita rascunho e completa |
| E6 | Motor de compatibilidade + UI de estados e % | Testes unitários do motor |
| E7 | Preparo + snapshot + 409 + consumirDisponivel | Integração atômica |
| E8 | Favoritos e catálogos (pessoal/despensa, duplicar) | Associações sem copiar receita |
| E9 | Listas de compras + lançar entradas | Consolidação testada |
| E10 | TheMealDB + import URL SSRF + tela de revisão | Testes SSRF |
| E11 | IA BYOK + schema + rate limit + degradação | Mock e 503 |
| E12 | Acessibilidade axe, CSP, backup script, polimento PWA, dados semente demo | Checklist RNF |
| E13 | Hardening, logs, documentação de restore, corte de escopo residual | Pronto para uso doméstico |

Não paralelizar E3 com E7 (ambos mexem em lote). E10/E11 podem seguir em paralelo após E5.

---

## 16. Estimativa de complexidade, custos e critérios de conclusão

### 16.1 Complexidade (relativa)

| Bloco | Complexidade | Notas |
| --- | --- | --- |
| Auth + despensa + convites | Média | Identity conhecido; convite sem e-mail simplifica |
| Estoque/lotes/FEFO/idempotência | Alta | Núcleo de confiabilidade |
| Receitas manuais + UI | Média | Formulários longos, mobile |
| Compatibilidade + unidades | Alta | Casos de incerteza |
| Preparo atômico | Alta | Concorrência |
| Catálogos/favoritos | Baixa | N:N simples |
| Listas | Média | Consolidação |
| Import URL/SSRF | Média-alta | Segurança |
| TheMealDB | Baixa | Cliente HTTP |
| IA BYOK | Média | Isolável |
| PWA + a11y | Média | Disciplina, não feature |

Esforço total de implementação (após aprovação): da ordem de **um sistema modular médio**, não um CRUD. O risco está em E3–E7 e E10, não em telas.

### 16.2 Custos operacionais (meta: zero)

| Item | Custo MVP | Observação |
| --- | --- | --- |
| React, Aria, Tailwind, Vite, PWA | R$ 0 | OSS |
| ASP.NET Core, EF Core | R$ 0 | OSS |
| PostgreSQL | R$ 0 | OSS + Docker |
| TheMealDB | R$ 0 | API pública com atribuição |
| Import URL | R$ 0 | Tráfego do próprio host |
| IA | R$ 0 | Desligada ou Ollama/BYOK do usuário |
| Auth0, SendGrid, S3, Redis, Spoonacular, OpenAI da aplicação | não usados | |
| Energia/CPU da máquina anfitriã | do usuário | único custo real |
| Domínio TLS | opcional | HTTP local no LAN; Caddy+Let's Encrypt se houver domínio próprio (grátis) |

Não usar Neon/Cloudflare no caminho crítico para não criar dependência de cartão ou quota surpresa. Se no futuro o aprovador quiser, D07 pode reabrir.

Capacidade estimada no compose 1 CPU / 1 GB RAM: dezenas de famílias leves; o produto é single-tenant-host (uma instalação = um servidor = N despensas de amigos/família).

### 16.3 Critérios de conclusão do planejamento

Este planejamento está **completo para implementação** quando o aprovador:

1. Aceitar P01–P15 ou listar exceções.
2. Responder D01–D15 (ou “seguir recomendações”).
3. Confirmar que o MVP da seção 2.1 é o escopo do primeiro ciclo de código.
4. Autorizar explicitamente o início da etapa E0.

### 16.4 Critérios de conclusão do produto MVP

- Todos os RF do MVP com testes da seção 13 verdes na carga de referência.
- RNF-03 medido (p95 leituras ≤ 500 ms sem externos).
- Nenhum caminho de estoque sem transação/idempotência.
- IA desligada não quebra nada.
- PWA instalável em Chromium.
- `docker compose up` documentado gera instância utilizável em PT-BR, sem chaves pagas.
- Backup/restore ensaiado uma vez.

---

## Apêndice A — Semente de unidades

| Código | Nome | Dimensão | Aproximada | Conversão para SI |
| --- | --- | --- | --- | --- |
| G | grama | massa | não | 1 g |
| KG | quilograma | massa | não | 1000 g |
| ML | mililitro | volume | não | 1 ml |
| L | litro | volume | não | 1000 ml |
| UN | unidade | unidade | não | 1 |
| XICARA | xícara (240 ml) | culinaria | sim | 240 ml |
| COLHER_SOPA | colher de sopa | culinaria | sim | 15 ml |
| COLHER_CHA | colher de chá | culinaria | sim | 5 ml |

## Apêndice B — Estados de compatibilidade (UI)

| Estado | Cor semântica | Quando |
| --- | --- | --- |
| Pode preparar | sucesso | RN-CMP-01 |
| Faltam ingredientes | aviso | RN-CMP-02 |
| Verificar ingredientes | informação | RN-CMP-03 |
| Rascunho | neutro | receita incompleta |

Texto do percentual: “Compatibilidade estimada. Itens sem correspondência confirmada e medidas aproximadas não entram como disponíveis.”

## Apêndice C — O que este documento não autoriza

- Implementar código de produção antes da aprovação das decisões D01–D15.
- Adicionar dependências pagas.
- Scraping genérico de HTML de blogs.
- Fila offline de escrita.
- Tratar catálogo de receitas como estoque de ingredientes.
