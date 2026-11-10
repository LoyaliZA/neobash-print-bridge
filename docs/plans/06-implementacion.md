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

## Incremento 1 — archivos probables

Sin crearlos ahora:

- `src/Agent/Program.cs` y `src/Agent/Worker.cs`. Sustituir el bucle de la plantilla por el proceso de sesión que escucha solo en la LAN.
- Un proyecto o tipos nuevos de almacén SQLite usados por el agente. No convertir `FileJobRecordStore`.
- Tipos de transferencia (`EnCurso`, `Aceptada`, `Rechazada`, `Conflicto`) fuera de `JobPhase`.
- Pruebas nuevas junto a `tests/UnitTests`, más las que exijan Windows en `tests/WindowsPrintTests` solo si llaman al spooler de verdad.
- Configuración local del puerto y de la ruta de la base, sin secretos en `appsettings.json`.

No entran en ese PR: `Package.appxmanifest`, `PdfSpoolerAdapter`, `PdfiumPrintSession`, `PageLayoutCalculator`, `PrintDispatcher` (salvo que una prueba demuestre un defecto ya existente), páginas de Conexiones y Accesos, `destinations.json`, el tema `Light.xaml`.

El receptor llama al `PrintDispatcher` y al `PdfSpoolerAdapter` que ya existen, con `application/pdf` y un `PreparedPdf`. El hilo STA sigue naciendo dentro del adaptador.

## Pruebas que el Incremento 1 tiene que traer

- Rechazo sin credencial: no hay fila y no hay llamada al puerto del spooler.
- Credencial válida y destino no concedido: igual.
- Certificado distinto del pin: la validación no devuelve éxito. Una prueba que instale un callback permisivo falla la revisión.
- Cuerpo distinto de `application/pdf`: rechazo antes del preparador PDF.
- Cuerpo que supera 32 MiB, segunda transferencia simultánea del mismo dispositivo, o el tope de 30 aceptaciones en 10 minutos: rechazo y temporal borrado.
- Misma clave, mismo resumen, mismo destino, repetida después de un commit: una sola llamada al spooler; la segunda respuesta repite la fase.
- Misma clave con otro resumen o con otro destino: conflicto, una sola fila, sin spooler.
- Corte a mitad del cuerpo: no queda fila. El reintento completo puede aceptarse una vez.
- Fila `Received` o `Prepared` al abrir el almacén: pasa a `Uncertain` y el sustituto del spooler no recibe `SubmitAsync`.
- Excepción del puerto con `Started` verdadero: `Uncertain`, sin segundo intento dentro del mismo proceso.
- `SubmittedToSpooler` sobrevive a la recuperación. La nota física sigue siendo imposible en otra fase.
- Dos procesos sobre la misma base: la clave única no deja dos filas. Esto cubre lo que el `Lock` de `FileJobRecordStore` no cubre.
- Identificador de origen con `..` o separadores: rechazo, sin archivo fuera de la raíz.
- El registro del ensayo no contiene el código de un solo uso, la credencial ni los bytes del PDF.
- Las pruebas ya existentes de Desktop, de deduplicación, de `Uncertain`, de ticket y de `OpenXpsLimits` siguen pasando. La ventana se sigue activando antes del catálogo y de la recuperación.

No hace falta impresora física para cerrar el Incremento 1. Un doble del puerto del spooler basta para demostrar que no hay reimpresión. El papel espera al Incremento 5.

## Criterios para autorizar el Incremento 1

Se puede abrir esa rama solo si se acepta todo lo siguiente:

- El receptor de la primera prueba ya autentica, autoriza por destino y valida TLS. No se aplaza ningún control a la UI del Incremento 4.
- El almacén de trabajos nuevos, estados, identidades y claves del agente es SQLite. Los JSON de Desktop no se migran en ese PR.
- Las máquinas de estados quedan separadas, como en `05-datos-y-estados.md`.
- La clave durable es dispositivo + origen. Otro contenido u otro destino es conflicto. Lo incompleto no es un trabajo. `Uncertain` no se reimprime.
- El motor PDF no cambia de contrato: solo `application/pdf`, `PreparedPdf`, GDI, tope de raster, sin DPI genérico, sin fingir papel.
- El agente es un proceso de la sesión interactiva, distinto de la ventana. No se convierte en servicio en ese PR.
- Una cola virtual y un destino remoto en el modelo de datos. Sin alta dinámica y sin GELIA-NV.
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
- Certificado del anfitrión validado, código de un solo uso, credencial en Credential Manager o DPAPI de la cuenta, revocación y renovación explícitas. Firewall solo descrito.

## Alternativas descartadas

- Aplazar autenticación, autorización o TLS hasta tener ventana de emparejamiento.
- Reutilizar `FileJobRecordStore` como almacén de la red, o migrar Desktop a SQLite en el mismo PR que el primer receptor.
- Meter la LAN, el spooler y el papel en `JobPhase`.
- Tratar una clave repetida con otro destino como el `DuplicateDetail` actual, que calla y conserva la primera cola.
- Reimprimir `Uncertain` cuando la respuesta se pierde o el proceso muere cerca de `Print`.
- ACL de tubería sin identidad del proceso, o usar el `LocalState` como transporte demostrado.
- Cerrar ya la puerta a cualquier adaptador OXPS que no sea el primero, o convertir OXPS a PDF con el raster.
- Enviar RAW, guardar el ticket, anunciar 80 mm o recortar A4 para simular un ticket.
- Hospedar el receptor dentro de la ventana WinUI.
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

## Criterios de aceptación del Incremento 1

Un PR del Incremento 1 está listo para revisión cuando:

- un cliente de prueba, con código de un solo uso gastado después, pin del certificado y credencial fuera del fuente, envía un PDF y el doble del spooler recibe una sola entrega;
- el mismo envío repetido no produce una segunda entrega;
- un cuerpo o un destino distinto con la misma clave responde conflicto y no imprime;
- un corte a medias y un arranque con `Received` o `Prepared` dejan `Uncertain` o ninguna fila, y en ambos casos cero llamadas nuevas al spooler;
- un destino no concedido, un tipo que no sea PDF, un certificado no fijado y un cuerpo por encima de 32 MiB no crean trabajo;
- Desktop sigue arrancando como en A1.4, con el mismo tema lavanda e índigo, los mismos textos que distinguen spooler y papel, y sus JSON intactos;
- el diff no toca el manifiesto de capacidades, no añade firewall y no modifica el motor de raster PDF.
