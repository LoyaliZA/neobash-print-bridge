# Incremento 0 — implementación

Este incremento es solo documentación. No añade receptor, servicio, controlador ni cambio de impresora. Los incrementos siguientes van en ramas y pull requests distintos. Ninguno se abre porque este documento exista: el Incremento 1 espera la autorización de abajo.

## Orden y dependencias

1. Incremento 0, esta rama. No depende de código nuevo.
2. Incremento 1. Depende de que estas decisiones queden aceptadas. No depende de la tubería ni de `IPrintDocumentPackageTarget`.
3. Incremento 2. Depende de dos pruebas de concepto hechas y anotadas: la tubería con identidad del cliente, y `IPrintDocumentPackageTarget` contra una cola física real. Si la segunda muestra incompatibilidad, se anota y se elige otro adaptador antes de integrar. No se convierte a PDF.
4. Incremento 3. Depende del receptor del Incremento 1 y del envío local del Incremento 2. Une la cola virtual del equipo cliente con ese receptor. Sigue habiendo una sola cola y un solo destino.
5. Incremento 4. Depende de que el emparejamiento ya funcione sin ventana. Añade la UI, el historial en Desktop y la migración de los JSON históricos. No estrena la autenticación.
6. Incremento 5. Depende de los anteriores en dos equipos. El papel se comprueba a mano. No se da por bueno el spooler ni la red.

GELIA-NV no entra en la cadena. Windows 10 tampoco.

## Plan definitivo del Incremento 1

No se abre con este documento. Cuando se autorice, es una rama y un pull request propios. El orden de dentro de esa rama es el de las dependencias, no cuatro frentes sueltos.

### Tareas

1. Crear la biblioteca Windows sin WinUI y mover, sin cambiar la lógica, `PdfDocumentPreparer`, `PdfiumPrintSession`, `PdfSpoolerAdapter`, `WindowsPrinterProbe` y el `StaQueue` de esa sonda. Desktop pasa a referenciarla. La vista previa y el XAML se quedan. `PrintDispatcher` y `PageLayoutCalculator` se quedan en Core.
2. Cambiar el moniker del agente a `net10.0-windows10.0.26100.0`, x64, sin `UseWinUI` y sin `Microsoft.WindowsAppSDK`. Referencia Core y la biblioteca. No referencia Desktop. Copiar junto a su ejecutable las licencias de PDFium que hoy solo copia Desktop. Sustituir el bucle de `Worker` por el proceso de sesión que escucha en la LAN.
3. Almacén SQLite nuevo del agente: dispositivos, destinos publicados, permisos, claves, trabajos y códigos ligados a una huella. No envolver `FileJobRecordStore`. No abrir `destinations.json`.
4. Alta local del único destino físico del MVP: configuración en el directorio del agente, volcada a `published_destinations`. El receptor lee solo la tabla.
5. Confianza inicial: código de un solo uso ligado a la huella, mostrada en el anfitrión; el cliente la recibe de quien opera, no del socket. Validación del autofirmado con vigencia, uso, identidad esperada y rechazo del resto de errores TLS.
6. Receptor: autenticación, autorización del destino publicado, tope de 32 MiB, y el protocolo temporal → validación → publicación → commit → despacho. El despacho usa el `PrintDispatcher` de Core y los adaptadores de la biblioteca, con `application/pdf` y `PreparedPdf`. El hilo STA sigue naciendo dentro del adaptador.

### Dependencias

- 2 depende de 1. Sin biblioteca no hay ejecución del motor en el agente.
- 6 depende de 2, 3, 4 y 5. No se despacha contra un destino leído de Desktop ni antes del commit.
- 4 depende de 3.
- 5 depende de 3 para guardar el código ligado a la huella.
- No depende de la tubería ni de `IPrintDocumentPackageTarget`. Esas pruebas siguen bloqueando el Incremento 2.
- El manifiesto del paquete, las páginas de Conexiones y Accesos, el tema y los JSON de Desktop no entran en el cambio de comportamiento. Mover los tres adaptadores no es una licencia para retocar el raster.

### Archivos probables

