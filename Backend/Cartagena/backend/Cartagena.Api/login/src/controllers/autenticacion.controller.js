import { z } from 'zod';
import { autenticar, registrar } from '../services/autenticacion.service.js';
import { variables } from '../config/entorno.js';
import { existeNombreUsuario } from '../repositories/usuarios.repository.js';

// Reglas compartidas con el frontend (Frontend/src/utils/validacion.ts).
const NOMBRE_COMPLETO = /^[\p{L}][\p{L}\s'.-]{1,149}$/u;
const NOMBRE_USUARIO = /^[A-Za-z0-9][A-Za-z0-9._-]{2,59}$/;
const CORREO_COM = /^[^\s@]+@[^\s@]+\.com(\.[a-z]{2,})?$/i;
const CONTRASENA_FUERTE = /^(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9\s]).{8,200}$/;

const mensajeContrasena =
  'La contraseña debe tener al menos 8 caracteres, una mayúscula, un número y un carácter especial';

function soloDigitos(valor) {
  return String(valor ?? '').replace(/\D/g, '');
}

function normalizarBase(valor) {
  return String(valor ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9._-]/g, '');
}

const esquemaCredenciales = z.object({
  nombreUsuario: z
    .string({ error: 'El nombre de usuario es obligatorio' })
    .trim()
    .min(3, 'El nombre de usuario debe tener al menos 3 caracteres')
    .max(60, 'El nombre de usuario no puede superar 60 caracteres'),
  contrasena: z
    .string({ error: 'La contrasena es obligatoria' })
    .min(1, 'La contrasena es obligatoria')
    .max(200, 'La contrasena no puede superar 200 caracteres')
});

const esquemaRegistro = z.object({
  nombreUsuario: z
    .string({ error: 'El nombre de usuario es obligatorio' })
    .trim()
    .min(3, 'El nombre de usuario debe tener al menos 3 caracteres')
    .max(60, 'El nombre de usuario no puede superar 60 caracteres')
    .regex(
      NOMBRE_USUARIO,
      'Usa entre 3 y 60 caracteres: letras, números, punto, guion o guion bajo; debe iniciar con letra o número'
    ),
  contrasena: z
    .string({ error: 'La contrasena es obligatoria' })
    .min(8, 'La contrasena debe tener al menos 8 caracteres')
    .max(200, 'La contrasena no puede superar 200 caracteres')
    .regex(CONTRASENA_FUERTE, mensajeContrasena),
  email: z
    .string({ error: 'El email debe ser una cadena de texto' })
    .trim()
    .email('El email no tiene un formato valido')
    .max(120, 'El email no puede superar 120 caracteres')
    .regex(CORREO_COM, 'El correo debe incluir @ y terminar en .com (por ejemplo, nombre@dominio.com)'),
  nombreCompleto: z
    .string({ error: 'El nombre completo debe ser una cadena de texto' })
    .trim()
    .min(2, 'El nombre completo debe tener al menos 2 caracteres')
    .max(150, 'El nombre completo no puede superar 150 caracteres')
    .regex(NOMBRE_COMPLETO, 'El nombre completo solo puede contener letras, espacios, apóstrofes, puntos y guiones'),
  telefono: z
    .string({ error: 'El teléfono es obligatorio' })
    .transform(soloDigitos)
    .refine((valor) => valor.length === 10, 'El teléfono debe tener exactamente 10 dígitos')
});

export async function iniciarSesion(req, res) {
  const credenciales = esquemaCredenciales.parse({
    nombreUsuario: req.body?.nombreUsuario,
    contrasena: req.body?.contrasena
  });

  const resultado = await autenticar(credenciales);

  res.status(200).json({
    exito: true,
    operacion: 'AUTENTICAR_USUARIO',
    mensaje: 'Autenticacion exitosa',
    datos: resultado
  });
}

