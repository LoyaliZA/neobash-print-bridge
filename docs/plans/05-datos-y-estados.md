# Incremento 0 — datos y estados

## Tres máquinas, sin un enum común

No se añade la red ni el papel a `JobPhase`. No se reutiliza `JobCaptureStatus` para el spooler.

### Cierre de la tarea ante Windows

`JobCaptureStatus` en `src/Core/Capture/JobCaptureStatus.cs`:

- `Succeeded`. El archivo quedó cerrado con tamaño mayor que cero y el tamaño leído coincide. `VirtualPrinterJobCompletion` puede llamar a `CompleteJob(Succeeded)` una sola vez.
- `Failed`. No se escribió un documento válido, o falló el metadato.
- `Canceled`. La cancelación gana aunque ya hubiera bytes.

Ese valor vive en `metadata.json` (`estado`) dentro del `LocalFolder` del paquete. No es una fase de Desktop y no será una fase del agente.

### Transferencia LAN

Estados de contrato, todavía no implementados:

- `EnCurso`. Llegaron bytes o se abrió un temporal. No hay clave confirmada. No es un trabajo. Dos peticiones a la vez pueden estar en `EnCurso` con la misma clave; eso no autoriza dos despachos.
- `Aceptada`. El cuerpo está entero en disco y la transacción de la clave y de la fila hizo commit.
- `Rechazada`. Falló la autenticación, el destino, el tipo, el tamaño o la frecuencia. Sin fila de trabajo.
- `Conflicto`. La clave ya existe con otro resumen de contenido o con otro destino. La fila vieja no se toca. No nace otra.

Una transferencia incompleta es la ausencia de commit. Al arrancar, un temporal sin fila se borra. No se promociona a `Received`.

### Impresión en el anfitrión

`JobPhase` se queda como está:

- `Received`. La transferencia ya fue `Aceptada`, o, en Desktop hoy, `TryBegin` creó el JSON.
- `Prepared`. El PDF quedó preparado.
- `SubmittedToSpooler`. `Print` volvió y el registro se actualizó. El detalle actual de Desktop es «El spooler aceptó el trabajo. Eso no confirma el papel.»
- `Failed`. El fallo ocurrió antes de que el puerto del spooler empezara (`SpoolerPortException.Started` falso), o falló la preparación, la cola, el papel o el tipo.
- `Uncertain`. El envío ya había empezado, o el proceso murió en `Received` o `Prepared`. No se reimprime.

La confirmación física es el campo `PhysicalConfirmation` de `JobRecord`. `RecordPhysicalConfirmation` exige una nota no vacía y la fase `SubmittedToSpooler`. Si no, lanza. La fase no cambia. La página de trabajos solo habilita esa acción en `SubmittedToSpooler`.

## Transiciones que el código ya cumple

Orden real de `PrintDispatcher.ExecuteAsync`:

1. Si el destino no está guardado, `Failed`, sin registro.
2. Si el tipo no es `application/pdf`, `Failed`, sin registro.
3. Si la cola no está o es virtual (`NPB - Prueba Virtual` o el controlador `Microsoft Virtual Print Class Driver`), `Failed`, sin registro.
4. Si las opciones no cuadran con la cola o el papel del trabajo no coincide con las opciones, `Failed`, sin registro. El consentimiento de recorte guardado en el destino no autoriza el recorte de un trabajo nuevo: solo cuenta el del propio trabajo.
5. `TryBegin`. Si el archivo existe, se devuelve la fase ya guardada y «El identificador ya fue registrado y no se envía de nuevo.» No se prepara ni se imprime.
6. Preparación correcta: `Prepared`. Excepción: `Failed`.
7. Cancelación antes de entregar al spooler: `Failed`.
8. Excepción con `Started` falso: `Failed`.
9. Cualquier otra excepción, incluida la de `Started` verdadero: `Uncertain`.
10. Retorno normal: `SubmittedToSpooler`.

`JobRecovery` en el arranque de Desktop, después de activar la ventana, toma una instantánea de identificadores y solo esos, si están en `Received` o `Prepared`, pasan a `Uncertain`. Un trabajo creado después de la instantánea sigue en `Received` (`Recovery_snapshot_leaves_a_later_job_received`).

El agente nuevo repite esa regla sobre sus filas SQLite. No llama a `SubmitAsync` desde la recuperación.

## Idempotencia durable

La clave es dispositivo + identificador de origen. Es única y sobrevive al proceso.

Junto a la clave se guardan el resumen SHA-256 del cuerpo ya escrito y el destino. Esos tres datos se confirman en la misma transacción que crea el trabajo.

