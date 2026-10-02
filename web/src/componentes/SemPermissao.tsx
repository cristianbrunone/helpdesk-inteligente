import { Anchor, Stack, Text, Title } from '@mantine/core';
import { Link } from 'react-router';

/**
 * Resposta 403: o perfil não dá acesso à página. Não é uma falha, então não leva o alerta vermelho nem o "Tentar
 * novamente", que daria a mesma resposta (Sprint 7, item M3 da análise de experiência).
 */
export function SemPermissao() {
  return (
    <Stack gap="xs" align="flex-start">
      <Title order={3} size="h4">
        Sem acesso a esta página
      </Title>
      <Text c="dimmed">O seu perfil não tem permissão para ver este conteúdo.</Text>
      <Anchor component={Link} to="/chamados">
        Voltar para os chamados
      </Anchor>
    </Stack>
  );
}
