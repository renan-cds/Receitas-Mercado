export type Papel = 'leitor' | 'editor' | 'admin'
export type Estado = 'pode_preparar' | 'faltam_ingredientes' | 'verificar_ingredientes' | 'rascunho'
export type Confianca = 'confirmada' | 'inferida' | 'incerta'
export type LineStatus = 'ok' | 'approximate' | 'insufficient' | 'uncertain' | 'no_quantity' | 'incompatible_unit'

export interface DespensaResumo { id: string; nome: string; papel: Papel }
export interface Me { id: string; email: string; nome: string; despensaAtivaId: string | null; despensas: DespensaResumo[] }
export interface Unidade { codigo: string; nome: string; abreviacao: string; dimensao: string; aproximada: boolean }
export interface Categoria { id: string; nome: string; ordem: number; ingredientes: number }
export interface GrupoEquivalencia { id: string; nome: string; ingredientes: { id: string; nome: string }[] }

export interface IngredienteItem {
  id: string; nome: string; categoriaId: string; categoriaNome: string; unidadeReferencia: string
  grupoEquivalenciaId: string | null; quantidadeMinima: number; localArmazenamento: string | null; observacoes: string | null
  arquivado: boolean; quantidadeTotal: number; proximaValidade: string | null
  situacaoValidade: 'sem_validade' | 'vencido' | 'vencendo' | 'ok'; abaixoDoMinimo: boolean
}
export interface Lote {
  id: string; quantidade: number; quantidadeInicial: number; dataValidade: string | null; dataEntrada: string
  origem: string; observacao: string | null; situacaoValidade: IngredienteItem['situacaoValidade']
}
export interface IngredienteDetalhe extends Omit<IngredienteItem, 'proximaValidade' | 'situacaoValidade' | 'abaixoDoMinimo'> { lotes: Lote[] }
export interface Page<T> { itens: T[]; proximoCursor: string | null }
export interface Resumo {
  ingredientes: number; comEstoque: number; semEstoque: number; abaixoDoMinimo: number; lotesVencidos: number
  lotesVencendo: number; alertaValidadeDias: number
  proximosVencimentos: { id: string; ingredienteId: string; nome: string; quantidade: number; unidade: string; dataValidade: string; situacaoValidade: string }[]
}
export interface Movimentacao {
  id: string; tipo: string; sentido: number; quantidade: number; unidade: string; motivo: string; criadoEm: string
  ingredienteNome: string; ingredienteId: string; usuarioNome: string | null
}

export interface CompatResumo { estado: Estado; percentual: number; obrigatoriosTotal: number; obrigatoriosOk: number }
export interface ReceitaResumo {
  id: string; titulo: string; descricao: string | null; porcoes: number; tempoPreparoMin: number | null
  dificuldade: string | null; status: 'rascunho' | 'completa'; origem: string; imagemArquivoId: string | null
  imagemUrl: string | null; tags: string[]; favorito: boolean; compatibilidade: CompatResumo
}
export interface ReceitaLinha {
  id?: string; ordem?: number; nome: string; textoOriginal?: string | null; ingredienteId: string | null
  quantidade: number | null; unidade: string | null; opcional: boolean; confianca: Confianca
}
export interface ReceitaDetalhe {
  id: string; titulo: string; descricao: string | null; porcoes: number; tempoPreparoMin: number | null
  dificuldade: string | null; status: 'rascunho' | 'completa'; origem: string; fonteCodigo: string
  urlOrigem: string | null; autorAtribuicao: string | null; imagemArquivoId: string | null; imagemUrl: string | null
  observacoes: string | null; favorito: boolean; tags: string[]; ingredientes: ReceitaLinha[]
  etapas: { ordem: number; instrucao: string }[]
}
export interface CompatLinha {
  ordem: number; linhaId: string; nome: string; ingredienteId: string | null; ingredienteNome: string | null
  quantidade: number | null; unidade: string | null; opcional: boolean; confianca: Confianca; status: LineStatus
  coberto: number; faltante: number; usaEquivalente: boolean
}
export interface Compat {
  estado: Estado; percentual: number; obrigatoriosTotal: number; obrigatoriosOk: number; aviso: string
  porcoes: number; linhas: CompatLinha[]
}
export interface Candidato { id: string; nome: string }
export interface RascunhoLinha {
  nome: string; textoOriginal: string | null; quantidade: number | null; unidade: string | null; opcional: boolean
  ingredienteId: string | null; confianca: Confianca; candidatos: Candidato[]
}
export interface Rascunho {
  titulo: string; descricao: string | null; porcoes: number; tempoPreparoMin: number | null; imagemUrl: string | null
  autorAtribuicao: string | null; urlOrigem: string | null; origem: string; status: 'rascunho' | 'completa'
  tags: string[]; etapas: string[]; ingredientes: RascunhoLinha[]; avisos: string[]
}

export interface Catalogo { id: string; nome: string; descricao: string | null; visibilidade: 'pessoal' | 'despensa'; quantidadeReceitas: number; meu: boolean }
export interface CatalogoDetalhe { id: string; nome: string; descricao: string | null; visibilidade: 'pessoal' | 'despensa'; meu: boolean; receitas: { id: string; titulo: string; imagemUrl: string | null; imagemArquivoId: string | null }[] }

export interface PreparoItem {
  ordem: number; texto: string; quantidade: number | null; unidade: string | null; opcional: boolean; status: LineStatus
  naoDescontavel: boolean; motivoNaoDescontavel: string | null; faltante: number
  baixas: { ingredienteId: string; nome: string; unidade: string; quantidade: number; aproximado: boolean; substituto: boolean; lotes: { loteId: string; validade: string | null; quantidade: number }[] }[]
}
export interface PreparoPreview {
  preparoId: string; receitaTitulo: string; porcoes: number; status: string; expiraEm: string; expirado: boolean
  temFaltantes: boolean; temNaoDescontaveis: boolean; itens: PreparoItem[]
}

export interface ListaResumo { id: string; nome: string; status: 'aberta' | 'comprada' | 'arquivada'; origem: string; criadoEm: string; itens: number; comprados: number }
export interface ListaItem {
  id: string; ordem: number; ingredienteId: string | null; ingredienteNome: string | null; texto: string
  quantidadeNecessaria: number; quantidadeDescontadaEstoque: number; quantidadeComprar: number; unidade: string
  comprado: boolean; entradaRegistrada: boolean; ajusteManual: boolean
}
export interface Lista { id: string; nome: string; status: 'aberta' | 'comprada' | 'arquivada'; itens: ListaItem[] }

export interface Membro { usuarioId: string; nome: string; email: string | null; papel: Papel; entrouEm: string }
export interface Convite { id: string; papel: Papel; expiraEm: string; criadoEm: string }
export interface IaConfig { configurada: boolean; ativa: boolean; baseUrl: string; modelo: string; temChave: boolean; limiteHora: number }
