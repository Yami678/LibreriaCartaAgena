import 'dotenv/config';
import mysql from 'mysql2/promise';
import { variables } from '../src/config/entorno.js';
import { logger } from '../src/utils/logger.js';

// Este script crea (o actualiza) el usuario de MySQL que usa la aplicacion.
// Las claves salen de .env, nunca estan escritas en el repositorio.
// Requiere un usuario root: defina DB_ROOT_USER y DB_ROOT_PASSWORD en .env.

const rootUser = process.env.DB_ROOT_USER ?? 'root';
const rootPassword = process.env.DB_ROOT_PASSWORD ?? '';

// 'localhost' sirve para ejecucion nativa y '%' para los contenedores Docker.
const HOSTS = ['localhost', '%'];

async function ejecutar() {
  const conexion = await mysql.createConnection({
    host: variables.DB_HOST,
    port: variables.DB_PORT,
    user: rootUser,
    password: rootPassword
  });

  try {
    const base = `\`${variables.DB_NAME}\``;

    await conexion.query(
      `CREATE DATABASE IF NOT EXISTS ${base} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci`
    );

    for (const host of HOSTS) {
      // Si la cuenta ya existe, ALTER la deja con la clave actual de .env.
      await conexion.query(`CREATE USER IF NOT EXISTS ?@? IDENTIFIED BY ?`, [
        variables.DB_USER,
        host,
        variables.DB_PASSWORD
      ]);
      await conexion.query(`ALTER USER ?@? IDENTIFIED BY ?`, [
        variables.DB_USER,
        host,
        variables.DB_PASSWORD
      ]);
      await conexion.query(`GRANT ALL PRIVILEGES ON ${base}.* TO ?@?`, [variables.DB_USER, host]);
    }
    await conexion.query('FLUSH PRIVILEGES');

    logger.info(`Usuario "${variables.DB_USER}" (localhost y %) listo para la base "${variables.DB_NAME}".`);
  } finally {
    await conexion.end();
  }
}

ejecutar().catch((error) => {
  logger.error(`No se pudo crear el usuario de aplicacion: ${error.message}`);
  logger.error('Defina DB_ROOT_USER y DB_ROOT_PASSWORD en .env y vuelva a intentarlo.');
  process.exitCode = 1;
});
