import { Navigate, Outlet, useLocation } from 'react-router';
import { useSessao } from '../api/autenticacao';
import { AlertaErro } from './AlertaErro';
import { CarregandoPagina } from './CarregandoPagina';

/**
 * Guarda das páginas da aplicação (ADR-0026): sem sessão, leva ao login guardando a página pedida em `?voltar=`,
 * para voltar a ela depois de entrar. Quem decide o que cada perfil pode fazer é a API; aqui só se exige a sessão.
 */
export function RotaProtegida() {
  const sessao = useSessao();
  const { pathname, search } = useLocation();

  if (sessao.isPending) {
    return <CarregandoPagina />;
  }

  if (sessao.isError) {
    return (
      <AlertaErro
        titulo="Não foi possível verificar a sua sessão"
        erro={sessao.error}
        aoTentarNovamente={() => void sessao.refetch()}
        tentando={sessao.isFetching}
      />
    );
  }

  if (!sessao.data) {
    return <Navigate to={`/entrar?voltar=${encodeURIComponent(pathname + search)}`} replace />;
  }

  return <Outlet />;
}
