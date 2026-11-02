using System.Text.RegularExpressions;
using Cartagena.Api.Data;
using Cartagena.Api.Dtos;
using Cartagena.Api.Infrastructure;
using Cartagena.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace Cartagena.Api.Controllers;

[Authorize]
public sealed class VentasController(DbFactory db, PdfService pdf, MailService mail) : ApiControllerBase(db)
{
    [HttpPost("api/checkout")]
    [Authorize(Policy = "Comprador")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutDto d)
    {
        Ensure(d.Id_Domicilio > 0, "id_domicilio inválido");
        Ensure(d.Extra is null || d.Extra.Count == 0, "Campos adicionales no permitidos (no envíes números de tarjeta ni CVV)");
        var method = d.Tipo_Pago.Trim().ToLowerInvariant();
        Ensure(method is "tarjeta" or "transferencia" or "efectivo contra entrega", "Método de pago no admitido");
        if (method == "tarjeta") Ensure(d.Sandbox_Resultado == "aprobado", "Tarjeta de prueba rechazada: enviar sandbox_resultado=aprobado");
        var key = Request.Headers["Idempotency-Key"].ToString();
        Ensure(Regex.IsMatch(key, "^[A-Za-z0-9_-]{8,100}$"), "Se requiere Idempotency-Key de 8 a 100 caracteres");
        await using var cn = await Db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        try
        {
            var old = await cn.QueryFirstOrDefaultAsync<Sale>(
                "SELECT id_venta,total,estado FROM venta WHERE clave_idempotencia=@key AND id_usuario=@uid", new { key, uid=UserId }, tx);
            if (old is not null) return Ok(new { id_venta=old.id_venta,total=old.total.ToString("F2"),estado=old.estado,repetido=true });
            var address = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM domicilio WHERE id_domicilio=@id AND id_usuario=@uid", new { id=d.Id_Domicilio,uid=UserId }, tx);
            Ensure(address == 1, "Domicilio no pertenece al cliente", 403);
            var payId = await cn.QueryFirstOrDefaultAsync<int?>("SELECT id_tipo_pago FROM tipo_pago WHERE LOWER(nombre)=@method", new { method }, tx);
            Ensure(payId is not null, "Método de pago no configurado", 409);
            var cartId = await cn.QueryFirstOrDefaultAsync<int?>("SELECT id_carrito FROM carrito WHERE id_usuario=@uid FOR UPDATE",new { uid=UserId },tx);
            Ensure(cartId is not null, "Carrito vacío", 409);
            var items = (await cn.QueryAsync<CartLine>("SELECT id_libro,cantidad FROM item_carrito WHERE id_carrito=@cartId ORDER BY id_libro",new { cartId },tx)).ToList();
            Ensure(items.Count > 0, "Carrito vacío", 409);
            decimal total = 0m;
            var selected = new List<(CartLine item, decimal price)>();
            foreach (var item in items)
            {
                var book = await cn.QueryFirstOrDefaultAsync<BookStock>(
                    "SELECT id_libro,precio,stock,descuento_porcentaje FROM libro WHERE id_libro=@id FOR UPDATE",new { id=item.id_libro },tx);
                Ensure(book is not null && item.cantidad > 0 && book.stock >= item.cantidad,
                    "Stock insuficiente para " + item.id_libro, 409);
                var price = Price(book!.precio,book.descuento_porcentaje);
                total += price * item.cantidad;
                selected.Add((item, price));
            }
            var estado = method == "tarjeta" ? "Confirmado" : "Pendiente";
            var estadoPago = method == "tarjeta" ? "AprobadoSandbox" : "Pendiente";
            await cn.ExecuteAsync("""
              INSERT INTO venta (id_usuario,id_tipo_pago,total,estado,estado_pago,clave_idempotencia)
              VALUES (@uid,@payId,@total,@estado,@estadoPago,@key)
              """,new { uid=UserId,payId,total,estado,estadoPago,key },tx);
            var saleId = await cn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()", transaction:tx);
            foreach (var (item, price) in selected)
            {
                await cn.ExecuteAsync("""
                  INSERT INTO detalle_venta(id_venta,id_libro,cantidad,precio_unitario)
                  VALUES (@saleId,@id,@qty,@price)
                  """,new { saleId,id=item.id_libro,qty=item.cantidad,price },tx);
                var updated = await cn.ExecuteAsync("UPDATE libro SET stock=stock-@qty WHERE id_libro=@id AND stock>=@qty",
                    new { id=item.id_libro,qty=item.cantidad },tx);
                Ensure(updated == 1,"Stock insuficiente",409);
                await cn.ExecuteAsync("""
                  INSERT INTO movimiento_inventario(id_libro,cantidad,tipo,referencia)
                  VALUES (@id,@cantidad,'VENTA',@saleId)
                  """,new { id=item.id_libro,cantidad=-item.cantidad,saleId=saleId.ToString() },tx);
            }
            await cn.ExecuteAsync("INSERT INTO envio(id_venta,id_domicilio,estado) VALUES (@saleId,@address,'Procesando')",
                new { saleId,address=d.Id_Domicilio },tx);
            await cn.ExecuteAsync("DELETE FROM item_carrito WHERE id_carrito=@cartId",new { cartId },tx);
            await cn.ExecuteAsync("UPDATE carrito SET total=0 WHERE id_carrito=@cartId",new { cartId },tx);
            await tx.CommitAsync();
            return StatusCode(201,new { id_venta=saleId,total=total.ToString("F2"),estado,pago=estadoPago });
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            await tx.RollbackAsync();
            // Se puede colisionar con otra compra paralela; sólo devolvemos la propia.
            var duplicate = await cn.QueryFirstOrDefaultAsync<Sale>(
                "SELECT id_venta,total,estado FROM venta WHERE clave_idempotencia=@key AND id_usuario=@uid",new { key,uid=UserId });
            if (duplicate is not null) return Ok(new { id_venta=duplicate.id_venta,total=duplicate.total.ToString("F2"),estado=duplicate.estado,repetido=true });
            throw;
        }
    }

