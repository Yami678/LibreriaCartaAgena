using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Cartagena.Api.Dtos;

public sealed class LibroCrearDto
{
    [Required, StringLength(50, MinimumLength = 1)] public string Id_Libro { get; set; } = "";
    [Required, StringLength(255)] public string Titulo { get; set; } = "";
    [Required, StringLength(150)] public string Autor { get; set; } = "";
    [Range(0, 99999999)] public decimal Precio { get; set; }
    [Range(0, int.MaxValue)] public int Stock { get; set; }
    [Range(0, int.MaxValue)] public int Stock_Minimo { get; set; } = 5;
    [Range(0, int.MaxValue)] public int Stock_Maximo { get; set; } = 100;
    public string? Descripcion { get; set; }
    [StringLength(17)] public string? Isbn { get; set; }
    [StringLength(150)] public string? Editorial { get; set; }
    [StringLength(100)] public string? Categoria { get; set; }
    [StringLength(100)] public string? Genero { get; set; }
    [StringLength(50)] public string? Formato { get; set; }
    [StringLength(500)] public string? Imagen_Portada { get; set; }
    [Range(0,100)] public decimal Descuento_Porcentaje { get; set; }
}
public sealed class DireccionDto
{
    [Required] public string Calle { get; set; } = "";
    [Required] public string Numero { get; set; } = "";
    [Required] public string Colonia { get; set; } = "";
    [Required] public string Ciudad { get; set; } = "";
    [Required] public string Estado { get; set; } = "";
    [Required] public string Codigo_Postal { get; set; } = "";
    [Required] public string Telefono { get; set; } = "";
}
public sealed record CantidadDto(int Cantidad);
public sealed class CheckoutDto
{
    [Range(1,int.MaxValue)] public int Id_Domicilio { get; set; }
    [Required] public string Tipo_Pago { get; set; } = "";
    public string? Sandbox_Resultado { get; set; }
    [JsonExtensionData] public Dictionary<string,System.Text.Json.JsonElement>? Extra { get; set; }
}
public sealed record ReferenciaDto(string Referencia);
public sealed record EstadoDto(string Estado, string? Numero_Guia = null);
public sealed record PaqueteCrearDto(int Id_Venta, decimal Peso_Kg, decimal Largo_Cm, decimal Ancho_Cm, decimal Alto_Cm);
public sealed record GarantiaDto(int Id_Venta, string Id_Libro, string Motivo);
public sealed record MermaDto(string Id_Libro, int Cantidad, string Referencia);
public sealed record DevolucionProveedorDto(int Id_Proveedor, string Id_Libro, int Cantidad, string Motivo, string Referencia);
public sealed record ItemCompraProveedorDto(string Id_Libro, int Cantidad, decimal Costo_Unitario);
public sealed record CompraProveedorDto(int Id_Proveedor, List<ItemCompraProveedorDto> Items);
public sealed record CorreoDto(string Correo);
public sealed record TokenDto(string Token);
public sealed record RestablecerDto(string Token, string Nueva_Contrasena);

// ---------------------------------------------------------------------------
// Usuarios y roles (panel de administración)
// ---------------------------------------------------------------------------
public sealed class UsuarioCrearDto
{
    [Required, StringLength(150)] public string Nombre { get; set; } = "";
    [Required, StringLength(60)] public string Nombre_Usuario { get; set; } = "";
    [Required, StringLength(120)] public string Correo { get; set; } = "";
    public string Telefono { get; set; } = "";
    [Required, StringLength(200)] public string Contrasena { get; set; } = "";
    [Required] public string Rol { get; set; } = "";
    public bool Activo { get; set; } = true;
}

public sealed class UsuarioActualizarDto
{
    [Required, StringLength(150)] public string Nombre { get; set; } = "";
    [Required, StringLength(60)] public string Nombre_Usuario { get; set; } = "";
    [Required, StringLength(120)] public string Correo { get; set; } = "";
    public string Telefono { get; set; } = "";
    [Required] public string Rol { get; set; } = "";
    public bool Activo { get; set; } = true;
}

public sealed record ContrasenaAdminDto(string Contrasena);

// ---------------------------------------------------------------------------
// Configuración de la cuenta (cliente)
// ---------------------------------------------------------------------------
public sealed class PerfilActualizarDto
{
    [Required, StringLength(150)] public string Nombre { get; set; } = "";
    [Required, StringLength(120)] public string Correo { get; set; } = "";
    public string Telefono { get; set; } = "";
}

public sealed record ContrasenaCambioDto(string Actual, string Nueva);

