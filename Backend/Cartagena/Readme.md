# Cartagena — Guía de integración para frontend

> **Alcance:** contrato de consumo HTTP(S) confirmado para autenticación de administrador y CRUD de libros. Los endpoints de otros módulos se deben incorporar cuando se verifiquen sus controladores y DTO. Esta guía no sustituye la documentación OpenAPI generada por ASP.NET Core.

## 1. Arquitectura y estado de integración

El sistema tiene dos responsabilidades separadas:

| Servicio | Tecnología | Responsabilidad |
|---|---|---|
| Login | Node.js | Autentica al usuario y devuelve un JWT |
| API de Cartagena | ASP.NET Core 10 | Expone endpoints de libros y otros módulos del negocio |
| Base de datos | MySQL | Almacena libros y usuarios; **el frontend nunca se conecta directamente** |
| Gateway / proxy inverso | Nginx | Debe publicar ambos servicios mediante una única dirección HTTPS |

**Validado en la máquina de desarrollo:**

- `GET http://localhost:5000/api/core/health` → `200`.
- `GET http://localhost:5000/api/libros` → `200`.
- `GET http://localhost:5000/api/libros/LIB001` → disponible.
- `POST http://localhost:5000/api/libros` con JWT `ADMIN` emitido por el **Login nuevo** → `201 Created`.
- `POST /api/libros` sin JWT → `401 Unauthorized`.

**Estado actual (corregido):** el gateway del compose (`cartagena-csharp-gateway`) publica
`localhost:8081` y enruta `/api/v1/*` al login Node y `/api/*` a la API C#. La red
`libreria-network` se crea y el MySQL existente se conecta con alias `mysql` mediante
`./start-docker.sh`. El login también queda publicado en `localhost:3000` para el proxy de Vite.

**Arranque recomendado:** usa `./start-dev.sh` (nativo) o `./start-docker.sh` (Docker) desde la
raíz del repositorio. Consulta el `README.md` de la raíz para puertos, credenciales y diagnóstico.

## 2. URL base por ambiente

| Ambiente | URL | Estado |
|---|---|---|
| API C# local (desde la misma PC) | `http://localhost:5000` | Verificada |
| Login nuevo en Docker | Puerto 3000 dentro de su red Docker | Verificado mediante JWT; **no se ha confirmado puerto publicado al host** |
| Gateway antiguo | `http://localhost:8081` | No utilizar como integración definitiva hasta corregir enrutamiento |
| Integración / producción | `https://api.tu-dominio.com` | **Ejemplo**, reemplazar con una URL real después del despliegue |

Para un frontend instalado en otra computadora, `localhost` apunta a **esa computadora**, no al servidor donde corre la API. El equipo necesitará un host accesible en su red o un dominio HTTPS.

**Rutas recomendadas a publicar en el mismo origen:**

- `POST https://api.tu-dominio.com/api/v1/auth/login` → Login Node.js **nuevo**.
- `GET/PATCH/POST/DELETE https://api.tu-dominio.com/api/libros...` → API C#.
- `GET https://api.tu-dominio.com/api/core/health` → API C#.

Este esquema de dominio único es una **configuración objetivo**, no una URL existente.

## 2.1. Arranque local del backend (para integrantes que también prueben servicios)

En una instalación de desarrollo que tenga MySQL en Docker publicado en `127.0.0.1:3307` y el archivo `.env` configurado en la raíz de `Cartagena`, ejecutar:

```bash
cd ~/Programacion/LibreriaCartagena/Backend/Cartagena
set -a; source .env; set +a
export DB_HOST=127.0.0.1 DB_PORT=3307
dotnet run --project backend/Cartagena.Api
```

La API se probó en `http://localhost:5000`, pero el puerto efectivo depende de la configuración de inicio. Consultar los mensajes de `dotnet run` si usa otro.

El servicio Login correcto es el del Compose **actual**, observado como contenedor `cartagena-csharp-login-1`, no el antiguo `cartagena-presentacion-login-1`. El frontend no puede consultar por nombre de contenedor desde el navegador; necesita un proxy accesible que publique el endpoint de Login. No iniciar todos los Compose históricos indiscriminadamente porque hay múltiples contenedores similares en ejecución.

