using System.Text.Json;
using Cartagena.Api.Data;
using Cartagena.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cartagena.Api.Controllers;

/// <summary>
/// Directorio de proveedores.
///
/// Sustituye al microservicio Flask (Backend/Cartagena/servicios/proveedores)
/// para que el panel funcione en un único despliegue. Mantiene el contrato
/// original que ya consume el frontend:
///
///   GET    /api/proveedores       -> 200 { success, data }
///   POST   /api/proveedores       -> 201 { success, message, data }
///   GET    /api/proveedores/{id}  -> 200 { success, data } | 404
///   PUT    /api/proveedores/{id}  -> 200 { success, message, data }
///   DELETE /api/proveedores/{id}  -> 200 { success, message }
///
/// Los errores responden { error } (ApiExceptionHandler) con los mismos
/// códigos que Flask: 400 validación, 404 inexistente y 409 conflicto
/// (RFC repetido o proveedor con compras).
/// </summary>
[Authorize(Policy = "Admin")]
public sealed class ProveedoresController(DbFactory db) : ApiControllerBase(db)
{
    private const string SelectProveedor = """
        SELECT id_proveedor AS id, nombre, contacto, razon_social, rfc,
               direccion, correo, telefono
        FROM proveedor
        """;

    private static readonly string[] Campos =
        { "nombre", "contacto", "razon_social", "rfc", "direccion", "correo", "telefono" };

    // Llaves que el alta exige aunque su valor sea null (igual que Flask).
    private static readonly string[] CamposObligatorios =
        { "nombre", "razon_social", "rfc", "direccion", "correo", "telefono" };

    // Límites reales de las columnas (evita un 503 engañoso por error 1406).
    private static readonly Dictionary<string, int> LargoMaximo = new()
    {
        ["nombre"] = 150,
        ["contacto"] = 150,
        ["razon_social"] = 150,
        ["rfc"] = 13,
        ["direccion"] = 255,
        ["correo"] = 150,
        ["telefono"] = 50,
    };

    [HttpGet("api/proveedores")]
    public async Task<IActionResult> Listar()
    {
        await using var cn = await Db.OpenAsync();
        var filas = await cn.QueryAsync(SelectProveedor + " ORDER BY id_proveedor DESC");
        return Ok(new { success = true, data = filas });
    }

    [HttpGet("api/proveedores/{id:int}")]
    public async Task<IActionResult> Obtener(int id)
    {
        await using var cn = await Db.OpenAsync();
        var fila = await cn.QueryFirstOrDefaultAsync(SelectProveedor + " WHERE id_proveedor = @id", new { id });
        Ensure(fila is not null, "Proveedor no encontrado", 404);
        return Ok(new { success = true, data = fila });
    }

    [HttpPost("api/proveedores")]
    public async Task<IActionResult> Crear([FromBody] JsonElement cuerpo)
    {
        var valores = LeerProveedor(cuerpo, completo: true);

        await using var cn = await Db.OpenAsync();
        var rfc = valores.GetValueOrDefault("rfc");
        if (!string.IsNullOrWhiteSpace(rfc))
        {
            var repetido = await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM proveedor WHERE rfc = @Rfc", new { Rfc = rfc });
            Ensure(repetido == 0, "Ya existe un proveedor con ese RFC", 409);
        }

        var parametros = new DynamicParameters();
        foreach (var campo in Campos) parametros.Add(campo, valores.GetValueOrDefault(campo));
        await cn.ExecuteAsync("""
            INSERT INTO proveedor (nombre, contacto, razon_social, rfc, direccion, correo, telefono)
            VALUES (@nombre, @contacto, @razon_social, @rfc, @direccion, @correo, @telefono)
            """, parametros);

        var id = await cn.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");
        var fila = await cn.QueryFirstOrDefaultAsync(SelectProveedor + " WHERE id_proveedor = @id", new { id });
        return StatusCode(201, new { success = true, message = "Proveedor creado correctamente", data = fila });
    }

