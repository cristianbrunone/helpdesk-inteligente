import { BarChart } from '@mantine/charts';
import {
  Paper,
  SimpleGrid,
  Skeleton,
  Stack,
  Table,
  Text,
  Title,
  VisuallyHidden,
} from '@mantine/core';
import type { ReactNode } from 'react';
import { useResumoDashboard, type ResumoDashboard } from '../api/dashboard';
import { AlertaErro } from '../componentes/AlertaErro';
import { ROTULO_PRIORIDADE, ROTULO_STATUS } from '../dominio/chamado';

const NUMERO = new Intl.NumberFormat('pt-BR');
const HORAS = new Intl.NumberFormat('pt-BR', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});
const PERCENTUAL = new Intl.NumberFormat('pt-BR', { style: 'percent', maximumFractionDigits: 1 });

const ALTURA_GRAFICO = 220;

/**
 * Dashboard (RF-40 a RF-43). Os números vêm prontos da API, agregados no banco. Cada gráfico é acompanhado de
 * uma tabela com os mesmos dados, oculta na tela e lida pelos leitores de tela (o SVG do gráfico não é acessível).
 */
export function Dashboard() {
  const { data, isPending, isError, error, refetch, isFetching } = useResumoDashboard();

  return (
    <Stack gap="md" maw={1100}>
      <Title order={2}>Dashboard</Title>

      {isPending ? (
        <Stack gap="md" aria-busy="true" aria-label="Carregando o dashboard">
          <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }}>
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} height={88} radius="md" />
            ))}
          </SimpleGrid>
          <Skeleton height={ALTURA_GRAFICO} radius="md" />
        </Stack>
      ) : isError ? (
        <AlertaErro
          titulo="Não foi possível carregar o dashboard"
          erro={error}
          aoTentarNovamente={() => void refetch()}
          tentando={isFetching}
        />
      ) : data.totalChamados === 0 ? (
        <Text c="dimmed">
          Ainda não há chamados. Os números aparecem aqui assim que o primeiro for aberto.
        </Text>
      ) : (
        <Conteudo resumo={data} />
      )}
    </Stack>
  );
}

