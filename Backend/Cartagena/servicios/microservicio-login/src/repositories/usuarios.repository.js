import { pool } from '../config/baseDatos.js';
import { ErrorBaseDatos } from '../utils/errores.js';

const CAMPOS_USUARIO = `
  u.id_usuario AS id,
  u.nombre_usuario AS nombreUsuario,
  u.contrasena_hash AS passwordHash,
  u.correo AS email,
  u.nombre AS nombreCompleto,
  u.telefono AS telefono,
  u.correo_verificado AS correoVerificado,
  u.activo AS activo,
  u.intentos_fallidos AS intentosFallidos,
  u.bloqueado_hasta AS bloqueadoHasta,
  u.ultimo_acceso AS ultimoAcceso,
  UPPER(r.nombre) AS rol
`;

export async function buscarUsuarioPorNombre(nombreUsuario) {
  try {
    const [rows] = await pool.execute(`SELECT ${CAMPOS_USUARIO} FROM usuario u
      JOIN rol r ON r.id_rol=u.id_rol
      WHERE u.nombre_usuario=? OR u.correo=? LIMIT 1`, [nombreUsuario, nombreUsuario]);
    return rows[0] ?? null;
  } catch(e) { throw e; }
}

/** Comprueba si un nombre de usuario ya existe (para el registro y las sugerencias). */
export async function existeNombreUsuario(nombreUsuario) {
  const [rows] = await pool.execute(
    'SELECT 1 FROM usuario WHERE nombre_usuario=? LIMIT 1',
    [nombreUsuario]
  );
  return rows.length > 0;
}

export async function registrarAcceso(idUsuario) {
  await pool.execute('UPDATE usuario SET ultimo_acceso=NOW(), intentos_fallidos=0, bloqueado_hasta=NULL WHERE id_usuario=?',[idUsuario]);
}

export async function registrarIntentoFallido(idUsuario,maxIntentos,minutosBloqueo) {
  await pool.execute('UPDATE usuario SET intentos_fallidos=intentos_fallidos+1 WHERE id_usuario=?',[idUsuario]);
  await pool.execute(`UPDATE usuario SET bloqueado_hasta=DATE_ADD(NOW(),INTERVAL ? MINUTE)
   WHERE id_usuario=? AND intentos_fallidos>=?`,[minutosBloqueo,idUsuario,maxIntentos]);
}

export async function crearUsuario({nombreUsuario,passwordHash,email,nombreCompleto,telefono,rol}) {
  const [result] = await pool.execute(`INSERT INTO usuario
    (nombre_usuario,contrasena_hash,correo,nombre,telefono,id_rol,correo_verificado)
    SELECT ?,?,?,?,?,id_rol,FALSE FROM rol WHERE UPPER(nombre)=?`,
    [nombreUsuario,passwordHash,email,nombreCompleto,telefono,rol]);
  if (!result.affectedRows) throw new Error('Rol no configurado');
  return result.insertId;
}
