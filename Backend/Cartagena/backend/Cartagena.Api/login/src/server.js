import http from 'node:http';
import https from 'node:https';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { crearAplicacion } from './app.js';
import { variables, esProduccion } from './config/entorno.js';
import { verificarMySQL, cerrarPool } from './config/baseDatos.js';
import { logger } from './utils/logger.js';

const app = crearAplicacion();

function crearServidor() {
  if (!variables.HTTPS_ENABLED) {
    logger.info('HTTPS deshabilitado (HTTPS_ENABLED=false). Sirviendo por HTTP para desarrollo local.');
    return http.createServer(app);
  }

  try {
    const opciones = {
      key: readFileSync(resolve(variables.HTTPS_KEY_PATH)),
      cert: readFileSync(resolve(variables.HTTPS_CERT_PATH))
    };
    logger.info('HTTPS habilitado con el certificado configurado.');
    return https.createServer(opciones, app);
  } catch (error) {
    throw new Error(
      `HTTPS_ENABLED=true pero no se pudieron leer los certificados (${error.message}). ` +
        'Genere un certificado autofirmado en ./certs o deje HTTPS_ENABLED=false.'
    );
  }
}

const servidor = crearServidor();

servidor.listen(variables.PORT, async () => {
  const protocolo = variables.HTTPS_ENABLED ? 'https' : 'http';
  logger.info(`Microservicio de login escuchando en ${protocolo}://localhost:${variables.PORT}${variables.API_PREFIX}`);
  logger.info(`Entorno: ${variables.NODE_ENV}`);

  const estado = await verificarMySQL();
  if (estado.conectado) {
    logger.info(`Conexion con MySQL "${variables.DB_NAME}" establecida (${estado.latenciaMs} ms).`);
  } else {
    logger.error(`MySQL no disponible: ${estado.error}`);
    if (esProduccion) {
      logger.error('El servicio arrancara, pero no podra autenticar usuarios hasta que MySQL responda.');
    }
  }
});

servidor.on('error', (error) => {
  if (error.code === 'EADDRINUSE') {
    logger.error(`El puerto ${variables.PORT} ya esta en uso. Cambie el valor PORT en .env.`);
  } else {
    logger.error(`Error al iniciar el servidor: ${error.message}`);
  }
  process.exit(1);
});

async function apagar(motivo) {
  logger.info(`${motivo}. Cerrando el microservicio de forma ordenada...`);
  servidor.close(async () => {
    await cerrarPool().catch(() => {});
    process.exit(0);
  });
  setTimeout(() => process.exit(1), 10000).unref();
}

process.on('SIGINT', () => apagar('SIGINT recibido'));
process.on('SIGTERM', () => apagar('SIGTERM recibido'));
process.on('unhandledRejection', (motivo) => {
  logger.error(`Promesa rechazada sin manejar: ${motivo}`);
});