export async function registrarUsuario(req, res) {
  if (process.env.RECAPTCHA_ENABLED === 'true') {
    if (!process.env.RECAPTCHA_SECRET) {
      throw new Error('RECAPTCHA_ENABLED requiere RECAPTCHA_SECRET');
    }
    const captcha = String(req.body?.captchaToken || '');
    if (!captcha) {
      return res.status(400).json({exito:false,mensaje:'Completa la verificación anti-bot'});
    }
    const payload = new URLSearchParams({secret:process.env.RECAPTCHA_SECRET,response:captcha});
    let verified = false;
    try {
      const answer = await fetch('https://www.google.com/recaptcha/api/siteverify',
        {method:'POST',body:payload,signal:AbortSignal.timeout(5000)});
      const json = await answer.json();
      verified = json.success === true;
    } catch { verified = false; }
    if (!verified) return res.status(403).json({exito:false,mensaje:'Verificación anti-bot inválida'});
  }

  const datos = esquemaRegistro.parse({
    nombreUsuario: req.body?.nombreUsuario,
    contrasena: req.body?.contrasena,
    email: req.body?.email ?? undefined,
    nombreCompleto: req.body?.nombreCompleto ?? undefined,
    telefono: req.body?.telefono ?? undefined
  });

  const resultado = await registrar(datos);

  res.status(201).json({
    exito: true,
    operacion: 'REGISTRAR_USUARIO',
    mensaje: 'Cuenta creada correctamente',
    datos: resultado
  });
}

export async function consultarDisponibilidadUsuario(req, res) {
  const nombre = String(req.query.nombre ?? '').trim();
  const nombreCompleto = String(req.query.nombreCompleto ?? '').trim();

  if (!NOMBRE_USUARIO.test(nombre)) {
    return res.status(200).json({
      exito: true,
      operacion: 'DISPONIBILIDAD_USUARIO',
      mensaje: 'El nombre de usuario no cumple el formato',
      datos: { valido: false, disponible: false, sugerencias: [] }
    });
  }

  const ocupado = await existeNombreUsuario(nombre);
  if (!ocupado) {
    return res.status(200).json({
      exito: true,
      operacion: 'DISPONIBILIDAD_USUARIO',
      mensaje: 'Nombre de usuario disponible',
      datos: { valido: true, disponible: true, sugerencias: [] }
    });
  }

  const base = normalizarBase(nombre).replace(/[0-9._-]+$/g, '') || normalizarBase(nombre);
  const candidatos = new Set();
  for (let i = 1; i <= 9; i += 1) candidatos.add(`${base}${i}`);

  const partes = nombreCompleto
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .split(/\s+/)
    .map((parte) => parte.replace(/[^a-z0-9]/g, ''))
    .filter(Boolean);
  if (partes.length >= 2) {
    candidatos.add(`${partes[0]}.${partes[partes.length - 1]}`);
    candidatos.add(`${partes[0]}_${partes[partes.length - 1]}`);
  }
  candidatos.add(`${base}.${new Date().getFullYear() % 100}`);
  candidatos.add(`${base}_lector`);

  const sugerencias = [];
  for (const candidato of candidatos) {
    const limpio = candidato.slice(0, 60);
    if (!NOMBRE_USUARIO.test(limpio) || sugerencias.includes(limpio)) continue;
    if (await existeNombreUsuario(limpio)) continue;
    sugerencias.push(limpio);
    if (sugerencias.length === 3) break;
  }

  return res.status(200).json({
    exito: true,
    operacion: 'DISPONIBILIDAD_USUARIO',
    mensaje: 'El nombre de usuario ya está registrado',
    datos: { valido: true, disponible: false, sugerencias }
  });
}

export function verificarToken(req, res) {
  res.status(200).json({
    exito: true,
    operacion: 'VERIFICAR_TOKEN',
    mensaje: 'Token valido',
    datos: {
      autenticado: true,
      idUsuario: Number(req.usuario.sub),
      nombreUsuario: req.usuario.nombreUsuario,
      rol: req.usuario.rol,
      email: req.usuario.email ?? null
    }
  });
}

export function consultarPerfil(req, res) {
  res.status(200).json({
    exito: true,
    operacion: 'CONSULTAR_PERFIL',
    mensaje: 'Perfil obtenido correctamente',
    datos: {
      idUsuario: Number(req.usuario.sub),
      nombreUsuario: req.usuario.nombreUsuario,
      rol: req.usuario.rol,
      email: req.usuario.email ?? null
    }
  });
}

export function estadoServicio(req, res) {
  res.status(200).json({
    exito: true,
    operacion: 'ESTADO_SERVICIO',
    mensaje: 'Microservicio de login operativo',
    datos: {
      servicio: 'microservicio-login',
      version: '1.0.0',
      entorno: variables.NODE_ENV,
      hora: new Date().toISOString()
    }
  });
}
