import { Alert, Button, Skeleton, Stack, Text } from '@mantine/core';
import { useCategorias } from '../api/categorias';
import { ErroApi, mensagemDeErro } from '../api/cliente';

const ITENS_DO_ESQUELETO = 5;

/** Lista de categorias com os três estados obrigatórios: carregando, erro e vazio. */
export function PainelCategorias() {
  const { data: categorias, isPending, isError, error, refetch, isFetching } = useCategorias();

  if (isPending) {
    return (
      <Stack gap="xs" aria-busy="true" aria-label="Carregando categorias">
        {Array.from({ length: ITENS_DO_ESQUELETO }, (_, i) => (
          <Skeleton key={i} height={22} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (isError) {
    return (
      <Alert color="red" variant="light" title="Não foi possível carregar as categorias">
        <Text size="sm">{mensagemDeErro(error)}</Text>
        {error instanceof ErroApi && error.correlationId && (
          <Text size="xs" c="dimmed" mt={4}>
            Código de rastreio: {error.correlationId}
          </Text>
        )}
        <Button
          mt="sm"
          size="xs"
          variant="light"
          color="red"
          loading={isFetching}
          onClick={() => void refetch()}
        >
          Tentar novamente
        </Button>
      </Alert>
    );
  }

  if (categorias.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        Nenhuma categoria cadastrada.
      </Text>
    );
  }

  return (
    <Stack component="ul" gap="xs" m={0} p={0} style={{ listStyle: 'none' }}>
      {categorias.map((categoria) => (
        <Text component="li" key={categoria.id} size="sm">
          {categoria.nome}
        </Text>
      ))}
    </Stack>
  );
}
