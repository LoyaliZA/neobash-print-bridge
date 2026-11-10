# Incremento 0 — decisiones

Cada decisión de este archivo queda cerrada para los incrementos siguientes. Las alternativas no elegidas no se dejan «por si acaso» dentro del mismo incremento. Donde el repositorio no contiene la pieza, la evidencia es la ausencia y el comportamiento actual que la decisión tiene que respetar.

## 1. Tubería con nombre entre la tarea y el agente

### Problema

La tarea de la cola virtual escribe el documento en el `LocalFolder` del paquete y termina. El agente tiene que recibir ese flujo en un proceso distinto sin convertirlo en una carpeta compartida no demostrada. El Incremento 2 necesita ese salto local. El Incremento 1 (PDF por la LAN) no lo necesita para aceptar un PDF.

### Alternativas

- Tubería con ACL solo por el SID de la cuenta de Windows.
- Tubería con ACL por SID y, además, comprobación de la identidad efectiva del cliente y, cuando el cliente va empaquetado, de la identidad MSIX.
- Hacer que el agente lea el `LocalState` del paquete.
- Dejar el documento en una carpeta común del perfil, fuera del paquete.

### Ventajas

- La ACL por SID impide que otra cuenta abra la tubería.
- Comprobar el token del cliente y la familia del paquete distingue la tarea `Neobash.PrintBridge.VirtualPrinter` de cualquier otro proceso de la misma cuenta, incluido Desktop, que no está empaquetado (`WindowsPackageType` `None`).
- No usar el `LocalState` como canal evita apoyarse en un permiso que el manifiesto no declara.

### Desventajas

- El repositorio no contiene tuberías, `GetNamedPipeClientProcessId`, suplantación del cliente ni lectura de la familia del paquete. Todo eso es diseño hasta la prueba de concepto.
- Una ACL de cuenta no basta: otro proceso de la misma cuenta puede conectar si el DACL nombra esa cuenta.
- Las API de identidad de paquete, en la documentación de Windows, fallan con `APPMODEL_ERROR_NO_PACKAGE` en un proceso no empaquetado. Aquí no están llamadas, así que no hay evidencia de que la tarea real devuelva `Neobash.PrintBridge.VirtualPrinter` ni de que un ejecutable suelto sea rechazado.
- `GetNamedPipeClientProcessId` puede observar un identificador de proceso reutilizado. La comprobación que importa es el token en el momento de la lectura, no una foto del PID.

### Riesgos

- Aceptar a cualquier proceso de la cuenta permitiría inyectar un documento en la cola del agente.
- Afirmar que el `LocalState` es ilegible desde fuera sería falso: el repositorio no lo prueba. Afirmar que se puede leer también sería falso.

### Recomendación

Tubería local, modo bytes, con longitud declarada y tope de tamaño. ACL de la cuenta interactiva, más token efectivo del cliente, más familia de paquete igual a `Neobash.PrintBridge.VirtualPrinter` cuando el emisor es la tarea. Un proceso de la misma cuenta sin esa familia se rechaza. El flujo incompleto no crea clave ni trabajo. El agente no abre el `LocalState` del paquete.

Esto no entra al Incremento 1. Es bloqueante para el Incremento 2, y solo después de la prueba de concepto descrita en `07-riesgos.md`.

### Evidencia

- Escritura única de la tarea: `VirtualPrinterBackgroundTask` usa `ApplicationData.Current.LocalFolder`.
- Capacidad única del manifiesto: `runFullTrust`. No está `broadFileSystemAccess`. `MinVersion` `10.0.26100.0`.
- `PackageIdentityTests` exige `runFullTrust`, prohíbe el texto `internetClient` en el manifiesto y el texto `LocalState` en el csproj de Core. No abre la carpeta de otro paquete.
- Búsqueda en `src`: no hay tuberías con nombre.

## 2. OpenXPS

### Problema

La cola virtual pide `application/oxps`. El motor que sabe imprimir solo acepta `application/pdf` y un `PreparedPdf`. Hace falta un adaptador distinto para el OXPS, sin dañar el PDF y sin inventar calidad de conversión.

