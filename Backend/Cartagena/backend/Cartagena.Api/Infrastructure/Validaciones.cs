using System.Text.RegularExpressions;

namespace Cartagena.Api.Infrastructure;

/// <summary>
/// Reglas de validación compartidas por el registro, el panel de usuarios y la
/// configuración de la cuenta. Deben coincidir con las del microservicio de
/// login y con Frontend/src/utils/validacion.ts.
/// </summary>
public static class Validaciones
{
    public static readonly Regex Nombre = new(
        @"^[\p{L}][\p{L}\s'.-]{1,149}$", RegexOptions.Compiled);

    public static readonly Regex NombreUsuario = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{2,59}$", RegexOptions.Compiled);

    public static readonly Regex Correo = new(
        @"^[^\s@]+@[^\s@]+\.com(\.[A-Za-z]{2,})?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex Telefono = new(@"^\d{10}$", RegexOptions.Compiled);

    public static readonly Regex Contrasena = new(
        @"^(?=.*[A-Z])(?=.*\d)(?=.*[^\p{L}\p{N}\s]).{8,200}$",
        RegexOptions.Compiled);

    public const string MensajeContrasena =
        "La contraseña debe tener al menos 8 caracteres, una mayúscula, un número y un carácter especial";

    public static string SoloDigitos(string? valor) =>
        new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string? ValidarNombre(string? valor)
    {
        var nombre = (valor ?? string.Empty).Trim();
        if (nombre.Length < 2) return "El nombre completo debe tener al menos 2 caracteres";
        if (nombre.Length > 150) return "El nombre completo no puede superar 150 caracteres";
        if (!Nombre.IsMatch(nombre))
            return "El nombre completo solo puede contener letras, espacios, apóstrofes, puntos y guiones";
        return null;
    }

    public static string? ValidarUsuario(string? valor)
    {
        var usuario = (valor ?? string.Empty).Trim();
        if (usuario.Length < 3) return "El nombre de usuario debe tener al menos 3 caracteres";
        if (usuario.Length > 60) return "El nombre de usuario no puede superar 60 caracteres";
        if (!NombreUsuario.IsMatch(usuario))
            return "Usa letras, números, punto, guion o guion bajo; debe iniciar con letra o número";
        return null;
    }

    public static string? ValidarCorreo(string? valor)
    {
        var correo = (valor ?? string.Empty).Trim();
        if (correo.Length == 0) return "El correo es obligatorio";
        if (correo.Length > 120) return "El correo no puede superar 120 caracteres";
        if (!Correo.IsMatch(correo))
            return "El correo debe incluir @ y terminar en .com (por ejemplo, nombre@dominio.com)";
        return null;
    }

    public static string? ValidarTelefono(string? valor)
    {
        var telefono = SoloDigitos(valor);
        if (telefono.Length != 10) return "El teléfono debe tener exactamente 10 dígitos";
        return null;
    }

    public static string? ValidarContrasena(string? valor)
    {
        var contrasena = valor ?? string.Empty;
        if (contrasena.Length < 8 || contrasena.Length > 200 || !Contrasena.IsMatch(contrasena))
            return MensajeContrasena;
        return null;
    }
}
