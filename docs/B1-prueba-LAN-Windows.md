# B1: prueba LAN entre dos equipos Windows

Comandos contrastados con el codigo y ejercitados por HTTPS en loopback en Windows.
La ejecucion entre dos equipos y la comprobacion de papel estan pendientes.

## Requisitos

Dos equipos Windows x64, version minima 10.0.26100, con .NET 10 y ASP.NET Core 10.
Anfitrion y cliente en una LAN privada de confianza. Una cola fisica instalada en
el anfitrion y papel cargado para la etapa de impresion real.
No usar Desktop para enviar el mismo trabajo al mismo tiempo.

No se crean reglas ni se abren puertos desde estos comandos. Comprobar las reglas
existentes con el administrador. Si el acceso no esta permitido, detener la prueba
y pedir una decision explicita sobre el puerto, programa, origen y perfil privado.
No usar el perfil publico ni desactivar el firewall.

## Preparar el anfitrion

Desde la raiz del repositorio, en PowerShell:

```powershell
dotnet publish src/Agent/Neobash.PrintBridge.Agent.csproj -c Release -r win-x64 --self-contained false -o artifacts/b1-audit/agent
$agentDir = (Resolve-Path artifacts/b1-audit/agent).Path
$env:Agent__DataDirectory = Join-Path $env:LOCALAPPDATA 'Neobash\PrintBridge\Agent'
New-Item -ItemType Directory -Force -Path $env:Agent__DataDirectory | Out-Null
if (-not (Test-Path (Join-Path $env:Agent__DataDirectory 'destination.local.json'))) {
    Copy-Item (Join-Path $agentDir 'destination.local.example.json') (Join-Path $env:Agent__DataDirectory 'destination.local.json') -ErrorAction Stop
}
notepad (Join-Path $env:Agent__DataDirectory 'destination.local.json')
```

**Si ya existe `destination.local.json`, omitir Copy-Item y conservar el archivo.**
Editar id (por ejemplo `mostrador`), displayName, queueName (nombre exacto de la
cola Windows) y paperName (papel admitido, por ejemplo `A4`). Es una configuracion
del puente; no cambia las preferencias permanentes de la impresora.

Seleccionar una IP IPv4 de la interfaz LAN privada del anfitrion (puede consultarse
con `Get-NetIPConfiguration` y `Get-NetConnectionProfile`). Antes del primer inicio:

```powershell
$lanIp = Read-Host 'IP privada LAN de este anfitrion'
$env:Agent__ListenAddress = $lanIp
$env:Agent__HostName = $lanIp
$env:Agent__Port = '8741'
Set-Location $agentDir
.\Neobash.PrintBridge.Agent.exe
```

La escucha vacia ahora significa **127.0.0.1**; configurar la IP LAN es obligatorio
para dos equipos. No elegir `0.0.0.0` ni `::`.

HostName fija la identidad SAN del certificado al primer arranque. Si la identidad
ya existe, usar su nombre/IP y comprobar que resuelve a la interfaz elegida.
Cambiar la variable no cambia el certificado existente. No borrar el almacen para
forzar un cambio: decidir explicitamente reemplazo y nuevo emparejamiento.

El anfitrion muestra nombre, huella SHA-256, puerto, codigo y direccion de escucha.
Compartir huella y codigo por un canal independiente. El codigo dura 10 minutos y
sirve una sola vez. La huella de confianza se lee en esa consola, nunca se aprende
de un anuncio de red. Mantener el agente en la cuenta interactiva que lo configuro.

## Preparar el cliente

Desde la raiz del repositorio en la segunda PC:

```powershell
dotnet publish src/Client/Neobash.PrintBridge.Client.csproj -c Release -r win-x64 --self-contained false -o artifacts/b1-audit/client
Set-Location artifacts/b1-audit/client
$hostIp = Read-Host 'IP o nombre exacto mostrado por el anfitrion'
Test-NetConnection -ComputerName $hostIp -Port 8741
```

Si `TcpTestSucceeded` es falso, detenerse. No abrir el firewall automaticamente.