- Biblioteca nueva bajo `src/Windows/`, todavía inexistente, más su entrada en `Neobash.PrintBridge.slnx`.
- `src/Agent/Neobash.PrintBridge.Agent.csproj`, `Program.cs`, `Worker.cs`.
- Tipos nuevos de SQLite junto al agente o detrás de una interfaz usada por él. `FileJobRecordStore` queda.
- Pruebas en `tests/UnitTests` para el protocolo y la confianza, y en `tests/WindowsPrintTests` para la biblioteca sin abrir WinUI.
- Configuración local del puerto, de la ruta de la base y del alta del destino, sin secretos en `appsettings.json`.

### Pruebas

Motor compartido:

- El csproj del agente y el de la biblioteca no contienen `UseWinUI` ni `Microsoft.WindowsAppSDK`, y el agente no referencia Desktop. Core sigue sin WinUI.
- Las versiones de `System.Drawing.Common` y de `bblanchon.PDFium.Win32` son las que Desktop tiene hoy: 10.0.0 y 157.0.8100.
- `tests/WindowsPrintTests` referencia la biblioteca y sigue sin lanzar la ventana. El proceso del agente, en sesión interactiva, construye el preparador y el adaptador y completa un despacho contra un sustituto del spooler. El hilo se llama `NPB print STA`.
- Las pruebas ya existentes de despacho, de diseño de página, de deduplicación, de `Uncertain`, de ticket y de `OpenXpsLimits` pasan sin cambiar de resultado. La ventana se activa antes del catálogo y de la recuperación.

Confianza:

- Una huella que solo llega en el material del servidor no autenticado no queda guardada como pin.
- El código falla si la huella presentada no es la ligada al código, si caducó o si se reutiliza.
- Certificado fuera de vigencia, sin uso de autenticación de servidor, o con un nombre distinto del esperado: rechazo.
- Un error TLS que no sea la raíz no confiable de ese certificado ya comprobado: rechazo. Una prueba cuyo callback devuelve verdadero para cualquier error falla la revisión.

Destinos:

- El agente no lee ni escribe `destinations.json`.
- Una petición con nombre de cola, o con un identificador no publicado o no concedido, no crea temporal y no despacha.
- El único destino del MVP sale de la configuración local del agente y de la fila SQLite.

Atomicidad, además de las de repetición ya previstas:

- Misma clave, mismo resumen y mismo destino, repetida después del commit: una llamada al sustituto del spooler; la segunda respuesta repite la fase.
- Dos peticiones concurrentes con la misma clave y el mismo cuerpo: un commit, una llamada al spooler, una fila. La otra recibe la fase guardada.
- Dos peticiones concurrentes con la misma clave y otro cuerpo u otro destino: una fila, un despacho como máximo —el de la ganadora, si llegó a confirmarse—, y conflicto para la otra, sin segundo despacho.
- Corte a mitad del cuerpo: no queda fila. El arranque borra el temporal. El reintento completo puede aceptarse una vez.
- Proceso interrumpido después de publicar el archivo y antes del commit: no queda fila, el arranque borra el huérfano y el sustituto del spooler no recibe llamadas.
- Proceso interrumpido después del commit y antes del despacho: la fila pasa a `Uncertain` y tampoco hay llamada nueva al spooler.
- Un espía cuenta llamadas al spooler y comprueba que la primera ocurre solo cuando el commit ya es durable.
- `SubmittedToSpooler` sobrevive a la recuperación. La nota física sigue siendo imposible en otra fase.
- Identificador de origen con `..` o separadores: rechazo, sin archivo fuera de la raíz.
- Cuerpo por encima de 32 MiB, o más de 30 aceptaciones en 10 minutos: rechazo y temporal borrado.
- El registro del ensayo no contiene el código, la credencial ni los bytes del PDF.

No hace falta impresora física para cerrar el Incremento 1. El sustituto del spooler basta para demostrar que no hay reimpresión ni segundo despacho. El papel espera al Incremento 5. Si `Windows.Data.Pdf` no compila sin WinUI, el incremento se detiene en esa evidencia y no se acopla el agente a la ventana.

## Criterios para autorizar el Incremento 1

Se puede abrir esa rama solo si se acepta todo lo siguiente:

