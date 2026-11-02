import { variables, esPruebas } from '../config/entorno.js';

const niveles = { error: 0, warn: 1, info: 2, debug: 3 };
const nivelActivo = esPruebas ? -1 : niveles[process.env.LOG_LEVEL ?? 'info'] ?? niveles.info;

function emitir(nivel, mensaje, detalles) {
  if (niveles[nivel] > nivelActivo) return;

  const metodo = nivel === 'error' ? console.error : nivel === 'warn' ? console.warn : console.log;
  const marcaTiempo = new Date().toISOString();
  const contexto = { marcaTiempo, nivel, servicio: 'microservicio-login' };

  metodo(`[${marcaTiempo}] ${nivel.toUpperCase().padEnd(5)} ${mensaje}`, detalles ? detalles : '');
}

export const logger = {
  error: (mensaje, detalles) => emitir('error', mensaje, detalles),
  warn: (mensaje, detalles) => emitir('warn', mensaje, detalles),
  info: (mensaje, detalles) => emitir('info', mensaje, detalles),
  debug: (mensaje, detalles) => emitir('debug', mensaje, detalles),
  entorno: variables.NODE_ENV
};