### Alternativas

- `IPrintDocumentPackageTarget` como primer adaptador hacia la cola física.
- Convertir OXPS a PDF con el raster de PDFium / `PageLayoutCalculator` y reutilizar `PdfSpoolerAdapter`.
- Enviar el OXPS como RAW a la cola.
- Descartar desde ya cualquier adaptador que no sea `IPrintDocumentPackageTarget`.

### Ventajas

- El API de paquete de documento es el camino de Windows para un XPS/OXPS, no el `PrintDocument` GDI del PDF.
- Dejar otros adaptadores abiertos evita bloquear el Incremento 2 si una cola concreta rechaza ese API.
- No tocar el motor PDF conserva deduplicación, fases, STA y la calidad ya medida en el límite del raster.

### Desventajas

- `IPrintDocumentPackageTarget` no aparece en el repositorio. No hay evidencia de que el controlador de una cola dada lo acepte.
- Mantener un segundo adaptador en reserva alarga la prueba. No autoriza a escribirlo antes de ver el fallo real.

### Riesgos

- Una conversión por raster repetiría el problema de papel pixelado que `docs/roadmap.md` deja abierto, y lo aplicaría a un formato que el motor PDF ni siquiera acepta.
- Un RAW sin capacidad anunciada imprime basura o lo rechaza el controlador. El PDC no declara paso directo.
- Paginar un ticket de 80 mm que el PDC no anuncia, o recortar un A4, cambia el documento sin un ancho real.

### Recomendación

Probar primero `IPrintDocumentPackageTarget` en el Incremento 2, contra una cola física concreta, sin afirmar el resto de controladores. Si esa prueba muestra incompatibilidad real, se evalúa otro adaptador de paquete. No se convierte a PDF. No se envía RAW sin una capacidad explícita de esa cola. No se modifica `PrintDispatcher` ni `PdfSpoolerAdapter`. El ticket se sigue validando en memoria y no se persiste. El papel del primer tramo es el de hojas que el PDC ya declara. El ancho tiene que existir antes de paginar. Star BSC10 y POS80 esperan.

### Evidencia

- `PhysicalQueueRules.IsPdf` y el rechazo en `PrintDispatcher` antes de `TryBegin`.
- `PdfSpoolerAdapter.SubmitCoreAsync`: si `Content` no es `PreparedPdf` o no hay páginas, `SpoolerPortException` con `Started` falso.
- `OpenXpsLimits.StructuralValidationAvailable` es `false`. `OpenXpsBoundaryTests` guarda la firma `PK` sin declarar estructura válida.
- `VirtualPrinterPrintTicket.DeclaredFeatures` y el PDC: A4, carta, orientación, color. El XML del ticket no se escribe; el registro solo dice si quedó resuelto o no.
- `PageLayoutCalculator.MaxRasterPixels` es 48 000 000. El comentario de `PdfiumPrintSession` pide conservar comandos de dibujo nativos. `FPDF_SetPrintMode(0)` selecciona EMF.
- `PdfSpoolerAdapter` recorre `PaperSizes` y falla si el nombre elegido ya no está.

## 3. Persistencia del agente

### Problema

El primer receptor LAN tiene que guardar trabajos nuevos, estados, identidades y claves de idempotencia de forma que un corte no cree un segundo trabajo ni acepte una transferencia a medias. El historial JSON de Desktop ya está en uso y no debe reescribirse en el mismo paso.

### Alternativas

- Seguir en JSON, con el mismo `FileJobRecordStore`, también para el agente.
- SQLite para lo nuevo del agente desde el Incremento 1, y dejar los JSON de Desktop hasta el Incremento 4.
- SQLite también para Desktop en el Incremento 1, migrando ya el historial visible.

### Ventajas del JSON actual

