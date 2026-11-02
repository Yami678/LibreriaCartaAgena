using Dapper;
using MySqlConnector;

// Migración no destructiva, idempotente y SOLO para tienda_online.
// Ejecutar con usuario administrativo temporal, nunca con usuario_app.
const string schema="tienda_online";
if (Environment.GetEnvironmentVariable("DB_NAME") is { } dbName && dbName!=schema)
    throw new InvalidOperationException("La migración solo admite tienda_online");
var cs=new MySqlConnectionStringBuilder
{
    Server=Environment.GetEnvironmentVariable("DB_HOST")??"mysql",
    Port=uint.Parse(Environment.GetEnvironmentVariable("DB_PORT")??"3306"),
    Database=schema,
    UserID=Environment.GetEnvironmentVariable("DB_USER")??throw new InvalidOperationException("Falta DB_USER"),
    Password=Environment.GetEnvironmentVariable("DB_PASSWORD")??throw new InvalidOperationException("Falta DB_PASSWORD"),
    SslMode=MySqlSslMode.Preferred
};
await using var cn=new MySqlConnection(cs.ConnectionString);
await cn.OpenAsync();

var additions=new Dictionary<string,Dictionary<string,string>>
{
    ["usuario"]=new() { ["nombre_usuario"]="VARCHAR(60) NULL",["telefono"]="VARCHAR(30) NULL",
      ["correo_verificado"]="BOOLEAN NOT NULL DEFAULT FALSE",["activo"]="BOOLEAN NOT NULL DEFAULT TRUE",
      ["intentos_fallidos"]="INT NOT NULL DEFAULT 0",["bloqueado_hasta"]="DATETIME NULL",["ultimo_acceso"]="DATETIME NULL" },
    ["libro"]=new() { ["isbn"]="VARCHAR(17) NULL",["editorial"]="VARCHAR(150) NULL",
      ["categoria"]="VARCHAR(100) NULL",["genero"]="VARCHAR(100) NULL",["formato"]="VARCHAR(50) NULL",
      ["imagen_portada"]="VARCHAR(500) NULL",["descuento_porcentaje"]="DECIMAL(5,2) NOT NULL DEFAULT 0",
      ["pdf_ruta"]="VARCHAR(255) NULL" },
    ["proveedor"]=new() { ["razon_social"]="VARCHAR(150) NULL",["rfc"]="VARCHAR(13) NULL",
      ["correo"]="VARCHAR(150) NULL",["direccion"]="VARCHAR(255) NULL" },
    ["domicilio"]=new() { ["calle"]="VARCHAR(150) NULL",["numero"]="VARCHAR(20) NULL",
      ["colonia"]="VARCHAR(120) NULL",["telefono"]="VARCHAR(30) NULL" },
    ["venta"]=new() { ["estado"]="VARCHAR(30) NOT NULL DEFAULT 'Pendiente'",
      ["estado_pago"]="VARCHAR(30) NOT NULL DEFAULT 'Pendiente'",["referencia_pago"]="VARCHAR(120) NULL",
      ["clave_idempotencia"]="VARCHAR(100) NULL" },
    ["envio"]=new() { ["numero_guia"]="VARCHAR(100) NULL",["fecha_entrega"]="DATETIME NULL",
      ["confirmado_cliente"]="BOOLEAN NOT NULL DEFAULT FALSE" },
    ["pedido_compra"]=new() { ["estado"]="VARCHAR(30) NOT NULL DEFAULT 'Pendiente'",
      ["fecha_recepcion"]="DATETIME NULL" }
};

