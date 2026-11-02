using System.Net;
using System.Net.Mail;

namespace Cartagena.Api.Services;

public sealed class MailService(IConfiguration settings, ILogger<MailService> logger)
{
    /** true cuando hay credenciales SMTP completas en la configuración (.env / appsettings). */
    public bool Configurado =>
        !string.IsNullOrWhiteSpace(settings["SMTP_HOST"]) &&
        !string.IsNullOrWhiteSpace(settings["SMTP_USER"]) &&
        !string.IsNullOrWhiteSpace(settings["SMTP_PASSWORD"]) &&
        !string.IsNullOrWhiteSpace(settings["SMTP_FROM"]);

    public async Task<bool> SendAsync(string destination,string subject,string message,byte[]? receipt=null)
    {
        var host=settings["SMTP_HOST"];
        var user=settings["SMTP_USER"];
        var pass=settings["SMTP_PASSWORD"];
        var from=settings["SMTP_FROM"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) ||
            string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(from))
        {
            logger.LogWarning(
                "Correo NO enviado: faltan claves SMTP (SMTP_HOST/SMTP_USER/SMTP_PASSWORD/SMTP_FROM). Complétalas en Backend/Cartagena/.env y reinicia el backend.");
            return false;
        }
        try
        {
            using var client=new SmtpClient(host,int.Parse(settings["SMTP_PORT"]??"587"))
            {
                EnableSsl=true,Credentials=new NetworkCredential(user,pass),Timeout=12000
            };
            using var mail=new MailMessage(from,destination,subject,message);
            if (receipt is not null)
            {
                var mem=new MemoryStream(receipt);
                mail.Attachments.Add(new Attachment(mem,"ticket.pdf","application/pdf"));
            }
            await client.SendMailAsync(mail);
            return true;
        }
        catch(Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex,"No se pudo enviar el correo");
            return false;
        }
    }
}