- Ya persiste fases, opciones y la nota física. `TryBegin` evita un segundo comienzo con el mismo identificador dentro de una instancia.
- El par temporal + `File.Move` hace que `List` no vea un archivo a medias: solo lee `*.json`, y un `.tmp` no sustituye al registro (`Unreadable_history_file_does_not_hide_the_rest_or_a_temporary_file`).
- No añade una dependencia. El proyecto Core hoy no referencia SQLite.

### Desventajas del JSON actual

- El candado es de instancia y de proceso. No hay mutex con nombre ni `FileShare.None` sostenido durante la comprobación. Dos procesos pueden ver el archivo ausente y escribir los dos. La última sustitución gana. La prueba de dos instancias muestra además que ni siquiera dos objetos del mismo proceso comparten el candado: la caché de `List` se queda vieja.
- `TryBegin` no compara contenido ni destino. Una segunda llamada con otra cola devuelve falso y conserva la primera. Eso no es un conflicto visible; es un silencio.
- No existe la clave dispositivo + origen. El nombre del archivo es el `Id` que elige quien llama.
- No hay una transacción que cubra cuerpo, clave y fila. Un corte entre `WriteAllText` del temporal y `Move` deja el temporal y el JSON anterior. No hay prueba que mate el proceso en ese hueco. La recuperación no borra temporales.
- Un JSON ilegible desaparece de la lista y deja aviso. No hay registro de transacciones que reconstruya la fila.
- La recuperación recorre archivos uno a uno. No es un commit único con el resultado del spooler.

### Cómo sería atómico en SQLite

No hay SQLite en el árbol. El contrato, para el almacén nuevo, es este:

- Un archivo de base, en modo WAL, solo del agente.
- La clave `dispositivo + identificador de origen` es única.
- El protocolo completo, en este orden, es el de la sección «Protocolo de aceptación» de `05-datos-y-estados.md`: temporal, validación, publicación del archivo, commit SQLite y solo entonces despacho. Ningún `SubmitAsync` ocurre antes de que ese commit haya vuelto.
- Si el proceso muere antes del commit, no hay fila. Al arrancar se borran temporales y archivos publicados que ninguna fila referencia. Eso no es un trabajo aceptado y no se despacha.
- Dos peticiones concurrentes con la misma clave pueden escribir dos temporales. La clave única deja un solo commit. La perdedora no despacha: si el resumen y el destino coinciden, devuelve la fila ganadora; si no, responde conflicto. En ninguno de los dos casos hay un segundo spooler.
- El cambio a `SubmittedToSpooler` es otro commit, después de que `Print` haya vuelto. Si el proceso muere antes de ese commit, la fila sigue en `Received` o `Prepared` y el arranque la pasa a `Uncertain` en una transacción, sin llamar a `SubmitAsync`.
- El bloqueo del archivo de SQLite vale entre procesos del mismo equipo. El `Lock` de `FileJobRecordStore` no.

Eso no vuelve atómico el spooler de Windows. El hueco «el spooler ya aceptó y el commit todavía no» sigue siendo `Uncertain` y no se reimprime. Es el mismo criterio que `JobRecovery`, con una frontera clara alrededor de la clave.

### Riesgos

- Meter el historial visible de Desktop en SQLite durante el Incremento 1 mezcla la primera prueba de red con una migración de la ventana.
- Dejar el agente en JSON obliga a la primera prueba autenticada a usar un almacén que no puede expresar el conflicto ni la unicidad entre procesos.

### Recomendación

SQLite desde el Incremento 1 para trabajos nuevos del agente, estados, identidades, destinos publicados y claves. Los JSON de Desktop (`jobs\*.json`, `destinations.json`, `preferences.json`) siguen siendo los de la ventana hasta el Incremento 4, que es cuando esa ventana muestra el historial de red y migra lo histórico. Ese paso no convierte `destinations.json` en la fuente de la LAN ni en un archivo escrito por los dos procesos. El agente todavía no tiene JSON propio: `Worker` no escribe trabajos. Elegir SQLite ahora evita migrar después los primeros trabajos de red.

`FileJobRecordStore` no se reescribe en el Incremento 1. Sigue siendo el historial de la ventana.

### Evidencia

