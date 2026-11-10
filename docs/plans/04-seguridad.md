# Incremento 0 — seguridad

Los controles de esta página son obligación del Incremento 1, no de la UI del Incremento 4. El repositorio de hoy no autentica, no escucha y no guarda credenciales. Donde se cita un archivo, es el comportamiento que el control tiene que conservar o el hueco que tiene que cerrar.

## Límites de confianza

- El equipo cliente es quien imprime. Su cola virtual captura. No es el anfitrión de la impresora física.
- El equipo anfitrión ejecuta el agente en la sesión interactiva de quien opera ese equipo, y el controlador de la cola física.
- La tarea MSIX solo escribe en su `LocalFolder`. No es un receptor de red. El manifiesto no le da `internetClient` ni `broadFileSystemAccess`.
- La ventana Desktop no es el receptor y no está empaquetada. Su carpeta `%LOCALAPPDATA%\Neobash\PrintBridge\Desktop` no es el `LocalState` del paquete.
- La red local no es una zona de confianza. Una máquina en el mismo segmento no queda autorizada por estar ahí.
- El spooler no es un límite de confianza del papel. Aceptar el trabajo no demuestra que haya salido una hoja.
- GELIA-NV no es un componente de confianza ni un canal. No hay cliente ni configuración.

El agente no se expone a Internet. No se documenta un reenvío de puertos ni una escucha pensada para una interfaz pública. Este cambio no crea ni modifica reglas de firewall.

## Amenazas y controles

- Trabajo anónimo. Control: sin credencial de dispositivo no se crea fila ni se llama al spooler. No hay un modo de invitado.
- Credencial robada de otro destino. Control: la autenticación no basta. La fila de permisos liga dispositivo y destino. En el MVP ese permiso es uno. Otro destino se rechaza con la sesión TLS ya válida.
- Suplantación del anfitrión. Control: validación TLS real y pin de la huella aprendida en el emparejamiento. Un certificado distinto se rechaza. Está prohibido un callback que devuelva verdadero para cualquier certificado, y está prohibido desactivar la comprobación del nombre o de la cadena para «hacer funcionar» un autofirmado sin pin.
- Token en el fuente o en `appsettings.json`. Control: prohibido. `appsettings.json` del agente hoy solo tiene niveles de registro; debe seguir sin secretos. Las pruebas no fijan una credencial de producción.
- Repetición con otro cuerpo o otro destino. Control: la clave única dispositivo + origen responde conflicto y no abre un trabajo nuevo.
- Respuesta perdida que el cliente interpreta como fallo y reintenta. Control: el reintento de la misma clave, el mismo resumen y el mismo destino devuelve la fase ya guardada y no llama al spooler. Si la fase es `Uncertain`, se devuelve `Uncertain`.
- Transferencia cortada. Control: sin commit no hay trabajo. El temporal se descarta al arrancar si no tiene fila.
- Caída junto al spooler. Control: `Uncertain` y ninguna reimpresión automática. Igual que `JobRecovery`.
- Otro proceso de la misma cuenta, cuando exista la tubería local. Control: ACL por SID más token efectivo más familia de paquete. Hasta la prueba de concepto, la tubería no se implementa. El Incremento 1 no la necesita: el PDF entra por TLS.
- Lectura del `LocalState` ajeno como atajo. Control: no es el diseño. El repositorio no demuestra esa lectura. No se escribe código que la dé por hecha.
- Recorrido de rutas. Control: el cliente no elige la ruta local. El agente asigna el archivo bajo su raíz. Se reutilizan `JobIdRules` y la comprobación de prefijo de `FileSystemJobFileStore` y de `FileJobRecordStore.PathFor` (rechazo de `..`, de separadores y de un resultado fuera de la raíz).
- Documento enorme o muchos envíos. Control: topes de contrato, no mediciones de una impresora. El código actual no tiene tope de tamaño: `JobCaptureStore` copia hasta que el flujo termina. El receptor nuevo tiene que dejar de escribir al pasar el tope y no confirmar la clave.
- PDF o OXPS malicioso. Control: no se añade un analizador nuevo. Lo que no sea `application/pdf` no llega a `PdfDocumentPreparer` ni a PDFium. El OXPS no entra al motor PDF. No se registra el cuerpo. No se afirma que PDFium o `Windows.Data.Pdf` sean un aislamiento: el riesgo residual es el de esas bibliotecas, ya usadas por Desktop.
- Registro que guarda el documento o el secreto. Control: la auditoría lista identificadores, destino, tamaños, estados y el resultado de la autenticación. No lista el código de un solo uso, la credencial, la clave privada, el XML del ticket ni los bytes del documento.
- Falsificar papel. Control: la confirmación física sigue siendo una nota escrita por quien ve la hoja, solo después de `SubmittedToSpooler`. Los textos de `HomeBanner` y de la página de trabajos se mantienen.

