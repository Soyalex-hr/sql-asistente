# SQL Asistente para SSMS 21 / 22

Complemento propio para SQL Server Management Studio, al estilo de Redgate SQL Prompt:

| Función | Cómo se usa |
|---|---|
| **Autocompletado de tablas, vistas y columnas** | Escribe normalmente. También con `Ctrl+Espacio`. |
| **JOIN completo sugerido** | Escribe `INNER JOIN ` y elige la tabla: se inserta `dbo.Pedidos AS ped ON ped.ClienteId = cli.ClienteId` usando las llaves foráneas (o columnas con el mismo nombre si no hay FK). |
| **Condiciones del ON** | Después de `ON ` sugiere las condiciones entre la última tabla y las anteriores. |
| **Columnas por alias** | `cli.` muestra las columnas de la tabla con ese alias, en su orden real. |
| **Alias automáticos** | `Clientes` → `cli`, `DetallePedidos` → `dp`, sin repetir. |
| **Fragmentos (snippets)** | Escribe el atajo y presiona `Tab`: `ssf`, `st100`, `cte`, `tran`, `sp`, `trycatch`, `merge`, `ct`, `idx`, `pag`, `dup`, `ultimo`, `buscarcol`, `buscartexto`, `tamanos`, `bloqueos`... |
| **Formatear SQL** | `Ctrl+K, Ctrl+Y` (todo el documento o solo la selección). Mayúsculas, sangría, una columna por línea, `;` al final. Conserva comentarios y `GO`. |
| **Solo MAYÚSCULAS** | Menú *SQL Asistente*: pone las palabras clave en mayúsculas sin mover nada. |
| **Análisis de código** | `Ctrl+K, Ctrl+A`. Resultados en *Ver > Lista de errores*; doble clic lleva a la línea. |
| **Protección al ejecutar (F5)** | Si hay `UPDATE`/`DELETE` sin `WHERE`, `TRUNCATE` o `DROP`, pregunta antes de ejecutar (el botón por defecto es **No**). |
| **Expandir `SELECT *`** | Pon el cursor en la consulta y `Ctrl+K, Ctrl+E`. También funciona con `alias.*`. |
| **Agregar esquema** | `Clientes` → `dbo.Clientes` en todo el script. |
| **Historial de consultas** | Cada F5 se guarda en `%LOCALAPPDATA%\SqlAsistente\Historial\AAAA-MM-DD.sql`. |

### Funciones de la versión 2

| Función | Cómo se usa |
|---|---|
| **Ejecutar solo la sentencia actual** | `Ctrl+Shift+E`: ejecuta la consulta donde está el cursor, sin seleccionarla. |
| **Ir a la definición** | `F12` sobre una tabla, vista, procedimiento o función: abre su script en una consulta nueva. |
| **Información al pasar el mouse** | Columnas, tipos, llaves, relaciones y parámetros al pasar el mouse sobre el nombre. |
| **Parámetros automáticos en EXEC** | `EXEC ` + elegir el procedimiento: se insertan todos sus parámetros con el tipo como comentario. |
| **Avisos mientras escribes** | Subrayado rojo/verde en vivo (UPDATE sin WHERE, `= NULL`, `SELECT *`...). Pasa el mouse para ver el detalle. |
| **¿Quién usa este objeto?** | Con el cursor sobre una tabla o `alias.Columna`: lista procedimientos, vistas y llaves que la usan. |
| **Buscar objetos** | Busca por nombre, nombre de columna o texto dentro del código, en la base actual o en todas. |
| **Renombrar** | `F2` sobre una variable (`@x`, en su lote) o un alias de tabla (en su sentencia). |
| **Generar clase C#** | Cursor sobre una tabla (o dentro de un SELECT, o con la consulta seleccionada): genera la clase. |
| **Generar llamada C#** | Cursor sobre un procedimiento: genera el método con Dapper y con ADO.NET, con su clase de resultado. |
| **Copiar resultados como INSERT / JSON** | Ejecuta el SELECT donde está el cursor (solo lectura, máximo 1000 filas por defecto) y genera los INSERT o el JSON. |
| **Recuperar pestañas** | Cada 30 s se guarda una copia de todas las consultas abiertas. *SQL Asistente > Recuperar pestañas...* para buscarlas por nombre o contenido. |
| **Mis fragmentos** | *SQL Asistente > Editar mis fragmentos*: agrega tus propios atajos (`-- #fragmento atajo | descripción` ... `-- #fin`). |

