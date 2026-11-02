import mysql from 'mysql2/promise';
import { dbConfig, esPruebas } from './entorno.js';
import { logger } from '../utils/logger.js';

export const pool = mysql.createPool(dbConfig);

pool.on('connection', (conexion) => {
  conexion.query("SET time_zone = '+00:00'", (error) => {
    if (error) {
      logger.error(`No se pudo fijar la zona horaria UTC en la conexion ${conexion.threadId}: ${error.message}`);
    } else if (!esPruebas) {
      logger.debug(`Conexion MySQL ${conexion.threadId} lista con zona horaria UTC.`);
    }
  });
});

export async function consultarConexion() {
  const [filas] = await pool.query('SELECT 1 AS ok');
  return filas[0]?.ok === 1;
}

export async function verificarMySQL() {
  const inicio = Date.now();
  try {
    await consultarConexion();
    return { conectado: true, latenciaMs: Date.now() - inicio };
  } catch (error) {
    return { conectado: false, latenciaMs: null, error: error.message };
  }
}

export async function cerrarPool() {
  await pool.end();
  logger.info('Pool de conexiones MySQL cerrado.');
}