- `FileJobRecordStore`: campo `Lock _gate`, `TryBegin` por `File.Exists`, escritura temporal y `File.Move`.
- Pruebas en `tests/UnitTests/DesktopStoreTests.cs`: segundo `TryBegin` bloqueado, dos instancias, temporal ignorado, confirmación física sin cambiar la fase, `Uncertain` al reabrir.
- `JobRecovery` y `DesktopSession.RecoverAsync`.
- Ausencia de SQLite en los csproj.

## 4. Identidad del agente y del dispositivo

### Problema

Un trabajo que llega por la red no puede ser anónimo, y el equipo cliente tiene que saber que habla con ese anfitrión. La UI de emparejamiento no existe y no debe adelantarse. Los controles sí.

### Alternativas

- Confiar en la red local sin credencial.
- Un token fijo en `appsettings.json` o en el código.
- TLS con validación desactivada y un secreto compartido.
- Certificado del anfitrión, código de un solo uso para la primera confianza, credencial de dispositivo en el almacén de secretos de Windows, permiso por destino.

### Ventajas de la opción elegida

- La primera conexión tiene un acto explícito en el equipo anfitrión, sin CA pública que este repositorio no tiene.
- El secreto no viaja en el fuente ni en el JSON del trabajo.
- Revocar es borrar el permiso y la credencial. No queda un camino anónimo de reserva.
- La UI del Incremento 4 solo muestra lo que el Incremento 1 ya exige.

### Desventajas

- Hay que generar y custodiar un certificado en el equipo anfitrión. Hoy no hay ningún API de certificado en `src`.
- Un código corto puede agotarse por prueba y error si no caduca y si no es de un solo uso. El contrato impone las dos cosas.
- Credential Manager y DPAPI están ligados a la cuenta. Encaja con el proceso en la sesión interactiva y no sirve, sin otra prueba, para un servicio en la sesión 0.

### Riesgos

- Un callback de certificado que siempre devuelve verdadero anula el pin.
- Escuchar en todas las interfaces y abrir el perfil público del firewall publicaría el agente. Este incremento no configura el firewall; el contrato prohíbe dejar el agente de cara a Internet.
- Guardar el código o la clave privada en el registro de diagnóstico filtraría el secreto.

### Recomendación

Desde el Incremento 1:

- El anfitrión presenta un certificado autofirmado. Quien conecta valida TLS. La huella o la clave pública que el cliente guarda la escribió quien opera, leída en el equipo anfitrión por un canal que no es el socket todavía no autenticado. Una huella recibida solo en esa conexión no se acepta como auténtica.
- El código temporal de un solo uso nace ligado a esa huella. Caduca pronto, se guarda solo como resumen y se consume al usarse. Presentarlo con otra clave falla. La ventana de emparejamiento espera al Incremento 4; el vínculo no.
- La comprobación del autofirmado exige a la vez vigencia, uso de clave de servidor, identidad esperada que también se mostró junto al código, y la huella ya verificada. El único error de cadena que puede acompañar a ese acierto es la raíz no confiable de ese certificado concreto. Cualquier otro error TLS rechaza. No hay una rama que devuelva verdadero para cualquier error.
- La credencial del dispositivo y la referencia a la clave del anfitrión viven en Credential Manager de esa cuenta, o en un blob DPAPI de `CurrentUser` si la prueba no puede usar Credential Manager. La fila SQLite guarda el nombre de la credencial y la huella, no el secreto.
- Cada petición comprueba dispositivo no revocado y un destino publicado y concedido. El cliente no envía un nombre de cola.
- Renovar exige un código nuevo ligado a la huella nueva, otra vez por el canal independiente, o una sustitución dentro de una sesión que ya autenticó el certificado anterior todavía vigente. Un certificado caducado no autoriza su propio reemplazo. Un anuncio de huella nueva en una conexión no autenticada se ignora.

### Evidencia

- `src/Agent` no escucha, no tiene certificados y su `appsettings.json` no contiene secretos de dispositivo.
- Accesos y Conexiones no emparejan.
- El manifiesto de la impresora virtual no declara `internetClient`. El receptor no debe meterse en ese paquete para «ganar» red: el agente es otro proceso.

