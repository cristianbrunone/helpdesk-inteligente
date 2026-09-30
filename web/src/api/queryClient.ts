import { QueryClient } from '@tanstack/react-query';
import { ErroApi } from './cliente';

const MAXIMO_DE_TENTATIVAS = 2;

export function criarQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Erro 4xx é definitivo (repetir não muda a resposta); falha de rede e 5xx podem ser transitórias.
        retry: (falhas, erro) =>
          !(erro instanceof ErroApi && erro.status < 500) && falhas < MAXIMO_DE_TENTATIVAS,
        refetchOnWindowFocus: false,
      },
    },
  });
}
