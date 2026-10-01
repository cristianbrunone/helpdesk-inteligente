import { AppShell, Burger, Group, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { Outlet } from 'react-router';
import { PainelCategorias } from './PainelCategorias';

/** Casca responsiva: no celular (< sm) a navegação vira um menu aberto pelo botão hambúrguer. */
export function LayoutBase() {
  const [menuAberto, { toggle }] = useDisclosure();

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 260, breakpoint: 'sm', collapsed: { mobile: !menuAberto } }}
      padding="md"
    >
      <AppShell.Header>
        <Group h="100%" px="md" gap="sm" wrap="nowrap">
          <Burger
            opened={menuAberto}
            onClick={toggle}
            hiddenFrom="sm"
            size="sm"
            aria-label={menuAberto ? 'Fechar menu' : 'Abrir menu'}
          />
          <Title order={1} size="h4">
            HelpDesk Inteligente
          </Title>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="md" aria-label="Navegação">
        <Title order={2} size="xs" c="dimmed" tt="uppercase" mb="sm">
          Categorias
        </Title>
        <PainelCategorias />
      </AppShell.Navbar>

      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  );
}
