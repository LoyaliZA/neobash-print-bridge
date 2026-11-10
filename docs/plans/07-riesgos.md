# Incremento 0 — riesgos

Cada ítem lleva una sola clase:

- resuelto con evidencia, cuando el código o una prueba ya cierran el punto;
- decisión recomendada, cuando este contrato elige un camino y el código aún no lo tiene;
- requiere prueba de concepto, cuando hace falta un resultado real antes de construir el incremento que depende de ello;
- bloqueante antes de implementar, cuando no se debe abrir ese incremento sin el cierre;
- puede diferirse, cuando está fuera del tramo indicado y no bloquea el anterior.

Un ítem puede citar evidencia y, además, dejar una cola distinta en otra clase. Aquí no se duplica: la clase es la que manda para el siguiente paso.

## Ya cerrados por el código actual

- El agente no imprime ni escucha. Clase: resuelto con evidencia. `Worker.ExecuteAsync` solo registra la hora cada segundo. `Program.cs` no añade otros servicios.
- Conexiones y Accesos no operan. Clase: resuelto con evidencia. `Activate` vacío y textos de «No disponible» / «Ningún equipo vinculado.»
- Hay dos historiales separados. Clase: resuelto con evidencia. La tarea escribe en `ApplicationData.Current.LocalFolder\Neobash\PrintBridge\Jobs`. Desktop escribe en `%LOCALAPPDATA%\Neobash\PrintBridge\Desktop\jobs`. No hay código que cruce los identificadores.
- El repositorio no demuestra lectura cruzada de `LocalState`. Clase: resuelto con evidencia de la ausencia. La tarea escribe en su carpeta. `PackageIdentityTests` solo mira textos del csproj y del manifiesto. No hay una prueba que abra la carpeta del paquete desde otro proceso. No se convierte esa ausencia en «es imposible leerla».
- La deduplicación actual es `TryBegin` por existencia del JSON, con candado de instancia. Clase: resuelto con evidencia. No es una clave de dispositivo ni un bloqueo entre procesos (`FileJobRecordStore`, `DesktopStoreTests`).
- `Uncertain` no reimprime en Desktop. Clase: resuelto con evidencia. `JobRecovery` cambia la fase y el detalle, y no llama al spooler. Un `TryBegin` posterior devuelve falso.
- La confirmación física no es una fase. Clase: resuelto con evidencia. Solo se anota tras `SubmittedToSpooler` y la fase se queda igual.
- El motor PDF rechaza lo demás. Clase: resuelto con evidencia. `PrintDispatcher` y `PdfSpoolerAdapter`.
- La validación estructural OpenXPS no existe. Clase: resuelto con evidencia. `OpenXpsLimits.StructuralValidationAvailable` es `false` y la prueba de la firma `PK` no escribe un campo de validez.
- El PrintTicket se valida en memoria y no se guarda. Clase: resuelto con evidencia. El registro dice «resuelto» o «no válido». El PDC no incluye 80 mm.
- El raster de PDF tiene topes y no inventa DPI. Clase: resuelto con evidencia. `PageLayoutCalculator` y el modo EMF de `PdfiumPrintSession`. La calidad en papel no está cerrada; eso es otro ítem.
- La ventana arranca antes del catálogo y de la recuperación, en tema lavanda `#EDEDF6` e índigo `#3D4BFF`. Clase: resuelto con evidencia. `App.xaml.cs` y `Light.xaml`.
- Una sola cola virtual, sin `internetClient` y sin `broadFileSystemAccess`. Clase: resuelto con evidencia. Manifiesto, `MinVersion` `10.0.26100.0`.
- GELIA-NV no está integrado. Clase: resuelto con evidencia. Texto de la página de Conexiones y `docs/roadmap.md`.

## Decisiones de este contrato

- Autenticación, autorización por destino y TLS reales en la primera prueba LAN. La UI queda en el Incremento 4. Clase: decisión recomendada.
- SQLite para trabajos nuevos, estados, identidades y claves del agente en el Incremento 1. Clase: decisión recomendada. El JSON no ofrece unicidad entre procesos ni conflicto por contenido.
- Los JSON históricos de Desktop se migran en el Incremento 4. Clase: decisión recomendada. Puede diferirse respecto del Incremento 1, y no antes: el 4 es su sitio.
- Máquinas de estado separadas y campo físico solo tras el spooler. Clase: decisión recomendada. El enum actual ya está separado de la nota; la máquina de transferencia es nueva.
- Clave durable dispositivo + origen, conflicto si cambia el cuerpo o el destino, incompleto distinto de aceptado, `Uncertain` sin reimpresión automática. Clase: decisión recomendada.
- Certificado fijado, código de un solo uso, Credential Manager o DPAPI de la cuenta, revocación y renovación sin camino anónimo. Clase: decisión recomendada. Ninguno de esos API está en `src`.
- Agente del MVP como proceso de la sesión interactiva, aparte de WinUI. Clase: decisión recomendada.
- Extraer el motor PDF a una biblioteca Windows compartida en el Incremento 1, sin `UseWinUI` en el agente. Clase: decisión recomendada. Hoy los tres adaptadores están en el csproj de Desktop y el agente solo referencia Core. Que `Windows.Data.Pdf` compile fuera de WinUI no está demostrado: lo tiene que demostrar la compilación, y un fallo no se tapa referenciando la ventana.
- La huella inicial se verifica por un canal independiente y el código queda ligado a ella. Vigencia, uso e identidad se exigen. Los demás errores TLS se rechazan. Clase: decisión recomendada. El código actual no autentica, así que esto no es una observación.
- Los destinos LAN viven solo en SQLite del agente, dados de alta por configuración local. `destinations.json` no se comparte. El cliente no envía nombres de cola. Clase: decisión recomendada.
- El despacho espera al commit durable. Dos peticiones con la misma clave no producen dos despachos. Los huérfanos se borran al arrancar. Clase: decisión recomendada. `FileJobRecordStore` no cumple ese orden.
- Una cola virtual vinculada a un destino remoto, sin alta dinámica. Clase: decisión recomendada. Encaja con el único `PrintSupportVirtualPrinter` del manifiesto.
- Papel de hojas primero. Clase: decisión recomendada. A4 y carta son los tamaños que el ticket ya acepta.
- Topes de 32 MiB, una transferencia en curso y 30 aceptaciones por 10 minutos. Clase: decisión recomendada. No salen de una medición del repositorio; sirven para probar el receptor.
- Firewall descrito y no configurado. Clase: decisión recomendada.