## 5. Transporte frente a fase de impresión

### Problema

Mezclar la transferencia, el spooler y el papel en un solo enum haría que una respuesta de red perdida pareciera una reimpresión, o que una aceptación del spooler pareciera papel.

### Alternativas

- Añadir valores de red a `JobPhase`.
- Dejar la red solo en el detalle de texto.
- Tres máquinas separadas: transferencia, `JobPhase`, cierre de la tarea ante Windows. La confirmación física sigue siendo un campo.

### Ventajas

- `JobRecovery` ya entiende exactamente cinco fases. Ampliar el enum obligaría a revisar Desktop, los filtros de la página de trabajos y las pruebas.
- Una transferencia a medias no tiene fase de impresión porque no es un trabajo.
- `JobCaptureStatus` ya está separado y cierra otro contrato (el de Windows).

### Desventajas

- Hay que documentar y probar las fronteras. Un cliente puede querer «un estado» para mostrarlo. La UI del Incremento 4 tendrá que mostrar las máquinas sin fundirlas.

### Riesgos

- Tratar el conflicto como `DuplicateDetail` ocultaría un destino distinto y devolvería la fase del trabajo viejo como si el nuevo hubiera sido aceptado.
- Reimprimir `Uncertain` para «resolver» una caída produciría una segunda copia cuando el spooler sí llegó a aceptar la primera.

### Recomendación

No se añaden valores a `JobPhase`. La transferencia tiene sus propios estados (`EnCurso`, `Aceptada`, `Rechazada`, `Conflicto`). Solo `Aceptada` crea un trabajo en `Received`. La nota física solo se escribe tras `SubmittedToSpooler`. `JobCaptureStatus` no se reutiliza para la red ni para el spooler. `Uncertain` no se reimprime solo.

### Evidencia

- `JobPhase.cs`, `JobCaptureStatus.cs`, `JobRecord.PhysicalConfirmation`.
- `RecordPhysicalConfirmation` lanza si la fase no es `SubmittedToSpooler`.
- `JobsPage` habilita la confirmación solo en esa fase y distingue la línea del spooler del papel.
- `HomeBanner`: una cola instalada, y unas capacidades leídas, no confirman papel.

## 6. Motor PDF compartido

### Problema

El agente tiene que despachar con el mismo PDFium, el mismo GDI y las mismas reglas que Desktop. Esos tipos están compilados dentro del proyecto WinUI. El agente solo referencia Core y su moniker es `net10.0`, sin Windows.

### Alternativas

- Hacer que el agente referencie el proyecto Desktop.
- Copiar los tres archivos dentro del agente y mantenerlos a mano.
- Extraer una biblioteca Windows sin WinUI y usarla desde los dos procesos.
- Dejar el motor solo en Desktop y que el agente le pida a la ventana que imprima.

### Ventajas

- Una sola implementación. Las pruebas de despacho de Core y el límite nativo que ya corre sin abrir WinUI siguen hablando del mismo código.
- El agente no arrastra la ventana, el tema ni `Microsoft.WindowsAppSDK`.

### Desventajas

- Hay que cambiar el moniker del agente a `net10.0-windows10.0.26100.0` para poder referenciar la biblioteca. Eso es el TFM de Windows, no WinUI.
- `Windows.Data.Pdf` hoy solo se compila dentro de Desktop. El repositorio no demuestra que compile sin `UseWinUI`. La compilación del Incremento 1 es la que lo tiene que demostrar. Si no compila, no se añade WinUI al agente: se detiene y se anota el paquete mínimo del SDK de Windows que haría falta, sin el de la ventana.

### Riesgos

- Una copia divergente cambiaría el raster, el STA o el rechazo de lo que no es PDF.
- Referenciar Desktop acoplaría el receptor a WinUI y a la ventana que el MVP quiere separada.

### Recomendación

