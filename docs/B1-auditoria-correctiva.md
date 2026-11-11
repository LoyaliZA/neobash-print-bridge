# B1: auditoria correctiva del PR #5

Fecha: 11 de noviembre de 2026. Equipo Windows 10.0.26300, x64.
SDK .NET 10.0.403; runtimes .NET, ASP.NET Core y Windows Desktop 10.0.14.
Base auditada: `5390fd0ebd98f10a3d5006c478ca4f1ba1fe4dc1`.
Rama: `feature/b1-lan-pdf`; base remota del PR: `9ea9dea8c36b5bfae053c6c6bf5bbced239bf390`.

## Veredicto

Correcciones aptas para revisar y publicar en el PR en borrador. La aprobacion
para uso operativo queda condicionada a la prueba entre dos equipos Windows y
a la comprobacion de papel con la impresora prevista. No se publico ningun commit.

Se verifico inicialmente un arbol limpio. El checkout estaba en
`feature/a15-desktop-experience`; se actualizaron referencias remotas y se cambio
sin merges a B1, exactamente al head del PR. `main` no se modifico.
El PR estaba listo para revision y se convirtio a borrador por instruccion del usuario.

## Defectos confirmados y correcciones

### Alta: certificado demasiado grande para Credential Manager

`PairingService.WriteMaterial` serializaba un PFX RSA con su contrasena y nombre
como JSON/base64. Se midieron sobres de **3735 y 3625 bytes** para localhost,
superiores al limite de **2560 bytes** de un blob generico. El arranque inicial
y la rotacion fallaban en el almacen Windows escogido por la fabrica.

`Secrets.cs` conserva las credenciales cortas en Credential Manager y guarda los
certificados con DPAPI `CurrentUser`, bajo `secrets` en el directorio de datos.
Escribe un archivo temporal protegido, hace flush y reemplaza el archivo final.
Lee el material legado conservando los mismos bytes e identidad y lo migra a
DPAPI; elimina el valor legado solo despues de persistir el nuevo almacenamiento.
La lectura nativa distingue secreto ausente (`ERROR_NOT_FOUND`) de otros errores.
Los secretos corruptos producen error. `PairingService` impide crear otra identidad
si falta el certificado y SQLite ya contiene dispositivos, incluso revocados.
No se cambia el formato del PFX ni la forma de cargarlo compatible con Schannel.

Pruebas: credencial nativa aleatoria, DPAPI de 8192 bytes, certificado real de
produccion, reinicio en otro proceso, autenticacion y TLS tras reinicio, rotacion
persistida, sobre corrupto rechazado y certificado ausente con dispositivos.

### Alta: segundo agente recuperaba una base activa

`LanSession.Open` ejecutaba `Recover` antes de que Kestrel detectara un puerto
ocupado. Otro proceso, incluso con otro puerto, podia eliminar temporales o
marcar como inciertos trabajos todavia activos.

`LanRuntime.cs` toma un handle exclusivo de `listener.lock` antes de abrir SQLite,
recuperar, sincronizar destinos o cargar certificados. El handle vive durante la
sesion y Windows lo libera al morir el proceso. Un archivo residual no bloquea
un reinicio: lo que excluye es el handle abierto, no la existencia del archivo.
Las sesiones administrativas no recuperan ni sincronizan destinos y no pueden
abrir Kestrel. El servidor se libera si falla el inicio.

Pruebas: dos procesos simultaneos; el segundo falla; temporal y fase `Prepared`
se conservan; al matar al primero, otro proceso inicia con la misma identidad,
borra el temporal y marca el trabajo `Uncertain`, sin despacharlo otra vez.

### Alta: revocacion durante una transferencia

El receptor conservaba un `DeviceRecord` autorizado antes de recibir el cuerpo.
`TryCommit` comprobaba el destino y permiso, pero no `devices.revoked_at`.
Una transferencia iniciada antes de revocar podia crear una nueva aceptacion
una vez efectiva la revocacion.

