# Manual de Usuario — Librería Cartagena

Sistema de tienda en línea **Cartagena Bookstore**: catálogo de libros, compras en línea,
cuentas de usuario y panel de administración.

| Dónde entrar | Dirección |
|---|---|
| Sitio en producción | https://libreria-carta-agena.web.app |
| Dirección alternativa | https://libreria-carta-agena.firebaseapp.com |
| Soporte por correo | [libreriacartaagena302987@gmail.com](mailto:libreriacartaagena302987@gmail.com) |

> El sitio funciona en cualquier navegador actualizado (Chrome, Firefox, Edge o Safari)
> y se adapta a teléfono, tablet y computadora. No requiere instalar nada.

---

## 1. Crear una cuenta

1. Entra al sitio y pulsa **«Regístrate aquí»** debajo del formulario de acceso.
2. Completa los datos que pide el formulario (nombre de usuario, correo y contraseña).
3. Marca la casilla **«No soy un robot»** (verificación anti-bot). Si no la marcas,
   el formulario avisa: *«Completa la verificación anti-bot para entrar»*.
4. Pulsa **«Crear mi cuenta»**.

Si el nombre de usuario ya existe, el formulario lo indica en el momento en que escribes.

### Roles (quién puede ver qué)

| Rol | Qué puede hacer |
|---|---|
| **CLIENTE** | Comprar, ver su carrito, sus pedidos y su perfil. |
| **ADMIN** | Todo lo anterior **más** el panel de administración completo. |
| **VENDEDOR** | Acceso normal de cliente: **no tiene panel de administración**. |
| **REPARTIDOR** | Acceso de cliente; las funciones de reparto se asignan desde el panel. |

---

## 2. Iniciar sesión

1. Escribe tu **correo o nombre de usuario** y tu **contraseña**.
   - Pulsa **MOSTRAR** si quieres ver la contraseña mientras escribes.
2. Marca **«No soy un robot»**.
3. Pulsa **«Entrar a la Madriguera»**.

Después de entrar serás llevado a la página que intentabas abrir, o a tu catálogo.

> ⏱ **Nota:** la sesión dura **15 minutos** por seguridad. Al expirar, el sitio te pide
> volver a entrar con tus mismos datos. Si lo ves muy seguido, puedes pedir una
> sesión más larga al equipo técnico.

---

## 3. ¿Olvidaste tu contraseña?

1. En la pantalla de acceso pulsa **«¿Olvidaste tu contraseña?»**.
2. Escribe tu **correo o nombre de usuario** y pulsa **«Enviar Enlace de Recuperación»**.
3. Revisa tu bandeja de entrada: recibirás un correo con un **token de un solo uso**
   válido por **20 minutos**.
4. Copia el token, vuelve a la pantalla de recuperación, pégalo y escribe tu
   **nueva contraseña**.
5. Inicia sesión con la contraseña nueva.

**Si aparece el mensaje:** *«El servicio de correo no está configurado, así que no
pudimos enviar el token…»* significa que el servidor de correo aún no está dado de
alta en el servidor. En ese caso escribe a
[libreriacartaagena302987@gmail.com](mailto:libreriacartaagena302987@gmail.com)
y te ayudamos a restablecerla manualmente.

---

## 4. Comprar un libro

1. **Buscar:** usa la barra de búsqueda del encabezado (título, autor o ISBN) o
   recorre el catálogo de la portada.
2. **Ver detalle:** pulsa un libro para ver descripción, precio, reseñas y stock.
3. **Agregar al carrito:** pulsa el botón de agregar. El contador del carrito en el
   encabezado se actualiza al instante.
4. **Carrito:** revisa cantidades, elimina lo que no quieras y pulsa **«Continuar
   con la compra»**.
5. **Datos de entrega:** elige o registra tu domicilio.
6. **Pago:** confirma el método de pago y finaliza.
7. Recibirás el comprobante y podrás descargar el **ticket en PDF** desde
   **Mi cuenta → Mis compras**.

---

## 5. Mi cuenta

Entra a **«Mi Cuenta»** (esquina superior derecha) para:

| Sección | Qué encuentras |
|---|---|
| **Perfil** | Tus datos, tu correo y tu **selector de personaje** (avatar) para personalizar la cuenta. |
| **Mis compras** | Historial de compras, estado y descarga del ticket en PDF. |
| **Mis pedidos** | Seguimiento de tus pedidos y envíos. |
| **Cambiar contraseña** | Botón visible para actualizar tu contraseña pidiendo la actual. |
| **Cerrar sesión** | Cierra la sesión en este dispositivo. |

