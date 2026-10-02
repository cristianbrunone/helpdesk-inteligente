import {
  Alert,
  Anchor,
  Badge,
  Button,
  CopyButton,
  Group,
  Loader,
  Modal,
  Progress,
  Stack,
  Text,
  Textarea,
  Title,
} from '@mantine/core';
import { useState } from 'react';
import { Link } from 'react-router';
import type { ChamadoVersionado } from '../api/chamados';
import {
  useConfiguracaoIA,
  useDecidirTriagem,
  useRefazerTriagem,
  type FonteTriagem,
  type TriagemDetalhe,
} from '../api/triagem';
import { formatarDataHora } from '../dominio/chamado';
import { BadgePrioridade } from './BadgesChamado';
import { notificarErroDeEscrita, telaDesatualizada } from './notificarErroDeEscrita';

const MOTIVO_TAMANHO_MAXIMO = 500;

interface Props {
  versionado: ChamadoVersionado;
  aoDesatualizar: () => void;
}

/**
 * Painel da triagem por IA (Sprint 2). A sugestão é sempre identificada como gerada por IA, e a decisão final é
 * do atendente. Os botões aparecem conforme o status da triagem e o que o domínio permite no chamado. Desde a
 * Sprint 3, mostra também em que documentos a sugestão se apoiou (RF-16).
 */
export function PainelTriagem({ versionado, aoDesatualizar }: Props) {
  const { chamado, etag } = versionado;
  const { data: configuracao } = useConfiguracaoIA();
  const refazer = useRefazerTriagem(chamado.id);
  const decidir = useDecidirTriagem(chamado.id);
  const [rejeitando, setRejeitando] = useState(false);
  const [motivo, setMotivo] = useState('');

  const triagem = chamado.triagem;
  const iaAtiva = configuracao?.triagem ?? true;
  // Chamado finalizado não aceita refazer (P-11); podeComentar já vem calculado pelo domínio.
  const podeRefazer = iaAtiva && chamado.podeComentar && triagem?.status !== 'Pendente';

  const executar = async (acao: () => Promise<unknown>) => {
    try {
      await acao();
      return true;
    } catch (erro) {
      notificarErroDeEscrita(erro);
      if (telaDesatualizada(erro)) aoDesatualizar();
      return false;
    }
  };

  const aceitar = () => void executar(() => decidir.mutateAsync({ acao: 'aceitar', etag }));

  const confirmarRejeicao = async () => {
    const ok = await executar(() =>
      decidir.mutateAsync({
        acao: 'rejeitar',
        motivo: motivo.trim() || undefined,
        etag,
      }),
    );
    if (ok) {
      setRejeitando(false);
      setMotivo('');
    }
  };

  return (
    <Stack gap="sm" component="section" aria-labelledby="titulo-triagem">
      <Group justify="space-between" gap="xs">
        <Title order={3} size="h5" id="titulo-triagem">
          Triagem por IA
        </Title>
        {triagem && triagem.status !== 'Pendente' && triagem.status !== 'Falhou' && (
          <Badge color="violet" variant="light">
            Gerado por IA
          </Badge>
        )}
      </Group>

      {!triagem ? (
        <Text size="sm" c="dimmed">
          {iaAtiva
            ? 'Este chamado ainda não tem triagem.'
            : 'Triagem por IA desativada no momento.'}
        </Text>
      ) : triagem.status === 'Pendente' ? (
        <Group gap="xs" aria-live="polite">
          <Loader size="xs" />
          <Text size="sm">A IA está analisando o chamado…</Text>
        </Group>
      ) : triagem.status === 'Falhou' ? (
        <Alert color="orange" variant="light" title="A triagem não foi concluída">
          <Text size="sm">{triagem.erro}</Text>
        </Alert>
      ) : (
        <Sugestao triagem={triagem} />
      )}

      {triagem?.status === 'Concluida' && (
        <Group gap="xs">
          <Button size="xs" loading={decidir.isPending} onClick={aceitar}>
            Aceitar sugestão
          </Button>
          <Button size="xs" variant="light" color="red" onClick={() => setRejeitando(true)}>
            Rejeitar
          </Button>
        </Group>
      )}

      {podeRefazer && (
        <Group>
          <Button
            size="xs"
            variant="subtle"
            loading={refazer.isPending}
            onClick={() => void executar(() => refazer.mutateAsync())}
          >
            {triagem ? 'Refazer triagem' : 'Solicitar triagem'}
          </Button>
        </Group>
      )}

      <Modal
        opened={rejeitando}
        onClose={() => setRejeitando(false)}
        title="Rejeitar sugestão da IA"
      >
        <Stack gap="sm">
          <Textarea
            label="Motivo (opcional)"
            description="Ajuda a melhorar o prompt da triagem."
            autosize
            minRows={2}
            value={motivo}
            onChange={(evento) => setMotivo(evento.currentTarget.value)}
            error={
              motivo.trim().length > MOTIVO_TAMANHO_MAXIMO
                ? `O motivo deve ter no máximo ${MOTIVO_TAMANHO_MAXIMO} caracteres.`
                : undefined
            }
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setRejeitando(false)}>
              Voltar
            </Button>
            <Button
              color="red"
              loading={decidir.isPending}
              disabled={motivo.trim().length > MOTIVO_TAMANHO_MAXIMO}
              onClick={() => void confirmarRejeicao()}
            >
              Confirmar rejeição
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}

