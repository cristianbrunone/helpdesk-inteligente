import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { deQueryString, paraQueryString, type FiltrosChamados } from '../api/filtrosChamados';

export type AlterarFiltros = (
  mudanca: Partial<FiltrosChamados>,
  opcoes?: { substituirHistorico?: boolean },
) => void;

/**
 * A URL é a fonte da verdade dos filtros: recarregar, voltar e compartilhar o link mantêm a consulta.
 * Qualquer mudança de filtro volta para a página 1, a não ser que a própria mudança seja de página.
 */
export function useFiltrosDaUrl(): [FiltrosChamados, AlterarFiltros] {
  const [params, setParams] = useSearchParams();
  const filtros = useMemo(() => deQueryString(params), [params]);

  const alterar = useCallback<AlterarFiltros>(
    (mudanca, opcoes) =>
      setParams(paraQueryString({ ...filtros, pagina: 1, ...mudanca }), {
        replace: opcoes?.substituirHistorico ?? false,
      }),
    [filtros, setParams],
  );

  return [filtros, alterar];
}