Para evitar errores de autenticación, `JWT_SECRET` y `JWT_ISSUER` de Node.js y ASP.NET Core deben coincidir en los **procesos en ejecución**. Nunca copiar estos valores al repositorio ni al frontend.

## 3. Inicio de sesión

**Solicitud**

```http
POST /api/v1/auth/login
Content-Type: application/json
```

```json
{
  "nombreUsuario": "admin",
  "contrasena": "TU_CONTRASEÑA"
}
```

**Respuesta de éxito — estructura observada en el servicio de Login:**

```json
{
  "exito": true,
  "datos": {
    "autenticado": true,
    "rol": "ADMIN",
    "token": "<JWT_DEVUELTO_POR_LOGIN>",
    "tipoToken": "Bearer",
    "expiraEn": 900
  }
}
```

La respuesta puede incluir campos adicionales (`mensaje`, `idUsuario`, `nombreUsuario`, etc.). `expiraEn: 900` significa que el token dura 15 minutos. No incluir contraseñas ni tokens reales en capturas, commits, issues o logs.

**Ejemplo de inicio de sesión (frontend):**

```ts
const respuesta = await fetch(`${API_URL}/api/v1/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ nombreUsuario, contrasena }),
});

const body = await respuesta.json();
if (!respuesta.ok || !body.exito || !body.datos?.token) {
  throw new Error(body.mensaje ?? 'No fue posible iniciar sesión');
}

const token = body.datos.token;
```

> El frontend solo debe acceder a una URL de Login que esté efectivamente conectada al microservicio nuevo. El Gateway antiguo puede emitir tokens con una clave distinta.

## 4. Enviar el token JWT

Para rutas protegidas, enviar siempre el encabezado:

```http
Authorization: Bearer <JWT_DEVUELTO_POR_LOGIN>
```

Ejemplo:

```ts
const respuesta = await fetch(`${API_URL}/api/libros`, {
  headers: { Authorization: `Bearer ${token}` },
});
```

`GET /api/libros` es público, de modo que obtener `200` **no demuestra** que un token sea válido. Para verificar autorización `ADMIN`, usar una operación administrativa en un entorno de pruebas y con datos de prueba.

### Recomendaciones de seguridad

- Usar **HTTPS** en producción; las pruebas actuales en `http://localhost:5000` son solo locales.
- Preferir guardar el access token **en memoria**. Esto implica volver a iniciar sesión si se recarga la página, a menos que se implemente un mecanismo de sesión más persistente.
- No almacenar contraseñas ni `JWT_SECRET` en frontend, `.env` de Vite, JavaScript compilado o Git.
- Un JWT expirado requiere volver a autenticarse. **No se ha confirmado un endpoint de refresh token.**
- No confiar únicamente en ocultar botones según el rol; el servidor es quien autoriza realmente las operaciones.

## 5. Endpoints verificados de libros

Base de rutas: `/api/libros`.

| Método | Ruta | Autenticación | Acción |
|---|---|---|---|
| `GET` | `/api/libros` | Pública | Lista hasta 200 libros, ordenados por título |
| `GET` | `/api/libros/{id}` | Pública | Obtiene un libro por ID |
| `POST` | `/api/libros` | JWT con rol `ADMIN` | Crea un libro |
| `PATCH` | `/api/libros/{id}` | JWT con rol `ADMIN` | Actualiza campos permitidos |
| `DELETE` | `/api/libros/{id}` | JWT con rol `ADMIN` | Elimina un libro |

### 5.1 Listar y filtrar

```http
GET /api/libros
GET /api/libros?categoria=Tecnologia
GET /api/libros?genero=Educativo
GET /api/libros?formato=Fisico
GET /api/libros?q=Code
```

La API acepta parámetros opcionales `categoria`, `genero`, `formato` y `q` (búsqueda de texto). Ejemplo de forma de respuesta:

```json
{
  "data": [
    {
      "id_libro": "LIB001",
      "titulo": "Clean Code",
      "autor": "Robert C. Martin",
      "precio": 450.00,
      "stock": 20,
      "stock_bajo": false,
      "stock_alto": true,
      "precio_oferta": "450.00"
    }
  ]
}
```

**Nota:** el objeto real puede incluir más campos (`isbn`, `editorial`, `categoria`, `imagen_portada`, etc.). `precio_oferta` se devuelve como **cadena decimal**, no como número; conviértelo explícitamente para operaciones aritméticas. La fila mostrada es ilustrativa, no la salida íntegra capturada de la API.

