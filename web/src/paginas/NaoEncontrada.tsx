import { Anchor, Stack, Text, Title } from '@mantine/core';
import { Link } from 'react-router';

export function NaoEncontrada() {
  return (
    <Stack gap="sm">
      <Title order={2}>Página não encontrada</Title>
      <Text>O endereço acessado não existe.</Text>
      <Anchor component={Link} to="/">
        Voltar ao início
      </Anchor>
    </Stack>
  );
}
