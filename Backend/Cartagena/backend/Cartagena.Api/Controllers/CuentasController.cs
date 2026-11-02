using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Cartagena.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[Route("api/cuenta")]
public sealed class CuentasController(DbFactory db,MailService mail,ILogger<CuentasController> logger) : ApiControllerBase(db)
{
    [HttpPost("verificacion/solicitar")]
    public async Task<IActionResult> SolicitarVerificacion([FromBody] CorreoDto d)
    {
        Ensure(!string.IsNullOrWhiteSpace(d.Correo),"Correo obligatorio");
        await using var cn=await Db.OpenAsync();
        var user=await cn.QueryFirstOrDefaultAsync<UserVerify>("SELECT id_usuario,correo,correo_verificado FROM usuario WHERE correo=@correo",new {correo=d.Correo});
        if (user is not null && !user.correo_verificado)
        {
            var token=await IssueTokenAsync(cn,user.id_usuario,"verificacion",60);
            // Sin frontend: los correos incluyen el token para que se envíe por HTTP.
            var enviado=await mail.SendAsync(user.correo,"Verifica tu correo en Cartagena",
                "Token de verificación (válido 60 minutos): "+token+"\nEnvía POST /api/cuenta/verificacion/confirmar con {\"token\":\"...\"}.");
            if (!enviado)
                logger.LogWarning("Verificación sin correo (SMTP no configurado o con error). Token {Token} para {Correo} (válido 60 min).",token,user.correo);
        }
        return Ok(new {mensaje="Si corresponde, enviaremos instrucciones"});
    }

    [HttpPost("verificacion/confirmar")]
    public async Task<IActionResult> ConfirmarVerificacion([FromBody] TokenDto d)
    {
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var uid=await ConsumeTokenAsync(cn,tx,d.Token,"verificacion");
        await cn.ExecuteAsync("UPDATE usuario SET correo_verificado=TRUE WHERE id_usuario=@uid",new {uid},tx);
        await tx.CommitAsync();
        return Ok(new {ok=true});
    }

    [HttpPost("recuperacion/solicitar")]
    public async Task<IActionResult> SolicitarRecuperacion([FromBody] CorreoDto d)
    {
        Ensure(!string.IsNullOrWhiteSpace(d.Correo),"Correo obligatorio");
        var dato=d.Correo.Trim();
        await using var cn=await Db.OpenAsync();
        // El formulario admite correo o nombre de usuario.
        var user=await cn.QueryFirstOrDefaultAsync<RecoveryUser>("""
          SELECT id_usuario,correo FROM usuario
          WHERE correo=@dato OR nombre_usuario=@dato
          LIMIT 1
          """,new {dato});
        // Respuesta idéntica exista o no la cuenta: no revelamos quién está registrado.
        if (user is null) return Ok(new {mensaje="Si el correo existe, enviaremos instrucciones"});

        var token=await IssueTokenAsync(cn,user.id_usuario,"recuperacion",20);
        var enviado=await mail.SendAsync(user.correo,"Recupera tu contraseña en Cartagena",
            "Hola 👋\n\nTu token de recuperación (válido 20 minutos) es:\n\n"+token+
            "\n\nRestablece tu contraseña en /restablecer-password?token="+token+
            "\n\nSi no pediste este correo, ignóralo.");

        if (!enviado)
        {
            // Sin SMTP no hay correo: avisamos en lugar de fingir un éxito.
            // En desarrollo el token queda en el log para poder probar el restablecimiento.
            logger.LogWarning("Recuperación sin correo (SMTP no configurado o con error). Token {Token} para {Correo} (válido 20 min).",token,user.correo);
            return StatusCode(503,new {
                codigo="SMTP_NO_CONFIGURADO_O_ERROR",
                estado="SMTP_NO_CONFIGURADO_O_ERROR",
                mensaje="El servicio de correo no está configurado, así que no pudimos enviar el token. Completa SMTP_HOST, SMTP_USER, SMTP_PASSWORD y SMTP_FROM en Backend/Cartagena/.env y reinicia el backend."
            });
        }
        return Ok(new {mensaje="Si el correo existe, enviaremos instrucciones"});
    }

    [HttpPost("recuperacion/restablecer")]
    public async Task<IActionResult> Restablecer([FromBody] RestablecerDto d)
    {
        Ensure(d.Nueva_Contrasena is {Length:>=8 and <=200},"La contraseña debe tener de 8 a 200 caracteres");
        var hash=BCrypt.Net.BCrypt.HashPassword(d.Nueva_Contrasena);
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var uid=await ConsumeTokenAsync(cn,tx,d.Token,"recuperacion");
        await cn.ExecuteAsync("""
          UPDATE usuario SET contrasena_hash=@hash,intentos_fallidos=0,bloqueado_hasta=NULL
          WHERE id_usuario=@uid
          """,new {hash,uid},tx);
        await tx.CommitAsync();
        return Ok(new {ok=true});
    }

    // -----------------------------------------------------------------------
    // Configuración de la cuenta (usuario autenticado)
    // -----------------------------------------------------------------------

