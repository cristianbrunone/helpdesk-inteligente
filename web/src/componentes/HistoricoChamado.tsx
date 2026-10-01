import { Text, Timeline, Title } from '@mantine/core';
import type { RegistroHistorico } from '../api/chamados';
import { formatarDataHora, ROTULO_STATUS } from '../dominio/chamado';

/** Linha do tempo das mudanças de status, desde a abertura (P-10). */
export function HistoricoChamado({ historico }: { historico: RegistroHistorico[] }) {
  return (
    <section aria-labelledby="titulo-historico">
      <Title order={3} size="h5" id="titulo-historico" mb="sm">
        Histórico
      </Title>
      <Timeline active={historico.length - 1} bulletSize={14} lineWidth={2}>
        {historico.map((registro, indice) => (
          <Timeline.Item
            key={`${registro.alteradoEm}-${indice}`}
            title={
              registro.statusAnterior
                ? `${ROTULO_STATUS[registro.statusAnterior]} → ${ROTULO_STATUS[registro.statusNovo]}`
                : `Aberto`
            }
          >
            <Text size="xs" c="dimmed">
              {registro.alteradoPor} · {formatarDataHora(registro.alteradoEm)}
            </Text>
          </Timeline.Item>
        ))}
      </Timeline>
    </section>
  );
}