function Conteudo({ resumo }: { resumo: ResumoDashboard }) {
  const { ia } = resumo;
  const emAberto = resumo.porStatus
    .filter((s) => s.status === 'Aberto' || s.status === 'EmAndamento')
    .reduce((soma, s) => soma + s.total, 0);

  const porStatus = resumo.porStatus.map((s) => ({
    rotulo: ROTULO_STATUS[s.status],
    total: s.total,
  }));
  const porPrioridade = resumo.porPrioridade.map((p) => ({
    rotulo: ROTULO_PRIORIDADE[p.prioridade],
    total: p.total,
  }));
  const tempos = resumo.tempoMedioResolucaoPorCategoria.map((t) => ({
    rotulo: t.categoria,
    horas: t.tempoMedioHoras ?? 0,
  }));
  const decisoes = ia.porCategoria.map((c) => ({
    rotulo: c.categoria,
    Aceitas: c.aceitas,
    Rejeitadas: c.rejeitadas,
  }));

  return (
    <Stack gap="md">
      <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }}>
        <Cartao titulo="Total de chamados" valor={NUMERO.format(resumo.totalChamados)} />
        <Cartao
          titulo="Em aberto"
          valor={NUMERO.format(emAberto)}
          detalhe="Aberto ou em andamento"
        />
        <Cartao
          titulo="Aceitação da IA"
          valor={ia.taxaAceitacao === null ? '—' : PERCENTUAL.format(ia.taxaAceitacao)}
          detalhe={
            ia.taxaAceitacao === null
              ? 'Nenhuma sugestão decidida ainda'
              : `${NUMERO.format(ia.aceitas)} aceitas, ${NUMERO.format(ia.rejeitadas)} rejeitadas`
          }
        />
        <Cartao
          titulo="Triagens"
          valor={`${NUMERO.format(ia.pendentes)} na fila`}
          detalhe={`${NUMERO.format(ia.falhas)} com falha`}
        />
      </SimpleGrid>

      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Secao titulo="Chamados por status">
          <BarChart
            h={ALTURA_GRAFICO}
            data={porStatus}
            dataKey="rotulo"
            series={[{ name: 'total', label: 'Chamados', color: 'blue.6' }]}
          />
          <TabelaOculta
            legenda="Chamados por status"
            colunas={['Status', 'Chamados']}
            linhas={porStatus.map((s) => [s.rotulo, NUMERO.format(s.total)])}
          />
        </Secao>

        <Secao titulo="Chamados por prioridade">
          <BarChart
            h={ALTURA_GRAFICO}
            data={porPrioridade}
            dataKey="rotulo"
            series={[{ name: 'total', label: 'Chamados', color: 'orange.6' }]}
          />
          <TabelaOculta
            legenda="Chamados por prioridade"
            colunas={['Prioridade', 'Chamados']}
            linhas={porPrioridade.map((p) => [p.rotulo, NUMERO.format(p.total)])}
          />
        </Secao>

        <Secao
          titulo="Tempo médio de resolução por categoria"
          detalhe="Em horas, só Resolvido e Fechado"
        >
          <BarChart
            h={ALTURA_GRAFICO}
            data={tempos}
            dataKey="rotulo"
            series={[{ name: 'horas', label: 'Horas', color: 'teal.6' }]}
          />
          <TabelaOculta
            legenda="Tempo médio de resolução por categoria"
            colunas={['Categoria', 'Resolvidos', 'Tempo médio (horas)']}
            linhas={resumo.tempoMedioResolucaoPorCategoria.map((t) => [
              t.categoria,
              NUMERO.format(t.resolvidos),
              t.tempoMedioHoras === null ? 'Sem resolvidos' : HORAS.format(t.tempoMedioHoras),
            ])}
          />
        </Secao>

        <Secao
          titulo="Sugestões da IA por categoria"
          detalhe="Aceitas × rejeitadas pelos atendentes"
        >
          {decisoes.length === 0 ? (
            <Text size="sm" c="dimmed">
              Nenhuma sugestão decidida ainda.
            </Text>
          ) : (
            <>
              <BarChart
                h={ALTURA_GRAFICO}
                data={decisoes}
                dataKey="rotulo"
                type="stacked"
                withLegend
                series={[
                  { name: 'Aceitas', color: 'teal.6' },
                  { name: 'Rejeitadas', color: 'red.6' },
                ]}
              />
              <TabelaOculta
                legenda="Sugestões da IA por categoria"
                colunas={['Categoria', 'Aceitas', 'Rejeitadas', 'Taxa de aceitação']}
                linhas={ia.porCategoria.map((c) => [
                  c.categoria,
                  NUMERO.format(c.aceitas),
                  NUMERO.format(c.rejeitadas),
                  c.taxaAceitacao === null ? '—' : PERCENTUAL.format(c.taxaAceitacao),
                ])}
              />
            </>
          )}
        </Secao>
      </SimpleGrid>

      <Secao titulo="Consumo de IA nos últimos 30 dias" detalhe="Cada tentativa ao provedor conta">
        {ia.consumo30d.length === 0 ? (
          <Text size="sm" c="dimmed">
            Nenhuma chamada ao provedor de IA nos últimos 30 dias.
          </Text>
        ) : (
          <Table.ScrollContainer minWidth={520}>
            <Table striped>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Operação</Table.Th>
                  <Table.Th>Modelo</Table.Th>
                  <Table.Th ta="right">Chamadas</Table.Th>
                  <Table.Th ta="right">Falhas</Table.Th>
                  <Table.Th ta="right">Tokens (entrada / saída)</Table.Th>
                  <Table.Th ta="right">Latência p95</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {ia.consumo30d.map((c) => (
                  <Table.Tr key={`${c.operacao}-${c.modelo}`}>
                    <Table.Td>{c.operacao}</Table.Td>
                    <Table.Td>{c.modelo}</Table.Td>
                    <Table.Td ta="right">{NUMERO.format(c.chamadas)}</Table.Td>
                    <Table.Td ta="right">{NUMERO.format(c.falhas)}</Table.Td>
                    <Table.Td ta="right">
                      {c.tokensEntrada === null ? '—' : NUMERO.format(c.tokensEntrada)} /{' '}
                      {c.tokensSaida === null ? '—' : NUMERO.format(c.tokensSaida)}
                    </Table.Td>
                    <Table.Td ta="right">
                      {c.latenciaP95Ms === null
                        ? '—'
                        : `${NUMERO.format(Math.round(c.latenciaP95Ms))} ms`}
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Secao>
    </Stack>
  );
}

function Cartao({ titulo, valor, detalhe }: { titulo: string; valor: string; detalhe?: string }) {
  return (
    <Paper withBorder p="md" component="section" aria-label={titulo}>
      <Text size="xs" c="dimmed">
        {titulo}
      </Text>
      <Text size="xl" fw={700}>
        {valor}
      </Text>
      {detalhe && (
        <Text size="xs" c="dimmed">
          {detalhe}
        </Text>
      )}
    </Paper>
  );
}

function Secao({
  titulo,
  detalhe,
  children,
}: {
  titulo: string;
  detalhe?: string;
  children: ReactNode;
}) {
  return (
    <Paper withBorder p="md" component="section" aria-label={titulo}>
      <Title order={3} size="h5">
        {titulo}
      </Title>
      {detalhe && (
        <Text size="xs" c="dimmed" mb="xs">
          {detalhe}
        </Text>
      )}
      {children}
    </Paper>
  );
}

function TabelaOculta({
  legenda,
  colunas,
  linhas,
}: {
  legenda: string;
  colunas: string[];
  linhas: string[][];
}) {
  return (
    <VisuallyHidden>
      <table>
        <caption>{legenda}</caption>
        <thead>
          <tr>
            {colunas.map((coluna) => (
              <th key={coluna} scope="col">
                {coluna}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {linhas.map((linha) => (
            <tr key={linha[0]}>
              {linha.map((celula, i) =>
                i === 0 ? (
                  <th key={i} scope="row">
                    {celula}
                  </th>
                ) : (
                  <td key={i}>{celula}</td>
                ),
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </VisuallyHidden>
  );
}
