using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

/// <summary>
/// Módulo Usuarios y roles: alta, consulta, edición, asignación de rol y
/// control de cuentas (activar/desactivar y restablecer contraseña).
/// </summary>
[Authorize(Policy = "Admin")]
public sealed class UsuariosController(DbFactory db) : ApiControllerBase(db)
{
    [HttpGet("api/admin/roles")]
    public async Task<IActionResult> Roles()
    {
        await using var cn = await Db.OpenAsync();
        var rows = await cn.QueryAsync("""
          SELECT id_rol, nombre FROM rol ORDER BY nombre
          """);
        return Ok(new { data = rows });
    }

    [HttpGet("api/admin/usuarios")]
    public async Task<IActionResult> Listar()
    {
        await using var cn = await Db.OpenAsync();
        var rows = await cn.QueryAsync("""
          SELECT u.id_usuario, u.nombre, u.nombre_usuario, u.correo, u.telefono,
                 u.activo, u.correo_verificado, u.fecha_registro, u.ultimo_acceso,
                 UPPER(r.nombre) AS rol
          FROM usuario u JOIN rol r ON r.id_rol = u.id_rol
          ORDER BY u.id_usuario
          """);
        return Ok(new { data = rows });
    }

    [HttpPost("api/admin/usuarios")]
    public async Task<IActionResult> Crear([FromBody] UsuarioCrearDto d)
    {
        Ensure(Validaciones.ValidarNombre(d.Nombre) is null, Validaciones.ValidarNombre(d.Nombre) ?? "");
        Ensure(Validaciones.ValidarUsuario(d.Nombre_Usuario) is null, Validaciones.ValidarUsuario(d.Nombre_Usuario) ?? "");
        Ensure(Validaciones.ValidarCorreo(d.Correo) is null, Validaciones.ValidarCorreo(d.Correo) ?? "");
        Ensure(Validaciones.ValidarTelefono(d.Telefono) is null, Validaciones.ValidarTelefono(d.Telefono) ?? "");
        Ensure(Validaciones.ValidarContrasena(d.Contrasena) is null, Validaciones.ValidarContrasena(d.Contrasena) ?? "");

        var usuario = d.Nombre_Usuario.Trim();
        var correo = d.Correo.Trim().ToLowerInvariant();
        var telefono = Validaciones.SoloDigitos(d.Telefono);

        await using var cn = await Db.OpenAsync();
        var rolId = await ResolverRolAsync(cn, d.Rol);
        var duplicado = await cn.QueryFirstOrDefaultAsync<string>("""
          SELECT CASE
            WHEN EXISTS(SELECT 1 FROM usuario WHERE nombre_usuario=@usuario) THEN 'usuario'
            WHEN EXISTS(SELECT 1 FROM usuario WHERE correo=@correo) THEN 'correo'
          END
          """, new { usuario, correo });
        Ensure(duplicado is null, duplicado == "usuario"
            ? "El nombre de usuario ya está registrado"
            : "El correo ya está registrado", 409);

        var hash = BCrypt.Net.BCrypt.HashPassword(d.Contrasena);
        await cn.ExecuteAsync("""
          INSERT INTO usuario(nombre, nombre_usuario, correo, contrasena_hash, telefono,
                              id_rol, correo_verificado, activo)
          VALUES (@nombre, @usuario, @correo, @hash, @telefono, @rol, TRUE, @activo)
          """, new
        {
            nombre = d.Nombre.Trim(),
            usuario,
            correo,
            hash,
            telefono,
            rol = rolId,
            activo = d.Activo
        });

        var id = await cn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
        return StatusCode(201, new { id_usuario = id });
    }

