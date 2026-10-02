import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ErroApi, requisitar } from './cliente';

export type Perfil = 'Atendente' | 'Solicitante';

/** Contrato: GET /api/auth/eu e POST /api/auth/login (ADR-0026). O token nunca chega aqui: fica num cookie httpOnly. */
export interface UsuarioSessao {
  id: string;
  nome: string;
  email: string;
  perfil: Perfil;
}

export const chaveSessao = ['sessao'] as const;

/**
 * Quem está na sessão, ou `null` sem sessão. O 401 aqui não é erro: é a resposta normal de quem ainda não entrou.
 * O cookie vai sozinho em toda requisição (mesma origem), então o front só pergunta à API.
 */
export function useSessao() {
  return useQuery({
    queryKey: chaveSessao,
    queryFn: async ({ signal }) => {
      try {
        return await requisitar<UsuarioSessao>('/api/auth/eu', { signal });
      } catch (erro) {
        if (erro instanceof ErroApi && erro.status === 401) {
          return null;
        }
        throw erro;
      }
    },
    staleTime: Infinity,
    retry: false,
  });
}

export interface Credenciais {
  email: string;
  senha: string;
}

export function useEntrar() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (credenciais: Credenciais) =>
      requisitar<UsuarioSessao>('/api/auth/login', { method: 'POST', corpo: credenciais }),
    onSuccess: (usuario) => queryClient.setQueryData(chaveSessao, usuario),
  });
}

/** Sai e descarta tudo o que estava em cache: o próximo usuário não vê dados do anterior. */
export function useSair() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => requisitar<undefined>('/api/auth/sair', { method: 'POST' }),
    onSettled: () => {
      queryClient.removeQueries({
        predicate: (consulta) => consulta.queryKey[0] !== chaveSessao[0],
      });
      queryClient.setQueryData(chaveSessao, null);
    },
  });
}
