# Hoja de ruta

## POC 01 — captura demostrada

Cola virtual registrada (`NPB - Prueba Virtual`, formato preferido `application/oxps`). Tres capturas independientes desde Bloc de notas quedaron verificadas, con el documento OXPS, los metadatos, el cierre comunicado a Windows, la cola despejada y la impresora predeterminada conservada.

Siguen abiertos la desinstalación del paquete y el evento 307 del servicio de impresión. Ninguno de los dos acredita papel impreso por NPB.

Por cada impresión, la tarea WinRT escribe en `LocalState\Neobash\PrintBridge\Jobs\{identificador}\` del paquete MSIX (`{identificador}` = marca UTC + GUID, sin reutilizar carpetas):

| Artefacto | Contenido exacto |
| --- | --- |
| `document.oxps` | Bytes del flujo que envía Windows cuando el tipo declarado es `application/oxps`; si no, `document.bin`. No se valida estructura OpenXPS: solo se copia el stream. |
| `metadata.json` | `identificador`, `fecha` (recepción UTC), `impresoraVirtual` (`NPB - Prueba Virtual`), `uriImpresora` (URI de la sesión o `neobash-print-bridge:npb-prueba-virtual`), `aplicacionOrigen` (nombre visible de la app que imprimió, si el sistema lo expone), `formato` (tipo declarado por Windows, p. ej. `application/oxps`), `tamanoBytes`, `estado` (`Succeeded`, `Failed` o `Canceled`), `archivo` (nombre del documento guardado) y `error` (mensaje cuando corresponde). |
| `error.txt` | Texto del fallo de captura, solo si el trabajo no termina bien. |
| `Logs\task.log` | Traza de la tarea (inicio de sesión, validación de PrintTicket, captura, cierre ante Windows); no forma parte del directorio del trabajo. |

El XML del PrintTicket solo se valida contra el PDC en memoria. No se guarda como archivo. La captura no extrae páginas ni contenido más allá del stream y de los campos anteriores.

## A1 — motor local, pendiente de papel

NPB Desktop ya puede guardar un destino, elegir un papel anunciado por esa cola e imprimir un PDF existente por el controlador, sin diálogo de NPB. La prueba automática cubre el rechazo de una cola ausente con un catálogo simulado. La impresión física todavía no está comprobada. A1 no queda aprobado hasta ver el papel. El procedimiento está en [A1-prueba-fisica.md](A1-prueba-fisica.md).

`application/oxps` se rechaza antes del spooler. Ese rechazo no es el conector de las capturas.

## Pendiente

- **A2.** Leer una captura OXPS ya existente y enviarla con un adaptador distinto. No usa `IJobFileStore.Open`, que solo crea carpetas nuevas.
- **B.** Publicar destinos en la red local, con dispositivo autorizado. Guardar un destino en este equipo no lo publica.
- **C.** PrintService propio de NPB para el selector de Android. El recorrido es Android, transporte, NPB Windows, controlador y papel.
- **D.** Conexión saliente del anfitrión entre redes, sin abrir un puerto público y sin depender de GELIA.

También quedan fuera el aprovisionamiento dinámico de varias colas virtuales, la administración comercial y cualquier generación de documentos de negocio.