function Sugestao({ triagem }: { triagem: TriagemDetalhe }) {
  const confianca = Math.round((triagem.confianca ?? 0) * 100);

  return (
    <Stack gap="xs">
      <Group gap={6}>
        <Text size="sm" fw={500}>
          {triagem.categoriaSugerida?.nome ?? 'Sem categoria'}
        </Text>
        {triagem.prioridadeSugerida && <BadgePrioridade prioridade={triagem.prioridadeSugerida} />}
      </Group>
      <Text size="sm">{triagem.resumo}</Text>

      <div>
        <Text size="xs" c="dimmed">
          Confiança da IA: {confianca}%
        </Text>
        <Progress value={confianca} size="sm" aria-label={`Confiança da IA: ${confianca}%`} />
      </div>

      <div>
        <Group justify="space-between" gap="xs">
          <Text size="xs" c="dimmed">
            Resposta sugerida ao solicitante
          </Text>
          <CopyButton value={triagem.respostaSugerida ?? ''}>
            {({ copied, copy }) => (
              <Button size="compact-xs" variant="subtle" onClick={copy}>
                {copied ? 'Copiada' : 'Copiar'}
              </Button>
            )}
          </CopyButton>
        </Group>
        <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
          {triagem.respostaSugerida}
        </Text>
      </div>

      {triagem.fontes.length > 0 && <Fontes fontes={triagem.fontes} />}

      {triagem.decididaPor && triagem.decididaEm && (
        <Text size="xs" c={triagem.status === 'Aceita' ? 'teal.9' : 'red.9'}>
          {triagem.status === 'Aceita' ? 'Aceita' : 'Rejeitada'} por {triagem.decididaPor} em{' '}
          {formatarDataHora(triagem.decididaEm)}
        </Text>
      )}

      <Text size="xs" c="dimmed">
        {triagem.modelo} · prompt {triagem.promptVersao}
        {triagem.totalTriagens > 1 ? ` · ${triagem.totalTriagens}ª triagem` : ''}
      </Text>
    </Stack>
  );
}

/**
 * Fontes do RAG (RF-16): o atendente vê em que chamados resolvidos e artigos a IA se apoiou. O chamado abre no
 * próprio detalhe; o artigo ainda não tem página (só o título). Sem fontes, a seção não aparece.
 */
function Fontes({ fontes }: { fontes: FonteTriagem[] }) {
  return (
    <div>
      <Text size="xs" c="dimmed" id="titulo-fontes">
        Baseado em
      </Text>
      <Stack
        gap={4}
        component="ul"
        aria-labelledby="titulo-fontes"
        m={0}
        p={0}
        style={{ listStyle: 'none' }}
      >
        {fontes.map((fonte) => (
          <Group
            key={`${fonte.tipo}-${fonte.id}`}
            component="li"
            gap={6}
            wrap="nowrap"
            align="flex-start"
          >
            <Badge size="xs" variant="outline" color={fonte.tipo === 'chamado' ? 'blue' : 'grape'}>
              {fonte.tipo === 'chamado' ? 'Chamado' : 'Artigo'}
            </Badge>
            <Text size="sm" style={{ overflowWrap: 'anywhere' }} flex={1}>
              {fonte.tipo === 'chamado' ? (
                <Anchor component={Link} to={`/chamados/${fonte.id}`} size="sm">
                  {fonte.numero !== null ? `#${fonte.numero} ` : ''}
                  {fonte.titulo}
                </Anchor>
              ) : (
                fonte.titulo
              )}
            </Text>
            <Text size="xs" c="dimmed" title="Similaridade com este chamado">
              {Math.round(fonte.similaridade * 100)}%
            </Text>
          </Group>
        ))}
      </Stack>
    </div>
  );
}