### 5.2 Detalle

```http
GET /api/libros/LIB001
```

La ruta ha sido probada, pero no se ha confirmado en esta guía si devuelve el objeto directamente o un envoltorio `data`. Comprobarlo en la respuesta real o en `/openapi/v1.json` antes de definir el tipo del frontend.

### 5.3 Crear (administrador)

```http
POST /api/libros
Authorization: Bearer <JWT>
Content-Type: application/json
```

```json
{
  "id_libro": "LIB006",
  "titulo": "Libro de prueba ASP.NET Core",
  "autor": "Administrador Cartagena",
  "precio": 350,
  "stock": 15,
  "stock_minimo": 3,
  "stock_maximo": 100,
  "descripcion": "Libro creado desde C#",
  "isbn": "9780000000001",
  "editorial": "Cartagena",
  "categoria": "Tecnologia",
  "genero": "Educativo",
  "formato": "Fisico",
  "imagen_portada": null,
  "descuento_porcentaje": 0
}
```

**Respuesta real validada:** `201 Created`

```json
{ "id_libro": "LIB006" }
```

No repetir ese `id_libro` si ya existe. El esquema exacto, obligatoriedad y validaciones de cada propiedad dependen de `LibroCrearDto`.

### 5.4 Modificar (administrador)

```http
PATCH /api/libros/LIB006
Authorization: Bearer <JWT>
Content-Type: application/json
```

```json
{
  "precio": 399.99,
  "stock": 25
}
```

**Respuesta implementada en el controlador:**

```json
{ "ok": true, "filas": 1 }
```

Campos permitidos en PATCH: `titulo`, `autor`, `precio`, `stock`, `stock_minimo`, `stock_maximo`, `descripcion`, `isbn`, `editorial`, `categoria`, `genero`, `formato`, `imagen_portada`, `descuento_porcentaje`.

### 5.5 Eliminar (administrador)

```http
DELETE /api/libros/LIB006
Authorization: Bearer <JWT>
```

**Respuesta implementada:**

```json
{ "ok": true }
```

Después de eliminarlo, el detalle debe responder `404 Not Found`.

## 6. Códigos HTTP y manejo de errores

| Estado | Interpretación | Comportamiento del frontend |
|---|---|---|
| `200 OK` | Consulta o cambio completado | Mostrar datos o confirmación |
| `201 Created` | Libro creado | Actualizar lista / navegar al detalle |
| `400 Bad Request` | Datos inválidos | Mostrar validaciones retornadas por la API |
| `401 Unauthorized` | Sin token, token vencido o firma incorrecta | Pedir nuevo inicio de sesión; **no reintentar infinitamente** |
| `403 Forbidden` | Usuario autenticado sin permiso suficiente | Mostrar «No tienes permiso» |
| `404 Not Found` | Recurso inexistente | Mostrar «No encontrado» |
| `409 Conflict` | Conflicto de regla de negocio | Mostrar mensaje de la API |
| `500` / `5xx` | Error del servidor | Mostrar error genérico; registrar diagnóstico sin secretos |

El cuerpo de error no está confirmado como único formato para todos los servicios. El frontend debe tolerar respuestas JSON y texto, y usar `response.ok`/`response.status` como fuente principal del resultado HTTP.

## 7. Cliente HTTP TypeScript reutilizable

El archivo adjunto [`src/api/cartagenaApi.ts`](src/api/cartagenaApi.ts) implementa:

- `login(nombreUsuario, contrasena)`.
- `setToken(...)` y `cerrarSesion()` con token en memoria.
- `listarLibros(filtros)`, `obtenerLibro(id)`, `crearLibro(libro)`, `actualizarLibro(id, cambios)`, `eliminarLibro(id)`.
- Una clase `ApiError` con código HTTP y cuerpo del error.

### Ejemplo en un componente