En el Incremento 1 se crea una biblioteca de clase, ausente hoy, por ejemplo `src/Windows/PrintEngine`. Recibe sin cambio de lógica `PdfDocumentPreparer`, `PdfiumPrintSession`, `PdfSpoolerAdapter` y, porque el despacho no resuelve colas sin ella, `WindowsPrinterProbe` con el `StaQueue` que ya usa. La vista previa y el XAML se quedan en Desktop. Desktop y el agente referencian la biblioteca. El agente no referencia Desktop. Se conservan el modo EMF, `PrintDocument`, el hilo `NPB print STA`, las validaciones y `PrintDispatcher` en Core. Las versiones de `System.Drawing.Common` y de PDFium no se mueven. La comprobación de compilación, dependencias y ejecución en el proceso interactivo del agente está en `02-arquitectura-a2-b1.md`.

### Evidencia

- Rutas y csproj descritos en el diagnóstico.
- `tests/WindowsPrintTests` ya compila `PdfiumPrintSession` sin lanzar WinUI, pero tomándolo del árbol de Desktop.
- `PackageIdentityTests` exige que Core no use WinUI.

## 7. Destinos publicados

### Problema

Desktop ya guarda destinos lógicos en `destinations.json`. Si el agente escribiera ese mismo archivo, la ventana y el receptor compartirían un JSON con candado de instancia. El cliente, además, no debe poder nombrar cualquier cola instalada en el anfitrión.

### Alternativas

- Compartir `destinations.json` entre Desktop y el agente.
- Aceptar en la petición el nombre de cola que elija el cliente.
- Registrar el destino en el agente, por configuración local de quien administra ese equipo, y guardarlo solo en SQLite.

### Ventajas

- La fuente de verdad de la LAN es una tabla con la misma transacción que la autorización.
- El nombre de cola no viaja como dato de confianza del cliente. El agente lo resuelve desde su fila.

### Desventajas

- En el Incremento 1 hay dos catálogos: el JSON local de la ventana y la tabla del agente. No se sincronizan solos. Es el costo de no mezclarlos antes del Incremento 4, y ni siquiera entonces el JSON pasa a ser la fuente de la red.

### Riesgos

- Una escritura compartida reintroduce la carrera de `FileDestinationStore`, que también usa un `Lock` de instancia y un temporal.
- Publicar una cola porque el cliente escribió su nombre saltaría el permiso.

### Recomendación

SQLite del agente es la única fuente de los destinos LAN. En el MVP, quien administra el equipo anfitrión registra un destino físico en una configuración local del agente, fuera de la carpeta Desktop. El agente vuelca ese registro a SQLite y, a partir de ahí, el receptor solo lee la tabla. No abre `destinations.json`. El cliente elige entre los destinos publicados y concedidos a su dispositivo, por identificador. Un nombre de cola arbitrario se rechaza antes del temporal. Sigue habiendo un solo destino publicado en el MVP.

### Evidencia

- `FileDestinationStore` escribe `%LOCALAPPDATA%\Neobash\PrintBridge\Desktop\destinations.json` cuando lo construye Desktop (`App.xaml.cs`).
- `PrintDestination` guarda `QueueName` junto al identificador. El receptor nuevo no acepta ese nombre desde la red.
- No hay tabla de destinos en el agente: el proyecto no tiene SQLite.

## Decisiones que acompañan a las anteriores

- El agente del MVP es un proceso propio en la sesión interactiva, separado de la ventana WinUI. Un servicio de Windows, o un servicio más un proceso de sesión, queda para una prueba posterior. No se escribe «imposible». La restricción STA de `System.Drawing.Printing` se comprueba antes de mover el adaptador a la sesión 0; no se usa como veto eterno.
- Seguridad desde el Incremento 1. El Incremento 4 añade la UI de emparejamiento y no estrena los controles.
- Una cola virtual vinculada a un destino publicado por el agente, sin alta dinámica. GELIA-NV fuera. Windows 10 fuera del requisito. El cliente no inventa el nombre de la cola.
- El firewall se documenta y no se configura en este cambio ni como efecto colateral del Incremento 1.