    [HttpPut("api/proveedores/{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] JsonElement cuerpo)
    {
        var valores = LeerProveedor(cuerpo, completo: false);

        await using var cn = await Db.OpenAsync();
        var existe = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM proveedor WHERE id_proveedor = @id", new { id });
        Ensure(existe > 0, "Proveedor no encontrado", 404);

        var rfc = valores.GetValueOrDefault("rfc");
        if (!string.IsNullOrWhiteSpace(rfc))
        {
            var repetido = await cn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM proveedor WHERE rfc = @rfc AND id_proveedor <> @id",
                new { rfc, id });
            Ensure(repetido == 0, "El RFC ya pertenece a otro proveedor", 409);
        }

        // UPDATE generado solo con las llaves que venían en el cuerpo.
        var set = string.Join(", ", valores.Keys.Select(campo => $"`{campo}` = @{campo}"));
        var parametros = new DynamicParameters();
        parametros.Add("id", id);
        foreach (var campo in valores.Keys) parametros.Add(campo, valores[campo]);
        await cn.ExecuteAsync($"UPDATE proveedor SET {set} WHERE id_proveedor = @id", parametros);

        var fila = await cn.QueryFirstOrDefaultAsync(SelectProveedor + " WHERE id_proveedor = @id", new { id });
        return Ok(new { success = true, message = "Proveedor actualizado correctamente", data = fila });
    }

    [HttpDelete("api/proveedores/{id:int}")]
    public async Task<IActionResult> Eliminar(int id)
    {
        await using var cn = await Db.OpenAsync();
        var existe = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM proveedor WHERE id_proveedor = @id", new { id });
        Ensure(existe > 0, "Proveedor no encontrado", 404);

        var compras = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pedido_compra WHERE id_proveedor = @id", new { id });
        Ensure(compras == 0, "No se puede eliminar el proveedor porque tiene compras registradas", 409);

        await cn.ExecuteAsync("DELETE FROM proveedor WHERE id_proveedor = @id", new { id });
        return Ok(new { success = true, message = "Proveedor eliminado correctamente" });
    }

    /// <summary>
    /// Valida el JSON con las mismas reglas que <c>validar_proveedor</c> de Flask:
    /// el alta exige las siete llaves (aunque su valor sea null) y la edición
    /// basta con que venga al menos un campo. Devuelve solo las llaves presentes
    /// (en el alta, además todas inicializadas en null).
    /// </summary>
    private static Dictionary<string, string?> LeerProveedor(JsonElement cuerpo, bool completo)
    {
        if (cuerpo.ValueKind != JsonValueKind.Object)
            throw new ApiException("El cuerpo debe ser un objeto JSON", 400);

        var valores = new Dictionary<string, string?>();
        if (completo)
            foreach (var campo in Campos) valores[campo] = null;

        foreach (var campo in Campos)
        {
            if (!cuerpo.TryGetProperty(campo, out var valor))
            {
                // Alta: todas las llaves obligatorias salvo 'contacto'.
                // Edición: la llave ausente significa "no tocar esa columna".
                if (completo && campo == "nombre")
                    throw new ApiException("El campo 'nombre' es obligatorio y debe ser texto", 400);
                if (completo && CamposObligatorios.Contains(campo))
                    throw new ApiException(
                        "Faltan campos obligatorios: razon_social, rfc, direccion, correo o telefono", 400);
                continue;
            }

            string? texto = valor.ValueKind switch
            {
                JsonValueKind.String => valor.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => throw new ApiException($"El campo '{campo}' debe ser texto", 400),
            };

            if (texto is not null && texto.Length > LargoMaximo[campo])
                throw new ApiException($"El campo '{campo}' no puede superar {LargoMaximo[campo]} caracteres", 400);

            valores[campo] = texto;
        }

        if (completo)
        {
            if (string.IsNullOrWhiteSpace(valores["nombre"]))
                throw new ApiException("El campo 'nombre' no puede estar vacío", 400);
        }
        else if (valores.Count == 0)
        {
            throw new ApiException("No hay campos válidos para actualizar", 400);
        }

        return valores;
    }
}
