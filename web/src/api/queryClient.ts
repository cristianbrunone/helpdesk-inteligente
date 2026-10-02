import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { chaveSessao } from './autenticacao';
import { ErroApi } from './cliente';

const MAXIMO_DE_TENTATIVAS = 2;

/**
 * Cache dos dados da API. Um 401 em qualquer consulta ou escrita quer dizer que a sessão acabou (expirou ou o
 * cookie foi apagado): a sessão vira `null`, e a guarda de rotas leva ao login (ADR-0026).
 */
export function criarQueryClient(): QueryClient {
  const aoFalhar = (erro: unknown) => {
    if (erro instanceof ErroApi && erro.status === 401) {
      cliente.setQueryData(chaveSessao, null);
    }
  };

  const cliente: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: aoFalhar }),
    mutationCache: new MutationCache({ onError: aoFalhar }),
    defaultOptions: {
      queries: {
        // Erro 4xx é definitivo (repetir não muda a resposta); falha de rede e 5xx podem ser transitórias.
        retry: (falhas, erro) =>
          !(erro instanceof ErroApi && erro.status < 500) && falhas < MAXIMO_DE_TENTATIVAS,
        refetchOnWindowFocus: false,
      },
    },
  });
  return cliente;
}
