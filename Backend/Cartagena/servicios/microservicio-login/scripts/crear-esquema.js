import 'dotenv/config';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import mysql from 'mysql2/promise';
import { dbConfig } from '../src/config/entorno.js';
import { logger } from '../src/utils/logger.js';

const rutaEsquema = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'database', 'esquema.sql');

const reset = process.argv.includes('--reset');

async function ejecutar() {
  const sql = await readFile(rutaEsquema, 'utf8');

  // Nos conectamos sin base de datos para poder crearla si no existe.
  const { database, ...conexionSinBase } = dbConfig;
  const conexion = await mysql.createConnection({
    ...conexionSinBase,
    multipleStatements: true
  });

  try {
    if (reset) {
      await conexion.query(`DROP DATABASE IF EXISTS \`${database}\``);
      logger.warn(`Base de datos ${database} eliminada. Se recreara desde cero.`);
    }

    await conexion.query(
      `CREATE DATABASE IF NOT EXISTS \`${database}\`
       CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci`
    );
    await conexion.changeUser({ database });

    await conexion.query(sql);
    logger.info(`Esquema aplicado correctamente en la base de datos "${database}".`);
    logger.info('Tablas de autenticacion listas: rol, usuario, token_cuenta.');
  } finally {
    await conexion.end();
  }
}

ejecutar().catch((error) => {
  logger.error(`No se pudo aplicar el esquema: ${error.message}`);
  logger.error('Verifica que MySQL este en marcha y que las credenciales de .env sean correctas.');
  process.exitCode = 1;
});