- El receptor de la primera prueba ya autentica, autoriza por destino y valida TLS. No se aplaza ningún control a la UI del Incremento 4.
- El almacén de trabajos nuevos, estados, identidades y claves del agente es SQLite. Los JSON de Desktop no se migran en ese PR.
- Las máquinas de estados quedan separadas, como en `05-datos-y-estados.md`.
- La clave durable es dispositivo + origen. Otro contenido u otro destino es conflicto. Lo incompleto no es un trabajo. `Uncertain` no se reimprime.
- El motor PDF no cambia de contrato: solo `application/pdf`, `PreparedPdf`, GDI, tope de raster, sin DPI genérico, sin fingir papel. Entra en el agente por la biblioteca compartida, sin WinUI.
- El agente es un proceso de la sesión interactiva, distinto de la ventana. No se convierte en servicio en ese PR.
- Una cola virtual y un destino publicado en SQLite del agente, dado de alta en local. Sin `destinations.json` compartido, sin nombres de cola elegidos por el cliente, sin alta dinámica y sin GELIA-NV.
- La huella inicial se verifica fuera del socket no autenticado. El código queda ligado a esa huella. Los errores TLS no se aceptan en bloque.
- Ningún despacho ocurre antes del commit durable, y una petición concurrente con la misma clave no crea un segundo despacho.
- Sin tokens en el código, sin trabajos anónimos, sin escucha pensada para Internet, sin desactivar TLS y sin script de firewall.
- Las pruebas de concepto de la tubería y de `IPrintDocumentPackageTarget` no son requisito para empezar el Incremento 1. Sí lo son para empezar el Incremento 2.

Si alguna de esas condiciones se quiere cambiar, se cambia antes en estos documentos. No se «ajusta» dentro del PR del receptor.

## Ramas

- Este PR: `cursor/a2-b1-contracts-ef55`, solo `docs/plans/`.
- Incremento 1: rama propia, cuando esté autorizado. No se apila sobre trabajo de impresora ni se mezcla con la UI.
- Cada incremento posterior, su rama y su pull request. No se fusionan entre sí por adelantado.

## Qué no se hace en ningún incremento sin una decisión nueva

- Compatibilidad universal con controladores. Cada cola se prueba con el papel que esa cola anuncia.
- OXPS→PDF por el raster de A1.5.
- RAW OXPS sin capacidad explícita.
- Persistir el PrintTicket.
- Anunciar 80 mm en el PDC o recortar A4. Star BSC10 y POS80 van después del papel de hojas.
- Leer el `LocalState` del paquete desde otro proceso como canal oficial.
- Declarar imposible un servicio de Windows. Tampoco implementarlo antes de una prueba de la sesión 0 con el adaptador STA, si algún día se propone.

## Decisiones definitivas

- Seguridad real en el Incremento 1. La UI de emparejamiento es el Incremento 4.
- SQLite para lo nuevo del agente en el Incremento 1. Migración de los JSON históricos de Desktop en el Incremento 4.
- Transferencia, `JobPhase` y `JobCaptureStatus` separados. La confirmación física sigue siendo un campo posterior a `SubmittedToSpooler`.
- Idempotencia durable por dispositivo y origen, con conflicto si cambia el cuerpo o el destino, sin reimpresión automática de `Uncertain`.
- Tubería local con ACL de cuenta, token del cliente e identidad de paquete MSIX. Hace falta prueba de concepto antes del Incremento 2. El repositorio no demuestra lectura cruzada de `LocalState`.
- `IPrintDocumentPackageTarget` es la primera alternativa OpenXPS. Otros adaptadores solo si la prueba muestra incompatibilidad real. El motor PDF no se modifica. Sin conversión de calidad desconocida y sin RAW no anunciado.
- Agente del MVP en la sesión interactiva, separado de WinUI. Servicio o híbrido, más adelante y con prueba. El STA de `System.Drawing.Printing` es una restricción a comprobar, no un veto eterno de la sesión 0.
- Certificado del anfitrión validado, con vigencia, uso e identidad. El código de un solo uso va ligado a una huella verificada por un canal independiente. Credencial en Credential Manager o DPAPI de la cuenta. Revocación y renovación explícitas. Firewall solo descrito.
- Biblioteca Windows compartida para el motor PDF, usable por el agente y por Desktop, sin acoplar el agente a WinUI.
- Destinos LAN solo en SQLite del agente. El cliente elige publicados y autorizados.
- Protocolo temporal, validación, publicación, commit y después despacho. Un concurrente con la misma clave no dispara otro spooler.

