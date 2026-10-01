/** Corpo de erro da API: ProblemDetails (contrato §2). */
export interface Problema {
  title?: string;
  detail?: string;
  codigo?: string;
  correlationId?: string;
}

/** Erro devolvido pela API. Mantém o código e o correlationId para a UI e para o suporte. */
export class ErroApi extends Error {
  readonly status: number;
  readonly codigo?: string;
  readonly correlationId?: string;

  constructor(status: number, problema?: Problema) {
    super(problema?.detail ?? problema?.title ?? `A API respondeu com o status ${status}.`);
    this.name = 'ErroApi';
    this.status = status;
    this.codigo = problema?.codigo;
    this.correlationId = problema?.correlationId;
  }
}

/**
 * Único ponto de acesso HTTP do frontend. A URL é relativa à própria origem: no Compose o Nginx
 * encaminha /api para a API, e em desenvolvimento o proxy do Vite faz o mesmo (sem CORS).
 */
export async function requisitar<T>(caminho: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');

  const resposta = await fetch(new URL(caminho, window.location.origin), { ...init, headers });
  if (!resposta.ok) {
    throw new ErroApi(resposta.status, await lerProblema(resposta));
  }
  return (await resposta.json()) as T;
}

/** Mensagem para exibir ao usuário, em pt-BR, para qualquer erro de requisição. */
export function mensagemDeErro(erro: unknown): string {
  return erro instanceof ErroApi
    ? erro.message
    : 'Não foi possível conectar ao servidor. Verifique sua conexão e tente novamente.';
}

async function lerProblema(resposta: Response): Promise<Problema | undefined> {
  if (!resposta.headers.get('content-type')?.includes('json')) {
    return undefined;
  }
  try {
    return (await resposta.json()) as Problema;
  } catch {
    return undefined;
  }
}
