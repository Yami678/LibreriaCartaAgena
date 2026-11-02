using Cartagena.Api.Data;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

/// <summary>Reseñas y calificaciones de libros, persistidas en la tabla `comentario`.</summary>
[Route("api/libros/{idLibro}/resenas")]
public sealed class ResenasController(DbFactory db) : ApiControllerBase(db)
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Listar(string idLibro)
    {
        await using var cn = await Db.OpenAsync();
        var existe = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM libro WHERE id_libro=@id", new { id = idLibro });
        Ensure(existe == 1, "Libro no encontrado", 404);
        var rows = (await cn.QueryAsync<ResenaFila>("""
          SELECT c.id_comentario AS id, c.calificacion, c.texto, c.fecha, u.nombre AS autor
          FROM comentario c
          JOIN usuario u ON u.id_usuario = c.id_usuario
          WHERE c.id_libro = @id
          ORDER BY c.fecha DESC, c.id_comentario DESC
          LIMIT 200
          """, new { id = idLibro })).ToList();
        var calificaciones = rows.Where(r => r.calificacion is not null).Select(r => (decimal)r.calificacion!).ToList();
        var promedio = calificaciones.Count == 0 ? 0m : Math.Round(calificaciones.Average(), 1, MidpointRounding.AwayFromZero);
        return Ok(new
        {
            data = rows.Select(r => new
            {
                id = r.id,
                calificacion = r.calificacion,
                texto = r.texto,
                fecha = r.fecha?.ToString("yyyy-MM-dd"),
                autor = r.autor
            }),
            total = rows.Count,
            promedio
        });
    }

    [HttpPost]
    [Authorize(Policy = "Comprador")]
    public async Task<IActionResult> Crear(string idLibro, [FromBody] ResenaDto d)
    {
        Ensure(d.Calificacion is >= 1 and <= 5, "La calificación debe ser un entero entre 1 y 5");
        var texto = (d.Texto ?? "").Trim();
        Ensure(texto.Length is >= 3 and <= 2000, "El comentario debe tener entre 3 y 2000 caracteres");

        await using var cn = await Db.OpenAsync();
        var libro = await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM libro WHERE id_libro=@id", new { id = idLibro });
        Ensure(libro == 1, "Libro no encontrado", 404);

        // Sólo quien compró el libro (venta confirmada/entregada) puede reseñarlo.
        var compro = await cn.ExecuteScalarAsync<int>("""
          SELECT COUNT(*) FROM venta v
          JOIN detalle_venta d ON d.id_venta = v.id_venta
          WHERE v.id_usuario = @uid AND d.id_libro = @id
            AND v.estado IN ('Confirmado','Entregado')
          """, new { uid = UserId, id = idLibro });
        Ensure(compro > 0, "Sólo puede reseñar este libro quien lo haya comprado (comprador verificado)", 403);

        await cn.ExecuteAsync("""
          INSERT INTO comentario (id_libro, id_usuario, calificacion, texto)
          VALUES (@id, @uid, @grado, @texto)
          """, new { id = idLibro, uid = UserId, grado = d.Calificacion, texto });
        var nuevoId = await cn.ExecuteScalarAsync<long>("SELECT LAST_INSERT_ID()");
        return StatusCode(201, new { id = nuevoId, ok = true, mensaje = "Reseña registrada" });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Comprador")]
    public async Task<IActionResult> Eliminar(int id)
    {
        await using var cn = await Db.OpenAsync();
        var afectadas = await cn.ExecuteAsync(
            "DELETE FROM comentario WHERE id_comentario=@id AND (id_usuario=@uid OR @admin=1)",
            new { id, uid = UserId, admin = Admin ? 1 : 0 });
        Ensure(afectadas > 0, "Reseña no encontrada o sin permisos", 404);
        return Ok(new { ok = true });
    }

    public sealed class ResenaFila
    {
        public int id { get; set; }
        public int? calificacion { get; set; }
        public string texto { get; set; } = "";
        public DateTime? fecha { get; set; }
        public string autor { get; set; } = "";
    }

    public sealed record ResenaDto(int Calificacion, string Texto);
}
