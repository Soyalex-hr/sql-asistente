using System.Collections.Generic;

namespace SqlAsistente
{
    internal enum Severidad
    {
        Error,
        Advertencia,
        Info
    }

    /// <summary>Un problema encontrado por el análisis de código.</summary>
    internal sealed class Hallazgo
    {
        public string Codigo { get; set; }
        public Severidad Severidad { get; set; }
        public string Mensaje { get; set; }
        /// <summary>Línea (1 = primera).</summary>
        public int Linea { get; set; }
        /// <summary>Columna (1 = primera).</summary>
        public int Columna { get; set; }
        public int Offset { get; set; }
        /// <summary>Largo del fragmento de código señalado.</summary>
        public int Largo { get; set; }

        /// <summary>Sentencias que pueden borrar o cambiar muchos datos.</summary>
        public bool EsPeligrosa => Codigo == "SA001" || Codigo == "SA002" || Codigo == "SA003" || Codigo == "SA004";

        public override string ToString() => $"{Codigo} (línea {Linea}): {Mensaje}";
    }

    internal sealed class ResultadoAnalisis
    {
        public List<Hallazgo> Hallazgos { get; } = new List<Hallazgo>();
        public bool ErrorDeSintaxis { get; set; }
    }

    /// <summary>Catálogo de reglas (código → descripción) para documentación y opciones.</summary>
    internal static class CatalogoReglas
    {
        public static readonly Dictionary<string, string> Reglas = new Dictionary<string, string>
        {
            ["SA000"] = "Error de sintaxis",
            ["SA001"] = "UPDATE sin WHERE",
            ["SA002"] = "DELETE sin WHERE",
            ["SA003"] = "TRUNCATE TABLE",
            ["SA004"] = "DROP de tabla o base de datos",
            ["SA005"] = "Comparación con NULL usando = o <>",
            ["SA006"] = "SELECT *",
            ["SA007"] = "JOIN implícito con comas en el FROM",
            ["SA008"] = "Función u operación sobre una columna en el WHERE (no SARGable)",
            ["SA009"] = "LIKE que empieza con %",
            ["SA010"] = "TOP sin ORDER BY",
            ["SA011"] = "Uso de NOLOCK",
            ["SA012"] = "Procedimiento sin SET NOCOUNT ON",
            ["SA013"] = "Procedimiento que modifica datos sin TRY...CATCH",
            ["SA014"] = "Variable declarada y no usada",
            ["SA015"] = "Uso de cursores",
            ["SA016"] = "INSERT sin lista de columnas",
            ["SA017"] = "ORDER BY por número de columna",
            ["SA018"] = "Tabla sin esquema (dbo.)",
            ["SA019"] = "Tipos obsoletos TEXT / NTEXT / IMAGE",
            ["SA020"] = "NOT IN con subconsulta",
            ["SA021"] = "Uso de @@IDENTITY",
            ["SA022"] = "Procedimiento con prefijo sp_",
            ["SA023"] = "BEGIN TRAN sin manejo de errores",
        };
    }
}
