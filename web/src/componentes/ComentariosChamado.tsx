import { zodResolver } from '@hookform/resolvers/zod';
import { Button, Group, Paper, Stack, Text, Textarea, Title } from '@mantine/core';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { useComentar, type ChamadoVersionado } from '../api/chamados';
import { ErroApi } from '../api/cliente';
import { formatarDataHora } from '../dominio/chamado';
import { notificarErroDeEscrita, telaDesatualizada } from './notificarErroDeEscrita';

const esquema = z.object({
  texto: z
    .string()
    .trim()
    .min(1, 'Escreva o comentário.')
    .max(4000, 'O comentário deve ter no máximo 4000 caracteres.'),
});

interface Props {
  versionado: ChamadoVersionado;
  atendente: string;
  aoDesatualizar: () => void;
}

/** Comentários em ordem cronológica. O formulário só aparece quando o domínio permite (`podeComentar`). */
export function ComentariosChamado({ versionado, atendente, aoDesatualizar }: Props) {
  const { chamado, etag } = versionado;
  const comentar = useComentar(chamado.id);
  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm({ resolver: zodResolver(esquema), defaultValues: { texto: '' } });

  const enviar = handleSubmit(async ({ texto }) => {
    try {
      await comentar.mutateAsync({ autor: atendente.trim(), texto, etag });
      reset();
    } catch (erro) {
      if (erro instanceof ErroApi && erro.status === 422 && erro.errosPorCampo['texto']) {
        setError('texto', { message: erro.errosPorCampo['texto'].join(' ') });
        return;
      }
      notificarErroDeEscrita(erro);
      if (telaDesatualizada(erro)) aoDesatualizar();
    }
  });

  return (
    <Stack gap="sm" component="section" aria-labelledby="titulo-comentarios">
      <Title order={3} size="h5" id="titulo-comentarios">
        Comentários ({chamado.comentarios.length})
      </Title>

      {chamado.comentarios.length === 0 ? (
        <Text size="sm" c="dimmed">
          Nenhum comentário ainda.
        </Text>
      ) : (
        <Stack component="ul" gap="xs" m={0} p={0} style={{ listStyle: 'none' }}>
          {chamado.comentarios.map((comentario) => (
            <Paper component="li" key={comentario.id} withBorder p="sm">
              <Text size="xs" c="dimmed">
                {comentario.autor} · {formatarDataHora(comentario.criadoEm)}
              </Text>
              <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                {comentario.texto}
              </Text>
            </Paper>
          ))}
        </Stack>
      )}

      {chamado.podeComentar ? (
        <form noValidate onSubmit={(e) => void enviar(e)}>
          <Stack gap="xs">
            <Textarea
              label="Novo comentário"
              autosize
              minRows={2}
              error={errors.texto?.message}
              {...register('texto')}
            />
            <Group justify="flex-end">
              <Button
                type="submit"
                size="xs"
                loading={isSubmitting}
                disabled={atendente.trim().length === 0}
              >
                Comentar
              </Button>
            </Group>
          </Stack>
        </form>
      ) : (
        <Text size="sm" c="dimmed">
          Chamados fechados ou cancelados não aceitam comentários.
        </Text>
      )}
    </Stack>
  );
}
