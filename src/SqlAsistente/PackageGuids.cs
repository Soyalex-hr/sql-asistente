using System;

namespace SqlAsistente
{
    internal static class PackageGuids
    {
        public const string PaqueteString = "3f6d2a1e-8b4c-4e7a-9d15-6a2c7b9e4f10";
        public const string ComandosString = "9b1c4d7e-2a3f-4b6c-8e9d-0f1a2b3c4d5e";
        public const string ListaErroresString = "c2d4e6f8-1a3b-4c5d-9e7f-2b4d6f8a0c1e";

        public static readonly Guid Paquete = new Guid(PaqueteString);
        public static readonly Guid Comandos = new Guid(ComandosString);
        public static readonly Guid ListaErrores = new Guid(ListaErroresString);
    }

    /// <summary>Identificadores de comandos. Deben coincidir con SqlAsistente.vsct.</summary>
    internal static class IdsComando
    {
        public const int Formatear = 0x0100;
        public const int Mayusculas = 0x0101;
        public const int Analizar = 0x0200;
        public const int LimpiarAnalisis = 0x0201;
        public const int ExpandirAsterisco = 0x0300;
        public const int CalificarEsquema = 0x0301;
        public const int RefrescarEsquema = 0x0400;
        public const int EstadoConexion = 0x0401;
        public const int AbrirHistorial = 0x0500;
        public const int AbrirOpciones = 0x0501;
        public const int EditarFragmentos = 0x0502;
        public const int RecuperarPestanas = 0x0503;

        public const int EjecutarSentencia = 0x0600;
        public const int IrADefinicion = 0x0601;
        public const int QuienUsa = 0x0602;
        public const int BuscarObjetos = 0x0603;
        public const int Renombrar = 0x0604;

        public const int ClaseCSharp = 0x0700;
        public const int LlamadaCSharp = 0x0701;
        public const int DatosInsert = 0x0702;
        public const int DatosJson = 0x0703;
    }
}
