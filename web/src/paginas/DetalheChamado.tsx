import { Anchor, Group, Paper, Skeleton, Stack, Text, Title } from '@mantine/core';
import { Link, useParams } from 'react-router';
import { useSessao } from '../api/autenticacao';
import { useChamado, type ChamadoDetalhe } from '../api/chamados';
import { ErroApi } from '../api/cliente';
import { AcoesDeStatus } from '../componentes/AcoesDeStatus';
import { AlertaErro } from '../componentes/AlertaErro';
import { BadgePrioridade, BadgeStatus } from '../componentes/BadgesChamado';
import { ComentariosChamado } from '../componentes/ComentariosChamado';
import { HistoricoChamado } from '../componentes/HistoricoChamado';
import { PainelCopiloto } from '../componentes/PainelCopiloto';
import { PainelTriagem } from '../componentes/PainelTriagem';
import { formatarDataHora } from '../dominio/chamado';
import { useTituloDaPagina } from '../hooks/useTituloDaPagina';
import classes from './DetalheChamado.module.css';
import { LARGURA_CONTEUDO } from '../tema';

export function DetalheChamado() {
  const { id = '' } = useParams();
  const { data, isPending, isError, error, refetch, isFetching } = useChamado(id);
  const { data: usuario } = useSessao();
  const ehAtendente = usuario?.perfil === 'Atendente';
  const recarregar = () => void refetch();
  useTituloDaPagina(data ? `#${data.chamado.numero} · ${data.chamado.titulo}` : 'Chamado');

  return (
    <Stack gap="md" maw={LARGURA_CONTEUDO}>
      <Anchor component={Link} to="/chamados" size="sm">
        ← Voltar para os chamados
      </Anchor>

      {isPending ? (
        <Stack gap="sm" aria-busy="true" aria-label="Carregando chamado">
          <Skeleton height={32} width="60%" />
          <Skeleton height={160} />
          <Skeleton height={120} />
        </Stack>
      ) : isError ? (
        error instanceof ErroApi && error.status === 404 ? (
          <Stack gap="xs">
            <Title order={2}>Chamado não encontrado</Title>
            <Text c="dimmed">Ele pode ter sido removido ou o endereço está incorreto.</Text>
          </Stack>
        ) : (
          <AlertaErro
            titulo="Não foi possível carregar o chamado"
            erro={error}
            aoTentarNovamente={recarregar}
            tentando={isFetching}
          />
        )
      ) : (
        <>
          <Stack gap={6}>
            <Title order={2} style={{ overflowWrap: 'anywhere' }}>
              #{data.chamado.numero} · {data.chamado.titulo}
            </Title>
            <Group gap={6}>
              <BadgeStatus status={data.chamado.status} />
              <BadgePrioridade prioridade={data.chamado.prioridade} />
              <Text size="sm" c="dimmed">
                {data.chamado.categoria?.nome ?? 'Sem categoria'}
              </Text>
            </Group>
          </Stack>

          {/* A ordem no HTML é a do celular; o CSS reposiciona as áreas a partir do `md`. */}
          <div className={classes.layout} data-com-acoes={ehAtendente || undefined}>
            {ehAtendente && (
              <Stack gap="md" className={classes.acoes}>
                <Paper withBorder p="md">
                  <Stack gap="sm">
                    <Title order={3} size="h5">
                      Ações
                    </Title>
                    <AcoesDeStatus versionado={data} aoDesatualizar={recarregar} />
                  </Stack>
                </Paper>
                <Paper withBorder p="md">
                  <PainelTriagem versionado={data} aoDesatualizar={recarregar} />
                </Paper>
              </Stack>
            )}

            <Stack gap="md" className={classes.principal}>
              <Paper withBorder p="md">
                <Title order={3} size="h5" mb="xs">
                  Descrição
                </Title>
                <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                  {data.chamado.descricao}
                </Text>
              </Paper>
              {ehAtendente && (
                <Paper withBorder p="md">
                  <PainelCopiloto chamadoId={data.chamado.id} />
                </Paper>
              )}
              <Paper withBorder p="md">
                <ComentariosChamado versionado={data} aoDesatualizar={recarregar} />
              </Paper>
            </Stack>

            <Stack gap="md" className={classes.lateral}>
              <Paper withBorder p="md">
                <DadosDoChamado chamado={data.chamado} />
              </Paper>
              <Paper withBorder p="md">
                <HistoricoChamado historico={data.chamado.historico} />
              </Paper>
            </Stack>
          </div>
        </>
      )}
    </Stack>
  );
}

function DadosDoChamado({ chamado }: { chamado: ChamadoDetalhe }) {
  const linhas: [string, string][] = [
    ['Solicitante', chamado.solicitanteNome],
    ['E-mail', chamado.solicitanteEmail],
    ['Aberto em', formatarDataHora(chamado.criadoEm)],
    ['Atualizado em', formatarDataHora(chamado.atualizadoEm)],
    ...(chamado.resolvidoEm
      ? ([['Resolvido em', formatarDataHora(chamado.resolvidoEm)]] as [string, string][])
      : []),
  ];

  return (
    <Stack gap={6} component="dl" m={0}>
      {linhas.map(([rotulo, valor]) => (
        <div key={rotulo}>
          <Text component="dt" size="xs" c="dimmed">
            {rotulo}
          </Text>
          <Text component="dd" size="sm" m={0} style={{ overflowWrap: 'anywhere' }}>
            {valor}
          </Text>
        </div>
      ))}
    </Stack>
  );
}
