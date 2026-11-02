import 'dotenv/config';
import { z } from 'zod';

const esquemaVariables = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().positive().default(3000),
  API_PREFIX: z.string().startsWith('/').default('/api/v1'),

  DB_HOST: z.string().min(1).default('localhost'),
  DB_PORT: z.coerce.number().int().positive().default(3306),
  DB_USER: z.string().min(1).default('root'),
  DB_PASSWORD: z.string().default(''),
  DB_NAME: z.string().min(1).default('ventas_db'),
  DB_CONNECTION_LIMIT: z.coerce.number().int().positive().default(10),

  JWT_SECRET: z.string().min(32, 'JWT_SECRET debe tener al menos 32 caracteres'),
  JWT_EXPIRES_IN: z.string().min(1).default('15m'),
  JWT_ISSUER: z.string().min(1).default('microservicio-login'),
  BCRYPT_SALT_ROUNDS: z.coerce.number().int().min(4).max(15).default(12),

  HTTPS_ENABLED: z
    .string()
    .default('false')
    .transform((valor) => valor === 'true'),
  HTTPS_KEY_PATH: z.string().default('./certs/server.key'),
  HTTPS_CERT_PATH: z.string().default('./certs/server.crt'),

  CORS_ORIGIN: z.string().default('*'),
  LOGIN_RATE_LIMIT_MAX: z.coerce.number().int().positive().default(10),
  LOGIN_RATE_LIMIT_WINDOW_MS: z.coerce.number().int().positive().default(15 * 60 * 1000),

  USUARIOS_DEMO_PASSWORD: z.string().min(8).default('ClaveDemo123')
});

const resultado = esquemaVariables.safeParse(process.env);

if (!resultado.success) {
  const detalle = resultado.error.issues
    .map((problema) => `  - ${problema.path.join('.')}: ${problema.message}`)
    .join('\n');

  throw new Error(`Configuracion invalida en .env:\n${detalle}\n\nCopia .env.example a .env y completa los valores.`);
}

export const variables = resultado.data;

export const dbConfig = {
  host: variables.DB_HOST,
  port: variables.DB_PORT,
  user: variables.DB_USER,
  password: variables.DB_PASSWORD,
  database: variables.DB_NAME,
  waitForConnections: true,
  connectionLimit: variables.DB_CONNECTION_LIMIT,
  queueLimit: 0,
  charset: 'utf8mb4_unicode_ci',
  timezone: 'Z'
};

export const esProduccion = variables.NODE_ENV === 'production';
export const esPruebas = variables.NODE_ENV === 'test';
