# login/ — copia de trabajo del microservicio de login

Este directorio es una **copia** de `Backend/Cartagena/servicios/microservicio-login`
necesaria para que el `Dockerfile` de `Cartagena.Api` pueda incluirla en la imagen:

* Render usa como contexto de build la carpeta `backend/Cartagena.Api/`, así que
  no puede copiar archivos que quedan fuera de ella.
* En el contenedor, `entrypoint.sh` arranca este servicio Node en el puerto 3000
  y la API C# hace de front sirviendo `/api/v1/*` mediante un proxy inverso (YARP).

**Si cambias el microservicio original, vuelve a copiarlo aquí:**

```sh
cd Backend/Cartagena
rsync -a --delete --exclude node_modules --exclude .env \
  servicios/microservicio-login/src/ backend/Cartagena.Api/login/src/
cp servicios/microservicio-login/package.json servicios/microservicio-login/package-lock.json \
  backend/Cartagena.Api/login/
```
