using System;
using System.Collections.Generic;

namespace SqlAsistente
{
    /// <summary>Listas de palabras clave, funciones y tipos de T-SQL.</summary>
    internal static class PalabrasSql
    {
        public static readonly string[] PalabrasClave =
        {
            "SELECT", "FROM", "WHERE", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL OUTER JOIN", "CROSS JOIN",
            "CROSS APPLY", "OUTER APPLY", "JOIN", "ON", "AND", "OR", "NOT", "IN", "EXISTS", "NOT EXISTS", "BETWEEN",
            "LIKE", "IS NULL", "IS NOT NULL", "GROUP BY", "ORDER BY", "HAVING", "DISTINCT", "TOP", "AS", "ASC", "DESC",
            "UNION", "UNION ALL", "EXCEPT", "INTERSECT", "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM",
            "MERGE", "USING", "WHEN MATCHED THEN", "WHEN NOT MATCHED THEN", "OUTPUT", "CASE", "WHEN", "THEN", "ELSE",
            "END", "BEGIN", "BEGIN TRAN", "COMMIT", "ROLLBACK", "DECLARE", "IF", "WHILE", "RETURN", "EXEC", "WITH",
            "OVER", "PARTITION BY", "OFFSET", "FETCH NEXT", "ROWS ONLY", "NOLOCK", "CREATE", "ALTER", "DROP",
            "TABLE", "VIEW", "PROCEDURE", "FUNCTION", "INDEX", "TRIGGER", "PRIMARY KEY", "FOREIGN KEY", "REFERENCES",
            "DEFAULT", "NULL", "NOT NULL", "IDENTITY", "CONSTRAINT", "TRUNCATE TABLE", "USE", "GO", "PRINT",
            "RAISERROR", "THROW", "TRY", "CATCH", "BEGIN TRY", "END TRY", "BEGIN CATCH", "END CATCH", "INTO",
            "SET NOCOUNT ON", "SET XACT_ABORT ON", "PIVOT", "UNPIVOT", "COLLATE", "ALL", "ANY", "SOME"
        };

        public static readonly string[] Funciones =
        {
            "GETDATE()", "SYSDATETIME()", "GETUTCDATE()", "SYSUTCDATETIME()", "DATEADD(", "DATEDIFF(", "DATEPART(",
            "DATENAME(", "EOMONTH(", "YEAR(", "MONTH(", "DAY(", "FORMAT(", "CONVERT(", "CAST(", "TRY_CAST(",
            "TRY_CONVERT(", "TRY_PARSE(", "ISNULL(", "COALESCE(", "NULLIF(", "IIF(", "CHOOSE(", "COUNT(*)", "COUNT(",
            "COUNT_BIG(", "SUM(", "AVG(", "MIN(", "MAX(", "STRING_AGG(", "STRING_SPLIT(", "LEN(", "DATALENGTH(",
            "LEFT(", "RIGHT(", "SUBSTRING(", "CHARINDEX(", "PATINDEX(", "REPLACE(", "STUFF(", "LTRIM(", "RTRIM(",
            "TRIM(", "UPPER(", "LOWER(", "CONCAT(", "CONCAT_WS(", "REPLICATE(", "REVERSE(", "ROUND(", "ABS(",
            "CEILING(", "FLOOR(", "POWER(", "SQRT(", "ROW_NUMBER() OVER (", "RANK() OVER (", "DENSE_RANK() OVER (",
            "NTILE(", "LAG(", "LEAD(", "FIRST_VALUE(", "LAST_VALUE(", "NEWID()", "SCOPE_IDENTITY()", "OBJECT_ID(",
            "DB_NAME()", "SUSER_SNAME()", "@@ROWCOUNT", "@@TRANCOUNT", "ERROR_MESSAGE()", "ERROR_NUMBER()",
            "ERROR_LINE()", "ERROR_PROCEDURE()", "ERROR_SEVERITY()", "ERROR_STATE()", "XACT_STATE()", "JSON_VALUE(",
            "JSON_QUERY(", "OPENJSON(", "ISJSON(", "EXISTS ("
        };

