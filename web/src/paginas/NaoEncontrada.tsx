import { Anchor, Stack, Text, Title } from '@mantine/core';
import { Link } from 'react-router';
import { useTituloDaPagina } from '../hooks/useTituloDaPagina';

export function NaoEncontrada() {
  useTituloDaPagina('Página não encontrada');
  return (
    <Stack gap="sm">
      <Title order={2}>Página não encontrada</Title>
      <Text>O endereço acessado não existe.</Text>
      <Anchor component={Link} to="/chamados">
        Voltar para os chamados
      </Anchor>
    </Stack>
  );
}
