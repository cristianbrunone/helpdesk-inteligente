import { describe, expect, it } from 'vitest';
import type { ChamadoVersionado } from './chamados';
import { intervaloDePolling } from './triagem';

const agora = Date.parse('2026-10-01T12:00:00Z');

function comTriagem(status: string | null, segundosAtras = 0): ChamadoVersionado {
  return {
    etag: '"1"',
    chamado: {
      triagem:
        status === null
          ? null
          : { status, criadoEm: new Date(agora - segundosAtras * 1000).toISOString() },
    },
  } as unknown as ChamadoVersionado;
}

describe('intervaloDePolling', () => {
  it('pendente: 2 s no começo, 5 s até 1 min e 15 s depois', () => {
    expect(intervaloDePolling(comTriagem('Pendente', 3), agora)).toBe(2_000);
    expect(intervaloDePolling(comTriagem('Pendente', 30), agora)).toBe(5_000);
    expect(intervaloDePolling(comTriagem('Pendente', 300), agora)).toBe(15_000);
  });

  it('fora de pendente (ou sem triagem), não consulta de novo', () => {
    expect(intervaloDePolling(comTriagem('Concluida'), agora)).toBe(false);
    expect(intervaloDePolling(comTriagem('Falhou'), agora)).toBe(false);
    expect(intervaloDePolling(comTriagem(null), agora)).toBe(false);
    expect(intervaloDePolling(undefined, agora)).toBe(false);
  });
});
