import jwt from 'jsonwebtoken';
import { verificarToken } from '../utils/jwt.js';
import { ErrorNoAutorizado } from '../utils/errores.js';

function extraerToken(cabecera) {
  if (!cabecera) return null;

  const [esquema, valor] = cabecera.split(' ');

  if (esquema?.toLowerCase() === 'bearer' && valor) return valor.trim();
  return null;
}

export function requiereAutenticacion(req, res, next) {
  const token = extraerToken(req.headers.authorization);

  if (!token) {
    return next(
      new ErrorNoAutorizado('Falta la cabecera Authorization. Formato esperado: Authorization: Bearer <token>')
    );
  }

  try {
    req.usuario = verificarToken(token);
    return next();
  } catch (error) {
    if (error instanceof jwt.TokenExpiredError) {
      return next(new ErrorNoAutorizado('El token ha expirado. Inicie sesion nuevamente.'));
    }
    return next(new ErrorNoAutorizado('El token proporcionado no es valido.'));
  }
}

export function requiereRol(...rolesPermitidos) {
  return (req, res, next) => {
    if (!req.usuario) {
      return next(new ErrorNoAutorizado());
    }

    if (!rolesPermitidos.includes(req.usuario.rol)) {
      return res.status(403).json({
        exito: false,
        codigo: 'ACCESO_DENEGADO',
        mensaje: 'Su rol no tiene permisos para acceder a este recurso',
        detalles: { rolRequerido: rolesPermitidos, rolActual: req.usuario.rol }
      });
    }

    return next();
  };
}
