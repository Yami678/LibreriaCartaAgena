import { ZodError } from 'zod';
import { ErrorAplicacion } from '../utils/errores.js';
import { variables } from '../config/entorno.js';
import { logger } from '../utils/logger.js';

function construirCuerpoError(res, { estadoHttp, codigo, mensaje, detalles }) {
  res.status(estadoHttp).json({
    exito: false,
    codigo,
    mensaje,
    ...(detalles ? { detalles } : {}),
    marcaTiempo: new Date().toISOString()
  });
}

export function noEncontrado(req, res) {
  res.status(404).json({
    exito: false,
    codigo: 'RUTA_NO_ENCONTRADA',
    mensaje: `No existe el recurso ${req.method} ${req.originalUrl}`,
    marcaTiempo: new Date().toISOString()
  });
}

export function manejadorDeErrores(error, req, res, next) {
  if (res.headersSent) return next(error);

  if (error instanceof ZodError) {
    return construirCuerpoError(res, {
      estadoHttp: 422,
      codigo: 'DATOS_INVALIDOS',
      mensaje: 'Los datos enviados no cumplen el formato requerido',
      detalles: error.issues.map((problema) => ({
        campo: problema.path.join('.') || '(raiz)',
        problema: problema.message
      }))
    });
  }

  if (error instanceof ErrorAplicacion) {
    if (error.estadoHttp >= 500) {
      logger.error(`${error.codigo}: ${error.message}`);
    }
    return construirCuerpoError(res, {
      estadoHttp: error.estadoHttp,
      codigo: error.codigo,
      mensaje: error.message,
      detalles: error.detalles
    });
  }

  if (error.type === 'entity.parse.failed') {
    return construirCuerpoError(res, {
      estadoHttp: 400,
      codigo: 'JSON_MAL_FORMADO',
      mensaje: 'El cuerpo de la peticion no es un JSON valido'
    });
  }

  logger.error(`Error no controlado en ${req.method} ${req.originalUrl}: ${error.stack ?? error.message}`);

  return construirCuerpoError(res, {
    estadoHttp: 500,
    codigo: 'ERROR_INTERNO',
    mensaje: 'Ocurrio un error inesperado al procesar la solicitud',
    detalles: variables.NODE_ENV === 'development' ? { causa: error.message } : undefined
  });
}
