using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAsistente
{
    internal enum TipoObjeto
    {
        Tabla,
        Vista,
        Procedimiento,
        FuncionEscalar,
        FuncionTabla,
        Sinonimo
    }

    internal sealed class ColumnaBd
    {
        public string Nombre { get; set; }
        public string TipoDato { get; set; }
        /// <summary>Tipo sin tamaño: nvarchar, int, decimal...</summary>
        public string TipoBase { get; set; }
        public bool EsNula { get; set; }
        public bool EsPk { get; set; }
        public bool EsIdentidad { get; set; }
        public int Orden { get; set; }

        public string Descripcion =>
            TipoDato
            + (EsNula ? " NULL" : " NOT NULL")
            + (EsPk ? " · PK" : "")
            + (EsIdentidad ? " · IDENTITY" : "");
    }

    internal sealed class ParametroBd
    {
        public string Nombre { get; set; }
        public string TipoDato { get; set; }
        public string TipoBase { get; set; }
        public bool EsSalida { get; set; }
        public int Orden { get; set; }

        public string Descripcion => $"{Nombre} {TipoDato.ToUpperInvariant()}{(EsSalida ? " OUTPUT" : "")}";
    }

    internal sealed class ObjetoBd
    {
        public int Id { get; set; }
        public string Esquema { get; set; }
        public string Nombre { get; set; }
        public TipoObjeto Tipo { get; set; }
        public List<ColumnaBd> Columnas { get; } = new List<ColumnaBd>();
        public List<ParametroBd> Parametros { get; } = new List<ParametroBd>();

        public bool EsProgramable => Tipo == TipoObjeto.Procedimiento || Tipo == TipoObjeto.FuncionEscalar
                                     || Tipo == TipoObjeto.FuncionTabla || Tipo == TipoObjeto.Vista;

        public string TipoTexto
        {
            get
            {
                switch (Tipo)
                {
                    case TipoObjeto.Tabla: return "Tabla";
                    case TipoObjeto.Vista: return "Vista";
                    case TipoObjeto.Procedimiento: return "Procedimiento";
                    case TipoObjeto.FuncionEscalar: return "Función";
                    case TipoObjeto.FuncionTabla: return "Función de tabla";
                    default: return "Sinónimo";
                }
            }
        }

        public string NombreCompleto => $"{Identificadores.Citar(Esquema)}.{Identificadores.Citar(Nombre)}";

        public bool TieneColumnas => Tipo == TipoObjeto.Tabla || Tipo == TipoObjeto.Vista || Tipo == TipoObjeto.FuncionTabla;

        public IEnumerable<ColumnaBd> ColumnasPk => Columnas.Where(c => c.EsPk);

        public ColumnaBd BuscarColumna(string nombre) =>
            Columnas.FirstOrDefault(c => string.Equals(c.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class LlaveForanea
    {
        public string Nombre { get; set; }
        public ObjetoBd Hija { get; set; }
        public ObjetoBd Padre { get; set; }
        /// <summary>Pares (columna de la tabla hija, columna de la tabla padre).</summary>
        public List<KeyValuePair<string, string>> Columnas { get; } = new List<KeyValuePair<string, string>>();
    }

    internal sealed class EsquemaBd
    {
        public string Servidor { get; set; }
        public string BaseDatos { get; set; }
        public DateTime Cargado { get; set; }
        public TimeSpan Duracion { get; set; }

        public Dictionary<int, ObjetoBd> PorId { get; } = new Dictionary<int, ObjetoBd>();
        public List<ObjetoBd> Objetos { get; } = new List<ObjetoBd>();
        public List<LlaveForanea> Llaves { get; } = new List<LlaveForanea>();
        public List<string> BasesDatos { get; } = new List<string>();
        public HashSet<string> Esquemas { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<ObjetoBd> TablasYVistas => Objetos.Where(o => o.Tipo == TipoObjeto.Tabla || o.Tipo == TipoObjeto.Vista);

        /// <summary>Busca un objeto por nombre; si no se indica esquema, prefiere dbo.</summary>
        public ObjetoBd Buscar(string esquema, string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return null;
            var candidatos = Objetos.Where(o => string.Equals(o.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(esquema))
                return candidatos.FirstOrDefault(o => string.Equals(o.Esquema, esquema, StringComparison.OrdinalIgnoreCase));

            var lista = candidatos.ToList();
            return lista.FirstOrDefault(o => string.Equals(o.Esquema, "dbo", StringComparison.OrdinalIgnoreCase)) ?? lista.FirstOrDefault();
        }

        public IEnumerable<LlaveForanea> LlavesDe(ObjetoBd objeto) =>
            Llaves.Where(l => l.Hija == objeto || l.Padre == objeto);
    }

    internal static class Identificadores
    {
        /// <summary>Pone corchetes solo cuando hace falta: [Mi Tabla], [Order], pero Clientes queda igual.</summary>
        public static string Citar(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return nombre;
            bool valido = (char.IsLetter(nombre[0]) || nombre[0] == '_' || nombre[0] == '#' || nombre[0] == '@')
                          && nombre.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@' || c == '$');
            if (valido && !PalabrasSql.Reservadas.Contains(nombre)) return nombre;
            return "[" + nombre.Replace("]", "]]") + "]";
        }

        /// <summary>Quita corchetes o comillas: [dbo] → dbo.</summary>
        public static string Limpiar(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return nombre;
            nombre = nombre.Trim();
            if (nombre.Length >= 2 && nombre[0] == '[' && nombre[nombre.Length - 1] == ']')
                return nombre.Substring(1, nombre.Length - 2).Replace("]]", "]");
            if (nombre.Length >= 2 && nombre[0] == '"' && nombre[nombre.Length - 1] == '"')
                return nombre.Substring(1, nombre.Length - 2);
            return nombre;
        }

        /// <summary>
        /// Genera un alias corto: Clientes → cli, DetallePedidos → dp, orden_compra → oc.
        /// Evita palabras reservadas y alias ya usados.
        /// </summary>
        public static string GenerarAlias(string nombreTabla, IEnumerable<string> existentes)
        {
            var usados = new HashSet<string>(existentes ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var nombre = (nombreTabla ?? "t").TrimStart('#', '@');
            if (nombre.Length == 0) nombre = "t";

            // Partes por mayúsculas (PascalCase) o guiones bajos.
            var partes = new List<string>();
            var actual = "";
            foreach (var c in nombre)
            {
                if (c == '_' || c == ' ')
                {
                    if (actual.Length > 0) partes.Add(actual);
                    actual = "";
                }
                else if (char.IsUpper(c) && actual.Length > 0 && !char.IsUpper(actual[actual.Length - 1]))
                {
                    partes.Add(actual);
                    actual = c.ToString();
                }
                else
                {
                    actual += c;
                }
            }
            if (actual.Length > 0) partes.Add(actual);

            string alias;
            if (partes.Count >= 2)
                alias = string.Concat(partes.Where(p => p.Length > 0 && char.IsLetter(p[0])).Select(p => char.ToLowerInvariant(p[0])));
            else
                alias = new string(nombre.Where(char.IsLetter).Take(3).ToArray()).ToLowerInvariant();

            if (string.IsNullOrEmpty(alias)) alias = "t";
            if (PalabrasSql.Reservadas.Contains(alias)) alias += "1";

            var candidato = alias;
            int n = 2;
            while (usados.Contains(candidato) || PalabrasSql.Reservadas.Contains(candidato))
                candidato = alias + n++;
            return candidato;
        }
    }
}