## Certificado del anfitrión

El agente genera un certificado en el equipo anfitrión. La clave privada no sale de esa cuenta.

Quien conecta, después del emparejamiento, solo acepta la huella guardada. La validación por defecto de la plataforma sigue activa para caducidad y para el uso de la clave. Si el certificado es autofirmado, el pin sustituye a una CA que este proyecto no tiene: eso es una comprobación explícita de una sola clave pública, no un `return true`.

El certificado no se copia al repositorio ni a `local-certs` de la POC de firma del MSIX. Ese certificado (`docs/installation.md`, `CN=Neobash Print Bridge POC`) firma el paquete. No autentica la LAN. Son materiales distintos.

## Emparejamiento

1. El agente, en la sesión interactiva, genera un código de un solo uso, con caducidad corta, y muestra el código en su salida local. No lo escribe en el registro de trabajos.
2. Quien opera el equipo cliente lo introduce una vez.
3. El agente entrega la huella del certificado del anfitrión y crea la credencial del dispositivo y el permiso del destino.
4. El código queda gastado. Un segundo uso falla. Un código caducado falla.
5. A partir de ahí cada envío usa TLS y esa credencial.

La ventana que hará estos pasos visibles es del Incremento 4. Un banco de pruebas del Incremento 1 puede introducir el código sin esa ventana. No puede saltarse el código, el pin ni el permiso.

## Dónde viven las credenciales

Recomendación: Credential Manager de la cuenta interactiva, tanto en el cliente como en el anfitrión. La base SQLite guarda el nombre del destino de la credencial y la huella del certificado.

Alternativa, si la prueba no puede usar Credential Manager: blob cifrado con DPAPI `CurrentUser`, en una ruta del agente distinta de la carpeta de documentos, con ACL de esa cuenta. No `LocalMachine`, porque el MVP no es un servicio.

Ni el código ni el JSON de un trabajo contienen el secreto. Desktop no recibe una copia «para mostrarla» en `preferences.json`.

El repositorio no llama hoy a Credential Manager ni a DPAPI. Elegir uno es parte del Incremento 1, no una observación.

## Revocación

Quien opera el anfitrión revoca un dispositivo borrando o marcando su fila y eliminando la credencial. La petición siguiente falla la autorización aunque el certificado del anfitrión siga vigente. No hay periodo de gracia anónimo. No hay lista de revocación pública: la tabla del agente es la autoridad, porque el emparejamiento también fue local.

Revocar no borra el historial de trabajos ya aceptados. Esos siguen en su fase, incluido `Uncertain`, para no ocultar un posible envío al spooler.

## Renovación

El certificado del anfitrión tiene caducidad. Renovarlo exige uno de estos caminos:

- un código nuevo de un solo uso, que fija la huella nueva;
- o una renovación ya autenticada, con el certificado viejo todavía vigente, que reemplaza la huella en el cliente.

Un cambio de certificado que no pase por uno de esos caminos se rechaza. El cliente no «actualiza el pin» porque la conexión falló.

La credencial de dispositivo se rota con el mismo criterio: la anterior deja de valer cuando la nueva queda confirmada. No conviven dos credenciales válidas para el mismo dispositivo.

