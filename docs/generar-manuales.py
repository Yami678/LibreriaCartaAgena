#!/usr/bin/env python3
"""Genera los manuales de la Librería Cartagena en DOCX y PDF.

Uso:  python3 docs/generar-manuales.py

Toma como fuente los Markdown de esta misma carpeta, les pone una portada
con el logo de la UAdeO, la materia (SOA), la tienda, los integrantes y la
fecha, y convierte el resultado con LibreOffice (soffice) a .docx y .pdf.

Requisitos: python3 con la librería `markdown` y LibreOffice (`soffice`).
"""
import base64
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

import markdown

DOCS = Path(__file__).resolve().parent          # carpeta docs/
LOGO = DOCS / "logo-uadeo.png"
SALIDA = DOCS                                    # .docx / .pdf finales
PERFIL = "file:///tmp/opencode/soffice-perfil"   # perfil aislado de LibreOffice

MATERIA = "SOA"
TIENDA = "Librería Cartagena"
FECHA = "03 de noviembre de 2026"
VINO = "#982850"                                 # color del logo UAdeO

INTEGRANTES = [
    ("Sarahi Yamileth García Bojórquez", "23020009"),
    ("Aranzazú Acosta Medina", "23021126"),
    ("Alan Zayas Maldonado", "23020112"),
    ("Cesar Omar Karass Cota", "23020143"),
]

MANUALES = [
    ("Manual-de-Usuario", "Manual de Usuario",
     "Guía de uso para clientes y administradores de la tienda en línea."),
    ("Manual-Tecnico", "Manual Técnico",
     "Arquitectura, despliegue y mantenimiento para el equipo de desarrollo."),
]

CSS = """
@page { margin: 2cm; }
body { font-family: "Liberation Sans", Arial, Helvetica, sans-serif;
       font-size: 11pt; color: #212121; line-height: 1.42; }
h1 { font-size: 20pt; color: %(vino)s; margin: 0 0 6pt 0; }
h2 { font-size: 14pt; color: %(vino)s; margin: 16pt 0 6pt 0;
     border-bottom: 1.5pt solid %(vino)s; padding-bottom: 2pt; }
h3 { font-size: 12pt; color: #333333; margin: 12pt 0 4pt 0; }
p  { margin: 4pt 0; text-align: justify; }
ul, ol { margin: 4pt 0 8pt 18pt; }
li { margin: 2pt 0; }
table { border-collapse: collapse; width: 100%%; margin: 8pt 0 12pt 0;
        font-size: 10pt; }
th { color: #ffffff; border: 0.75pt solid #8c8c8c; padding: 4pt 6pt;
     text-align: left; }
td { border: 0.75pt solid #8c8c8c; padding: 4pt 6pt; vertical-align: top;
     text-align: left; }
code { font-family: "DejaVu Sans Mono", "Liberation Mono", monospace;
       font-size: 9.5pt; background: #eeeeee; }
pre { font-family: "DejaVu Sans Mono", "Liberation Mono", monospace;
      font-size: 8.5pt; border: 0.75pt solid #bbbbbb;
      padding: 6pt; margin: 8pt 0; white-space: pre; }
pre code { background: none; font-size: 8.5pt; }
blockquote { margin: 8pt 0; padding: 4pt 10pt; border-left: 3pt solid %(vino)s;
             color: #444444; }
hr { border: none; border-top: 0.75pt solid #bbbbbb; margin: 12pt 0; }
a { color: %(vino)s; text-decoration: none; }
"""

PORTADA_CSS = """
.portada { text-align: center; }
.portada .logo { width: 5cm; margin: 0 auto 6pt auto; }
.portada .escuela { font-size: 10.5pt; letter-spacing: 1.5pt; color: #666666;
                    margin: 0 0 2pt 0; }
.portada .materia { font-size: 13pt; color: %(vino)s; margin: 14pt 0 2pt 0; }
.portada .materia b { font-size: 20pt; }
.portada .titulo { font-size: 27pt; color: %(vino)s; font-weight: bold;
                   margin: 4pt 0; }
.portada .tienda { font-size: 13pt; color: #333333; margin: 2pt 0 2pt 0; }
.portada .bajada { font-size: 10pt; color: #666666; margin: 0 0 10pt 0; }
.portada .regla { border-top: 2.5pt solid %(vino)s; width: 60%%;
                  margin: 0 auto 10pt auto; }
.portada .rotulo { font-size: 11pt; color: %(vino)s; margin: 0 0 4pt 0; }
.portada table.integrantes { width: 80%%; margin: 0 auto; font-size: 10.5pt; }
.portada table.integrantes td { padding: 3pt 6pt; }
.portada td.num { text-align: center; white-space: nowrap; }
.portada .fecha { margin-top: 14pt; font-size: 11pt; color: #333333; }
.portada .pie { margin-top: 16pt; font-size: 9pt; color: #888888; }
"""


