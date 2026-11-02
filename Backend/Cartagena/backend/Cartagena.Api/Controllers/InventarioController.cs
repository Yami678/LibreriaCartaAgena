using System.Globalization;
using Cartagena.Api.Data;
using Cartagena.Api.Infrastructure;
using Cartagena.Api.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

/// <summary>Panel de administración: inventario con alertas de stock y reporte PDF.</summary>
[Authorize(Policy = "Admin")]
[Route("api/admin/inventario")]
public sealed class InventarioController(DbFactory db, PdfService pdf) : ApiControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        await using var cn = await Db.OpenAsync();
        var rows = (await cn.QueryAsync<LibroInventario>("""
          SELECT id_libro, titulo, autor, precio, descuento_porcentaje, stock,
                 stock_minimo, stock_maximo, categoria, editorial, isbn,
                 genero, formato, imagen_portada, descripcion
          FROM libro ORDER BY titulo
          """)).ToList();

        var data = rows.Select(r => new
        {
            id_libro = r.id_libro,
            titulo = r.titulo,
            autor = r.autor,
            precio = r.precio.ToString("F2", CultureInfo.InvariantCulture),
            descuento_porcentaje = r.descuento_porcentaje.ToString("F2", CultureInfo.InvariantCulture),
            precio_oferta = Price(r.precio, r.descuento_porcentaje).ToString("F2", CultureInfo.InvariantCulture),
            stock = r.stock,
            stock_minimo = r.stock_minimo,
            stock_maximo = r.stock_maximo,
            categoria = r.categoria,
            editorial = r.editorial,
            isbn = r.isbn,
            genero = r.genero,
            formato = r.formato,
            imagen_portada = r.imagen_portada,
            descripcion = r.descripcion,
            alerta = Alerta(r),
            valor = Price(r.precio, r.descuento_porcentaje) * r.stock
        }).ToList();

        var resumen = new
        {
            titulos = data.Count,
            unidades = data.Sum(d => d.stock),
            bajo_stock = data.Count(d => d.alerta == "bajo"),
            sobre_stock = data.Count(d => d.alerta == "alto"),
            valor_inventario = data.Sum(d => d.valor).ToString("F2", CultureInfo.InvariantCulture)
        };
        return Ok(new { data, resumen });
    }

    [HttpGet("reporte.pdf")]
    public async Task<IActionResult> ReportePdf()
    {
        await using var cn = await Db.OpenAsync();
        var rows = (await cn.QueryAsync<LibroInventario>("""
          SELECT id_libro, titulo, autor, precio, descuento_porcentaje, stock,
                 stock_minimo, stock_maximo, categoria, editorial, isbn
          FROM libro ORDER BY titulo
          """)).ToList();

        var lines = new List<string>
        {
            "Generado: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            "Titulos: " + rows.Count +
                "   Unidades: " + rows.Sum(r => r.stock) +
                "   Bajo stock: " + rows.Count(r => Alerta(r) == "bajo") +
                "   Sobre stock: " + rows.Count(r => Alerta(r) == "alto"),
            "Valor de inventario: $ " + rows.Sum(r => Price(r.precio, r.descuento_porcentaje) * r.stock).ToString("F2"),
            new string('-', 78)
        };

        foreach (var r in rows)
        {
            var oferta = r.descuento_porcentaje > 0
                ? $" ${Price(r.precio, r.descuento_porcentaje):F2} (antes ${r.precio:F2}, {r.descuento_porcentaje:F0}% off)"
                : $" ${r.precio:F2}";
            lines.Add($"{r.id_libro} | {r.titulo}");
            lines.Add($"    {r.autor} | stock {r.stock} (min {r.stock_minimo} / max {r.stock_maximo}) |{oferta} | {Etiqueta(r)}");
        }

        var bytes = pdf.Create("Reporte de inventario - Libreria Cartagena", lines);
        Response.Headers["Cache-Control"] = "no-store";
        return File(bytes, "application/pdf", "reporte-inventario.pdf");
    }

    private static string Alerta(LibroInventario r) =>
        r.stock <= r.stock_minimo ? "bajo" : r.stock >= r.stock_maximo ? "alto" : "ok";

    private static string Etiqueta(LibroInventario r) => Alerta(r) switch
    {
        "bajo" => "[ALERTA: STOCK MINIMO]",
        "alto" => "[ALERTA: STOCK MAXIMO]",
        _ => "[OK]"
    };

    public sealed class LibroInventario
    {
        public string id_libro { get; set; } = "";
        public string titulo { get; set; } = "";
        public string autor { get; set; } = "";
        public decimal precio { get; set; }
        public decimal descuento_porcentaje { get; set; }
        public int stock { get; set; }
        public int stock_minimo { get; set; }
        public int stock_maximo { get; set; }
        public string? categoria { get; set; }
        public string? editorial { get; set; }
        public string? isbn { get; set; }
        public string? genero { get; set; }
        public string? formato { get; set; }
        public string? imagen_portada { get; set; }
        public string? descripcion { get; set; }
    }
}
