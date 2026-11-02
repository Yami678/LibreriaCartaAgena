import { variables } from '../config/entorno.js';
import { firmarToken, segundosDeVigencia } from '../utils/jwt.js';
import { generarHash, verificarPassword } from '../utils/passwords.js';
import { ErrorAplicacion, ErrorCredenciales } from '../utils/errores.js';
import { logger } from '../utils/logger.js';
import {
  buscarUsuarioPorNombre,
  crearUsuario,
  registrarAcceso,
  registrarIntentoFallido
} from '../repositories/usuarios.repository.js';

const MAX_INTENTOS_FALLIDOS = 5;
const MINUTOS_BLOQUEO = 15;
const ROL_REGISTRO = 'CLIENTE';

// Hash señuelo: si el usuario no existe se compara contra el, de modo que la
// respuesta tarde lo mismo y no se pueda adivinar que cuentas estan dadas de alta.
const hashSeñuelo = generarHash('señuelo-para-igualar-tiempos');

function estaBloqueado(usuario) {
  return usuario.bloqueadoHasta ? new Date(usuario.bloqueadoHasta).getTime() > Date.now() : false;
}

function mensajeBloqueo(usuario) {
  const minutos = Math.max(1, Math.ceil((new Date(usuario.bloqueadoHasta) - Date.now()) / 60000));
  return `Usuario bloqueado temporalmente por intentos fallidos. Intente nuevamente en ${minutos} minuto(s).`;
}

function datosPublicos(usuario) {
  return {
    idUsuario: usuario.id,
    nombreUsuario: usuario.nombreUsuario,
    email: usuario.email ?? null,
    nombreCompleto: usuario.nombreCompleto ?? null,
    rol: usuario.rol,
    ultimoAcceso: usuario.ultimoAcceso ?? null
  };
}

// Abre una sesion: registra el acceso y firma el token. Lo usan el login y el registro.
async function abrirSesion(usuario) {
  await registrarAcceso(usuario.id);

  return {
    ...datosPublicos(usuario),
    autenticado: true,
    token: firmarToken(usuario),
    tipoToken: 'Bearer',
    expiraEn: segundosDeVigencia(),
    expiraEnTexto: variables.JWT_EXPIRES_IN
  };
}

export async function autenticar({ nombreUsuario, contrasena }) {
  const usuario = await buscarUsuarioPorNombre(nombreUsuario);

  if (!usuario) {
    await verificarPassword(contrasena, await hashSeñuelo);
    logger.warn(`Intento de autenticacion con usuario inexistente: "${nombreUsuario}"`);
    throw new ErrorCredenciales();
  }

  if (estaBloqueado(usuario)) {
    throw new ErrorAplicacion(mensajeBloqueo(usuario), {
      estadoHttp: 423,
      codigo: 'USUARIO_BLOQUEADO'
    });
  }

  if (!usuario.activo) {
    logger.warn(`Intento de autenticacion de usuario inactivo: "${nombreUsuario}"`);
    throw new ErrorAplicacion('El usuario esta inactivo. Contacte al administrador.', {
      estadoHttp: 403,
      codigo: 'USUARIO_INACTIVO'
    });
  }

  const coincide = await verificarPassword(contrasena, usuario.passwordHash);

  if (!coincide) {
    await registrarIntentoFallido(usuario.id, MAX_INTENTOS_FALLIDOS, MINUTOS_BLOQUEO);
    logger.warn(`Contrasena incorrecta para el usuario "${nombreUsuario}" (id ${usuario.id}).`);
    throw new ErrorCredenciales();
  }

  if (process.env.REQUIRE_EMAIL_VERIFICATION === 'true' && !usuario.correoVerificado) {
    throw new ErrorAplicacion('Verifica tu correo antes de iniciar sesión.', { estadoHttp: 403, codigo: 'CORREO_PENDIENTE' });
  }
  logger.info(`Autenticacion exitosa del usuario "${nombreUsuario}" (id ${usuario.id}, rol ${usuario.rol}).`);
  return abrirSesion(usuario);
}

export async function registrar({ nombreUsuario, contrasena, email, nombreCompleto, telefono }) {
  if (await buscarUsuarioPorNombre(nombreUsuario)) {
    throw new ErrorAplicacion('El nombre de usuario ya esta registrado.', {
      estadoHttp: 409,
      codigo: 'USUARIO_YA_EXISTE'
    });
  }

  try {
    const passwordHash = await generarHash(contrasena);
    await crearUsuario({ nombreUsuario, passwordHash, email, nombreCompleto, telefono, rol: ROL_REGISTRO });
  } catch (error) {
    // La base impide nombres o emails duplicados; si llegan aqui, ya existe la cuenta.
    if (error.code === 'ER_DUP_ENTRY') {
      throw new ErrorAplicacion('El nombre de usuario o el email ya estan registrados.', {
        estadoHttp: 409,
        codigo: 'USUARIO_YA_EXISTE'
      });
    }
    throw error;
  }

  const nuevo = await buscarUsuarioPorNombre(nombreUsuario);
  logger.info(`Usuario registrado "${nombreUsuario}" (id ${nuevo.id}, rol ${nuevo.rol}).`);
  if (process.env.REQUIRE_EMAIL_VERIFICATION === 'true') {
    return { idUsuario: nuevo.id, verificacionPendiente: true, mensaje: 'Solicita el correo de verificación con /api/cuenta/verificacion/solicitar' };
  }
  return abrirSesion(nuevo);
}