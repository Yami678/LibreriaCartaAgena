import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import { variables } from './config/entorno.js';
import rutas from './routes/index.js';
import { manejadorDeErrores, noEncontrado } from './middlewares/errores.js';
import { logger } from './utils/logger.js';

export function crearAplicacion() {
  const app = express();

  app.disable('x-powered-by');
  app.set('trust proxy', 1);

  app.use(helmet());
  app.use(
    cors({
      origin: variables.CORS_ORIGIN === '*' ? true : variables.CORS_ORIGIN.split(',').map((o) => o.trim()),
      methods: ['GET', 'POST', 'OPTIONS'],
      allowedHeaders: ['Content-Type', 'Authorization'],
      exposedHeaders: ['X-Total-Count']
    })
  );
  app.use(express.json({ limit: '16kb' }));
  app.use(express.urlencoded({ extended: false, limit: '16kb' }));

  app.use((req, res, next) => {
    const inicio = Date.now();
    res.on('finish', () => {
      logger.info(`${req.method} ${req.originalUrl} -> ${res.statusCode} (${Date.now() - inicio} ms)`);
    });
    next();
  });

  app.get('/', (req, res) => {
    res.status(200).json({
      exito: true,
      mensaje: 'Microservicio de login y verificacion de identidad',
      datos: {
        version: '1.0.0',
        documentacion: 'README.md',
        endpoints: [
          `POST ${variables.API_PREFIX}/auth/registro`,
          `POST ${variables.API_PREFIX}/auth/login`,
          `GET  ${variables.API_PREFIX}/auth/verificar`,
          `GET  ${variables.API_PREFIX}/auth/perfil`,
          `GET  ${variables.API_PREFIX}/estado`,
          `GET  ${variables.API_PREFIX}/salud`
        ]
      }
    });
  });

  app.use(variables.API_PREFIX, rutas);

  app.use(noEncontrado);
  app.use(manejadorDeErrores);

  return app;
}

export default crearAplicacion;