Las opciones están en **SQL Asistente > Opciones...** (o *Herramientas > Opciones > SQL Asistente*).

---

## 1. Requisitos

- **Visual Studio 2022** (Community sirve) con la carga de trabajo **"Desarrollo de extensiones de Visual Studio"**.
  Si no la tienes: abre *Visual Studio Installer* → *Modificar* → marca esa carga de trabajo → *Modificar*.
- SSMS 21 o 22 instalado (en tu PC están los dos).

## 2. Compilar

1. Abre `SqlAsistente.sln` con Visual Studio 2022.
2. Arriba elige **Release**.
3. Menú **Compilar > Compilar solución** (`Ctrl+Shift+B`). La primera vez descarga los paquetes NuGet.
4. Se genera `src\SqlAsistente\bin\Release\SqlAsistente.vsix`.

Si aparece algún error de compilación, cópialo completo y pásamelo para corregirlo.

## 3. Instalar en SSMS

### Opción A (recomendada): instalador

Al compilar la solución en **Release** se genera:

`src\SqlAsistente.Instalador\bin\Release\net472\InstalarSqlAsistente.exe`

Es un solo archivo (trae el complemento adentro). Para instalar en cualquier equipo:

1. Cierra SSMS.
2. Doble clic en `InstalarSqlAsistente.exe` (pide permiso de administrador).
3. Marca SSMS 21 y/o 22 → **Instalar**.

Queda registrado en *Configuración de Windows > Aplicaciones* como **"SQL Asistente para SSMS"**, desde donde también se desinstala.

Modo silencioso (para instalar en varios equipos):

```
InstalarSqlAsistente.exe /instalar /silencioso
InstalarSqlAsistente.exe /desinstalar /silencioso
```

### Opción B: script de PowerShell

1. **Cierra SSMS.**
2. Abre **PowerShell como administrador** y ejecuta:

   ```powershell
   cd E:\ALX\SqlAsistente
   powershell -ExecutionPolicy Bypass -File .\instalar.ps1
   ```

   Lo instala en SSMS 22 y en SSMS 21 (los que encuentre). Para uno solo: `.\instalar.ps1 -Version 22`.
3. Abre SSMS: aparece el menú **SQL Asistente** en la barra de menús.

Para quitarlo: `.\desinstalar.ps1` (también como administrador y con SSMS cerrado).

> Microsoft no da soporte oficial a extensiones de terceros en SSMS 21/22, pero no las bloquea
> (SQL Prompt, dbForge y otras funcionan igual). Si una actualización de SSMS lo rompe, se recompila.

## 4. Recomendado: evitar dos listas de sugerencias a la vez

Si ves la lista de IntelliSense de SSMS **y** la de SQL Asistente al mismo tiempo, desactiva la automática de SSMS:

*Herramientas → Opciones → Editor de texto → Transact-SQL → IntelliSense* → desmarca **"Lista de miembros automática"**
(deja IntelliSense habilitado para el subrayado de errores).

## 5. Si no salen tablas ni columnas

Usa **SQL Asistente > Ver estado de la conexión**. Te dice a qué servidor/base está conectada la ventana,
cuántas tablas y llaves foráneas leyó, o el error.

- La lectura usa la misma conexión de la ventana de consulta (Windows o usuario SQL).
- Con **Microsoft Entra / MFA** escribe una cadena en *Opciones > 4. Conexión*, por ejemplo:
  `Server=MiServidor;Database=MiBD;Integrated Security=True;TrustServerCertificate=True`
