# Instalación y desinstalación de la POC 01

La compilación no instala el paquete ni modifica la confianza del equipo. Los cambios de sistema quedan en un comando aparte, con aprobación explícita y una consola elevada.

El MSIX que debe usarse es el de la versión `1.0.1.0`:

`src\Windows\VirtualPrinter\AppPackages\Neobash.PrintBridge.VirtualPrinter_1.0.1.0_x64_Test\Neobash.PrintBridge.VirtualPrinter_1.0.1.0_x64.msix`

`1.0.0.0` puede seguir instalada. Este procedimiento no la desinstala y no registra la versión nueva hasta que se autorice el comando elevado.

El firmante es `CN=Neobash Print Bridge POC`. En Windows 11, un MSIX firmado puede registrarse por sideloading cuando ese certificado público está en `Cert:\LocalMachine\TrustedPeople`. El modo de desarrollador no forma parte del intento inicial y este procedimiento no lo activa.

## Qué queda fuera del intento inicial

- No se activa el modo de desarrollador ni se escribe `AllowDevelopmentWithoutDevLicense`.
- No se cambia `AllowAllTrustedApps` ni se ejecuta `bcdedit /set TESTSIGNING`.
- No se usa `local-certs\npb-poc.pfx` para establecer confianza. El PFX solo sirve para firmar y para comprobar que es el mismo certificado.
- No se importa el certificado en `Cert:\CurrentUser\TrustedPeople`, en `Root` ni en `CA`.
- No se llama a `Remove-Printer` ni se eliminan otros paquetes.

Si el registro falla, el script muestra el código y se detiene. Un cambio adicional solo se plantea cuando el error lo justifica, y queda para una autorización distinta.

## Comprobación, sin cambios de sistema

Desde la raíz del repositorio:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\poc\install.ps1
```

Ese comando:

1. Elige el MSIX de mayor versión de `Neobash.PrintBridge.VirtualPrinter`.
2. Rechaza una firma dañada, ausente o de otro firmante.
3. Lee `AppxManifest.xml` y `resources.pri`. Exige una sola cola cuyo recurso resuelva a `NPB - Prueba Virtual`, formato `application/oxps`, sin `OutputFileTypes`, `windows.printSupportExtension` y la capacidad `runFullTrust`.
4. Comprueba que el PFX local, si existe, tiene el mismo thumbprint que la firma. No lo importa.
5. Exporta el certificado público a `local-certs\npb-poc.cer` desde el firmante del MSIX y registra el thumbprint en `local-certs\npb-poc.thumbprint.txt`.

Esos dos archivos están fuera de Git, junto con el PFX. El comando termina con código 2 y no toca almacenes, impresoras ni paquetes.

Authenticode puede informar que la cadena termina en una raíz no confiable. Eso es esperable antes de confiar el certificado público y no equivale a una firma inválida.

## Instalación autorizada

Hace falta la misma cuenta de Windows que va a imprimir, en una consola abierta como administrador. La confianza queda en el equipo; el paquete se registra para esa cuenta. Otra cuenta de administración no debe ejecutar este paso.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\poc\install.ps1 -ConfirmSystemChanges
```

Sin el modificador, o sin elevación, no hay cambios. Con ambas condiciones el script hace solo esto:

1. Repite la comprobación de firma y contenido.
2. Importa `local-certs\npb-poc.cer` en `Cert:\LocalMachine\TrustedPeople` si ese thumbprint todavía no está.
3. Si ya hay otro certificado con el mismo nombre y otro thumbprint, se detiene sin borrarlo ni sustituirlo.
4. Ejecuta `Add-AppxPackage` sobre el MSIX comprobado.
5. Comprueba que la única cola nueva es `NPB - Prueba Virtual`, que las demás conservan controlador y puerto, y que no apareció ni desapareció otro paquete de otra identidad.
6. Si Windows cambia la impresora predeterminada, intenta devolver la anterior.

El certificado que debe quedar confiado es el de esta firma:

