import { Badge } from '@mantine/core';
import {
  COR_PRIORIDADE,
  COR_STATUS,
  ROTULO_PRIORIDADE,
  ROTULO_STATUS,
  type Prioridade,
  type StatusChamado,
} from '../dominio/chamado';

export function BadgeStatus({ status }: { status: StatusChamado }) {
  return (
    <Badge color={COR_STATUS[status]} variant="filled">
      {ROTULO_STATUS[status]}
    </Badge>
  );
}

export function BadgePrioridade({ prioridade }: { prioridade: Prioridade }) {
  return (
    <Badge color={COR_PRIORIDADE[prioridade]} variant="dot">
      {ROTULO_PRIORIDADE[prioridade]}
    </Badge>
  );
}