```ts
import { login, listarLibros, crearLibro, cerrarSesion } from './api/cartagenaApi';

await login('admin', passwordIngresadaPorUsuario);
const libros = await listarLibros({ categoria: 'Tecnologia' });
console.log(libros.data);

// En una pantalla exclusiva para administradores:
await crearLibro({
  id_libro: 'LIBTEST007',
  titulo: 'Ejemplo',
  autor: 'Equipo de pruebas',
  precio: 99,
  stock: 5,
  stock_minimo: 1,
  stock_maximo: 20,
  descripcion: 'Prueba',
  isbn: '9780000000002',
  editorial: 'Pruebas',
  categoria: 'Tecnologia',
  genero: 'Educativo',
  formato: 'Fisico',
  imagen_portada: null,
  descuento_porcentaje: 0,
});

cerrarSesion();
```

**Importante:** `login(...)` funcionará desde el navegador cuando `/api/v1/auth/login` apunte al microservicio nuevo, por ejemplo mediante un Gateway corregido.

## 8. Configuración del frontend

Para Vite/React/Vue, copiar `.env.frontend.example` al archivo `.env.local` de **la aplicación frontend** y ajustar la URL:

```dotenv
# Ejemplo: una sola URL, una vez configurado correctamente el Gateway HTTPS
VITE_API_BASE_URL=https://api.tu-dominio.com
```

Cuando el frontend y la API estén en el **mismo origen**, se puede dejar vacío:

```dotenv
VITE_API_BASE_URL=
```

En ese caso `fetch('/api/libros')` utiliza automáticamente el dominio del sitio. Evitar publicar una variable `VITE_JWT_SECRET`; las variables `VITE_*` terminan incluidas en el bundle del navegador.

Si frontend y API están en dominios diferentes, el servidor debe incluir el origen exacto del frontend en `CORS_ORIGIN` y habilitar solicitudes `Authorization` y `Content-Type`. La configuración observada en `Program.cs` activa CORS solo si `CORS_ORIGIN` no está vacío. Recuerda que **Login Node.js requiere su propia configuración de CORS** cuando se accede directamente desde otro origen.

## 9. OpenAPI y Scalar

La aplicación ASP.NET Core configura:

- `GET /openapi/v1.json`: especificación OpenAPI para importar en Swagger/Postman/Insomnia o generar tipos.
- `/scalar/v1`: interfaz de documentación Scalar (verificar ruta final según la versión y el proxy).

Si la URL publicada no incorpora aún estos endpoints, consultarlos directamente en la máquina de desarrollo con `http://localhost:5000/openapi/v1.json`.

**No asumir que el OpenAPI de ASP.NET Core incluye los endpoints de Login Node.js:** pertenecen a otro servicio.

## 10. Checklist de entrega para backend / DevOps

- [ ] Elegir un único Login canónico: el que emite JWT con el secreto compartido con C#.
- [ ] Configurar el Gateway para enviar `/api/v1/...` de autenticación al Login nuevo y `/api/...` de libros a C#.
- [ ] Comprobar que Gateway y ambos servicios se ven por sus redes Docker; evitar DNS ambiguo `login` con múltiples contenedores.
- [ ] Publicar una URL accesible para los equipos y activar TLS/HTTPS con certificado válido.
- [ ] Configurar `JWT_SECRET` y `JWT_ISSUER` de forma idéntica entre Login y API C#; mantener el secreto solo en servidores.
- [ ] Verificar CORS si se usan orígenes distintos.
- [ ] Repetir login → POST protegido → GET → PATCH → DELETE a través del **Gateway publicado**.
- [ ] Comprobar que `401`, `403` y `404` son tratados correctamente en frontend.
- [ ] Publicar los DTO y controladores de los otros módulos antes de documentar sus rutas como definitivas.
- [ ] No subir `.env`, contraseñas, claves o JWT reales al repositorio.

## 11. Ejemplos rápidos con cURL

```bash
# La URL es un ejemplo de configuración final, NO una dirección ya desplegada.
export API_URL='https://api.tu-dominio.com'

# Consulta pública
curl -i "$API_URL/api/libros"

# Detalle público
curl -i "$API_URL/api/libros/LIB001"

# Para operaciones protegidas, realizar antes el login y guardar el token
# de manera segura; no pegar un JWT real en el README ni en Git.
curl -i "$API_URL/api/libros" -H "Authorization: Bearer $TOKEN"
```

---

**Mantenimiento:** actualizar esta guía cuando cambien los DTO, las rutas, el flujo de autenticación o el Gateway. Mantener un contrato de API probado y estable ayuda a evitar errores de integración en el frontend.
