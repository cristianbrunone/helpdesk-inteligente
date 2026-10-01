import { Anchor, Grid, Group, Paper, Skeleton, Stack, Text, TextInput, Title } from '@mantine/core';
import { useLocalStorage } from '@mantine/hooks';
import { Link, useParams } from 'react-router';
import { useChamado, type ChamadoDetalhe } from '../api/chamados';
import { ErroApi } from '../api/cliente';
import { AcoesDeStatus } from '../componentes/AcoesDeStatus';
import { AlertaErro } from '../componentes/AlertaErro';
import { BadgePrioridade, BadgeStatus } from '../componentes/BadgesChamado';
import { ComentariosChamado } from '../componentes/ComentariosChamado';
import { HistoricoChamado } from '../componentes/HistoricoChamado';
import { PainelTriagem } from '../componentes/PainelTriagem';
import { formatarDataHora } from '../dominio/chamado';

export function DetalheChamado() {
  const { id = '' } = useParams();
  const { data, isPending, isError, error, refetch, isFetching } = useChamado(id);
  // Sem login na v1 (P-03): o nome do atendente vai em alteradoPor/autor e fica lembrado neste navegador.
  const [atendente, setAtendente] = useLocalStorage({
    key: 'helpdesk.atendente',
    defaultValue: '',
  });
  const recarregar = () => void refetch();

  return (
    <Stack gap="md" maw={1100}>
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

          <Grid gap="md">
            <Grid.Col span={{ base: 12, md: 8 }}>
              <Stack gap="md">
                <Paper withBorder p="md">
                  <Title order={3} size="h5" mb="xs">
                    Descrição
                  </Title>
                  <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                    {data.chamado.descricao}
                  </Text>
                </Paper>
                <Paper withBorder p="md">
                  <ComentariosChamado
                    versionado={data}
                    atendente={atendente}
                    aoDesatualizar={recarregar}
                  />
                </Paper>
              </Stack>
            </Grid.Col>

            <Grid.Col span={{ base: 12, md: 4 }}>
              <Stack gap="md">
                <Paper withBorder p="md">
                  <Stack gap="sm">
                    <Title order={3} size="h5">
                      Ações
                    </Title>
                    <TextInput
                      label="Seu nome (atendente)"
                      placeholder="Ex.: Ana (suporte)"
                      value={atendente}
                      onChange={(evento) => setAtendente(evento.currentTarget.value)}
                      maxLength={120}
                    />
                    <AcoesDeStatus
                      versionado={data}
                      atendente={atendente}
                      aoDesatualizar={recarregar}
                    />
                  </Stack>
                </Paper>
                <Paper withBorder p="md">
                  <PainelTriagem
                    versionado={data}
                    atendente={atendente}
                    aoDesatualizar={recarregar}
                  />
                </Paper>
                <Paper withBorder p="md">
                  <DadosDoChamado chamado={data.chamado} />
                </Paper>
                <Paper withBorder p="md">
                  <HistoricoChamado historico={data.chamado.historico} />
                </Paper>
              </Stack>
            </Grid.Col>
          </Grid>
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