## Tamaño y frecuencia

Topes de contrato para el Incremento 1, elegidos para poder probarlos, no medidos en una impresora de este repositorio:

- Cuerpo máximo aceptado: 32 MiB. Al superarlos se corta la lectura, se borra el temporal y la respuesta es rechazo. No hay fila.
- Una transferencia en curso por dispositivo.
- Como máximo 30 trabajos aceptados por dispositivo en 10 minutos. El exceso se rechaza sin crear clave.

`PageLayoutCalculator.MaxRasterPixels` (48 000 000) es un tope de píxeles del PDF ya aceptado. No sustituye al tope de bytes de la red.

La captura actual de la tarea no aplica estos topes. No se le cambian en el Incremento 1: el límite nuevo es del receptor del agente.

## Recorrido de rutas y nombres

`JobIdRules.EnsureSafe` rechaza vacío, longitud mayor de 80, `..`, caracteres inválidos y un inicio que no sea letra o dígito ASCII. `FileSystemJobFileStore` y `FileJobRecordStore.PathFor` comprueban que la ruta final siga bajo la raíz.

El identificador de origen que llega por la red pasa por la misma regla antes de usarse en un nombre de archivo. Un destino es una clave de la tabla de permisos, no un fragmento de ruta. Está prohibido concatenar un texto del cliente con `Path.Combine` hacia el perfil o hacia el paquete.

## Documentos maliciosos

- Tipo distinto de `application/pdf`: rechazo de transporte, sin `TryBegin` y sin PDFium. Es el mismo criterio que `PrintDispatcher`, adelantado al receptor para no crear un trabajo.
- PDF que el preparador o el spooler no pueden abrir: `Failed` si el envío al spooler no empezó, con el mensaje de la excepción ya existente. No se reintenta solo.
- OXPS en el receptor del Incremento 1: rechazo. No se transforma.
- No se ejecuta un conversor externo ni se descomprime el OXPS «para mirar». La validación estructural sigue desactivada (`OpenXpsLimits`).

## Retención

El Incremento 1 no instala un barrido automático.

- La fila del trabajo se conserva, incluida la fase y la nota física si existe.
- El cuerpo de un trabajo `Uncertain` se conserva. Borrarlo escondería el único archivo asociado a un envío que quizá llegó al spooler.
- Borrar cuerpos de trabajos `SubmittedToSpooler` o `Failed` queda para una política posterior, explícita. No forma parte de la autorización del Incremento 1.
- Los JSON históricos de Desktop no se purgan ni se migran aquí.

## Auditoría

Eventos mínimos del agente, en texto o en una tabla de la misma base, sin el documento:

- emparejamiento aceptado o rechazado, sin el código;
- autenticación aceptada o rechazada, con identificador de dispositivo;
- rechazo por destino, tamaño, tipo o frecuencia;
- conflicto de idempotencia, con la clave y sin el cuerpo;
- transición de fase del trabajo;
- arranque que pasa filas a `Uncertain`, con el recuento.

`FileDiagnosticLog` hoy escribe mensajes libres, incluidos textos de excepción. El agente nuevo no debe volcar ahí la excepción si el mensaje puede incluir una ruta elegida por el cliente o un fragmento del documento. Puede registrar el tipo de fallo y el identificador.

Los registros actuales de la tarea (`task.log`) y de Desktop (`diagnostic.log`) no se convierten en auditoría de red. Siguen sirviendo a su proceso.

## Firewall, solo como nota operativa

Cuando alguien vaya a probar dos equipos, la regla que tendría que crear a mano en el anfitrión es: entrada TCP, perfil privado, puerto elegido por la configuración local del agente, programa del agente, sin perfil público y sin «cualquier programa».

Este repositorio no incluye script, manifiesto de firewall ni instrucción ejecutable que la cree. El Incremento 1 tampoco debe añadirla por su cuenta. Si el puerto no está abierto, la prueba se detiene y se pide la regla; no se abre el perfil público para compensar.