`AgentDatabase.TryCommit` vuelve a comprobar `revoked_at IS NULL` dentro de la
misma transaccion `BEGIN IMMEDIATE` que crea la aceptacion. `Revoke` usa tambien
una transaccion de escritura: ambas operaciones tienen un orden definido.
La consulta de idempotencia anterior se mantiene: una aceptacion previa sigue
siendo la misma y no vuelve a despacharse. Una peticion nueva sin autenticacion
vigente sigue siendo rechazada en HTTP.

Prueba: cuerpo pausado, revocacion desde otra conexion SQLite, reanudacion,
rechazo sin fila de idempotencia nueva ni documento huerfano, historial conservado
y repeticion de la clave aceptada sin otra llamada al spooler.

### Media: exposicion en todas las interfaces por defecto

`AgentConfig.ResolveListenAddress` devolvia `IPAddress.Any`. Ahora devuelve
loopback si no se configura direccion. Para LAN hay que seleccionar explicitamente
la IP privada del anfitrion. No se modificaron reglas del firewall.
La configuracion explicita sigue bajo responsabilidad del operador; el codigo
no descubre ni garantiza el perfil de red de esa interfaz.

### Media: fallo del agente devolvia exito

El ejecutable publicado detenia el host por una excepcion del `BackgroundService`,
pero `Program.cs` devolvia 0. Se demostro en la prueba del segundo arranque.
`AgentSession.cs` marca los errores inesperados con codigo 1 y `Program.cs`
conserva ese codigo. La cancelacion normal no se marca como fallo.

### Media: comandos y persistencia del cliente

La descripcion del PR indicaba `--file`, pero `send` exigia `--pdf`.
Se adopta `--file` y se mantiene `--pdf` como alias; dos rutas distintas se rechazan.
`status` devuelve error cuando no hay aceptacion; `destinations` rechaza respuestas
HTTP fallidas. Un intento de `pair` fallido ya no sobrescribe la confianza guardada.
Los errores comunes de argumentos, archivos y red se presentan como error de consola.

Pruebas HTTPS de `pair`, `send` con ambas opciones e idempotencia, `status` existente
y ausente, `destinations`, `renew` y consulta con el certificado rotado.
Los comandos administrativos `pair`, `stage`, `revoke` y `renew` se ejecutaron
con el agente Windows publicado en almacenamiento temporal aislado.

## Validacion en este Windows

- Baseline unitario: **150/151**, una falla por lectura exclusiva del archivo SQLite
  desde la propia prueba. Se corrigio para abrirlo con `FileShare.ReadWrite | Delete`.
  No se elimino la comprobacion de ausencia de credencial en la base.
- Unitarias corregidas: **154/154**, sin omitidas.
- WindowsPrintTests: **8/8**, sin omitidas. PDFium/GDI, EMF, validaciones,
  vista previa WinRT y cola STA; estas pruebas no imprimen papel.
- Desktop Release x64: compilacion XAML correcta.
- Publicacion Release `win-x64`: Agent, Client y Desktop verificadas.
- Agent publicado contiene `pdfium.dll` y avisos/licencias de terceros.
- `LanProcessProbe`: persistencia nativa y DPAPI, dos procesos, fallo del segundo,
  recuperacion tras muerte, misma identidad, credencial y TLS tras reinicio,
  rechazo anonimo, rotacion y material corrupto.
- `Smoke-Agent.ps1`: ejecutable Agent real, segundo inicio con error, operaciones
  administrativas, muerte, reinicio y reemplazo explicito del certificado.
- No se modificaron el manifiesto MSIX, OXPS, motor PDFium/GDI, colas o preferencias
  permanentes de impresoras, reglas de firewall ni certificados de confianza Windows.

