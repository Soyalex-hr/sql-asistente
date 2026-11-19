-- ============================================================
-- Fragmentos (snippets) incluidos en SQL Asistente.
-- Escribe el atajo en una consulta y presiona Tab.
--
-- Formato de cada fragmento:
--   -- #fragmento atajo | descripción
--   ...texto...
--   -- #fin
-- ============================================================

-- #fragmento ssf | SELECT * FROM
SELECT * FROM 
-- #fin

-- #fragmento st100 | SELECT TOP 100 * FROM
SELECT TOP (100) * FROM 
-- #fin

-- #fragmento scf | SELECT COUNT(*) FROM
SELECT COUNT(*) FROM 
-- #fin

-- #fragmento sd | SELECT DISTINCT
SELECT DISTINCT 
-- #fin

-- #fragmento ij | INNER JOIN
INNER JOIN 
-- #fin

-- #fragmento lj | LEFT JOIN
LEFT JOIN 
-- #fin

-- #fragmento rj | RIGHT JOIN
RIGHT JOIN 
-- #fin

-- #fragmento ca | CROSS APPLY
CROSS APPLY 
-- #fin

-- #fragmento oa | OUTER APPLY
OUTER APPLY 
-- #fin

-- #fragmento gb | GROUP BY
GROUP BY 
-- #fin

-- #fragmento ob | ORDER BY
ORDER BY 
-- #fin

-- #fragmento w | WHERE
WHERE 
-- #fin

-- #fragmento ii | INSERT INTO ... VALUES
INSERT INTO dbo.Tabla
(
    Columna1,
    Columna2
)
VALUES
(
    @Valor1,
    @Valor2
);
-- #fin

-- #fragmento is | INSERT INTO ... SELECT
INSERT INTO dbo.Tabla
(
    Columna1,
    Columna2
)
SELECT
    Columna1,
    Columna2
FROM dbo.Origen
WHERE 1 = 1;
-- #fin

-- #fragmento upd | UPDATE seguro con WHERE
UPDATE t
SET t.Columna = @Valor
FROM dbo.Tabla AS t
WHERE t.Id = @Id;
-- #fin

-- #fragmento del | DELETE seguro con WHERE
DELETE t
FROM dbo.Tabla AS t
WHERE t.Id = @Id;
-- #fin

-- #fragmento merge | MERGE (insertar o actualizar)
MERGE dbo.Destino AS d
USING dbo.Origen AS o
    ON d.Id = o.Id
WHEN MATCHED THEN
    UPDATE SET d.Columna = o.Columna
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Columna)
    VALUES (o.Id, o.Columna)
WHEN NOT MATCHED BY SOURCE THEN
    DELETE
OUTPUT $action, inserted.*, deleted.*;
-- #fin

-- #fragmento cte | Expresión de tabla común (WITH)
WITH cte AS
(
    SELECT
        Columna1,
        Columna2
    FROM dbo.Tabla
)
SELECT *
FROM cte;
-- #fin

-- #fragmento tmp | Tabla temporal
DROP TABLE IF EXISTS #Temporal;

CREATE TABLE #Temporal
(
    Id INT NOT NULL PRIMARY KEY,
    Nombre NVARCHAR(100) NULL
);
-- #fin

-- #fragmento tv | Variable de tabla
DECLARE @Tabla TABLE
(
    Id INT NOT NULL PRIMARY KEY,
    Nombre NVARCHAR(100) NULL
);
-- #fin

-- #fragmento dv | DECLARE variable
DECLARE @Variable INT = 0;
-- #fin

-- #fragmento trycatch | BEGIN TRY ... BEGIN CATCH
BEGIN TRY
    
END TRY
BEGIN CATCH
    THROW;
END CATCH;
-- #fin

-- #fragmento tran | Transacción segura (XACT_ABORT + TRY/CATCH + ROLLBACK)
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- tu código aquí

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
-- #fin

-- #fragmento sp | CREATE OR ALTER PROCEDURE con buenas prácticas
CREATE OR ALTER PROCEDURE dbo.usp_NombreProcedimiento
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @Id AS Id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
GO
-- #fin

-- #fragmento fn | Función escalar
CREATE OR ALTER FUNCTION dbo.fn_Nombre
(
    @Valor INT
)
RETURNS INT
AS
BEGIN
    RETURN @Valor;
END;
GO
-- #fin

-- #fragmento tvf | Función de tabla en línea
CREATE OR ALTER FUNCTION dbo.tvf_Nombre
(
    @Id INT
)
RETURNS TABLE
AS
RETURN
(
    SELECT *
    FROM dbo.Tabla
    WHERE Id = @Id
);
GO
-- #fin

-- #fragmento vw | CREATE OR ALTER VIEW
CREATE OR ALTER VIEW dbo.vw_Nombre
AS
SELECT
    Columna1,
    Columna2
