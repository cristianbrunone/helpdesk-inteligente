import {
  Alert,
  Anchor,
  Badge,
  Button,
  Group,
  Loader,
  Paper,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import {
  conversarComCopiloto,
  type EventoCopiloto,
  type FonteCopiloto,
  type MensagemCopiloto,
} from '../api/copiloto';
import { useConfiguracaoIA } from '../api/triagem';
import { ErroApi } from '../api/cliente';

interface FerramentaAtiva {
  nome: string;
  fase: 'iniciada' | 'concluida';
  descricao?: string;
  resultados?: number;
}

interface AvisoMensagem {
  tipo: 'referencia_nao_verificada' | 'resposta_truncada';
  referencias?: string[];
}

interface MensagemChat {
  id: string;
  papel: 'usuario' | 'assistente';
  texto: string;
  ferramentas?: FerramentaAtiva[];
  fontes?: FonteCopiloto[];
  avisos?: AvisoMensagem[];
  carregando?: boolean;
}

interface Props {
  chamadoId: string;
}

const SUGESTOES = [
  'Já tivemos casos parecidos?',
  'Qual é o histórico deste chamado?',
  'Qual o procedimento na base de conhecimento?',
];

/**
 * Painel do Copiloto no detalhe do chamado (Sprint 4, ADR-0012).
 * Exibe a conversa em streaming SSE, etapas de ferramentas em tempo real ("Consultando..."),
 * fontes verificadas clicáveis, selo de referências não verificadas (ADR-0020) e botão parar.
 * Fica oculto quando a flag de IA do copiloto estiver desativada (ADR-0021).
 */
export function PainelCopiloto({ chamadoId }: Props) {
  const { data: config } = useConfiguracaoIA();
  const [mensagens, setMensagens] = useState<MensagemChat[]>([]);
  const [entrada, setEntrada] = useState('');
  const [respondendo, setRespondendo] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const contadorRef = useRef(0);

  // Kill switch: esconde o painel se desativado por configuração
  if (config && !config.copiloto) {
    return null;
  }

  const parar = () => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
      abortControllerRef.current = null;
    }
    setRespondendo(false);
    setMensagens((atuais) =>
      atuais.map((msg) => (msg.carregando ? { ...msg, carregando: false } : msg)),
    );
  };

  const enviar = async (textoPergunta: string) => {
    const textoAparado = textoPergunta.trim();
    if (!textoAparado || respondendo) return;

    setErro(null);
    contadorRef.current += 1;
    const idUsuario = `u-${contadorRef.current}`;
    const idAssistente = `a-${contadorRef.current}`;

    const novaMensagemUsuario: MensagemChat = {
      id: idUsuario,
      papel: 'usuario',
      texto: textoAparado,
    };

    const novaMensagemAssistente: MensagemChat = {
      id: idAssistente,
      papel: 'assistente',
      texto: '',
      carregando: true,
      ferramentas: [],
      fontes: [],
      avisos: [],
    };

    const historicoAtualizado = [...mensagens, novaMensagemUsuario];
    setMensagens([...historicoAtualizado, novaMensagemAssistente]);
    setEntrada('');
    setRespondendo(true);

    const abortController = new AbortController();
    abortControllerRef.current = abortController;

    const payloadApi: MensagemCopiloto[] = historicoAtualizado.map((m) => ({
      papel: m.papel,
      conteudo: m.texto,
    }));

    try {
      const stream = conversarComCopiloto(chamadoId, payloadApi, abortController.signal);

      for await (const evento of stream) {
        atualizarMensagemAssistente(idAssistente, evento);
      }
    } catch (e: unknown) {
      if (abortController.signal.aborted) {
        return;
      }
      if (e instanceof ErroApi) {
        if (e.status === 429) {
          setErro('Limite de requisições excedido. Tente novamente em instantes.');
        } else if (e.status === 503) {
          setErro('O copiloto está temporariamente indisponível.');
        } else {
          setErro(e.message);
        }
      } else {
        setErro('Não foi possível se comunicar com o copiloto. Verifique sua conexão.');
      }
    } finally {
      abortControllerRef.current = null;
      setRespondendo(false);
      setMensagens((atuais) =>
        atuais.map((msg) => (msg.id === idAssistente ? { ...msg, carregando: false } : msg)),
      );
    }
  };

  const atualizarMensagemAssistente = (id: string, evento: EventoCopiloto) => {
    setMensagens((atuais) =>
      atuais.map((msg) => {
        if (msg.id !== id) return msg;

        switch (evento.tipo) {
          case 'ferramenta': {
            const ferramentas = [...(msg.ferramentas ?? [])];
            const existenteIndex = ferramentas.findIndex(
              (f) => f.nome === evento.nome && f.fase === 'iniciada',
            );

            if (evento.fase === 'iniciada') {
              ferramentas.push({
                nome: evento.nome,
                fase: 'iniciada',
                descricao: evento.descricao,
              });
            } else if (existenteIndex !== -1) {
              const anterior = ferramentas[existenteIndex];
              if (anterior) {
                ferramentas[existenteIndex] = {
                  nome: anterior.nome,
                  descricao: anterior.descricao,
                  fase: 'concluida',
                  resultados: evento.resultados,
                };
              }
            } else {
              ferramentas.push({
                nome: evento.nome,
                fase: 'concluida',
                resultados: evento.resultados,
              });
            }

            return { ...msg, ferramentas };
          }

          case 'delta':
            return { ...msg, texto: msg.texto + evento.texto };

          case 'fontes':
            return { ...msg, fontes: evento.itens };

          case 'aviso': {
            const avisos = [
              ...(msg.avisos ?? []),
              { tipo: evento.subtipo, referencias: evento.referencias },
            ];
            return { ...msg, avisos };
          }

          case 'fim':
            return { ...msg, carregando: false };

          default:
            return msg;
        }
      }),
    );
  };

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    void enviar(entrada);
  };

  return (
    <Stack gap="sm" data-testid="painel-copiloto">
      <Group justify="space-between" align="center">
        <Group gap="xs">
          <Title order={3} size="h5">
            Copiloto do Chamado
          </Title>
          <Badge variant="light" color="indigo" size="sm">
            IA
          </Badge>
        </Group>
      </Group>

      {erro && (
        <Alert color="red" title="Erro no copiloto" withCloseButton onClose={() => setErro(null)}>
          {erro}
        </Alert>
      )}

      {mensagens.length === 0 ? (
        <Stack gap="xs" py="xs">
          <Text size="sm" c="dimmed">
            Pergunte ao copiloto para investigar casos parecidos, procedimentos e histórico:
          </Text>
          <Group gap="xs">
            {SUGESTOES.map((sugestao) => (
              <Button
                key={sugestao}
                variant="subtle"
                size="xs"
                onClick={() => void enviar(sugestao)}
                disabled={respondendo}
              >
                {sugestao}
              </Button>
            ))}
          </Group>
        </Stack>
      ) : (
        <Stack gap="md" py="xs">
          {mensagens.map((msg) => (
            <Paper
              key={msg.id}
              p="sm"
              withBorder
              bg={msg.papel === 'usuario' ? 'var(--mantine-color-blue-light)' : undefined}
            >
              <Stack gap="xs">
                <Text size="xs" fw={700} c={msg.papel === 'usuario' ? 'blue' : 'indigo'}>
                  {msg.papel === 'usuario' ? 'Atendente' : 'Copiloto'}
                </Text>

                {msg.ferramentas && msg.ferramentas.length > 0 && (
                  <Stack gap={4}>
                    {msg.ferramentas.map((f, i) => (
                      <Group key={`${f.nome}-${i}`} gap={6}>
                        {f.fase === 'iniciada' ? (
                          <>
                            <Loader size={12} color="indigo" />
                            <Text size="xs" c="dimmed">
                              Consultando… {f.descricao ?? f.nome}
                            </Text>
                          </>
                        ) : (
                          <Text size="xs" c="dimmed">
                            ✓ {f.descricao ?? f.nome} ({f.resultados ?? 0}{' '}
                            {f.resultados === 1 ? 'resultado' : 'resultados'})
                          </Text>
                        )}
                      </Group>
                    ))}
                  </Stack>
                )}

                {msg.texto && (
                  <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                    {msg.texto}
                  </Text>
                )}

                {msg.carregando && !msg.texto && (
                  <Group gap="xs">
                    <Loader size="xs" color="indigo" />
                    <Text size="xs" c="dimmed">
                      Pensando…
                    </Text>
                  </Group>
                )}

                {/* Selos de aviso (ADR-0020 / ADR-0021) */}
                {msg.avisos && msg.avisos.length > 0 && (
                  <Stack gap={4} pt="xs">
                    {msg.avisos.map((aviso, idx) =>
                      aviso.tipo === 'referencia_nao_verificada' ? (
                        <Badge
                          key={idx}
                          color="yellow"
                          variant="filled"
                          size="sm"
                          data-testid="selo-referencia-nao-verificada"
                        >
                          Contém referências não verificadas
                          {aviso.referencias && aviso.referencias.length > 0
                            ? `: ${aviso.referencias.join(', ')}`
                            : ''}
                        </Badge>
                      ) : (
                        <Badge
                          key={idx}
                          color="orange"
                          variant="light"
                          size="sm"
                          data-testid="selo-resposta-truncada"
                        >
                          Resposta interrompida pelo limite de tamanho
                        </Badge>
                      ),
                    )}
                  </Stack>
                )}

                {/* Fontes consultadas e verificadas */}
                {msg.fontes && msg.fontes.length > 0 && (
                  <Stack gap={4} pt="xs">
                    <Text size="xs" fw={600} c="dimmed">
                      Fontes consultadas:
                    </Text>
                    <Group gap="xs">
                      {msg.fontes.map((fonte) =>
                        fonte.tipo === 'chamado' && fonte.numero ? (
                          <Anchor
                            key={fonte.id}
                            component={Link}
                            to={`/chamados/${fonte.id}`}
                            size="xs"
                            underline="hover"
                            data-testid="fonte-chamado"
                          >
                            #{fonte.numero} · {fonte.titulo}
                          </Anchor>
                        ) : (
                          <Badge key={fonte.id} variant="outline" color="gray" size="xs">
                            {fonte.titulo}
                          </Badge>
                        ),
                      )}
                    </Group>
                  </Stack>
                )}
              </Stack>
            </Paper>
          ))}
        </Stack>
      )}

      {/* Input e controle */}
      <form onSubmit={onSubmit}>
        <Group gap="xs" align="flex-end">
          <TextInput
            placeholder="Faça uma pergunta ao copiloto..."
            value={entrada}
            onChange={(e) => setEntrada(e.currentTarget.value)}
            disabled={respondendo}
            style={{ flex: 1 }}
            data-testid="input-copiloto"
          />
          {respondendo ? (
            <Button
              color="red"
              variant="outline"
              onClick={parar}
              data-testid="botao-parar-copiloto"
            >
              Parar
            </Button>
          ) : (
            <Button type="submit" disabled={!entrada.trim()} data-testid="botao-enviar-copiloto">
              Enviar
            </Button>
          )}
        </Group>
      </form>
    </Stack>
  );
}