    [HttpPost("api/pagos/{id:int}/confirmar")]
    [Authorize(Policy="Admin")]
    public async Task<IActionResult> ConfirmarTransferencia(int id,[FromBody] ReferenciaDto d)
    {
        Ensure(!string.IsNullOrWhiteSpace(d.Referencia) && d.Referencia.Length >= 6, "Referencia bancaria inválida");
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var p=await cn.QueryFirstOrDefaultAsync<PayInfo>("""
          SELECT v.estado_pago,t.nombre AS metodo FROM venta v
          JOIN tipo_pago t ON t.id_tipo_pago=v.id_tipo_pago
          WHERE v.id_venta=@id FOR UPDATE
          """,new { id },tx);
        Ensure(p is not null,"Venta no encontrada",404);
        Ensure(p!.metodo.Equals("Transferencia",StringComparison.OrdinalIgnoreCase),"La venta no usa transferencia",409);
        Ensure(p.estado_pago=="Pendiente","Pago ya procesado",409);
        await cn.ExecuteAsync("UPDATE venta SET estado_pago='ConfirmadoManual',estado='Confirmado',referencia_pago=@ref WHERE id_venta=@id",
            new { id,@ref=d.Referencia },tx);
        await tx.CommitAsync();
        return Ok(new { ok=true,nota="Confirmación administrativa, sin conexión bancaria" });
    }

    [HttpPost("api/ventas/{id:int}/cancelar")]
    public async Task<IActionResult> Cancelar(int id)
    {
        await using var cn=await Db.OpenAsync();
        await using var tx=await cn.BeginTransactionAsync();
        var sale=await cn.QueryFirstOrDefaultAsync<CancelInfo>(
            "SELECT id_usuario,estado,estado_pago FROM venta WHERE id_venta=@id FOR UPDATE",new { id },tx);
        Ensure(sale is not null,"Venta no encontrada",404);
        RequireOwnerOrAdmin(sale!.id_usuario);
        Ensure(sale.estado=="Pendiente" && sale.estado_pago=="Pendiente","Sólo se cancelan pedidos pendientes",409);
        var items=await cn.QueryAsync<CartLine>("SELECT id_libro,cantidad FROM detalle_venta WHERE id_venta=@id ORDER BY id_libro",new { id },tx);
        foreach(var item in items)
        {
            await cn.ExecuteAsync("UPDATE libro SET stock=stock+@qty WHERE id_libro=@book",new { qty=item.cantidad,book=item.id_libro },tx);
            await cn.ExecuteAsync("""
              INSERT INTO movimiento_inventario(id_libro,cantidad,tipo,referencia)
              VALUES (@book,@qty,'CANCELACION',@ref)
              """,new { book=item.id_libro,qty=item.cantidad,@ref=id.ToString() },tx);
        }
        await cn.ExecuteAsync("UPDATE venta SET estado='Cancelado',estado_pago='Cancelado' WHERE id_venta=@id",new { id },tx);
        await cn.ExecuteAsync("UPDATE envio SET estado='Cancelado' WHERE id_venta=@id",new { id },tx);
        await tx.CommitAsync();
        return Ok(new { ok=true });
    }