    [HttpGet("perfil")]
    [Authorize]
    public async Task<IActionResult> Perfil()
    {
        await using var cn=await Db.OpenAsync();
        var perfil=await cn.QueryFirstOrDefaultAsync("""
          SELECT u.id_usuario, u.nombre, u.nombre_usuario, u.correo, u.telefono,
                 u.correo_verificado, u.activo, u.fecha_registro, u.ultimo_acceso,
                 UPPER(r.nombre) AS rol
          FROM usuario u JOIN rol r ON r.id_rol=u.id_rol
          WHERE u.id_usuario=@uid
          """,new { uid=UserId });
        Ensure(perfil is not null,"Usuario no encontrado",404);
        return Ok(perfil);
    }

    [HttpPatch("perfil")]
    [Authorize]
    public async Task<IActionResult> ActualizarPerfil([FromBody] PerfilActualizarDto d)
    {
        Ensure(Validaciones.ValidarNombre(d.Nombre) is null,Validaciones.ValidarNombre(d.Nombre) ?? "");
        Ensure(Validaciones.ValidarCorreo(d.Correo) is null,Validaciones.ValidarCorreo(d.Correo) ?? "");
        if (!string.IsNullOrWhiteSpace(d.Telefono))
            Ensure(Validaciones.ValidarTelefono(d.Telefono) is null,Validaciones.ValidarTelefono(d.Telefono) ?? "");

        var correo=d.Correo.Trim().ToLowerInvariant();
        var telefono=Validaciones.SoloDigitos(d.Telefono);
        await using var cn=await Db.OpenAsync();
        var duplicado=await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM usuario WHERE correo=@correo AND id_usuario<>@uid",new { correo,uid=UserId });
        Ensure(duplicado==0,"El correo ya está registrado por otra cuenta",409);

        await cn.ExecuteAsync("""
          UPDATE usuario SET nombre=@nombre, correo=@correo, telefono=@telefono
          WHERE id_usuario=@uid
          """,new { nombre=d.Nombre.Trim(), correo, telefono, uid=UserId });
        return Ok(new { ok=true, nombre=d.Nombre.Trim(), correo, telefono });
    }

    [HttpPost("contrasena")]
    [Authorize]
    public async Task<IActionResult> CambiarContrasena([FromBody] ContrasenaCambioDto d)
    {
        Ensure(!string.IsNullOrWhiteSpace(d.Actual),"Escribe tu contraseña actual");
        Ensure(Validaciones.ValidarContrasena(d.Nueva) is null,Validaciones.ValidarContrasena(d.Nueva) ?? "");
        await using var cn=await Db.OpenAsync();
        var hash=await cn.QueryFirstOrDefaultAsync<string>(
            "SELECT contrasena_hash FROM usuario WHERE id_usuario=@uid",new { uid=UserId });
        Ensure(hash is not null,"Usuario no encontrado",404);
        Ensure(BCrypt.Net.BCrypt.Verify(d.Actual,hash),"La contraseña actual no es correcta",401);
        var nuevo=BCrypt.Net.BCrypt.HashPassword(d.Nueva);
        await cn.ExecuteAsync("""
          UPDATE usuario SET contrasena_hash=@nuevo, intentos_fallidos=0, bloqueado_hasta=NULL
          WHERE id_usuario=@uid
          """,new { nuevo, uid=UserId });
        return Ok(new { ok=true });
    }

    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static async Task<string> IssueTokenAsync(MySqlConnector.MySqlConnection cn,int uid,string type,int minutes)
    {
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using var tx=await cn.BeginTransactionAsync();
        await cn.ExecuteAsync("DELETE FROM token_cuenta WHERE id_usuario=@uid AND tipo=@type",new {uid,type},tx);
        await cn.ExecuteAsync("""
          INSERT INTO token_cuenta(id_usuario,tipo,hash_token,expira_en)
          VALUES (@uid,@type,@hash,DATE_ADD(UTC_TIMESTAMP(),INTERVAL @minutes MINUTE))
          """,new {uid,type,hash=Digest(token),minutes},tx);
        await tx.CommitAsync();
        return token;
    }

    private static async Task<int> ConsumeTokenAsync(MySqlConnector.MySqlConnection cn,MySqlConnector.MySqlTransaction tx,string token,string type)
    {
        Ensure(!string.IsNullOrWhiteSpace(token) && Regex.IsMatch(token,"^[0-9a-f]{64}$"),"Token inválido o expirado");
        var found=await cn.QueryFirstOrDefaultAsync<TokenRow>("""
          SELECT id,id_usuario FROM token_cuenta
          WHERE hash_token=@hash AND tipo=@type AND usado_en IS NULL AND expira_en>UTC_TIMESTAMP()
          FOR UPDATE
          """,new {hash=Digest(token),type},tx);
        Ensure(found is not null,"Token inválido o expirado");
        await cn.ExecuteAsync("UPDATE token_cuenta SET usado_en=UTC_TIMESTAMP() WHERE id=@id",new {id=found!.id},tx);
        return found.id_usuario;
    }

    public sealed class UserVerify {public int id_usuario {get;set;} public string correo {get;set;}=""; public bool correo_verificado {get;set;} }
    public sealed class RecoveryUser {public int id_usuario {get;set;} public string correo {get;set;}=""; }
    public sealed class TokenRow {public long id {get;set;} public int id_usuario {get;set;} }
}
