# Arquitectura de la POC 01

## Decisión

La impresora virtual es una Print Support App v4. Windows instala la cola al registrar el paquete MSIX y activa una tarea en segundo plano cuando una aplicación imprime. El contrato es `windows.printSupportVirtualPrinterWorkflow`.

No se usa `Windows.Devices.Printers.VirtualPrinter`. En el ejemplo oficial de Microsoft esas APIs de alta y baja dinámica siguen desactivadas con `VIRTUAL_PRINTER_DISABLED` porque la proyección de .NET no las expone de forma utilizable. Crear un sustituto simulado no registraría una cola real.

Tampoco se instalan controladores V3 o V4 ni se activa TESTSIGNING. El registro en Windows 11 es sideloading del MSIX firmado: el certificado público va a `Cert:\LocalMachine\TrustedPeople` y el modo de desarrollador no se activa por defecto.

```mermaid
flowchart LR
  appCliente[Aplicacion convencional] --> cola["Cola NPB - Prueba Virtual"]
  cola --> spooler[Print Spooler]
  spooler --> tarea[VirtualPrinterBackgroundTask]
  tarea --> almacen[JobCaptureStore]
  almacen --> localState["LocalState/Neobash/PrintBridge/Jobs"]
```

## Proyectos

| Proyecto | Función |
| --- | --- |
| `src/Core` | Almacén de trabajos, metadatos y cierre único de la tarea. No referencia WinUI ni una ruta fija. |
| `src/Windows/VirtualPrinter.Tasks` | Tarea WinRT que recibe el flujo OXPS. |
| `src/Windows/VirtualPrinter` | Paquete MSIX y ventana mínima de la aplicación. La ventana no imprime ni simula trabajos. |
| `src/Agent` | Proceso existente. Esta POC no lo conecta al spooler ni a la red. |

`JobCaptureStore` recibe un `IJobFileStore`. La tarea le pasa la carpeta local del paquete. Más adelante el Agent puede construir `FileSystemJobFileStore` con otra ruta, sin cambiar el almacén.

## Impresora

Hay una sola declaración `PrintSupportVirtualPrinter`:

- Nombre visible: `NPB - Prueba Virtual`
- URI estable: `neobash-print-bridge:npb-prueba-virtual`
- Formato preferido: `application/oxps`
- Sin `OutputFileTypes`, para que Windows entregue el trabajo sin un diálogo de guardar archivo
- Sin formatos de paso directo
- PDC con tamaños A4 y carta, orientación y color. No hay PDR porque no se declaran recursos personalizados que localizar

## Identidad y versiones

Permanecen fijos entre compilaciones:

- Nombre del paquete: `Neobash.PrintBridge.VirtualPrinter`
- Publisher: `CN=Neobash Print Bridge POC`

La versión inicial es `1.0.0.0`. Vive en `src/Windows/Directory.Build.props` como `NpbPackageVersion` y debe coincidir con `Package.appxmanifest`. La compilación se detiene si difieren.

Para reemplazar una instalación posterior se incrementa solo esa versión, se conserva el mismo certificado y se instala el paquete nuevo sobre el anterior. Cambiar el nombre o el Publisher crearía otra identidad y podría dejar otra cola.

## Tarea de impresión

`VirtualPrinterBackgroundTask` obtiene el aplazamiento, se suscribe a `VirtualPrinterDataAvailable` y después llama a `Start`.

- Una excepción al iniciar cierra el aplazamiento y no informa éxito.
- Una cancelación marca el trabajo como cancelado. Si la captura ya hubiera escrito bytes, el resultado comunicado a Windows sigue siendo cancelado.
- `CompleteJob` y el aplazamiento se ejecutan una sola vez mediante `VirtualPrinterJobCompletion`.
- `CompleteJob(Succeeded)` solo ocurre si `JobCaptureStore` cerró un archivo con tamaño mayor que cero.
- El error queda en `error.txt` del trabajo, cuando el directorio ya existe, y en `LocalState\Neobash\PrintBridge\Logs\task.log`.

## Capacidades

El manifiesto declara únicamente `runFullTrust`.

Windows App SDK ejecuta la aplicación WinUI 3 empaquetada como proceso de escritorio de plena confianza. El ejemplo oficial de Microsoft declara la misma capacidad restringida. Sin ella, el paquete de escritorio no arranca y el spooler no puede activar la tarea.

No se declara `internetClient`, acceso amplio al sistema de archivos ni ninguna otra capacidad. La tarea escribe solo en la carpeta local del paquete.

## Captura

Cada trabajo ocupa `Neobash/PrintBridge/Jobs/{id}/` dentro del almacén configurado:

- `document.oxps` cuando el sistema declara `application/oxps`; en otro caso, `document.bin`
- `metadata.json` con identificador, fecha, impresora, URI, aplicación de origen si está disponible, formato declarado, tamaño y estado
- `error.txt` cuando la captura no termina bien

El componente no valida la estructura OpenXPS. Una firma ZIP `PK` solo demuestra que se guardaron esos bytes. El formato registrado es el tipo declarado por Windows, no una comprobación del documento.

Los identificadores generados combinan la hora UTC y un GUID. Un identificador con `..` o separadores de ruta se rechaza y no crea directorios.