- Si creaste tablas nuevas: **SQL Asistente > Actualizar tablas y columnas**.
- Si la base no tiene llaves foráneas, los JOIN se sugieren por nombre de columna (`ClienteId = ClienteId`).
  Es mucho mejor crear las FK.

## 6. Reglas del análisis de código

| Código | Tipo | Qué detecta |
|---|---|---|
| SA000 | Error | Error de sintaxis |
| SA001 | Error | `UPDATE` sin `WHERE` |
| SA002 | Error | `DELETE` sin `WHERE` |
| SA003 | Advertencia | `TRUNCATE TABLE` |
| SA004 | Advertencia | `DROP TABLE` / `DROP DATABASE` |
| SA005 | Error | `= NULL` o `<> NULL` (usar `IS NULL`) |
| SA006 | Advertencia | `SELECT *` |
| SA007 | Advertencia | JOIN implícito con comas en el `FROM` |
| SA008 | Advertencia | Función sobre una columna en el `WHERE` (no SARGable) |
| SA009 | Info | `LIKE '%...'` |
| SA010 | Advertencia | `TOP` sin `ORDER BY` |
| SA011 | Info | `NOLOCK` |
| SA012 | Advertencia | Procedimiento sin `SET NOCOUNT ON` |
| SA013 | Info | Procedimiento que modifica datos sin `TRY...CATCH` |
| SA014 | Advertencia | Variable declarada y no usada |
| SA015 | Info | Cursores |
| SA016 | Advertencia | `INSERT` sin lista de columnas |
| SA017 | Advertencia | `ORDER BY 1, 2` |
| SA018 | Info | Tabla sin esquema (`dbo.`) |
| SA019 | Advertencia | Tipos obsoletos `TEXT` / `NTEXT` / `IMAGE` |
| SA020 | Advertencia | `NOT IN` con subconsulta (problema con NULL) |
| SA021 | Advertencia | `@@IDENTITY` (usar `SCOPE_IDENTITY()`) |
| SA022 | Info | Procedimiento con prefijo `sp_` |
| SA023 | Info | `BEGIN TRAN` sin `TRY...CATCH` ni `XACT_ABORT` |

Para ignorar reglas: *Opciones > 3. Análisis y seguridad > Reglas desactivadas*, por ejemplo `SA011,SA018`.

## 7. Atajos de teclado

| Atajo | Comando |
|---|---|
| `Ctrl+K, Ctrl+Y` | Formatear SQL |
| `Ctrl+K, Ctrl+A` | Analizar código |
| `Ctrl+K, Ctrl+E` | Expandir `SELECT *` |
| `Ctrl+Shift+E` | Ejecutar solo la sentencia actual |
| `F12` | Ir a la definición |
| `F2` | Renombrar alias o variable |

Para cambiarlos: *Herramientas → Opciones → Entorno → Teclado*, busca `SQLAsistente`.

## 8. Depurar (para modificar el complemento)

1. Abre Visual Studio **como administrador**.
2. En las propiedades del proyecto ya está configurado que **F5** abra `SSMS.exe` de la versión 22.
   (Para SSMS 21 cambia `RutaSsms` en `SqlAsistente.csproj`.)
3. Compila con despliegue directo: en *Herramientas > Línea de comandos > Símbolo del sistema para desarrolladores*:
   `msbuild SqlAsistente.sln /p:Configuration=Debug /p:DesplegarEnSsms=true`
4. Presiona F5 y pon puntos de interrupción.

## Estructura

```
src/SqlAsistente/
  SqlAsistentePackage.cs        Paquete principal (carga en segundo plano)
  SqlAsistente.vsct             Menú "SQL Asistente" y atajos
  Comandos/                     Comandos del menú
  Completado/                   Autocompletado, JOIN, snippets
  Esquema/                      Conexión de SSMS y caché de tablas/columnas/FK
  Formato/                      Formateador (ScriptDom)
  Analisis/                     Reglas de análisis y Lista de errores
  Refactor/                     Expandir *, agregar esquema
  Proteccion/                   Confirmación antes de F5
  Historial/                    Historial de consultas
  Opciones/                     Página de opciones
```