    [HttpGet("api/ventas")]
    public async Task<IActionResult> MisVentas()
    {
        await using var cn=await Db.OpenAsync();
        var rows=await cn.QueryAsync("""
          SELECT v.id_venta,v.fecha,v.total,v.estado,v.estado_pago,e.estado AS envio_estado,e.numero_guia
          FROM venta v LEFT JOIN envio e ON e.id_venta=v.id_venta
          WHERE v.id_usuario=@uid ORDER BY v.fecha DESC LIMIT 200
          """,new { uid=UserId });
        return Ok(new { data=rows });
    }

    /// <summary>
    /// Historial de compras del cliente con envío, guía, estado de entrega y
    /// garantías/devoluciones asociadas. Usado por "Mis pedidos".
    /// </summary>
    [HttpGet("api/mis-compras")]
    public async Task<IActionResult> MisCompras()
    {
        await using var cn=await Db.OpenAsync();
        var compras=(await cn.QueryAsync("""
          SELECT v.id_venta, v.fecha, v.total, v.estado, v.estado_pago, v.referencia_pago,
                 t.nombre AS metodo_pago,
                 e.id_envio, e.estado AS estado_envio, e.numero_guia, e.fecha_entrega,
                 e.confirmado_cliente,
                 d.direccion, d.ciudad, d.estado AS domicilio_estado, d.codigo_postal,
                 CONCAT_WS(', ', NULLIF(d.calle,''), NULLIF(d.numero,''), NULLIF(d.colonia,''),
                           NULLIF(d.ciudad,''), NULLIF(d.estado,''), NULLIF(d.codigo_postal,'')) AS direccion_completa
          FROM venta v
          JOIN tipo_pago t ON t.id_tipo_pago=v.id_tipo_pago
          LEFT JOIN envio e ON e.id_venta=v.id_venta
          LEFT JOIN domicilio d ON d.id_domicilio=e.id_domicilio
          WHERE v.id_usuario=@uid
          ORDER BY v.fecha DESC LIMIT 200
          """,new { uid=UserId })).ToList();

        var ids=compras.Select(c=> (int)c.id_venta).ToArray();
        var items=ids.Length==0
          ? Array.Empty<dynamic>()
          : (await cn.QueryAsync("""
              SELECT dv.id_venta, dv.id_libro, l.titulo, l.imagen_portada, l.autor,
                     dv.cantidad, dv.precio_unitario
              FROM detalle_venta dv JOIN libro l ON l.id_libro=dv.id_libro
              WHERE dv.id_venta IN @ids ORDER BY l.titulo
              """,new { ids })).ToArray();
        var garantias=ids.Length==0
          ? Array.Empty<dynamic>()
          : (await cn.QueryAsync("""
              SELECT g.id_solicitud, g.id_venta, g.id_libro, l.titulo, g.motivo, g.estado, g.fecha
              FROM solicitud_garantia g JOIN libro l ON l.id_libro=g.id_libro
              WHERE g.id_usuario=@uid AND g.id_venta IN @ids ORDER BY g.fecha DESC
              """,new { uid=UserId, ids })).ToArray();

        var data=compras.Select(c=>
        {
            var id=(int)c.id_venta;
            return new
            {
                c.id_venta, c.fecha, c.total, c.estado, c.estado_pago, c.referencia_pago, c.metodo_pago,
                c.id_envio, c.estado_envio, c.numero_guia, c.fecha_entrega, c.confirmado_cliente,
                c.direccion, c.ciudad, c.domicilio_estado, c.codigo_postal, c.direccion_completa,
                items=items.Where(i=> (int)i.id_venta==id),
                garantias=garantias.Where(g=> (int)g.id_venta==id)
            };
        });
        return Ok(new { data });
    }

