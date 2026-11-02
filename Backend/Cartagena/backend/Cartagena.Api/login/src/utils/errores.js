export class ErrorAplicacion extends Error {
  constructor(mensaje, { estadoHttp = 500, codigo = 'ERROR_INTERNO', detalles = null } = {}) {
    super(mensaje);
    this.name = 'ErrorAplicacion';
    this.estadoHttp = estadoHttp;
    this.codigo = codigo;
    this.detalles = detalles;
    this.esOperacional = true;
    Error.captureStackTrace(this, this.constructor);
  }
}

export class ErrorCredenciales extends ErrorAplicacion {
  constructor(mensaje = 'Credenciales invalidas') {
    super(mensaje, { estadoHttp: 401, codigo: 'CREDENCIALES_INVALIDAS' });
    this.name = 'ErrorCredenciales';
  }
}

export class ErrorNoAutorizado extends ErrorAplicacion {
  constructor(mensaje = 'No cuenta con un token de acceso valido') {
    super(mensaje, { estadoHttp: 401, codigo: 'TOKEN_INVALIDO' });
    this.name = 'ErrorNoAutorizado';
  }
}

export class ErrorBaseDatos extends ErrorAplicacion {
  constructor(mensaje = 'No fue posible conectar con la base de datos') {
    super(mensaje, { estadoHttp: 503, codigo: 'BASE_DATOS_NO_DISPONIBLE' });
    this.name = 'ErrorBaseDatos';
  }
}