    [HttpPatch("api/admin/usuarios/{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] UsuarioActualizarDto d)
    {
        Ensure(Validaciones.ValidarNombre(d.Nombre) is null, Validaciones.ValidarNombre(d.Nombre) ?? "");
        Ensure(Validaciones.ValidarUsuario(d.Nombre_Usuario) is null, Validaciones.ValidarUsuario(d.Nombre_Usuario) ?? "");
        Ensure(Validaciones.ValidarCorreo(d.Correo) is null, Validaciones.ValidarCorreo(d.Correo) ?? "");
        if (!string.IsNullOrWhiteSpace(d.Telefono))
            Ensure(Validaciones.ValidarTelefono(d.Telefono) is null, Validaciones.ValidarTelefono(d.Telefono) ?? "");

        var usuario = d.Nombre_Usuario.Trim();
        var correo = d.Correo.Trim().ToLowerInvariant();
        var telefono = Validaciones.SoloDigitos(d.Telefono);

        await using var cn = await Db.OpenAsync();
        var actual = await cn.QueryFirstOrDefaultAsync<string>("""
          SELECT UPPER(r.nombre) FROM usuario u JOIN rol r ON r.id_rol=u.id_rol
          WHERE u.id_usuario=@id
          """, new { id });
        Ensure(actual is not null, "Usuario no encontrado", 404);

        var rolId = await ResolverRolAsync(cn, d.Rol);
        var rolNombre = await cn.ExecuteScalarAsync<string>("SELECT UPPER(nombre) FROM rol WHERE id_rol=@rolId", new { rolId });

        if (id == UserId)
        {
            Ensure(d.Activo, "No puedes desactivar tu propia cuenta");
            Ensure(rolNombre == "ADMIN", "No puedes quitarte el rol de administrador");
        }

        var duplicado = await cn.QueryFirstOrDefaultAsync<string>("""
          SELECT CASE
            WHEN EXISTS(SELECT 1 FROM usuario WHERE nombre_usuario=@usuario AND id_usuario<>@id) THEN 'usuario'
            WHEN EXISTS(SELECT 1 FROM usuario WHERE correo=@correo AND id_usuario<>@id) THEN 'correo'
          END
          """, new { usuario, correo, id });
        Ensure(duplicado is null, duplicado == "usuario"
            ? "El nombre de usuario ya está registrado"
            : "El correo ya está registrado", 409);

        await cn.ExecuteAsync("""
          UPDATE usuario SET nombre=@nombre, nombre_usuario=@usuario, correo=@correo,
                 telefono=@telefono, id_rol=@rol, activo=@activo
          WHERE id_usuario=@id
          """, new
        {
            nombre = d.Nombre.Trim(),
            usuario,
            correo,
            telefono,
            rol = rolId,
            activo = d.Activo,
            id
        });
        return Ok(new { ok = true });
    }

    [HttpPost("api/admin/usuarios/{id:int}/contrasena")]
    public async Task<IActionResult> RestablecerContrasena(int id, [FromBody] ContrasenaAdminDto d)
    {
        Ensure(Validaciones.ValidarContrasena(d.Contrasena) is null, Validaciones.ValidarContrasena(d.Contrasena) ?? "");
        await using var cn = await Db.OpenAsync();
        var existe = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario WHERE id_usuario=@id", new { id });
        Ensure(existe == 1, "Usuario no encontrado", 404);
        var hash = BCrypt.Net.BCrypt.HashPassword(d.Contrasena);
        await cn.ExecuteAsync("""
          UPDATE usuario SET contrasena_hash=@hash, intentos_fallidos=0, bloqueado_hasta=NULL
          WHERE id_usuario=@id
          """, new { hash, id });
        return Ok(new { ok = true });
    }

    private static async Task<int> ResolverRolAsync(MySqlConnector.MySqlConnection cn, string rol)
    {
        var id = await cn.QueryFirstOrDefaultAsync<int?>(
            "SELECT id_rol FROM rol WHERE UPPER(nombre)=UPPER(@rol)", new { rol = (rol ?? "").Trim() });
        Ensure(id is not null, "Rol inválido");
        return id!.Value;
    }
}