```powershell
$pin = Read-Host 'Huella SHA-256 leida en el anfitrion'
$code = Read-Host 'Codigo de un solo uso leido en el anfitrion'
.\Neobash.PrintBridge.Client.exe pair --host $hostIp --port 8741 --fingerprint $pin --code $code
.\Neobash.PrintBridge.Client.exe destinations
```

Guardar el ID del dispositivo mostrado por pair para probar revocacion. Verificar
que destinations muestra el destino publicado y no expone el nombre de cola.
Un codigo equivocado debe fallar y conservar la confianza previamente guardada.

## Enviar un PDF y comprobar idempotencia

La siguiente accion **puede imprimir papel**. Ejecutarla solo con el PDF de prueba
y la impresora listos, observando el resultado en el anfitrion.

```powershell
$pdf = Read-Host 'Ruta absoluta al PDF de prueba'
$destination = Read-Host 'ID publicado del destino'
$source = 'b1-' + [guid]::NewGuid().ToString('N')
.\Neobash.PrintBridge.Client.exe send --destination $destination --source $source --file $pdf
.\Neobash.PrintBridge.Client.exe status --source $source
# Repetir exactamente el mismo documento/destino/origen: no debe crear otra impresion.
.\Neobash.PrintBridge.Client.exe send --destination $destination --source $source --file $pdf
.\Neobash.PrintBridge.Client.exe status --source $source
```

`--file` es la opcion recomendada; `--pdf` es un alias compatible. No proporcionar
dos rutas diferentes. Misma clave con otro contenido o destino debe dar conflicto.
Guardar source y jobId. `Aceptada` indica persistencia; `SubmittedToSpooler` indica
aceptacion por el spooler. Anotar aparte quien vio la hoja y si tamano, margenes,
escala y contenido coinciden. No declarar impresion fisica solo por el estado.

## Administracion, reinicio y revocacion

En otra consola del anfitrion, definir **las mismas variables Agent__*** y ejecutar
el mismo binario. Estos comandos no arrancan otro receptor:

```powershell
.\Neobash.PrintBridge.Agent.exe pair
.\Neobash.PrintBridge.Agent.exe stage
```

`pair` emite otro codigo sin recuperar la base. `stage` muestra una nueva huella,
pero todavia no cambia la escucha. Para confirmarla, en el cliente vinculado:

```powershell
$newPin = Read-Host 'Huella nueva leida directamente en el anfitrion'
.\Neobash.PrintBridge.Client.exe renew --fingerprint $newPin
.\Neobash.PrintBridge.Client.exe status --source $source
```

Coordinar esta operacion: la huella TLS es global del anfitrion. Otros clientes que
conservan la anterior no aceptaran la nueva automaticamente. Si se pierde el resultado
de la renovacion, resolverlo con el operador; no desactivar la validacion TLS.

Detener normalmente el agente con Ctrl+C y arrancarlo otra vez en la misma cuenta
y directorio. La huella debe ser la misma que la ultima activada; status debe conservar
historial. Un segundo receptor simultaneo sobre ese directorio debe fallar con
codigo de salida distinto de cero, aunque se le configure otro puerto.

Para revocar, en el anfitrion:

```powershell
$device = Read-Host 'ID del dispositivo mostrado al vincular'
.\Neobash.PrintBridge.Agent.exe revoke $device
```

Un envio o consulta posterior desde ese cliente debe rechazarse. Para volver a
vincularlo, emitir otro codigo y ejecutar pair nuevamente. La prueba automatica
`dotnet run --project tests/LanProcessProbe -c Release` y la regresion unitaria
cubren recuperacion entre procesos y revocacion con cuerpo pausado, sin impresoras.

El comando administrativo `renew` reemplaza explicitamente el certificado local;
requiere reiniciar la escucha y volver a emparejar. Usarlo con el agente detenido:

```powershell
.\Neobash.PrintBridge.Agent.exe renew
.\Neobash.PrintBridge.Agent.exe
```

## Evidencia a devolver

Registrar versiones de Windows/.NET de ambos equipos, IP/interfaz/perfil privado,
puerto, huellas antes y despues del reinicio, destino, source, jobId, fases y codigos
de salida. No copiar codigos de emparejamiento, credenciales ni PFX al informe.
Anotar por separado si hubo hoja observada y los defectos fisicos que aparezcan.
