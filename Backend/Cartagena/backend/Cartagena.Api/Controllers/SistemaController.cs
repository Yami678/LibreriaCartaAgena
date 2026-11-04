using System.Net;
using System.Net.Sockets;
using System.Text;
using Cartagena.Api.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

[ApiController]
public sealed class SistemaController(DbFactory db, IConfiguration settings) : ControllerBase
{
    [HttpGet("api/core/health")]
    public async Task<IActionResult> Health()
    {
        try
        {
            await using var cn=await db.OpenAsync();
            var one=await cn.ExecuteScalarAsync<int>("SELECT 1");
            return Ok(new {ok=one==1,servicio="cartagena-api-csharp"});
        }
        catch { return StatusCode(503,new {ok=false,servicio="cartagena-api-csharp"}); }
    }

    /// <summary>
    /// Diagnostica la conexion SMTP sin enviar ningun correo: resuelve el host,
    /// intenta TCP por cada direccion (IPv4 e IPv6) y hace el apretón de manos
    /// EHLO + STARTTLS. Sirve para ver desde Render por qué falla el envío.
    /// </summary>
    [HttpGet("api/core/correo/diagnostico")]
    public async Task<IActionResult> DiagnosticoCorreo([FromQuery] string host="smtp.gmail.com",[FromQuery] int puerto=587)
    {
        var hostCfg=settings["SMTP_HOST"];
        var puertoCfg=settings["SMTP_PORT"];
        var pasos=new List<object>();

        // 1) DNS (con tope: si se cuelga, lo vemos en vez de colgar la peticion)
        IPAddress[] ipv4; IPAddress[] ipv6;
        try
        {
            var resolucion=Dns.GetHostAddressesAsync(host);
            if (await Task.WhenAny(resolucion,Task.Delay(8000))!=resolucion)
                return Ok(new {ok=false,host,puerto,host_cfg=hostCfg,puerto_cfg=puertoCfg,
                    pasos,paso_fallido="dns",error="Timeout de 8s resolviendo el host"});
            var todo=await resolucion;
            ipv4=todo.Where(a=>a.AddressFamily==AddressFamily.InterNetwork).ToArray();
            ipv6=todo.Where(a=>a.AddressFamily==AddressFamily.InterNetworkV6).ToArray();
            pasos.Add(new {paso="dns",ok=true,direcciones=todo.Select(a=>a.ToString()).ToArray(),
                ipv4=ipv4.Length,ipv6=ipv6.Length});
        }
        catch (Exception ex)
        {
            return Ok(new {ok=false,host,puerto,host_cfg=hostCfg,puerto_cfg=puertoCfg,
                pasos,paso_fallido="dns",error=ex.Message});
        }

        // 2) TCP por cada direccion (tope de 8s: una familia colgada no cuelga el resto)
        var mejorTiempo=TimeSpan.MaxValue;
        IPAddress? mejorDir=null;
        foreach (var dir in ipv4.Concat(ipv6))
        {
            var sw=System.Diagnostics.Stopwatch.StartNew();
            var familia=dir.AddressFamily==AddressFamily.InterNetwork?"IPv4":"IPv6";
            try
            {
                using var socket=new Socket(dir.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
                var tcs=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var args=new SocketAsyncEventArgs { RemoteEndPoint=new IPEndPoint(dir,puerto) };
                args.Completed+=(_,e)=>tcs.TrySetResult(e.SocketError==SocketError.Success);
                if (!socket.ConnectAsync(args)) { /* ya termino */ }
                var conecto=await Task.WhenAny(tcs.Task,Task.Delay(8000))==tcs.Task && await tcs.Task;
                var fallo=conecto?SocketError.Success:SocketError.TimedOut;
                sw.Stop();
                pasos.Add(new {paso="tcp",direccion=dir.ToString(),familia,ok=conecto,
                    ms=(int)sw.ElapsedMilliseconds,error=conecto?null:fallo.ToString()});
                if (conecto && sw.Elapsed<mejorTiempo) { mejorTiempo=sw.Elapsed; mejorDir=dir; }
            }
            catch (Exception ex)
            {
                sw.Stop();
                pasos.Add(new {paso="tcp",direccion=dir.ToString(),familia,
                    ok=false,ms=(int)sw.ElapsedMilliseconds,error=ex.Message});
            }
        }

        if (mejorDir is null)
            return Ok(new {ok=false,host,puerto,host_cfg=hostCfg,puerto_cfg=puertoCfg, pasos,
                paso_fallido="tcp",error="Ninguna direccion acepto la conexion"});

        // 3) EHLO + STARTTLS contra la mejor direccion (sin autenticar ni enviar)
        try
        {
            using var tcp=new TcpClient();
            var conexion=tcp.ConnectAsync(mejorDir,puerto);
            if (await Task.WhenAny(conexion,Task.Delay(8000))!=conexion)
                return Ok(new {ok=false,host,puerto,pasos,paso_fallido="smtp_connect",
                    error="Timeout al conectar para EHLO"});
            await conexion;
            await using var flujo=tcp.GetStream();
            // UTF8 sin BOM: si lleva BOM, Gmail recibe "\xEF\xBB\xBFEHLO ..." y responde 502.
            var utf8=new UTF8Encoding(encoderShouldEmitUTF8Identifier:false);
            using var lector=new StreamReader(flujo,utf8);
            await using var escritor=new StreamWriter(flujo,utf8){NewLine="\r\n",AutoFlush=true};

            var bienvenida=await ConTimeout(lector.ReadLineAsync(),8000);
            await escritor.WriteLineAsync("EHLO cartagena");
            // Respuesta multilinea: "250-..." continua, "250 " termina.
            var ehlo=new List<string>();
            string? linea;
            while ((linea=await ConTimeout(lector.ReadLineAsync(),8000))!=null)
            {
                ehlo.Add(linea);
                if (!linea.StartsWith("250-")) break;
            }
            var ehloFinal=linea??"";

            var starttls=ehlo.Any(e=>e.Contains("STARTTLS",StringComparison.OrdinalIgnoreCase));
            string? tlsResultado=null;
            if (starttls)
            {
                await escritor.WriteLineAsync("STARTTLS");
                var resp=await ConTimeout(lector.ReadLineAsync(),8000);
                tlsResultado=resp;
                if (resp!=null && resp.StartsWith("220"))
                {
                    var ssl=new System.Net.Security.SslStream(flujo,false,(_,_,_,_)=>true);
                    await ssl.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions
                    {
                        TargetHost=host,
                        EnabledSslProtocols=System.Security.Authentication.SslProtocols.Tls12
                    });
                    tlsResultado+=" | TLS "+ssl.SslProtocol+" OK";
                }
            }

            return Ok(new {ok=true,host,puerto,host_cfg=hostCfg,puerto_cfg=puertoCfg,
                usado=mejorDir.ToString(),familia=mejorDir.AddressFamily==AddressFamily.InterNetwork?"IPv4":"IPv6",
                ms_conexion=(int)mejorTiempo.TotalMilliseconds,pasos,
                smtp=new {bienvenida,ehlo=ehloFinal,soporta_starttls=starttls,tls=tlsResultado}});
        }
        catch (Exception ex)
        {
            return Ok(new {ok=false,host,puerto,pasos,paso_fallido="smtp",error=ex.Message});
        }
    }

    private static async Task<T?> ConTimeout<T>(Task<T?> tarea,int ms)
    {
        return await Task.WhenAny(tarea,Task.Delay(ms))==tarea ? await tarea : default;
    }
}
