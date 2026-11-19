using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlAsistente
{
    /// <summary>Recorre el árbol de T-SQL y aplica las reglas de buenas prácticas.</summary>
    internal sealed class ReglasVisitor : TSqlFragmentVisitor
    {
        private readonly ResultadoAnalisis resultado;
        private readonly PaginaOpciones op;
        private readonly HashSet<string> nombresCte;
        private readonly HashSet<int> offsetsReportados = new HashSet<int>();

        private int profundidadExists;
        private int profundidadTry;
        private bool hayXactAbort;

        // Variables por lote (GO): nombre → elemento declarado
        private Dictionary<string, TSqlFragment> declaradas = new Dictionary<string, TSqlFragment>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ReglasVisitor(ResultadoAnalisis resultado, PaginaOpciones op, HashSet<string> nombresCte, bool hayXactAbort)
        {
            this.resultado = resultado;
            this.op = op;
            this.nombresCte = nombresCte;
            this.hayXactAbort = hayXactAbort;
        }

        private void Agregar(string codigo, Severidad severidad, TSqlFragment nodo, string mensaje)
        {
            if (!op.ReglaActiva(codigo) || nodo == null) return;
            // Evita duplicar el mismo aviso en la misma posición.
            int clave = nodo.StartOffset * 100 + int.Parse(codigo.Substring(2));
            if (!offsetsReportados.Add(clave)) return;

            resultado.Hallazgos.Add(new Hallazgo
            {
                Codigo = codigo,
                Severidad = severidad,
                Mensaje = mensaje,
                Linea = nodo.StartLine,
                Columna = nodo.StartColumn,
                Offset = nodo.StartOffset,
                Largo = nodo.FragmentLength
            });
        }

        // ---------- Lotes y variables ----------

        public override void ExplicitVisit(TSqlBatch node)
        {
            var declaradasPrevias = declaradas;
            var usadasPrevias = usadas;
            declaradas = new Dictionary<string, TSqlFragment>(StringComparer.OrdinalIgnoreCase);
            usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            base.ExplicitVisit(node);

            foreach (var par in declaradas)
            {
                if (!usadas.Contains(par.Key))
                    Agregar("SA014", Severidad.Advertencia, par.Value, $"La variable {par.Key} se declara pero nunca se usa.");
            }

            declaradas = declaradasPrevias;
            usadas = usadasPrevias;
        }

        public override void Visit(DeclareVariableElement node)
        {
            if (node is ProcedureParameter) return;
            var nombre = node.VariableName?.Value;
            if (!string.IsNullOrEmpty(nombre) && !declaradas.ContainsKey(nombre))
                declaradas[nombre] = node;
        }

        public override void Visit(DeclareTableVariableBody node)
        {
            var nombre = node.VariableName?.Value;
            if (!string.IsNullOrEmpty(nombre) && !declaradas.ContainsKey(nombre))
                declaradas[nombre] = node;
        }

        public override void Visit(VariableReference node)
        {
            if (!string.IsNullOrEmpty(node.Name)) usadas.Add(node.Name);
        }

        // ---------- Sentencias peligrosas ----------

        public override void Visit(UpdateSpecification node)
        {
            if (node.WhereClause == null && !TieneFromConJoin(node.FromClause))
                Agregar("SA001", Severidad.Error, node, "UPDATE sin WHERE: se van a modificar TODAS las filas de la tabla.");
        }

        public override void Visit(DeleteSpecification node)
        {
            if (node.WhereClause == null && !TieneFromConJoin(node.FromClause))
                Agregar("SA002", Severidad.Error, node, "DELETE sin WHERE: se van a borrar TODAS las filas de la tabla.");
        }

        private static bool TieneFromConJoin(FromClause from)
        {
            // UPDATE ... FROM tabla INNER JOIN otra ON ... filtra por el JOIN, así que no es "sin WHERE" puro.
            return from != null && from.TableReferences.Any(t => t is QualifiedJoin);
        }

        public override void Visit(TruncateTableStatement node)
        {
            Agregar("SA003", Severidad.Advertencia, node, "TRUNCATE TABLE borra todos los datos de la tabla y no se puede filtrar.");
        }

        public override void Visit(DropTableStatement node)
        {
            Agregar("SA004", Severidad.Advertencia, node, "DROP TABLE elimina la tabla y sus datos.");
        }

        public override void Visit(DropDatabaseStatement node)
        {
            Agregar("SA004", Severidad.Advertencia, node, "DROP DATABASE elimina la base de datos completa.");
        }

        // ---------- Errores lógicos ----------

        public override void Visit(BooleanComparisonExpression node)
        {
            if (node.FirstExpression is NullLiteral || node.SecondExpression is NullLiteral)
            {
                var sugerencia = node.ComparisonType == BooleanComparisonType.Equals ? "IS NULL" : "IS NOT NULL";
                Agregar("SA005", Severidad.Error, node, $"Comparar con NULL usando = o <> nunca es verdadero. Usa {sugerencia}.");
            }
        }

        public override void Visit(InPredicate node)
        {
            if (node.NotDefined && node.Subquery != null)
                Agregar("SA020", Severidad.Advertencia, node, "NOT IN con subconsulta: si la subconsulta devuelve algún NULL no trae " +
                    "ninguna fila. Usa NOT EXISTS.");
        }

        public override void Visit(GlobalVariableExpression node)
        {
            if (string.Equals(node.Name, "@@IDENTITY", StringComparison.OrdinalIgnoreCase))
                Agregar("SA021", Severidad.Advertencia, node, "@@IDENTITY puede devolver el valor de un trigger. Usa SCOPE_IDENTITY().");
        }

        // ---------- Rendimiento ----------

        public override void ExplicitVisit(ExistsPredicate node)
        {
            profundidadExists++;
            base.ExplicitVisit(node);
            profundidadExists--;
        }

        public override void Visit(SelectStarExpression node)
        {
            if (profundidadExists > 0) return; // EXISTS (SELECT * ...) está bien
            Agregar("SA006", Severidad.Advertencia, node, "Evita SELECT *: trae columnas que no necesitas y se rompe si cambia la tabla. " +
                "Usa SQL Asistente > Expandir SELECT * (Ctrl+K, Ctrl+E).");
        }

        public override void Visit(QuerySpecification node)
        {
            if (node.FromClause != null && node.FromClause.TableReferences.Count > 1)
                Agregar("SA007", Severidad.Advertencia, node.FromClause, "JOIN implícito con comas en el FROM. Usa INNER JOIN ... ON " +
                    "para que sea claro y evitar productos cartesianos.");

            if (node.TopRowFilter != null && node.OrderByClause == null && profundidadExists == 0)
                Agregar("SA010", Severidad.Advertencia, node.TopRowFilter, "TOP sin ORDER BY: las filas que devuelve no son predecibles.");
        }

        public override void Visit(WhereClause node)
        {
            if (node.SearchCondition == null) return;
            var buscador = new BuscadorNoSargable();
            node.SearchCondition.Accept(buscador);
            foreach (var expr in buscador.Encontrados)
                Agregar("SA008", Severidad.Advertencia, expr, "Función u operación sobre una columna en el WHERE: impide usar índices " +
                    "(no SARGable). Pasa la operación al otro lado de la comparación.");
        }

        public override void Visit(LikePredicate node)
        {
            if (node.SecondExpression is StringLiteral lit && lit.Value != null && lit.Value.StartsWith("%"))
                Agregar("SA009", Severidad.Info, node, "LIKE que empieza con % no puede usar índices y recorre toda la tabla.");
        }

        public override void Visit(TableHint node)
        {
            if (node.HintKind == TableHintKind.NoLock)
                Agregar("SA011", Severidad.Info, node, "NOLOCK puede leer datos sin confirmar, duplicados o faltantes. Úsalo solo si " +
                    "sabes el riesgo.");
        }

        public override void Visit(DeclareCursorStatement node)
        {
            Agregar("SA015", Severidad.Info, node, "Los cursores procesan fila por fila. Revisa si se puede hacer con una sola consulta " +
                "(UPDATE/INSERT ... SELECT).");
        }

        // ---------- Procedimientos ----------

        public override void Visit(ProcedureStatementBody node)
        {
            var nombre = node.ProcedureReference?.Name?.BaseIdentifier?.Value ?? "";

            if (nombre.StartsWith("sp_", StringComparison.OrdinalIgnoreCase))
                Agregar("SA022", Severidad.Info, node.ProcedureReference, "El prefijo sp_ está reservado para el sistema y hace que SQL " +
                    "Server busque primero en master. Usa otro prefijo (usp_, por ejemplo).");

            if (node.StatementList == null) return;

            var inspector = new InspectorProcedimiento();
            node.StatementList.Accept(inspector);

            if (!inspector.TieneNoCount)
                Agregar("SA012", Severidad.Advertencia, node.ProcedureReference, "Falta SET NOCOUNT ON; al inicio del procedimiento " +
                    "(evita mensajes de 'filas afectadas' y mejora el rendimiento).");

            if (inspector.ModificaDatos && !inspector.TieneTryCatch)
                Agregar("SA013", Severidad.Info, node.ProcedureReference, "El procedimiento modifica datos pero no tiene BEGIN TRY...END " +
                    "TRY / BEGIN CATCH...END CATCH.");
        }

        public override void ExplicitVisit(TryCatchStatement node)
        {
            profundidadTry++;
            base.ExplicitVisit(node);
            profundidadTry--;
        }

        public override void Visit(BeginTransactionStatement node)
        {
            if (profundidadTry == 0 && !hayXactAbort)
                Agregar("SA023", Severidad.Info, node, "BEGIN TRAN sin TRY...CATCH ni SET XACT_ABORT ON: si hay un error la transacción " +
                    "puede quedar abierta.");
        }

        // ---------- Mantenibilidad ----------

        public override void Visit(InsertSpecification node)
        {
            if ((node.Columns == null || node.Columns.Count == 0) && !(node.InsertSource is ExecuteInsertSource))
                Agregar("SA016", Severidad.Advertencia, node, "INSERT sin lista de columnas: se rompe si cambia el orden o la cantidad " +
                    "de columnas de la tabla.");
        }

        public override void Visit(OrderByClause node)
        {
            foreach (var elemento in node.OrderByElements)
            {
                if (elemento.Expression is IntegerLiteral)
                {
                    Agregar("SA017", Severidad.Advertencia, elemento, "ORDER BY por número de columna: usa el nombre de la columna.");
                }
            }
        }

        public override void Visit(NamedTableReference node)
        {
            var obj = node.SchemaObject;
            if (obj == null || obj.SchemaIdentifier != null || obj.BaseIdentifier == null) return;

            var nombre = obj.BaseIdentifier.Value ?? "";
            if (nombre.StartsWith("#")) return;
            if (nombresCte.Contains(nombre)) return;
            if (EsTablaDeTrigger(nombre)) return;

            Agregar("SA018", Severidad.Info, node, $"La tabla {nombre} no tiene esquema. Escribe dbo.{nombre} (más rápido y sin " +
                $"ambigüedades).");
        }

        /// <summary>inserted / deleted: tablas virtuales de los triggers (no llevan esquema).</summary>
        internal static bool EsTablaDeTrigger(string nombre) =>
            nombre.Equals("inserted", StringComparison.OrdinalIgnoreCase)
            || nombre.Equals("deleted", StringComparison.OrdinalIgnoreCase);

        public override void Visit(SqlDataTypeReference node)
        {
            var t = node.SqlDataTypeOption;
            if (t == SqlDataTypeOption.Text || t == SqlDataTypeOption.NText || t == SqlDataTypeOption.Image)
                Agregar("SA019", Severidad.Advertencia, node, "TEXT, NTEXT e IMAGE están obsoletos. Usa VARCHAR(MAX), NVARCHAR(MAX) o " +
                    "VARBINARY(MAX).");
        }
    }

    /// <summary>Busca funciones u operaciones aplicadas a columnas dentro de comparaciones.</summary>
    internal sealed class BuscadorNoSargable : TSqlFragmentVisitor
    {
        public List<TSqlFragment> Encontrados { get; } = new List<TSqlFragment>();

        public override void Visit(BooleanComparisonExpression node)
        {
            Revisar(node.FirstExpression);
            Revisar(node.SecondExpression);
        }

        public override void Visit(LikePredicate node)
        {
            Revisar(node.FirstExpression);
        }

        // No entra en subconsultas: tienen su propio WHERE y se revisan aparte.
        public override void ExplicitVisit(ScalarSubquery node) { }
        public override void ExplicitVisit(ExistsPredicate node) { }

        private void Revisar(ScalarExpression expr)
        {
            if (expr == null) return;
            bool esOperacion = expr is FunctionCall || expr is CastCall || expr is ConvertCall
                               || expr is TryCastCall || expr is TryConvertCall || expr is BinaryExpression;
            if (!esOperacion) return;

            var detector = new DetectorColumna();
            expr.Accept(detector);
            if (detector.TieneColumna) Encontrados.Add(expr);
        }
    }

    internal sealed class DetectorColumna : TSqlFragmentVisitor
    {
        public bool TieneColumna { get; private set; }

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.ColumnType == ColumnType.Regular) TieneColumna = true;
        }

        public override void ExplicitVisit(ScalarSubquery node) { }
    }

    internal sealed class InspectorProcedimiento : TSqlFragmentVisitor
    {
        public bool TieneNoCount { get; private set; }
        public bool TieneTryCatch { get; private set; }
        public bool ModificaDatos { get; private set; }

        public override void Visit(PredicateSetStatement node)
        {
            if (node.IsOn && (node.Options & SetOptions.NoCount) == SetOptions.NoCount)
                TieneNoCount = true;
        }

        public override void Visit(TryCatchStatement node) => TieneTryCatch = true;
        public override void Visit(InsertStatement node) => ModificaDatos = true;
        public override void Visit(UpdateStatement node) => ModificaDatos = true;
        public override void Visit(DeleteStatement node) => ModificaDatos = true;
        public override void Visit(MergeStatement node) => ModificaDatos = true;
    }

    /// <summary>Recolecta nombres de CTE y si existe SET XACT_ABORT ON en el script.</summary>
    internal sealed class RecolectorPrevio : TSqlFragmentVisitor
    {
        public HashSet<string> NombresCte { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool HayXactAbort { get; private set; }

        public override void Visit(CommonTableExpression node)
        {
            if (node.ExpressionName?.Value != null) NombresCte.Add(node.ExpressionName.Value);
        }

        public override void Visit(PredicateSetStatement node)
        {
            if (node.IsOn && (node.Options & SetOptions.XactAbort) == SetOptions.XactAbort)
                HayXactAbort = true;
        }
    }
}
