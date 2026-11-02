import bcrypt from 'bcryptjs';
import { variables } from '../config/entorno.js';

export async function generarHash(password) {
  return bcrypt.hash(password, variables.BCRYPT_SALT_ROUNDS);
}

export function verificarPassword(password, hash) {
  return bcrypt.compare(password, hash);
}
