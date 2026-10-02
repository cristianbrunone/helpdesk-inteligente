import {
  Anchor,
  Button,
  Group,
  Pagination,
  Paper,
  Skeleton,
  Stack,
  Text,
  Title,
} from '@mantine/core';
import { Link } from 'react-router';
import { useSessao } from '../api/autenticacao';
import { useChamados, type ChamadoResumo } from '../api/chamados';
import { contarFiltrosAtivos, FILTROS_PADRAO } from '../api/filtrosChamados';
import { AlertaErro } from '../componentes/AlertaErro';
import { BadgePrioridade, BadgeStatus } from '../componentes/BadgesChamado';
import { FiltrosChamados } from '../componentes/FiltrosChamados';
import { formatarDataHora } from '../dominio/chamado';
import { useFiltrosDaUrl } from '../hooks/useFiltrosDaUrl';

const ITENS_DO_ESQUELETO = 5;

export function ListaChamados() {
  const [filtros, alterar] = useFiltrosDaUrl();
  const { data: usuario } = useSessao();
  const ehSolicitante = usuario?.perfil === 'Solicitante';
  const { data, isPending, isError, error, refetch, isFetching, isPlaceholderData } =
    useChamados(filtros);

  return (
    <Stack gap="md" maw={960}>
      <Group justify="space-between" wrap="wrap">
        <Title order={2}>{ehSolicitante ? 'Meus chamados' : 'Chamados'}</Title>
        <Button component={Link} to="/chamados/novo">
          Novo chamado
        </Button>
      </Group>

      <Paper withBorder p="md">
        <FiltrosChamados filtros={filtros} alterar={alterar} />
      </Paper>

      {isPending ? (
        <Stack gap="sm" aria-busy="true" aria-label="Carregando chamados">
          {Array.from({ length: ITENS_DO_ESQUELETO }, (_, i) => (
            <Skeleton key={i} height={72} radius="md" />
          ))}
        </Stack>
      ) : isError ? (
        <AlertaErro
          titulo="Não foi possível carregar os chamados"
          erro={error}
          aoTentarNovamente={() => void refetch()}
          tentando={isFetching}
        />
      ) : data.itens.length === 0 && contarFiltrosAtivos(filtros) === 0 ? (
        <SemChamados ehSolicitante={ehSolicitante} />
      ) : data.itens.length === 0 ? (
        <Stack gap="xs" align="flex-start">
          <Text c="dimmed">Nenhum chamado encontrado com esses filtros.</Text>
          <Button variant="light" size="xs" onClick={() => alterar(FILTROS_PADRAO)}>
            Limpar filtros
          </Button>
        </Stack>
      ) : (
        <Stack gap="sm" aria-busy={isPlaceholderData}>
          <Text size="sm" c="dimmed" aria-live="polite">
            {data.totalItens === 1 ? '1 chamado' : `${data.totalItens} chamados`}
          </Text>
          <Stack component="ul" gap="sm" m={0} p={0} style={{ listStyle: 'none' }}>
            {data.itens.map((chamado) => (
              <ItemChamado key={chamado.id} chamado={chamado} />
            ))}
          </Stack>
          {data.totalPaginas > 1 && (
            <Pagination
              total={data.totalPaginas}
              value={data.pagina}
              onChange={(pagina) => alterar({ pagina })}
              siblings={0}
              getControlProps={(controle) => ({
                'aria-label': controle === 'previous' ? 'Página anterior' : 'Próxima página',
              })}
              getItemProps={(pagina) => ({ 'aria-label': `Página ${pagina}` })}
            />
          )}
        </Stack>
      )}
    </Stack>
  );
}

/**
 * Lista vazia sem nenhum filtro: não há o que limpar, e sim um primeiro chamado a abrir (Sprint 7, item A4 da análise
 * de experiência). O solicitante novo cai aqui no primeiro acesso.
 */
function SemChamados({ ehSolicitante }: { ehSolicitante: boolean }) {
  return (
    <Paper withBorder p="lg">
      <Stack gap="xs" align="flex-start">
        <Text fw={500}>
          {ehSolicitante ? 'Você ainda não abriu nenhum chamado.' : 'Ainda não há chamados.'}
        </Text>
        <Text size="sm" c="dimmed">
          {ehSolicitante
            ? 'Descreva o problema e acompanhe a resposta da equipe de suporte por aqui.'
            : 'Os chamados abertos pelos solicitantes aparecem aqui.'}
        </Text>
        <Button component={Link} to="/chamados/novo" mt="xs">
          Abrir chamado
        </Button>
      </Stack>
    </Paper>
  );
}

function ItemChamado({ chamado }: { chamado: ChamadoResumo }) {
  return (
    <Paper component="li" withBorder p="sm">
      <Stack gap={6}>
        <Anchor component={Link} to={`/chamados/${chamado.id}`} fw={500} lineClamp={2}>
          #{chamado.numero} · {chamado.titulo}
        </Anchor>
        <Group gap={6}>
          <BadgeStatus status={chamado.status} />
          <BadgePrioridade prioridade={chamado.prioridade} />
          <Text size="xs" c="dimmed">
            {chamado.categoria?.nome ?? 'Sem categoria'}
          </Text>
        </Group>
        <Text size="xs" c="dimmed">
          {chamado.solicitanteNome} · aberto em {formatarDataHora(chamado.criadoEm)}
        </Text>
      </Stack>
    </Paper>
  );
}