    [HttpGet("api/ventas/{id:int}")]
    public async Task<IActionResult> Venta(int id)
    {
        await using var cn=await Db.OpenAsync();
        var owner=await cn.QueryFirstOrDefaultAsync<CancelInfo>("SELECT id_usuario FROM venta WHERE id_venta=@id",new { id });
        Ensure(owner is not null,"Venta no encontrada",404);
        RequireOwnerOrAdmin(owner!.id_usuario);
        var sale=await cn.QueryFirstAsync("SELECT * FROM venta WHERE id_venta=@id",new { id });
        var details=await cn.QueryAsync("""
          SELECT d.id_libro,l.titulo,d.cantidad,d.precio_unitario FROM detalle_venta d
          JOIN libro l ON l.id_libro=d.id_libro WHERE d.id_venta=@id
          """,new { id });
        return Ok(new { venta=sale,items=details });
    }

    [HttpGet("api/ventas/{id:int}/ticket.pdf")]
    public async Task<IActionResult> Ticket(int id)
    {
        var (sale,lines)=await GetReceiptAsync(id);
        var bytes=pdf.Create("Comprobante de compra - Cartagena",lines);
        Response.Headers["Cache-Control"]="no-store";
        return File(bytes,"application/pdf",$"ticket-{id}.pdf");
    }

    [HttpPost("api/ventas/{id:int}/enviar-confirmacion")]
    public async Task<IActionResult> EnviarConfirmacion(int id)
    {
        var (sale,lines)=await GetReceiptAsync(id);
        var sent=await mail.SendAsync(sale.correo,"Confirmación de compra #"+id,
            $"Tu compra #{id} fue registrada. Total: {sale.total:F2}.",pdf.Create("Comprobante de compra",lines));
        return StatusCode(sent ? 200 : 503,new { enviado=sent,estado=sent ? "enviado" : "SMTP_NO_CONFIGURADO_O_ERROR" });
    }

    private async Task<(Receipt info,List<string> lines)> GetReceiptAsync(int id)
    {
        await using var cn=await Db.OpenAsync();
        var sale=await cn.QueryFirstOrDefaultAsync<Receipt>("""
          SELECT v.id_usuario,v.total,v.fecha,v.estado,v.estado_pago,u.nombre,u.correo
          FROM venta v JOIN usuario u ON u.id_usuario=v.id_usuario WHERE v.id_venta=@id
          """,new { id });
        Ensure(sale is not null,"Venta no encontrada",404);
        RequireOwnerOrAdmin(sale!.id_usuario);
        var items=await cn.QueryAsync<ReceiptLine>("""
          SELECT l.titulo,d.cantidad,d.precio_unitario FROM detalle_venta d
          JOIN libro l ON l.id_libro=d.id_libro WHERE d.id_venta=@id
          """,new { id });
        var lines=new List<string> { $"Venta #{id}",$"Cliente: {sale.nombre}",$"Fecha: {sale.fecha:yyyy-MM-dd HH:mm}" };
        lines.AddRange(items.Select(i=>$"{i.titulo} x{i.cantidad} $ {i.precio_unitario:F2}"));
        lines.Add($"Total: $ {sale.total:F2}");
        lines.Add($"Estado: {sale.estado} / pago: {sale.estado_pago}");
        return (sale,lines);
    }

    public sealed class Sale { public int id_venta {get;set;} public decimal total {get;set;} public string estado {get;set;}=""; }
    public sealed class PayInfo { public string estado_pago {get;set;}=""; public string metodo {get;set;}=""; }
    public sealed class CancelInfo { public int id_usuario {get;set;} public string estado {get;set;}=""; public string estado_pago {get;set;}=""; }
    public sealed class SaleFull { public int id_venta {get;set;} public int id_usuario {get;set;} public decimal total {get;set;} public string estado {get;set;}=""; public string estado_pago {get;set;}=""; public DateTime fecha {get;set;} }
    public sealed class CartLine { public string id_libro {get;set;}=""; public int cantidad {get;set;} }
    public sealed class BookStock { public string id_libro {get;set;}=""; public decimal precio {get;set;} public int stock {get;set;} public decimal descuento_porcentaje {get;set;} }
    public sealed class Receipt { public int id_usuario {get;set;} public decimal total {get;set;} public DateTime fecha {get;set;} public string estado {get;set;}=""; public string estado_pago {get;set;}=""; public string nombre {get;set;}=""; public string correo {get;set;}=""; }
    public sealed class ReceiptLine { public string titulo {get;set;}=""; public int cantidad {get;set;} public decimal precio_unitario {get;set;} }
}
