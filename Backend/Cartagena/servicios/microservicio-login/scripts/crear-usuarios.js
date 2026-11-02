import { variables } from '../src/config/entorno.js';
import { generarHash } from '../src/utils/passwords.js';
import { crearUsuario, buscarUsuarioPorNombre } from '../src/repositories/usuarios.repository.js';
import { pool, cerrarPool } from '../src/config/baseDatos.js';
import { logger } from '../src/utils/logger.js';

// Los roles deben existir en la tabla `rol` (los crea el esquema).
const usuariosDemo = [
  { nombreUsuario: 'admin', contrasena: variables.USUARIOS_DEMO_PASSWORD, email: 'admin@ventasonline.com', nombreCompleto: 'Administrador General', telefono: null, rol: 'ADMIN' },
  { nombreUsuario: 'vendedor', contrasena: variables.USUARIOS_DEMO_PASSWORD, email: 'vendedor@ventasonline.com', nombreCompleto: 'Vendedor de Mostrador', telefono: null, rol: 'VENDEDOR' },
  { nombreUsuario: 'cliente', contrasena: variables.USUARIOS_DEMO_PASSWORD, email: 'cliente@ventasonline.com', nombreCompleto: 'Cliente de Prueba', telefono: null, rol: 'CLIENTE' },
  { nombreUsuario: 'inactivo', contrasena: variables.USUARIOS_DEMO_PASSWORD, email: 'inactivo@ventasonline.com', nombreCompleto: 'Usuario Deshabilitado', telefono: null, rol: 'CLIENTE', activo: false }
];

async function ejecutar() {
  logger.info(`Generando usuarios de prueba en la base de datos "${variables.DB_NAME}"...`);
  const contrasenaCifrada = await generarHash(variables.USUARIOS_DEMO_PASSWORD);

  for (const usuario of usuariosDemo) {
    const existente = await buscarUsuarioPorNombre(usuario.nombreUsuario);

    if (existente) {
      logger.warn(`El usuario "${usuario.nombreUsuario}" ya existe (id ${existente.id}). Se omite.`);
      continue;
    }

    const id = await crearUsuario({
      nombreUsuario: usuario.nombreUsuario,
      passwordHash: contrasenaCifrada,
      email: usuario.email,
      nombreCompleto: usuario.nombreCompleto,
      telefono: usuario.telefono,
      rol: usuario.rol
    });

    // La tabla real es `usuario` y la llave es `id_usuario`.
    if (usuario.activo === false) {
      await pool.execute('UPDATE usuario SET activo = 0 WHERE id_usuario = ?', [id]);
    }

    const estado = usuario.activo === false ? ' | inactivo' : '';
    logger.info(`Usuario creado -> id ${id} | ${usuario.nombreUsuario} | rol ${usuario.rol}${estado}`);
  }

  logger.info('Usuarios de prueba listos. Contrasena compartida: la definida en USUARIOS_DEMO_PASSWORD.');
  logger.info('  admin     -> ADMIN    (activo)');
  logger.info('  vendedor  -> VENDEDOR (activo)');
  logger.info('  cliente   -> CLIENTE  (activo)');
  logger.info('  inactivo  -> CLIENTE  (deshabilitado, demuestra el rechazo 403)');
}

ejecutar()
  .catch((error) => {
    logger.error(`No se pudieron crear los usuarios de prueba: ${error.message}`);
    logger.error('Ejecute primero "npm run db:schema" y revise las credenciales de .env.');
    process.exitCode = 1;
  })
  .finally(async () => {
    await cerrarPool().catch(() => {});
  });
