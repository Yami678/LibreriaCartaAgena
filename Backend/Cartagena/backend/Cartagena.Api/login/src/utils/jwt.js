import jwt from 'jsonwebtoken';
import { variables } from '../config/entorno.js';

export function firmarToken(datosUsuario) {
  const payload = {
    sub: String(datosUsuario.id),
    nombreUsuario: datosUsuario.nombreUsuario,
    rol: datosUsuario.rol,
    email: datosUsuario.email ?? null
  };

  return jwt.sign(payload, variables.JWT_SECRET, {
    expiresIn: variables.JWT_EXPIRES_IN,
    issuer: variables.JWT_ISSUER
  });
}

export function verificarToken(token) {
  return jwt.verify(token, variables.JWT_SECRET, { issuer: variables.JWT_ISSUER });
}

export function segundaADatos(valor, predeterminado = 900) {
  const coincidencia = /^(\d+)\s*(s|m|h|d)?$/i.exec(String(valor).trim());
  if (!coincidencia) return predeterminado;

  const cantidad = Number(coincidencia[1]);
  const factor = { s: 1, m: 60, h: 3600, d: 86400 }[coincidencia[2]?.toLowerCase() ?? 's'] ?? 1;
  return cantidad * factor;
}

export const segundosDeVigencia = () => segundaADatos(variables.JWT_EXPIRES_IN);