`70C7483FE4B6ECAC9FAD7CB72002C9AD18FFBA37`

Caduca el 6 de noviembre de 2027. Ese thumbprint es el único que la limpieza posterior puede retirar.

## Si Windows rechaza el registro

| Código | Qué significa | Qué no hace el script |
| --- | --- | --- |
| `0x80073CFF` | La directiva bloquea el sideloading | No activa el modo de desarrollador ni cambia `AllowAllTrustedApps` |
| `0x800B0109` | La cadena del certificado no es de confianza | No copia el certificado a `Trusted Root` |
| Texto que pida modo de desarrollador | Windows lo menciona de forma explícita | No lo activa; haría falta otra autorización |

El certificado público puede haber quedado importado aunque el paquete no. En ese caso el propio script indica el comando de limpieza y el thumbprint.

## Dónde quedan los trabajos

La tarea escribe en la carpeta local del paquete:

```powershell
$family = (Get-AppxPackage -Name Neobash.PrintBridge.VirtualPrinter).PackageFamilyName
Get-ChildItem "$env:LOCALAPPDATA\Packages\$family\LocalState\Neobash\PrintBridge" -Recurse
```

Cada trabajo está en `Jobs\{id}\` con `document.oxps`, `metadata.json` y, si falló, `error.txt`. El registro de la tarea está en `Logs\task.log`.

Esos archivos pueden contener documentos confidenciales. Permanecen en el perfil de la cuenta y no deben copiarse al repositorio.

## Actualizar 1.0.0.0 a 1.0.1.0

`1.0.0.0` queda en estado Ok y no crea `NPB - Prueba Virtual` porque el nombre de la cola está escrito como texto literal y el manifiesto no declara `windows.printSupportExtension`. Windows acepta ese paquete y el administrador de impresión no crea la cola. `1.0.1.0` conserva el nombre `Neobash.PrintBridge.VirtualPrinter`, el Publisher `CN=Neobash Print Bridge POC` y el mismo certificado. El nombre visible pasa a `ms-resource:PrintSupportVirtualPrinterDisplayName` y la extensión queda registrada.

No desinstalar `1.0.0.0` antes de actualizar. Quitar el paquete puede dejar un puerto del monitor virtual y, al volver a instalar, Windows reutiliza ese registro viejo. La actualización sustituye la misma identidad.

La compilación y la firma no instalan nada. Cuando se autorice, en una consola elevada de la misma cuenta que imprime:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\poc\install.ps1 -ConfirmSystemChanges
```

El script elige el MSIX de mayor versión. Si encuentra `1.0.0.0`, ejecuta `Add-AppxPackage` sobre `1.0.1.0` y no llama a `Remove-AppxPackage`, `Remove-Printer` ni edita el registro. Después comprueba que la única cola nueva es `NPB - Prueba Virtual` y que las demás conservan controlador y puerto.

La retirada del nombre completo de `1.0.0.0` es normal al sustituirlo por `1.0.1.0`. La validación permite únicamente versiones anteriores con el mismo nombre, arquitectura x64, recurso vacío y PublisherId `81d0rh61c409w`, y exige que el paquete de destino esté presente. Rechaza retiradas, incorporaciones y cambios de versión de paquetes ajenos, así como una sustitución por otra identidad o una versión inferior.

La comprobación posterior exige un único paquete con el nombre y Publisher previstos, la versión exacta del MSIX seleccionado (`1.0.1.0` en esta corrección) y estado `Ok`. Consulta por separado las colas reales de Windows incluso si falla la validación MSIX. Los errores se presentan como fallos MSIX o de aprovisionamiento/integridad de impresoras; que el paquete esté en estado `Ok` no acredita que exista la cola. Los errores de consulta tampoco se interpretan como ausencia.

Diagnóstico de solo lectura realizado el 7 de noviembre de 2026: `Neobash.PrintBridge.VirtualPrinter_1.0.1.0_x64__81d0rh61c409w` registrado con estado `Ok`, Spooler activo y una cola `NPB - Prueba Virtual` con `Microsoft Virtual Print Class Driver`. No se ejecutó una instalación ni una desinstalación para comprobarlo. Sin una instantánea previa al intento original, esta lectura no demuestra que los paquetes o impresoras ajenos permanecieran sin cambios durante aquel intento.