> La sección **«Preferencias»** ya no existe: todo lo que estaba ahí pasó a **Perfil**
> y a **Cambiar contraseña**.

---

## 6. Panel de administración (solo rol ADMIN)

Si tu cuenta es **ADMIN**, verás **«Panel de administración»** con el saludo
*«Hola, …»* y el sello **ADMIN**. El panel confirma primero tu rol con el backend
(*«El backend confirmó tu rol ADMIN…»*) y luego muestra los módulos:

| Módulo | Qué puedes hacer ahí |
|---|---|
| **Usuarios y roles** | Dar de alta usuarios, asignar roles y deshabilitar cuentas. |
| **Catálogo e inventario** | Alta, edición y borrado de libros, precios, descuentos y alertas de stock. |
| **Pedidos y ventas** | Seguimiento de compras, pagos y estados de venta. |
| **Logística y entregas** | Actualizar estados de envío, guías manuales y validar entregas. |
| **Proveedores y compras** | Directorio de proveedores, órdenes de compra, mermas, devoluciones y reportes. |
| **Reportes** | Indicadores de ventas y descarga del reporte de inventario en PDF. |

Al pie del panel siempre está el contacto de soporte:
**libreriacartaagena302987@gmail.com**.

### 6.1 Proveedores y compras

Ruta: **Panel → Proveedores y compras** (pestañas en la parte superior).

**Agregar un proveedor**

1. Pulsa **«+ Nuevo proveedor»**.
2. Llena el formulario. **Nombre** es obligatorio; el resto (razón social, RFC,
   teléfono, contacto, correo y dirección) se guardan tal cual los escribas.
3. Pulsa **«Registrar proveedor»**. Verás el aviso
   *«Proveedor "…" registrado»* y el contador *«2 de 2 proveedores»*.

**Editar:** pulsa **«Editar»** en la fila, cambia lo que necesites y guarda.
**Eliminar:** pulsa **«Eliminar»** y confirma. Solo es posible si el proveedor
**no tiene compras registradas**.

**Búsqueda:** escribe en *«BUSCAR PROVEEDOR»* para filtrar por nombre, razón
social, RFC, contacto, correo o teléfono.

Otras pestañas del módulo:

- **Órdenes de compra:** crea órdenes por proveedor, recíbelas y actualiza el stock.
- **Mermas y devoluciones:** registra mermas y devoluciones al proveedor.
- **Reportes de compras:** resumen mensual de abastecimiento.

---

## 7. Errores frecuentes

| Mensaje | Qué significa | Qué hacer |
|---|---|---|
| *«Completa la verificación anti-bot para entrar»* | No marcaste el reCAPTCHA | Marca la casilla «No soy un robot» y vuelve a enviar. |
| *«Acceso restringido»* | Tu rol no alcanza para esa sección | Pide a un administrador que te asigne el rol adecuado. |
| *«El servicio de correo no está configurado…»* | Falta configurar el envío de correos | Escribe a libreriacartaagena302987@gmail.com. |
| *«Error 404 al consultar /proveedores»* | El backend no tenía esa ruta | Ya está corregido; recarga la página (Ctrl + F5). |
| *«Requiere rol ADMIN» / 403* | Entraste con una cuenta que no es administrador | Entra con una cuenta ADMIN. |
| *«Ya existe un proveedor con ese RFC»* | RFC duplicado (409) | Usa el RFC real o edita el proveedor existente. |
| *«No se puede eliminar el proveedor porque tiene compras registradas»* | El proveedor tiene historial (409) | Déjalo activo; el historial no se borra. |
| La página sale **en blanco** | Caché de una versión anterior | Recarga con **Ctrl + F5** (ya quedó corregido en el servidor). |
| *«Tu sesión expiró»* / te manda al login | Pasaron los 15 minutos | Vuelve a iniciar sesión. |

---

## 8. Contacto y soporte

- **Correo:** [libreriacartaagena302987@gmail.com](mailto:libreriacartaagena302987@gmail.com)
  (también al pie del panel de administración).
- **Aviso de Privacidad** y **Términos y Condiciones**: en el pie del sitio.

---

*Manual de usuario · Librería Cartagena · Edición 2026.*