## Pruebas de concepto

- Identidad efectiva del cliente de la tubería y familia de paquete MSIX, además de la ACL por SID. Clase: requiere prueba de concepto. Hay que ver, en Windows 11 26100, que la tarea empaquetada presenta `Neobash.PrintBridge.VirtualPrinter` y que un proceso no empaquetado de la misma cuenta es rechazado. También el modo bytes con longitud, el corte a mitad y el tope de tamaño. Sin ese resultado no empieza el Incremento 2.
- `IPrintDocumentPackageTarget` contra una cola física concreta. Clase: requiere prueba de concepto. Si falla de verdad, se anota la incompatibilidad y se prueba otro adaptador. No se pasa por el raster PDF. Bloquea el Incremento 2, no el 1.
- STA y `System.Drawing.Printing` fuera de la sesión interactiva, solo si alguien propone un servicio. Clase: requiere prueba de concepto. El adaptador crea el hilo `NPB print STA` y no hay experimento en sesión 0. No se usa esa ausencia para declarar el servicio imposible.

## Bloqueos

- Empezar el Incremento 1 sin los criterios de `06-implementacion.md`. Clase: bloqueante antes de implementar. En particular sin la biblioteca compartida, sin TLS que rechace los errores que no sean el caso autofirmado ya fijado, sin destinos publicados en SQLite, sin el commit anterior al despacho y sin la regla de no reimprimir `Uncertain`.
- Empezar el Incremento 2 sin las dos pruebas de concepto de tubería y de paquete de documento. Clase: bloqueante antes de implementar.
- Dar por impresa una hoja porque el spooler aceptó o porque la red respondió `Aceptada`. Clase: bloqueante antes de implementar cualquier texto o estado que lo afirme. El Incremento 5 mira el papel.
- Afirmar que todos los controladores aceptan el PDF o el OXPS. Clase: bloqueante antes de implementar esa frase. `PdfSpoolerAdapter` solo sabe mirar `PaperSizes` e `IsValid` de la cola que tiene delante.

## Se puede dejar para más adelante

- UI de emparejamiento, historial de red en Desktop y migración JSON. Clase: puede diferirse al Incremento 4. Los controles no se difieren con ella.
- Servicio de Windows o híbrido servicio más sesión. Clase: puede diferirse. No bloquea el MVP en sesión interactiva.
- Windows 10. Clase: puede diferirse. El manifiesto no lo admite hoy.
- GELIA-NV y la conexión saliente entre redes. Clase: puede diferirse. Fuera de A2/B1.
- Alta dinámica de colas virtuales. Clase: puede diferirse. El MVP es una cola y un destino.
- Star BSC10, POS80 y cualquier ancho de ticket que el PDC no anuncia. Clase: puede diferirse hasta que el papel de hojas esté comprobado y la cola física anuncie ese nombre. No se recorta A4 mientras tanto.
- RAW OXPS. Clase: puede diferirse de forma indefinida mientras no haya una capacidad explícita. No es un plan B del Incremento 2.
- Conversión OXPS→PDF. Clase: puede diferirse y, con la calidad de raster aún abierta, no es un camino aprobado.
- Barrido de cuerpos ya terminados. Clase: puede diferirse. El cuerpo de `Uncertain` no entra en ese barrido.
- Crear la regla de firewall. Clase: puede diferirse a la persona que prepare el Incremento 5, a mano. No entra en un PR de código como configuración automática.
- Homologar más de un controlador. Clase: puede diferirse. Cada cola es un ensayo aparte.

## Residuos que no se cierran con documentación

- Entre el momento en que `Print` vuelve y el commit de `SubmittedToSpooler` el trabajo puede haber entrado al spooler y la fila seguir en `Prepared` hasta el arranque, que la verá `Uncertain`. Es intencionado. No hay API en este repositorio que resuelva esa duda, y no se añade una reimpresión para taparla.
- PDFium y `Windows.Data.Pdf` parsean el PDF aceptado. El contrato limita tipo y tamaño; no convierte esas bibliotecas en un aislamiento nuevo.
- La primera comparación de papel al 90 % frente al navegador sigue pendiente en `docs/A1-prueba-fisica.md` y `docs/roadmap.md`. Los incrementos de red no la sustituyen.