## Alternativas descartadas

- Aplazar autenticación, autorización o TLS hasta tener ventana de emparejamiento.
- Reutilizar `FileJobRecordStore` como almacén de la red, o migrar Desktop a SQLite en el mismo PR que el primer receptor.
- Meter la LAN, el spooler y el papel en `JobPhase`.
- Tratar una clave repetida con otro destino como el `DuplicateDetail` actual, que calla y conserva la primera cola.
- Reimprimir `Uncertain` cuando la respuesta se pierde o el proceso muere cerca de `Print`.
- ACL de tubería sin identidad del proceso, o usar el `LocalState` como transporte demostrado.
- Cerrar ya la puerta a cualquier adaptador OXPS que no sea el primero, o convertir OXPS a PDF con el raster.
- Enviar RAW, guardar el ticket, anunciar 80 mm o recortar A4 para simular un ticket.
- Hospedar el receptor dentro de la ventana WinUI, o hacer que el agente referencie el proyecto Desktop.
- Aceptar la huella que anuncia el servidor antes de autenticarlo, o tratar cualquier error TLS como éxito.
- Publicar destinos escribiendo `destinations.json` desde el agente, o aceptar un nombre de cola arbitrario.
- Despachar en cuanto el archivo está en disco, antes del commit, o dejar que la petición perdedora también llame al spooler.
- Escribir que un servicio de Windows es imposible.
- Token fijo, trabajo anónimo, TLS sin validación, agente publicado en Internet.
- Configurar el firewall desde el repositorio.
- Varias colas virtuales dinámicas, GELIA-NV, o Windows 10 como requisito.

## Riesgos que siguen abiertos

- La tubería y la identidad de paquete del cliente no están probadas. Bloquean el Incremento 2, no el 1.
- `IPrintDocumentPackageTarget` puede ser incompatible con la cola que se pruebe. No se sabe: no está en el árbol.
- El hueco entre el retorno del spooler y el commit seguirá existiendo. Se clasifica `Uncertain` y puede haber papel sin nota, o nota imposible porque nadie vio la hoja. No se cierra con una reimpresión.
- La calidad del PDF en papel sigue pendiente de la comparación con el navegador al 90 %. El Incremento 1 no la da por cerrada.
- Credential Manager frente a DPAPI se confirma en la implementación del Incremento 1. La fila no guardará el secreto en ninguno de los dos casos.
- Ningún controlador queda homologado por estos documentos.
- No está demostrado que `Windows.Data.Pdf` compile sin WinUI. Si la biblioteca no compila, el Incremento 1 se detiene ahí y no referencia Desktop.

## Criterios de aceptación del Incremento 1

Un PR del Incremento 1 está listo para revisión cuando:

- la biblioteca y el agente compilan en `win-x64` sin WinUI, y un proceso del agente en sesión interactiva despacha un PDF de prueba contra el sustituto del spooler usando los mismos adaptadores;
- un cliente de prueba escribe código y huella tomados del anfitrión, no del socket; el código queda gastado; la credencial no está en el fuente; el sustituto del spooler recibe una sola entrega;
- el mismo envío repetido, y dos envíos concurrentes de la misma clave, no producen una segunda entrega;
- un cuerpo o un destino distinto con la misma clave responde conflicto y no crea otro despacho;
- un corte a medias, un archivo publicado sin commit y un arranque con `Received` o `Prepared` no llaman al spooler; los dos primeros no dejan fila y el tercero queda `Uncertain`;
- un destino no publicado, un nombre de cola, un tipo que no sea PDF, un certificado que falle vigencia, uso o identidad, y un cuerpo por encima de 32 MiB no crean trabajo;
- Desktop sigue arrancando como en A1.4, con el mismo tema lavanda e índigo, los mismos textos que distinguen spooler y papel, y sus JSON intactos, incluido `destinations.json`;
- el diff no toca el manifiesto de capacidades, no añade firewall y no cambia las reglas del raster: el traslado a la biblioteca conserva PDFium, GDI, STA y las validaciones.
