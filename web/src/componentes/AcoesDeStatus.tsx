import { Button, Group, Modal, Stack, Text, Textarea } from '@mantine/core';
import { useState } from 'react';
import { useMudarStatus, type ChamadoVersionado } from '../api/chamados';
import { rotuloDaAcao, type StatusChamado } from '../dominio/chamado';
import { notificarErroDeEscrita, telaDesatualizada } from './notificarErroDeEscrita';

const COMENTARIO_TAMANHO_MAXIMO = 4000;

interface Props {
  versionado: ChamadoVersionado;
  aoDesatualizar: () => void;
}

/**
 * Um botão por item de `transicoesPermitidas`, que vem calculado pelo domínio no backend: a tela nunca decide
 * sozinha quais mudanças de status existem.
 */
export function AcoesDeStatus({ versionado, aoDesatualizar }: Props) {
  const { chamado, etag } = versionado;
  const mudanca = useMudarStatus(chamado.id);
  const [destino, setDestino] = useState<StatusChamado | null>(null);
  const [comentario, setComentario] = useState('');

  if (chamado.transicoesPermitidas.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        Chamado finalizado: não aceita mais mudanças de status.
      </Text>
    );
  }

  const fechar = () => {
    setDestino(null);
    setComentario('');
  };

  const confirmar = async () => {
    if (!destino) return;
    try {
      await mudanca.mutateAsync({
        status: destino,
        comentario: comentario.trim() || undefined,
        etag,
      });
      fechar();
    } catch (erro) {
      notificarErroDeEscrita(erro);
      if (telaDesatualizada(erro)) {
        fechar();
        aoDesatualizar();
      }
    }
  };

  const longo = comentario.trim().length > COMENTARIO_TAMANHO_MAXIMO;

  return (
    <>
      <Group gap="xs" role="group" aria-label="Mudar status">
        {chamado.transicoesPermitidas.map((status) => (
          <Button
            key={status}
            size="xs"
            variant={status === 'Cancelado' ? 'light' : 'filled'}
            color={status === 'Cancelado' ? 'red' : undefined}
            onClick={() => setDestino(status)}
          >
            {rotuloDaAcao(chamado.status, status)}
          </Button>
        ))}
      </Group>

      <Modal
        opened={destino !== null}
        onClose={fechar}
        title={destino ? `${rotuloDaAcao(chamado.status, destino)} · #${chamado.numero}` : ''}
      >
        <Stack gap="sm">
          <Textarea
            label="Comentário (opcional)"
            description={
              destino === 'Resolvido'
                ? 'Descreva a solução: ela ajuda a resolver casos parecidos no futuro.'
                : undefined
            }
            autosize
            minRows={3}
            value={comentario}
            onChange={(evento) => setComentario(evento.currentTarget.value)}
            error={
              longo
                ? `O comentário deve ter no máximo ${COMENTARIO_TAMANHO_MAXIMO} caracteres.`
                : undefined
            }
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={fechar}>
              Voltar
            </Button>
            <Button loading={mudanca.isPending} disabled={longo} onClick={() => void confirmar()}>
              Confirmar
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
