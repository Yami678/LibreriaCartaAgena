#!/bin/sh
# Contenedor único con dos procesos:
#   1. microservicio de login (Node) en el puerto interno 3000
#   2. API C# en 8080, que ademas hace de front: proxy de /api/v1/* al login
# El proceso principal es la API C# (exec) para que Render le pueda enviar
# SIGTERM y apagar todo de forma ordenada.
set -eu

export PORT=3000
export NODE_ENV="${NODE_ENV:-production}"

# La API C# ya inyecta las cabeceras CORS (UseCors). Si el login tambien las
# pusiera, el navegador veria Access-Control-Allow-Origin duplicado y rechazaria
# la peticion, por eso se deshabilitan solo en este proceso.
cd /app/login
CORS_ORIGIN="" node src/server.js &
LOGIN_PID=$!
echo "Microservicio de login (Node) escuchando en el puerto $PORT (pid $LOGIN_PID)."

cd /app
exec dotnet Cartagena.Api.dll
