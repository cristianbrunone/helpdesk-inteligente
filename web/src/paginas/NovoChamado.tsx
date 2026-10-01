import { zodResolver } from '@hookform/resolvers/zod';
import {
  Button,
  Group,
  NativeSelect,
  Paper,
  SimpleGrid,
  Stack,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core';
import { useForm } from 'react-hook-form';
import { Link, useNavigate } from 'react-router';
import { useCategorias } from '../api/categorias';
import { useCriarChamado } from '../api/chamados';
import { ErroApi } from '../api/cliente';
import { AlertaErro } from '../componentes/AlertaErro';
import { PRIORIDADES, ROTULO_PRIORIDADE } from '../dominio/chamado';
import {
  esquemaNovoChamado,
  paraNovoChamado,
  VALORES_INICIAIS,
  type ValoresNovoChamado,
} from '../formularios/esquemaNovoChamado';

const CAMPOS = new Set(Object.keys(VALORES_INICIAIS));

export function NovoChamado() {
  const navegar = useNavigate();
  const { data: categorias = [] } = useCategorias();
  const criacao = useCriarChamado();
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm({ resolver: zodResolver(esquemaNovoChamado), defaultValues: VALORES_INICIAIS });

  const enviar = handleSubmit(async (valores) => {
    try {
      const chamado = await criacao.mutateAsync(paraNovoChamado(valores));
      await navegar(`/chamados/${chamado.id}`);
    } catch (erro) {
      // 422: as mensagens da API vão para os campos (os nomes do contrato são os nomes do formulário).
      if (erro instanceof ErroApi && erro.status === 422) {
        for (const [campo, mensagens] of Object.entries(erro.errosPorCampo)) {
          if (CAMPOS.has(campo)) {
            setError(campo as keyof ValoresNovoChamado, { message: mensagens.join(' ') });
          }
        }
      }
    }
  });

  // Os 422 já aparecem nos campos; o alerta fica para os demais erros (rede, 500...).
  const erroGeral =
    criacao.isError && !(criacao.error instanceof ErroApi && criacao.error.status === 422)
      ? criacao.error
      : null;

  return (
    <Stack gap="md" maw={720}>
      <Title order={2}>Novo chamado</Title>

      {erroGeral && <AlertaErro titulo="Não foi possível abrir o chamado" erro={erroGeral} />}

      <Paper withBorder p="md" component="form" noValidate onSubmit={(e) => void enviar(e)}>
        <Stack gap="sm">
          <TextInput
            label="Título"
            withAsterisk
            placeholder="Resumo do problema"
            error={errors.titulo?.message}
            {...register('titulo')}
          />
          <Textarea
            label="Descrição"
            withAsterisk
            autosize
            minRows={4}
            placeholder="O que aconteceu, desde quando e o que você já tentou"
            error={errors.descricao?.message}
            {...register('descricao')}
          />
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
            <TextInput
              label="Nome do solicitante"
              withAsterisk
              autoComplete="name"
              error={errors.solicitanteNome?.message}
              {...register('solicitanteNome')}
            />
            <TextInput
              label="E-mail do solicitante"
              withAsterisk
              type="email"
              autoComplete="email"
              error={errors.solicitanteEmail?.message}
              {...register('solicitanteEmail')}
            />
            <NativeSelect
              label="Categoria"
              description="Opcional: a triagem por IA sugere."
              data={[
                { value: '', label: 'Não sei / deixar a triagem sugerir' },
                ...categorias.map((c) => ({ value: String(c.id), label: c.nome })),
              ]}
              error={errors.categoriaId?.message}
              {...register('categoriaId')}
            />
            <NativeSelect
              label="Prioridade"
              description="Opcional: se não informada, fica Média."
              data={[
                { value: '', label: 'Não sei / deixar a triagem sugerir' },
                ...PRIORIDADES.map((p) => ({ value: p, label: ROTULO_PRIORIDADE[p] })),
              ]}
              error={errors.prioridade?.message}
              {...register('prioridade')}
            />
          </SimpleGrid>
          <Text size="xs" c="dimmed">
            Campos com * são obrigatórios.
          </Text>
          <Group justify="flex-end">
            <Button variant="default" component={Link} to="/chamados">
              Cancelar
            </Button>
            <Button type="submit" loading={isSubmitting}>
              Abrir chamado
            </Button>
          </Group>
        </Stack>
      </Paper>
    </Stack>
  );
}