Pruebas automatizadas con instantáneas sintéticas, sin modificar Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Poc\PackageUpgrade.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Poc\InstallFailureGuidance.Tests.ps1
```

Si el paquete nuevo queda registrado y la cola no aparece, el script se detiene y ejecuta un diagnóstico de solo lectura:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\poc\diagnose-queue.ps1
```

Ese diagnóstico consulta el servicio de cola, el paquete de esta cuenta, la impresora `NPB - Prueba Virtual` y los nombres de puerto bajo `HKLM\SYSTEM\CurrentControlSet\Control\Print\Monitors\Virtual Port Monitor\Ports` que coinciden con esta POC. No borra claves ni impresoras. Si aparece un puerto antiguo y la cola sigue ausente, Windows conservó el registro de la instalación anterior. La limpieza de esa clave no forma parte de este paso: primero se revisa el diagnóstico y solo después, con otra autorización, se decide si hace falta reiniciar y retirar ese puerto. No se hace esa limpieza para probar `1.0.1.0`.

Si el certificado firmante cambiara, primero hay que retirar el thumbprint anterior con la limpieza de abajo. El script no sustituye un certificado distinto.

## Desinstalar solo esta POC

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\poc\uninstall.ps1
```

Ese comando llama a `Remove-AppxPackage` solo si el nombre es exactamente `Neobash.PrintBridge.VirtualPrinter`. No elimina certificados, no llama a `Remove-Printer` y no cambia otras colas. Windows retira `NPB - Prueba Virtual` al quitar el paquete. Si la predeterminada era otra impresora y Windows la cambia, el script intenta restaurarla.

Para retirar también el certificado público, en una consola elevada:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\poc\uninstall.ps1 -RemoveTrustedCertificate
```

La limpieza solo borra `Cert:\LocalMachine\TrustedPeople\70C7483FE4B6ECAC9FAD7CB72002C9AD18FFBA37`, y únicamente si ese thumbprint coincide con `local-certs\npb-poc.cer` y con `local-certs\npb-poc.thumbprint.txt`. No recorre el almacén por el nombre `CN=Neobash Print Bridge POC` y no toca `Root`, `CA`, `My` ni los almacenes de la cuenta.

Conviene volver a listar las impresoras y confirmar que las colas físicas y del sistema siguen presentes.

## Riesgos de la primera instalación

- La confianza en `LocalMachine\TrustedPeople` vale para todo el equipo mientras ese certificado siga ahí. Quien posea el PFX podría firmar otro paquete que este equipo acepte. El PFX permanece en `local-certs\`, fuera de Git.
- El certificado no entra en `Trusted Root`. No debe usarse como raíz general. Authenticode puede seguir mostrando una raíz no confiable; el sideloading de MSIX usa `TrustedPeople`.
- El paquete se registra para la cuenta que ejecuta el comando elevado. Conviene que sea la misma cuenta que imprime.
- `Add-AppxPackage` puede hacer que Windows cambie la impresora predeterminada. El script intenta restaurarla y avisa si no lo consigue.
- La cola nueva es virtual. El script considera fallo que aparezca o desaparezca cualquier otra impresora, o que cambie su controlador o su puerto.
- La aplicación empaquetada declara `runFullTrust`, igual que el ejemplo de Print Support App. No declara red ni acceso amplio al sistema de archivos.
- Los trabajos capturados después pueden contener documentos de la cuenta. No forman parte del repositorio.
- Si el registro falla a mitad de camino, el certificado puede quedar instalado y el paquete no. La limpieza es el comando elevado con `-RemoveTrustedCertificate`.
- La captura desde Bloc de notas ya quedó demostrada. Esta página no repite esa prueba. Siguen abiertos la desinstalación del paquete y el evento 307.