FROM dbo.Tabla;
GO
-- #fin

-- #fragmento ct | CREATE TABLE con PK
CREATE TABLE dbo.NuevaTabla
(
    Id INT IDENTITY(1, 1) NOT NULL,
    Nombre NVARCHAR(100) NOT NULL,
    Activo BIT NOT NULL CONSTRAINT DF_NuevaTabla_Activo DEFAULT (1),
    FechaCreacion DATETIME2(0) NOT NULL CONSTRAINT DF_NuevaTabla_FechaCreacion DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_NuevaTabla PRIMARY KEY CLUSTERED (Id)
);
-- #fin

-- #fragmento fk | Agregar llave foránea
ALTER TABLE dbo.TablaHija
ADD CONSTRAINT FK_TablaHija_TablaPadre
FOREIGN KEY (TablaPadreId) REFERENCES dbo.TablaPadre (Id);
-- #fin

-- #fragmento idx | Crear índice
CREATE NONCLUSTERED INDEX IX_Tabla_Columna
ON dbo.Tabla (Columna)
INCLUDE (OtraColumna);
-- #fin

-- #fragmento ac | Agregar columna
ALTER TABLE dbo.Tabla
ADD NuevaColumna NVARCHAR(100) NULL;
-- #fin

-- #fragmento ife | IF EXISTS
IF EXISTS (SELECT 1 FROM dbo.Tabla WHERE Id = @Id)
BEGIN
    
END;
-- #fin

-- #fragmento ifne | IF NOT EXISTS
IF NOT EXISTS (SELECT 1 FROM dbo.Tabla WHERE Id = @Id)
BEGIN
    
END;
-- #fin

-- #fragmento be | BEGIN ... END
BEGIN
    
END;
-- #fin

-- #fragmento whl | WHILE
DECLARE @i INT = 1;

WHILE @i <= 10
BEGIN
    
    SET @i += 1;
END;
-- #fin

-- #fragmento case | CASE WHEN
CASE
    WHEN condicion THEN valor
    ELSE otroValor
END
-- #fin

-- #fragmento pag | Paginación OFFSET / FETCH
ORDER BY Id
OFFSET (@Pagina - 1) * @TamanoPagina ROWS
FETCH NEXT @TamanoPagina ROWS ONLY;
-- #fin

-- #fragmento rn | ROW_NUMBER() OVER
ROW_NUMBER() OVER (PARTITION BY Columna ORDER BY OtraColumna DESC) AS Fila
-- #fin

-- #fragmento dup | Buscar registros duplicados
SELECT
    Columna,
    COUNT(*) AS Cantidad
FROM dbo.Tabla
GROUP BY Columna
HAVING COUNT(*) > 1
ORDER BY Cantidad DESC;
-- #fin

-- #fragmento ultimo | Último registro por grupo
WITH ordenados AS
(
    SELECT
        *,
        ROW_NUMBER() OVER (PARTITION BY ClienteId ORDER BY Fecha DESC) AS Fila
    FROM dbo.Tabla
)
SELECT *
FROM ordenados
WHERE Fila = 1;
-- #fin

-- #fragmento buscarcol | Buscar una columna en todas las tablas
SELECT
    s.name AS Esquema,
    t.name AS Tabla,
    c.name AS Columna
FROM sys.columns AS c
INNER JOIN sys.tables AS t ON t.object_id = c.object_id
INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE c.name LIKE '%Texto%'
ORDER BY Esquema, Tabla;
-- #fin

-- #fragmento buscartexto | Buscar texto en procedimientos y vistas
SELECT
    OBJECT_SCHEMA_NAME(m.object_id) AS Esquema,
    OBJECT_NAME(m.object_id) AS Objeto,
    o.type_desc AS Tipo
FROM sys.sql_modules AS m
INNER JOIN sys.objects AS o ON o.object_id = m.object_id
WHERE m.definition LIKE '%Texto%'
ORDER BY Esquema, Objeto;
-- #fin

-- #fragmento tamanos | Tamaño y filas de cada tabla
SELECT
    s.name AS Esquema,
    t.name AS Tabla,
    SUM(p.rows) AS Filas,
    CAST(SUM(a.total_pages) * 8 / 1024.0 AS DECIMAL(18, 2)) AS TamanoMB
FROM sys.tables AS t
INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
INNER JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
INNER JOIN sys.allocation_units AS a ON a.container_id = p.partition_id
GROUP BY s.name, t.name
ORDER BY TamanoMB DESC;
-- #fin

-- #fragmento bloqueos | Ver sesiones bloqueadas
SELECT
    r.session_id,
    r.blocking_session_id,
    r.wait_type,
    r.wait_time,
    r.status,
    DB_NAME(r.database_id) AS BaseDatos,
    t.text AS Consulta
FROM sys.dm_exec_requests AS r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
WHERE r.blocking_session_id <> 0;
-- #fin