Hubo avisos NU1900 al principio por acceso a NuGet y tres avisos CS8629 preexistentes
en PageLayoutTests. Las restauraciones necesarias se completaron con acceso de red.
La prueba MSIX que fallo en Linux paso aqui con los artefactos locales disponibles;
no se reconstruyo ni modifico el paquete MSIX como parte de esta auditoria.

## Archivos modificados

- `src/Lan/Secrets.cs`, `PairingService.cs`: almacenamiento e identidad.
- `src/Lan/LanRuntime.cs`, `LanHost.cs`: exclusividad y liberacion del servidor.
- `src/Agent/AgentSession.cs`, `Program.cs`: codigo de fallo del host.
- `src/Lan/AgentDatabase.cs`: autorizacion atomica al aceptar.
- `src/Lan/AgentConfig.cs`: loopback predeterminado.
- `src/Lan/Consoles.cs`, `LanClient.cs`: comandos del cliente.
- `tests/UnitTests/LanProtocolTests.cs`: regresiones y lectura SQLite Windows.
- `tests/LanProcessProbe/`: comprobacion reproducible entre procesos Windows.
- `scripts/b1/Smoke-Agent.ps1`: prueba del ejecutable publicado.
- `docs/B1-auditoria-correctiva.md`, `docs/B1-prueba-LAN-Windows.md`, `README.md`:
  resultados e instrucciones concordantes con el codigo.

## Repetir la validacion

Desde PowerShell, en la raiz del repositorio:

```powershell
dotnet test tests/UnitTests -c Release --logger "trx;LogFileName=corrected-unit.trx" --results-directory artifacts/b1-audit
dotnet test tests/WindowsPrintTests -c Release --logger "trx;LogFileName=windows-print.trx" --results-directory artifacts/b1-audit
dotnet build src/Windows/Desktop/Neobash.PrintBridge.Desktop.csproj -c Release -p:Platform=x64
dotnet publish src/Agent/Neobash.PrintBridge.Agent.csproj -c Release -r win-x64 --self-contained false -o artifacts/b1-audit/agent
dotnet publish src/Client/Neobash.PrintBridge.Client.csproj -c Release -r win-x64 --self-contained false -o artifacts/b1-audit/client
dotnet publish src/Windows/Desktop/Neobash.PrintBridge.Desktop.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained false -o artifacts/b1-audit/desktop
dotnet run --project tests/LanProcessProbe -c Release
pwsh -NoProfile -File scripts/b1/Smoke-Agent.ps1
```

Los resultados TRX y publicaciones estan bajo `artifacts/b1-audit` (ignorados por Git).
Las pruebas de proceso muestran su directorio temporal de evidencia; usan nombres
aleatorios de credenciales y almacenan el estado de prueba protegido con DPAPI.

## Limites y comprobacion fisica pendiente

No hubo segunda PC ni observacion de papel. Falta probar DNS/IP, perfil privado,
firewall existente, conectividad real y Schannel entre ambos equipos, ademas de
permisos de la cola, controlador, papel, margenes y calidad en la impresora prevista.
La prueba de procesos usa spooler simulado; el smoke del agente no envia trabajos.
No se validaron aislamiento DPAPI con otra cuenta, corte electrico durante un
reemplazo, restauracion de perfil Windows ni todas las politicas corporativas.
El bloqueo se valido en disco local Windows, no sobre carpetas compartidas.

El certificado incluye la identidad elegida al primer arranque. Cambiar HostName
no reemite el certificado automaticamente. No borrar `secrets` ni iniciar con otra
cuenta para intentar reparar una identidad existente: conservar la cuenta y restaurar
el almacenamiento, o decidir explicitamente reemplazo y nuevo emparejamiento.
La renovacion TLS global requiere coordinar los clientes vinculados; los que aun
conservan la huella anterior no deben aceptar la nueva automaticamente.
`SubmittedToSpooler` no demuestra salida fisica. B1 queda antes de A2 hasta cerrar
la prueba descrita en [la guia de dos PCs](B1-prueba-LAN-Windows.md).
