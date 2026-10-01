import { z } from 'zod';
import type { NovoChamado } from '../api/chamados';
import { PRIORIDADES, type Prioridade } from '../dominio/chamado';

/**
 * Mesmas regras da API (contrato: POST /api/chamados), para o usuário ver o erro antes de enviar. A API continua
 * validando: os erros 422 dela também são exibidos por campo.
 */
export const esquemaNovoChamado = z.object({
  titulo: z
    .string()
    .trim()
    .min(1, 'Informe o título.')
    .refine((t) => t.length >= 5 && t.length <= 150, 'O título deve ter entre 5 e 150 caracteres.'),
  descricao: z
    .string()
    .trim()
    .min(1, 'Informe a descrição.')
    .refine(
      (d) => d.length >= 10 && d.length <= 5000,
      'A descrição deve ter entre 10 e 5000 caracteres.',
    ),
  solicitanteNome: z
    .string()
    .trim()
    .min(1, 'Informe o nome do solicitante.')
    .max(120, 'O nome do solicitante deve ter no máximo 120 caracteres.'),
  solicitanteEmail: z
    .string()
    .trim()
    .min(1, 'Informe o e-mail.')
    .max(254, 'O e-mail deve ter no máximo 254 caracteres.')
    .pipe(z.email('Informe um e-mail válido.')),
  // Selects: '' = não informado (a triagem por IA sugere depois, P-02).
  categoriaId: z.string(),
  prioridade: z.union([z.literal(''), z.enum(PRIORIDADES)]),
});

export type ValoresNovoChamado = z.input<typeof esquemaNovoChamado>;

export const VALORES_INICIAIS: ValoresNovoChamado = {
  titulo: '',
  descricao: '',
  solicitanteNome: '',
  solicitanteEmail: '',
  categoriaId: '',
  prioridade: '',
};

export function paraNovoChamado(valores: z.output<typeof esquemaNovoChamado>): NovoChamado {
  return {
    titulo: valores.titulo,
    descricao: valores.descricao,
    solicitanteNome: valores.solicitanteNome,
    solicitanteEmail: valores.solicitanteEmail,
    categoriaId: valores.categoriaId ? Number(valores.categoriaId) : null,
    prioridade: valores.prioridade === '' ? null : (valores.prioridade as Prioridade),
  };
}