foreach(var (table, columns) in additions)
{
    var count=await cn.ExecuteScalarAsync<int>("""
      SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=@schema AND table_name=@table
      """,new {schema,table});
    if (count!=1) throw new InvalidOperationException($"Falta tabla oficial {table}. Importa primero el esquema oficial.");
    foreach(var (column, definition) in columns)
    {
        var exists=await cn.ExecuteScalarAsync<int>("""
          SELECT COUNT(*) FROM information_schema.columns
          WHERE table_schema=@schema AND table_name=@table AND column_name=@column
          """,new {schema,table,column});
        if(exists>0) continue;
        // Nombres y tipos son constantes internas, no provienen del usuario.
        var statement=$"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}";
        Console.WriteLine(statement);
        await cn.ExecuteAsync(statement);
    }
}
var sqls=new[]
{
    """
      CREATE TABLE IF NOT EXISTS token_cuenta (
      id BIGINT AUTO_INCREMENT PRIMARY KEY,id_usuario INT NOT NULL,tipo VARCHAR(30) NOT NULL,
      hash_token CHAR(64) NOT NULL UNIQUE,expira_en DATETIME NOT NULL,usado_en DATETIME NULL,
      FOREIGN KEY(id_usuario) REFERENCES usuario(id_usuario) ON DELETE CASCADE,
      INDEX idx_token_usuario(id_usuario,tipo)) ENGINE=InnoDB
    """,
    """
      CREATE TABLE IF NOT EXISTS solicitud_garantia (
      id_solicitud INT AUTO_INCREMENT PRIMARY KEY,id_venta INT NOT NULL,id_usuario INT NOT NULL,
      id_libro VARCHAR(50) NOT NULL,motivo TEXT NOT NULL,estado VARCHAR(30) NOT NULL DEFAULT 'Solicitada',
      fecha TIMESTAMP DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(id_venta) REFERENCES venta(id_venta),
      FOREIGN KEY(id_usuario) REFERENCES usuario(id_usuario),
      FOREIGN KEY(id_libro) REFERENCES libro(id_libro)) ENGINE=InnoDB
    """,
    """
      CREATE TABLE IF NOT EXISTS movimiento_inventario (
      id_movimiento BIGINT AUTO_INCREMENT PRIMARY KEY,id_libro VARCHAR(50) NOT NULL,cantidad INT NOT NULL,
      tipo VARCHAR(30) NOT NULL,referencia VARCHAR(100) NOT NULL,fecha TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      UNIQUE KEY uq_movimiento_ref(tipo,referencia,id_libro),
      FOREIGN KEY(id_libro) REFERENCES libro(id_libro)) ENGINE=InnoDB
    """,
    """
      CREATE TABLE IF NOT EXISTS devolucion_proveedor (
      id_devolucion BIGINT AUTO_INCREMENT PRIMARY KEY,id_proveedor INT NOT NULL,id_libro VARCHAR(50) NOT NULL,
      cantidad INT NOT NULL,motivo TEXT NOT NULL,referencia VARCHAR(100) NOT NULL UNIQUE,
      fecha TIMESTAMP DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(id_proveedor) REFERENCES proveedor(id_proveedor),
      FOREIGN KEY(id_libro) REFERENCES libro(id_libro)) ENGINE=InnoDB
    """,
    """
      CREATE TABLE IF NOT EXISTS paquete (
      id_paquete BIGINT AUTO_INCREMENT PRIMARY KEY,id_venta INT NOT NULL UNIQUE,
      peso_kg DECIMAL(8,2) NOT NULL,largo_cm DECIMAL(8,2) NOT NULL,
      ancho_cm DECIMAL(8,2) NOT NULL,alto_cm DECIMAL(8,2) NOT NULL,
      estado VARCHAR(40) NOT NULL DEFAULT 'Preparando',id_usuario_empaqueto INT NULL,
      fecha_creacion TIMESTAMP DEFAULT CURRENT_TIMESTAMP,fecha_actualizacion DATETIME NULL,
      FOREIGN KEY(id_venta) REFERENCES venta(id_venta) ON DELETE CASCADE,
      FOREIGN KEY(id_usuario_empaqueto) REFERENCES usuario(id_usuario)) ENGINE=InnoDB
    """
};
foreach(var sql in sqls) await cn.ExecuteAsync(sql);

foreach(var (table,index,column) in new[] {
    ("venta","uq_venta_idempotencia","clave_idempotencia"),
    ("usuario","uq_usuario_login","nombre_usuario")})
{
    var exists=await cn.ExecuteScalarAsync<int>("""
      SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=@schema
      AND table_name=@table AND index_name=@index
      """,new {schema,table,index});
    if(exists==0) await cn.ExecuteAsync($"CREATE UNIQUE INDEX `{index}` ON `{table}` (`{column}`)");
}
await cn.ExecuteAsync("INSERT IGNORE INTO rol(nombre) VALUES ('Repartidor')");
await cn.ExecuteAsync("INSERT IGNORE INTO tipo_pago(nombre) VALUES ('Efectivo contra entrega')");
Console.WriteLine("Migración terminada. Comprueba DESCRIBE libro; y SHOW TABLES;");