        /// <summary>
        /// Identificadores que SIEMPRE se ponen en mayúsculas (sugerencias de tabla, opciones SET y tipos de datos).
        /// No incluye nombres que suelen usarse como columnas (Date, Time, Text, Year...).
        /// </summary>
        public static readonly HashSet<string> SiempreEnMayusculas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NOLOCK", "READPAST", "UPDLOCK", "ROWLOCK", "HOLDLOCK", "TABLOCK", "TABLOCKX", "READUNCOMMITTED",
            "NOCOUNT", "XACT_ABORT", "ANSI_NULLS", "QUOTED_IDENTIFIER", "MATCHED", "INT", "BIGINT", "SMALLINT",
            "TINYINT", "BIT", "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY", "FLOAT", "DATETIME", "DATETIME2",
            "SMALLDATETIME", "DATETIMEOFFSET", "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "NTEXT", "VARBINARY",
            "UNIQUEIDENTIFIER", "SYSNAME"
        };

        /// <summary>Funciones: se ponen en mayúsculas solo cuando van seguidas de "(".</summary>
        public static readonly HashSet<string> IdentificadoresEnMayusculas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GETDATE", "SYSDATETIME", "GETUTCDATE", "SYSUTCDATETIME", "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART",
            "DATENAME", "EOMONTH", "YEAR", "MONTH", "DAY", "FORMAT", "CAST", "TRY_CAST", "TRY_CONVERT", "TRY_PARSE",
            "ISNULL", "COALESCE", "NULLIF", "IIF", "CHOOSE", "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX",
            "STRING_AGG", "STRING_SPLIT", "LEN", "DATALENGTH", "SUBSTRING", "CHARINDEX", "PATINDEX", "REPLACE", "STUFF",
            "LTRIM", "RTRIM", "TRIM", "UPPER", "LOWER", "CONCAT", "CONCAT_WS", "REPLICATE", "REVERSE", "ROUND", "ABS",
            "CEILING", "FLOOR", "POWER", "SQRT", "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD",
            "FIRST_VALUE", "LAST_VALUE", "NEWID", "SCOPE_IDENTITY", "OBJECT_ID", "DB_NAME", "SUSER_SNAME",
            "ERROR_MESSAGE", "ERROR_NUMBER", "ERROR_LINE", "ERROR_PROCEDURE", "ERROR_SEVERITY", "ERROR_STATE",
            "XACT_STATE", "JSON_VALUE", "JSON_QUERY", "OPENJSON", "ISJSON", "NOLOCK", "READPAST", "UPDLOCK",
            "ROWLOCK", "HOLDLOCK", "NOCOUNT", "XACT_ABORT", "ANSI_NULLS", "QUOTED_IDENTIFIER", "TRY", "CATCH",
            "THROW", "PARTITION", "ROWS", "ONLY", "NEXT", "FIRST", "LAST", "OFFSET", "FETCH", "OUTPUT", "MATCHED",
            "TARGET", "SOURCE", "INT", "BIGINT", "SMALLINT", "TINYINT", "BIT", "DECIMAL", "NUMERIC", "MONEY",
            "FLOAT", "REAL", "DATE", "DATETIME", "DATETIME2", "SMALLDATETIME", "DATETIMEOFFSET", "TIME", "CHAR",
            "VARCHAR", "NCHAR", "NVARCHAR", "TEXT", "NTEXT", "BINARY", "VARBINARY", "UNIQUEIDENTIFIER", "XML",
            "SYSNAME", "MAX", "STRING_ESCAPE", "QUOTENAME", "SPACE", "SIGN", "LOG", "EXP", "PI", "RAND"
        };

        /// <summary>Palabras reservadas que no se pueden usar como alias sin corchetes.</summary>
        public static readonly HashSet<string> Reservadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "BEGIN", "BETWEEN", "BY", "CASE", "CHECK", "COLUMN",
            "COMMIT", "CREATE", "CROSS", "CURRENT", "DATABASE", "DECLARE", "DEFAULT", "DELETE", "DESC", "DISTINCT",
            "DROP", "ELSE", "END", "EXCEPT", "EXEC", "EXISTS", "FETCH", "FOR", "FOREIGN", "FROM", "FULL", "FUNCTION",
            "GO", "GOTO", "GRANT", "GROUP", "HAVING", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO",
            "IS", "JOIN", "KEY", "LEFT", "LIKE", "MERGE", "NOT", "NULL", "OF", "OFF", "ON", "OPEN", "OPTION", "OR",
            "ORDER", "OUTER", "OVER", "PIVOT", "PRIMARY", "PROC", "PROCEDURE", "RETURN", "RIGHT", "ROLLBACK",
            "SELECT", "SET", "TABLE", "THEN", "TO", "TOP", "TRAN", "TRIGGER", "TRUNCATE", "UNION", "UNIQUE",
            "UPDATE", "USE", "USER", "VALUES", "VIEW", "WHEN", "WHERE", "WHILE", "WITH"
        };
    }
}
