-- =====================================================================
-- Microservicio Login - Esquema de base de datos (MySQL 8+)
--
-- IMPORTANTE:
--   * Este esquema se aplica sobre la base definida en DB_NAME (.env),
--     por defecto `tienda_online`. NO crea una base `ventas_db`.
--   * Las tablas se llaman `rol`, `usuario` y `token_cuenta`, que son las
--     que usa realmente el codigo (src/repositories/usuarios.repository.js).
--   * Es idempotente: se puede ejecutar varias veces sin perder datos.
--
-- Ejecutar con:  npm run db:schema
-- =====================================================================

-- ---------------------------------------------------------------------
-- Tabla: rol
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS rol (
  id_rol INT NOT NULL AUTO_INCREMENT,
  nombre VARCHAR(50) NOT NULL,
  PRIMARY KEY (id_rol),
  UNIQUE KEY uq_rol_nombre (nombre)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- Roles que entiende la aplicacion (login, frontend y API C#).
INSERT INTO rol (nombre) VALUES
  ('Admin'),
  ('Cliente'),
  ('Vendedor'),
  ('Proveedor'),
  ('Repartidor')
ON DUPLICATE KEY UPDATE nombre = VALUES(nombre);

-- ---------------------------------------------------------------------
-- Tabla: usuario
-- El password nunca se guarda en texto plano: se almacena el hash bcrypt.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS usuario (
  id_usuario INT NOT NULL AUTO_INCREMENT,
  id_rol INT NOT NULL,
  nombre VARCHAR(100) NOT NULL,
  correo VARCHAR(150) NOT NULL,
  contrasena_hash VARCHAR(255) NOT NULL,
  fecha_registro TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP,
  nombre_usuario VARCHAR(60) DEFAULT NULL,
  telefono VARCHAR(30) DEFAULT NULL,
  correo_verificado TINYINT(1) NOT NULL DEFAULT 0,
  activo TINYINT(1) NOT NULL DEFAULT 1,
  intentos_fallidos INT NOT NULL DEFAULT 0,
  bloqueado_hasta DATETIME DEFAULT NULL,
  ultimo_acceso DATETIME DEFAULT NULL,
  PRIMARY KEY (id_usuario),
  UNIQUE KEY uq_usuario_correo (correo),
  UNIQUE KEY uq_usuario_login (nombre_usuario),
  KEY idx_usuario_rol (id_rol),
  CONSTRAINT fk_usuario_rol FOREIGN KEY (id_rol) REFERENCES rol (id_rol)
    ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Tabla: token_cuenta
-- Tokens de un solo uso para recuperacion de contrasena y verificacion
-- de correo. La emite y consume la API C# (CuentasController).
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS token_cuenta (
  id BIGINT NOT NULL AUTO_INCREMENT,
  id_usuario INT NOT NULL,
  tipo VARCHAR(30) NOT NULL,
  hash_token CHAR(64) NOT NULL,
  expira_en DATETIME NOT NULL,
  usado_en DATETIME DEFAULT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_token_hash (hash_token),
  KEY idx_token_usuario (id_usuario, tipo),
  CONSTRAINT fk_token_usuario FOREIGN KEY (id_usuario) REFERENCES usuario (id_usuario)
    ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- Nota: el resto del esquema (libro, venta, carrito, etc.) vive en la misma
-- base `tienda_online` y se restaura desde backups/tienda_online.sql.
