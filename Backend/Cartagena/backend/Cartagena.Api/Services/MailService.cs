using System.Net;
using System.Net.Mail;
using Cartagena.Api.Data;
using Dapper;

namespace Cartagena.Api.Services;

/**
 * Envío de correos por SMTP.
 *
 * Las claves SMTP_* se buscan PRIMERO en la tabla `config_sistema` (base de
 * datos en la nube) y, si no están ahí, en la configuración del proceso
 * (.env en local, variables de Environment en Render). Así se puede configurar
 * el correo sin volver a tocar el despliegue.
 */
public sealed class MailService(DbFactory db, IConfiguration settings, ILogger<MailService> logger)
{
    private static readonly string[] Claves =
    {
        "SMTP_HOST", "SMTP_PORT", "SMTP_USER", "SMTP_PASSWORD", "SMTP_FROM"
    };

    private Dictionary<string, string>? _nube;
    private bool _leida;

    private sealed record Fila(string clave, string valor);

    /** Lee config_sistema una sola vez y lo deja en caché (hasta reiniciar). */
    private async Task<Dictionary<string, string>> NubeAsync()
    {
        if (_leida) return _nube ?? new(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cn = await db.OpenAsync();
            var filas = await cn.QueryAsync<Fila>("SELECT clave, valor FROM config_sistema");
            _nube = filas.ToDictionary(f => f.clave, f => f.valor, StringComparer.OrdinalIgnoreCase);
            _leida = true;   // solo se marca si la lectura salió bien: si la BD
            logger.LogInformation("Configuración SMTP leída de config_sistema ({N} claves)", _nube.Count);
        }
        catch (Exception ex)
        {
            // Sin marcar _leida: se vuelve a intentar en el próximo correo.
            logger.LogWarning(ex, "No se pudo leer config_sistema; se usan las variables del entorno");
            _nube = new(StringComparer.OrdinalIgnoreCase);
        }
        return _nube;
    }

    private async Task<string?> ClaveAsync(string nombre)
    {
        var nube = await NubeAsync();
        if (nube.TryGetValue(nombre, out var valor) && !string.IsNullOrWhiteSpace(valor))
            return valor;
        return settings[nombre];
    }

    public async Task<bool> SendAsync(string destination, string subject, string message, byte[]? receipt = null)
    {
        var host = await ClaveAsync("SMTP_HOST");
        var user = await ClaveAsync("SMTP_USER");
        var pass = await ClaveAsync("SMTP_PASSWORD");
        var from = await ClaveAsync("SMTP_FROM");
        var port = await ClaveAsync("SMTP_PORT") ?? "587";

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(from))
        {
            logger.LogWarning(
                "Correo NO enviado: faltan claves SMTP (SMTP_HOST/SMTP_USER/SMTP_PASSWORD/SMTP_FROM). " +
                "Pruébalas en la tabla config_sistema o en Backend/Cartagena/.env (y en Render) y reinicia el backend.");
            return false;
        }
        try
        {
            using var client = new SmtpClient(host, int.Parse(port))
            {
                EnableSsl = true, Credentials = new NetworkCredential(user, pass), Timeout = 30000
            };
            using var mail = new MailMessage(from, destination, subject, message);
            if (receipt is not null)
            {
                var mem = new MemoryStream(receipt);
                mail.Attachments.Add(new Attachment(mem, "ticket.pdf", "application/pdf"));
            }
            await client.SendMailAsync(mail);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo");
            return false;
        }
    }
}
