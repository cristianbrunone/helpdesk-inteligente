import { Alert, Button, Text } from '@mantine/core';
import { ErroApi, mensagemDeErro } from '../api/cliente';

interface Props {
  titulo: string;
  erro: unknown;
  aoTentarNovamente?: () => void;
  tentando?: boolean;
}

/** Estado de erro padrão: mensagem amigável, código de rastreio para o suporte e "Tentar novamente". */
export function AlertaErro({ titulo, erro, aoTentarNovamente, tentando = false }: Props) {
  return (
    <Alert color="red" variant="light" title={titulo}>
      <Text size="sm">{mensagemDeErro(erro)}</Text>
      {erro instanceof ErroApi && erro.correlationId && (
        <Text size="xs" c="dimmed" mt={4}>
          Código de rastreio: {erro.correlationId}
        </Text>
      )}
      {aoTentarNovamente && (
        <Button
          mt="sm"
          size="xs"
          variant="light"
          color="red"
          loading={tentando}
          onClick={aoTentarNovamente}
        >
          Tentar novamente
        </Button>
      )}
    </Alert>
  );
}
