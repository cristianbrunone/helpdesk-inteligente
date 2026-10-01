import { Stack, Text, Title } from '@mantine/core';

export function Inicio() {
  return (
    <Stack gap="sm" maw={720}>
      <Title order={2}>Bem-vindo</Title>
      <Text>
        Aqui você vai abrir e acompanhar chamados de suporte, com triagem sugerida por IA e decisão
        final sempre humana.
      </Text>
      <Text c="dimmed" size="sm">
        As categorias ao lado vêm da API e confirmam que frontend, API e banco estão conectados.
      </Text>
    </Stack>
  );
}