- Misma clave, mismo resumen, mismo destino: se devuelve el trabajo existente y su fase. No hay segundo `Print`. Sirve para una respuesta que el cliente no llegó a ver.
- Misma clave, resumen distinto o destino distinto: `Conflicto`. No se crea un trabajo. No se cambia el destino de la fila vieja. No se usa el `DuplicateDetail` actual, porque ese texto no dice que el contenido difiera: hoy `TryBegin` ignora el contenido.
- Clave desconocida y cuerpo incompleto: no hay fila. El cliente puede volver a empezar. No está reimprimiendo un trabajo aceptado, porque no lo hubo.
- Fase `Uncertain` en una repetición idéntica: se devuelve `Uncertain`. Sigue sin spooler.

El identificador de origen pasa por `JobIdRules` antes de acercarse al sistema de archivos. El identificador interno del trabajo lo genera el agente. No es la clave de idempotencia y no lo elige el cliente como ruta.

El destino de la clave es el identificador publicado en SQLite, no un nombre de cola recibido en la petición.

## Protocolo de aceptación

Este orden es obligatorio. El repositorio no lo implementa: `FileJobRecordStore` escribe el JSON antes de preparar y no tiene este commit. El Incremento 1 lo cumple en el almacén del agente.

1. Autenticar y comprobar que el identificador de destino está publicado y concedido a ese dispositivo. Si falla, no se crea temporal, ni fila, ni despacho. Un nombre de cola en la petición corta aquí.
2. Escribir el cuerpo en un archivo temporal, fuera del directorio de trabajos confirmados, con un nombre que ninguna fila pueda citar. Se calcula el SHA-256 mientras se escribe. Si el flujo se corta, el tipo no es `application/pdf` o se pasa de 32 MiB, se borra el temporal. Sigue sin fila y sin despacho.
3. Validar el temporal ya cerrado: tamaño, resumen, tipo, identificador de origen según `JobIdRules`, y que el destino siga publicado. Si falla, se borra el temporal.
4. Publicar el archivo: volcar el temporal a disco y renombrarlo a la ruta definitiva que asigna el agente, dentro de su raíz. Si el proceso muere antes del renombre, al arrancar solo existe el temporal y se borra. El archivo ya renombrado todavía no es un trabajo.
5. Commit SQLite, en una transacción inmediata: insertar la clave (`device_id`, `source_id`, resumen, destino, `job_id`) y la fila del trabajo en `Received`, apuntando a esa ruta. Cuando el commit vuelve, la transferencia pasa a `Aceptada`. Si el commit no vuelve, no hay despacho. Si la clave choca, se aplica la regla de concurrencia de abajo y no se inserta otra fila.
6. Despachar solo después de ese commit. `PrintDispatcher`, el preparador y `SubmitAsync` no se llaman antes. El paso a `Prepared` y el paso a `SubmittedToSpooler` son commits posteriores.

El límite de una transferencia en curso por dispositivo no sustituye este commit. Si dos peticiones entran de todas formas, el paso 5 deja un solo ganador.

### Dos peticiones con la misma clave

Cada una puede tener su temporal y, si ambas llegan al paso 4, su propio archivo publicado. Las dos intentan el insert. La restricción única admite un commit.

- La ganadora, y solo ella, puede pasar al paso 6.
- La perdedora no despacha. Si el resumen y el destino son los de la fila ya confirmada, borra su archivo de más y devuelve la fase guardada. Si difieren, responde `Conflicto`, borra su archivo si ninguna fila lo cita, y no crea trabajo.

### Recuperación e huérfanos

Al abrir la base, antes de aceptar nada nuevo y sin llamar al spooler:

- Temporales no renombrados: se borran. No hubo commit.
- Archivos publicados que ninguna fila de `jobs` cita: huérfanos. Se borran. No se despachan. Un corte entre el paso 4 y el paso 5 deja exactamente este caso.
- Filas `Received` o `Prepared`: pasan a `Uncertain`. No se despachan, aunque el archivo siga ahí. El commit ya ocurrió y el spooler puede no haber empezado, o haber empezado sin dejar `SubmittedToSpooler`. La regla de no reimprimir no cambia.
- Fila cuya ruta no está en disco y cuya fase sigue en `Received`: tampoco se despacha. Pasa a `Uncertain` con el mismo detalle de interrupción. No se fabrica otro PDF.
- `SubmittedToSpooler` y `Failed` se dejan como están.

## Recuperación ante pérdida y caídas

Respuesta perdida. El cliente no sabe si el anfitrión aceptó. Reintenta la misma clave y el mismo cuerpo. Si el commit había ocurrido, recibe la fase guardada. Si no había commit, el reintento es la primera aceptación. En ninguno de los dos casos hay dos llamadas al spooler para esa clave.

Caída justo antes del spooler. La fila está en `Received` o, si la preparación ya se guardó, en `Prepared`. El arranque la marca `Uncertain` con el mismo criterio que `JobRecovery`: el proceso se interrumpió antes de conocer el resultado y no se reimprime. Nadie llama a `PdfSpoolerAdapter` para «terminar lo que faltaba».

Caída justo después de enviar al spooler. Hay dos huecos, y los dos terminan igual:

- `Print` lanzó después de empezar y el proceso vivió lo bastante para ejecutar el `catch` de `PrintDispatcher`: la fase queda `Uncertain` en ese momento.
- `Print` volvió, o el proceso murió dentro de `Print`, y no llegó el `Update` a `SubmittedToSpooler`: el último commit sigue en `Prepared` (o `Received` si ni siquiera se guardó la preparación). El arranque lo pasa a `Uncertain`.

No hay forma, en este repositorio, de preguntarle al spooler si aquella llamada concreta quedó aceptada y entonces reimprimir solo si no. `JobRecovery` no lo intenta. El contrato tampoco. La duda se queda visible. La nota física no se inventa.

Caída después del commit de `SubmittedToSpooler`. La recuperación no modifica esa fila. El reintento idéntico devuelve `SubmittedToSpooler`. El papel sigue siendo un campo aparte, escrito solo si alguien lo confirma.

`Uncertain` no tiene un trabajo de fondo que lo reimprima. Esa prohibición ya está en el detalle fijo de `JobRecovery` y en el `TryBegin` que devuelve falso. El almacén nuevo la mantiene aunque la clave de red permita encontrar la fila.

## Esquema SQLite inicial del agente

No está implementado. Es el contrato del Incremento 1. Los nombres pueden ajustarse en el código; las unicidades y el momento del commit no.

- `devices`. `device_id` único, nombre visible, referencia a la credencial (no el secreto), huella que quien opera registró por el canal independiente, nombre esperado del anfitrión, alta, revocación nula o con fecha.
- `published_destinations`. Fuente de verdad de la LAN. `destination_id` único, nombre visible, `queue_name` y papel copiados desde la configuración administrativa local del agente. El receptor no acepta `queue_name` desde el cliente. En el MVP hay un destino.
- `destination_grants`. Par único `device_id` + `destination_id`. El destino tiene que existir en `published_destinations`. En el MVP hay un permiso, el de la única cola virtual hacia ese destino.
- `idempotency`. Clave primaria `device_id` + `source_id`. Columnas `content_sha256`, `destination_id`, `job_id` único.
- `jobs`. `job_id` primario, `media_type`, ruta local asignada por el agente, `destination_id`, nombre de cola copiado de `published_destinations` y no de la petición, `phase` con los cinco valores actuales, detalle, confirmación física nula, `received_at`.
- `pairing_codes`. Solo el resumen del código, la huella a la que está ligado, el nombre esperado, caducidad y momento de uso. Sin el código en claro. Una fila usada no vuelve a servir. Un código no vale para otra huella.

No hay tabla que mezcle `EnCurso` con `JobPhase`. `EnCurso` no se inserta. `Rechazada` y `Conflicto` pueden anotarse en la auditoría sin fila en `jobs`.

Modo WAL. El commit de `idempotency` y `jobs` es uno solo, después de volcar el archivo. Las actualizaciones de fase son commits posteriores. La recuperación de arranque actualiza en una transacción las filas `Received` y `Prepared` a `Uncertain` y no inserta envíos.

`Microsoft.Data.Sqlite` no está referenciado. Añadirlo es trabajo del Incremento 1, detrás de un almacén nuevo. `FileJobRecordStore` no pasa a ser un adaptador de esa base.

## Qué sigue en JSON hasta el Incremento 4

Bajo `%LOCALAPPDATA%\Neobash\PrintBridge\Desktop`, escritos por la ventana no empaquetada:

- `jobs\{id}.json`. Historial de despacho local: fase, opciones, cola, nota física.
- `destinations.json`. Destinos lógicos de las colas de este equipo.
- `preferences.json`. Destino activo y apariencia.

Siguen con su `Lock` de instancia y su temporal + `Move`. El Incremento 1 no los migra, no los borra y no los usa como cola de idempotencia de la red.

`destinations.json` tampoco es la fuente de los destinos LAN. El agente no lo abre. La configuración administrativa local que alimenta `published_destinations` vive en el directorio del agente, fuera de `Desktop`, y solo sirve para ese alta. Después del volcado, la petición lee SQLite. El Incremento 4 puede migrar el historial visible de la ventana; no convierte ese JSON en una escritura compartida con el agente.

Bajo el `LocalFolder` del paquete:

- `Neobash\PrintBridge\Jobs\{id}\document.oxps` o `document.bin`, `metadata.json`, `error.txt` si hubo fallo.
- `Neobash\PrintBridge\Logs\task.log`.

Esa captura tampoco se migra en el Incremento 1. El Incremento 2, después de la prueba de la tubería, podrá leer el flujo que la tarea le entregue por la tubería. No se diseña como un lector de esa carpeta.

## Qué no cabe en estos datos

- El XML del PrintTicket. Se valida en memoria y se descarta.
- El secreto del dispositivo, el código de un solo uso en claro y la clave privada.
- Una fase «impreso en papel». Eso sigue siendo la nota, y solo tras el spooler.
- Una segunda cola virtual o una lista dinámica de colas. El vínculo del MVP es una fila de permiso, no un registro nuevo en el manifiesto.
