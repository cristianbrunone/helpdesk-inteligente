import {
  Button,
  Chip,
  Group,
  NativeSelect,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
} from '@mantine/core';
import { useId, useMediaQuery } from '@mantine/hooks';
import { useState } from 'react';
import { useCategorias } from '../api/categorias';
import {
  contarFiltrosAtivos,
  FILTROS_PADRAO,
  type Direcao,
  type FiltrosChamados as Filtros,
  type Ordenacao,
} from '../api/filtrosChamados';
import {
  PRIORIDADES,
  ROTULO_PRIORIDADE,
  ROTULO_STATUS,
  STATUS_CHAMADO,
  type Prioridade,
  type StatusChamado,
} from '../dominio/chamado';
import type { AlterarFiltros } from '../hooks/useFiltrosDaUrl';
import { CampoBusca } from './CampoBusca';

const SEM_CATEGORIA = 'sem';

const ORDENACOES: { valor: string; rotulo: string; ordenarPor: Ordenacao; direcao: Direcao }[] = [
  {
    valor: 'criadoEm-desc',
    rotulo: 'Mais recentes primeiro',
    ordenarPor: 'criadoEm',
    direcao: 'desc',
  },
  {
    valor: 'criadoEm-asc',
    rotulo: 'Mais antigos primeiro',
    ordenarPor: 'criadoEm',
    direcao: 'asc',
  },
  {
    valor: 'prioridade-desc',
    rotulo: 'Maior prioridade primeiro',
    ordenarPor: 'prioridade',
    direcao: 'desc',
  },
  {
    valor: 'prioridade-asc',
    rotulo: 'Menor prioridade primeiro',
    ordenarPor: 'prioridade',
    direcao: 'asc',
  },
];

interface Props {
  filtros: Filtros;
  alterar: AlterarFiltros;
}

/** Abaixo do breakpoint `sm` da Mantine (48em), o mesmo do menu hambúrguer. */
const CELULAR = '(max-width: 47.99em)';

/**
 * Filtros da lista. Chips (repetíveis = OR, como na API) funcionam bem no toque, inclusive em 375 px. No celular, só
 * a busca fica sempre visível: o resto ocupava a primeira tela inteira e fica num painel recolhível, que já abre
 * quando a URL traz algum filtro (Sprint 7, item A2 da análise de experiência).
 */
export function FiltrosChamados({ filtros, alterar }: Props) {
  const { data: categorias = [] } = useCategorias();
  const ativos = contarFiltrosAtivos(filtros);
  const ocultos = ativos - (filtros.q ? 1 : 0);
  const ehCelular = useMediaQuery(CELULAR, false, { getInitialValueInEffect: false });
  const [aberto, setAberto] = useState(ocultos > 0);
  const idPainel = useId();
  const mostrarPainel = !ehCelular || aberto;

  const categoriasMarcadas = [
    ...filtros.categoriaId.map(String),
    ...(filtros.semCategoria ? [SEM_CATEGORIA] : []),
  ];

  return (
    <Stack gap="sm" component="section" aria-label="Filtros">
      <CampoBusca
        valor={filtros.q}
        aoBuscar={(q) => alterar({ q }, { substituirHistorico: true })}
      />

      {ehCelular && (
        <Group>
          <Button
            variant="light"
            size="xs"
            aria-expanded={aberto}
            aria-controls={aberto ? idPainel : undefined}
            onClick={() => setAberto((valor) => !valor)}
          >
            {aberto ? 'Ocultar filtros' : 'Filtros'}
            {ocultos > 0 && ` (${ocultos})`}
          </Button>
        </Group>
      )}

      {mostrarPainel && (
        <Stack gap="sm" id={idPainel}>
          <Grupo titulo="Status">
            <Chip.Group
              multiple
              value={filtros.status}
              onChange={(status) => alterar({ status: status as StatusChamado[] })}
            >
              {STATUS_CHAMADO.map((status) => (
                <Chip key={status} value={status} size="xs">
                  {ROTULO_STATUS[status]}
                </Chip>
              ))}
            </Chip.Group>
          </Grupo>

          <Grupo titulo="Prioridade">
            <Chip.Group
              multiple
              value={filtros.prioridade}
              onChange={(prioridade) => alterar({ prioridade: prioridade as Prioridade[] })}
            >
              {PRIORIDADES.map((prioridade) => (
                <Chip key={prioridade} value={prioridade} size="xs">
                  {ROTULO_PRIORIDADE[prioridade]}
                </Chip>
              ))}
            </Chip.Group>
          </Grupo>

          <Grupo titulo="Categoria">
            <Chip.Group
              multiple
              value={categoriasMarcadas}
              onChange={(marcadas) =>
                alterar({
                  categoriaId: marcadas.filter((v) => v !== SEM_CATEGORIA).map(Number),
                  semCategoria: marcadas.includes(SEM_CATEGORIA),
                })
              }
            >
              {categorias.map((categoria) => (
                <Chip key={categoria.id} value={String(categoria.id)} size="xs">
                  {categoria.nome}
                </Chip>
              ))}
              <Chip value={SEM_CATEGORIA} size="xs">
                Sem categoria
              </Chip>
            </Chip.Group>
          </Grupo>

          <SimpleGrid cols={{ base: 1, xs: 3 }} spacing="sm">
            <TextInput
              type="date"
              label="Criado de"
              value={filtros.criadoDe}
              max={filtros.criadoAte || undefined}
              onChange={(evento) => alterar({ criadoDe: evento.currentTarget.value })}
            />
            <TextInput
              type="date"
              label="Criado até"
              value={filtros.criadoAte}
              min={filtros.criadoDe || undefined}
              onChange={(evento) => alterar({ criadoAte: evento.currentTarget.value })}
            />
            <NativeSelect
              label="Ordenar por"
              value={`${filtros.ordenarPor}-${filtros.direcao}`}
              data={ORDENACOES.map(({ valor, rotulo }) => ({ value: valor, label: rotulo }))}
              onChange={(evento) => {
                const escolhida = ORDENACOES.find((o) => o.valor === evento.currentTarget.value);
                if (escolhida)
                  alterar({ ordenarPor: escolhida.ordenarPor, direcao: escolhida.direcao });
              }}
            />
          </SimpleGrid>

          {ativos > 0 && (
            <Group>
              <Button
                variant="subtle"
                size="xs"
                onClick={() =>
                  alterar({
                    ...FILTROS_PADRAO,
                    ordenarPor: filtros.ordenarPor,
                    direcao: filtros.direcao,
                  })
                }
              >
                Limpar filtros ({ativos})
              </Button>
            </Group>
          )}
        </Stack>
      )}
    </Stack>
  );
}

function Grupo({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div role="group" aria-label={titulo}>
      <Text size="sm" fw={500} mb={4}>
        {titulo}
      </Text>
      <Group gap={6}>{children}</Group>
    </div>
  );
}