def posproceso(html: str) -> str:
    """Ajustes que LibreOffice necesita para que el HTML salga bien.

    - Dibuja bordes de tabla solo si la etiqueta trae el atributo `border`.
    - Ignora el color de fondo en CSS: hay que usar el atributo `bgcolor`.
    - Descarta el contenido de <thead> (se pierde la fila de encabezado).
    """
    html = html.replace("<thead>", "").replace("</thead>", "")
    html = html.replace("<tbody>", "").replace("</tbody>", "")
    html = html.replace("<table>", '<table border="1">')
    html = html.replace("<th>",
                        '<th bgcolor="%s" style="color:#ffffff">' % VINO)
    return html


def portada(titulo: str, bajada: str) -> str:
    filas = "\n".join(
        f'      <tr><td>{n}</td><td class="num">{c}</td></tr>'
        for n, c in INTEGRANTES)
    # Las imágenes referenciadas por ruta quedan *vinculadas* (no incrustadas)
    # en el ODT/DOCX: con data URI LibreOffice las guarda dentro del archivo.
    b64 = base64.b64encode(LOGO.read_bytes()).decode()
    return f"""<div class="portada">
  <img class="logo" src="data:image/png;base64,{b64}"
       width="170" height="170" alt="Logo UAdeO">
  <p class="escuela">UNIVERSIDAD AUTÓNOMA DE OCCIDENTE</p>
  <div class="regla"></div>
  <p class="materia">Materia<br><b>{MATERIA}</b></p>
  <p class="titulo">{titulo}</p>
  <p class="tienda">{TIENDA} — tienda en línea</p>
  <p class="bajada">{bajada}</p>
  <div class="regla"></div>
  <p class="rotulo">Integrantes</p>
  <table class="integrantes">
    <tr><th>Nombre</th><th>Número de cuenta</th></tr>
{filas}
  </table>
  <p class="fecha">Fecha de creación: {FECHA}</p>
  <p class="pie">Documento académico — Materia {MATERIA} · {TIENDA}</p>
</div>
"""


def construir_html(nombre: str, titulo: str, bajada: str, destino: Path) -> Path:
    md = (DOCS / f"{nombre}.md").read_text(encoding="utf-8")
    cuerpo = markdown.markdown(
        md, extensions=["tables", "fenced_code", "sane_lists", "toc"])
    html = f"""<!DOCTYPE html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>{titulo} — {TIENDA}</title>
<style>
{CSS % {'vino': VINO}}
{PORTADA_CSS % {'vino': VINO}}
</style>
</head>
<body>
{portada(titulo, bajada)}
<div class="contenido">
{cuerpo}
</div>
</body>
</html>
"""
    html = posproceso(html)
    # La portada ocupa la página 1; el contenido empieza en la 2. LibreOffice
    # aplica page-break-before al primer elemento de bloque, no al div contenedor.
    html = re.sub(r"<h1(?![^>]*page-break)",
                  '<h1 style="page-break-before: always"', html, count=1)
    salida = destino / f"{nombre}.html"
    salida.write_text(html, encoding="utf-8")
    return salida


def convertir(entrada: Path, fmt: str, salida: Path) -> Path | None:
    cmd = ["soffice", f"-env:UserInstallation={PERFIL}",
           "--headless", "--norestore",
           "--convert-to", fmt, "--outdir", str(salida), str(entrada)]
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
    destino = salida / (entrada.stem + "." + fmt.split(":")[0])
    if not destino.exists():
        print(f"    FALLÓ {fmt}: {r.stdout} {r.stderr}")
        return None
    print(f"    {fmt} -> {destino.name}")
    return destino


def main():
    if not LOGO.exists():
        raise SystemExit(f"Falta el logo: {LOGO}")
    if not shutil.which("soffice"):
        raise SystemExit("No se encontró soffice (LibreOffice).")

    with tempfile.TemporaryDirectory(prefix="manuales-") as tmp:
        tmpdir = Path(tmp)
        for nombre, titulo, bajada in MANUALES:
            print(f"== {nombre}")
            html = construir_html(nombre, titulo, bajada, tmpdir)
            print(f"    html -> {html.name}")
            odt = convertir(html, "odt", tmpdir)
            if odt:
                convertir(odt, "docx", SALIDA)
                convertir(odt, "pdf", SALIDA)
    print("Listos en", SALIDA)


if __name__ == "__main__":
    main()
