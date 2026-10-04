using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arba.Comun;

namespace FootingRebar
{
    /// <summary>Direccion de las barras principales (la capa mas baja de la parrilla inferior).</summary>
    public class DirectionCfg
    {
        /// <summary>
        /// "long" = paralelas al lado largo de la zapata (lo normal: las barras del lado largo
        /// van abajo, con mas peralte util), "short" = paralelas al lado corto, "x" / "y" =
        /// ejes del proyecto, "angle" = el angulo AngleDeg medido desde el eje X del proyecto.
        /// Cambiable zapata a zapata en la ventana.
        /// </summary>
        public string Mode { get; set; } = "long";
        public double AngleDeg { get; set; } = 0;
    }

    /// <summary>Una capa de barras rectas y paralelas de la parrilla (principal o secundaria).</summary>
    public class LayerCfg
    {
        /// <summary>Se coloca o no (en las capas obligatorias se ignora).</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Tipo de barra (RebarBarType): exacto, o un fragmento que lo identifique.</summary>
        public string BarTypeName { get; set; } = "";
        /// <summary>Separacion maxima entre barras (mm); se reparten por igual sin superarla.</summary>
        public double SpacingMm { get; set; } = 200;
        /// <summary>
        /// Nombre (o fragmento) del RebarHookType en los dos extremos de cada barra (gancho
        /// estandar de la parrilla, hacia arriba en la inferior y hacia abajo en la superior).
        /// Vacio = barra recta. En los bordes de un hueco nunca hay gancho.
        /// </summary>
        public string HookTypeName { get; set; } = "";
        /// <summary>
        /// Longitud de los ganchos de la capa (mm), tal como la mide Revit (longitud de gancho inicial/final):
        /// se fija en cada barra con "Sobrescribir longitudes de gancho", sin tocar el tipo de barra.
        /// 0 = la que da el tipo de barra para ese gancho.
        /// </summary>
        public double HookLengthMm { get; set; } = 0;
    }

    /// <summary>Una parrilla: capa principal (a lo largo de u) y capa secundaria (a lo largo de v).</summary>
    public class MeshCfg
    {
        /// <summary>Se coloca la parrilla (la inferior siempre).</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Barras a lo largo de u (la capa mas alejada de la cara de la zapata: la mas baja abajo, la mas alta arriba).</summary>
        public LayerCfg Main { get; set; } = new LayerCfg();
        /// <summary>Barras a lo largo de v, apoyadas en las principales (encima abajo, debajo arriba).</summary>
        public LayerCfg Secondary { get; set; } = new LayerCfg();
    }

    public class AppConfig
    {
        /// <summary>Recubrimiento desde la cara inferior de la zapata (contra el terreno) a la cara de la barra (mm).</summary>
        public double CoverBottomMm { get; set; } = 75;
        /// <summary>Recubrimiento desde la cara superior de la zapata (mm).</summary>
        public double CoverTopMm { get; set; } = 50;
        /// <summary>Recubrimiento en los bordes laterales de la zapata y de los huecos (mm).</summary>
        public double CoverEdgeMm { get; set; } = 75;

        public DirectionCfg Direction { get; set; } = new DirectionCfg();
        /// <summary>Parrilla inferior (obligatoria).</summary>
        public MeshCfg Bottom { get; set; } = new MeshCfg();
        /// <summary>Parrilla superior (opcional: zapatas combinadas, conectadas o de gran canto).</summary>
        public MeshCfg Top { get; set; } = new MeshCfg { Enabled = false };

        /// <summary>Detectar las columnas que apoyan sobre la zapata (solo para mostrarlas en los esquemas y el informe).</summary>
        public bool DetectColumns { get; set; } = true;

        /// <summary>
        /// Plantilla por defecto del contrato ARBA para este add-in: categoria del anfitrion (CIMIENTOS), prefijo
        /// ZAP y marca, sin codigo de capa ("CIMIENTOS - ZAP-Z1"); el detalle de la capa va en "ARBA - Codigo".
        /// Una plantilla que no empiece por "{categoria} - {prefijo}-" incumple el contrato (la ventana lo avisa).
        /// </summary>
        public const string DefaultPartitionTemplate = "{categoria} - {prefijo}-{marca}";

        /// <summary>
        /// Plantilla del parametro Particion de cada barra. Comodines (ver <see cref="PartitionName.Help"/> del
        /// codigo comun ARBA): {categoria}, {prefijo}, {marca} (Marca del anfitrion; si esta vacia se usa el Id),
        /// {id}, {codigo} o {capa} (inferior, inferior-sec, superior, superior-sec), {tipo}, {familia} y {conjunto}.
        /// </summary>
        public string PartitionTemplate { get; set; } = DefaultPartitionTemplate;

        /// <summary>Tolerancia geometrica al agrupar coordenadas y comparar (mm).</summary>
        public double ToleranceMm { get; set; } = 2;

        /// <summary>Longitud minima de una barra para colocarla (mm); las mas cortas se omiten con aviso.</summary>
        public double MinBarLengthMm { get; set; } = 300;

        /// <summary>Deja la configuracion en un estado coherente.</summary>
        public void Normalize()
        {
            if (Direction == null) Direction = new DirectionCfg();
            if (Bottom == null) Bottom = new MeshCfg();
            if (Top == null) Top = new MeshCfg { Enabled = false };
            if (Bottom.Main == null) Bottom.Main = new LayerCfg();
            if (Bottom.Secondary == null) Bottom.Secondary = new LayerCfg();
            if (Top.Main == null) Top.Main = new LayerCfg();
            if (Top.Secondary == null) Top.Secondary = new LayerCfg();

            Direction.Mode = NormalizeDirection(Direction.Mode);

            foreach (LayerCfg l in new[] { Bottom.Main, Bottom.Secondary, Top.Main, Top.Secondary })
            {
                if (l.BarTypeName == null) l.BarTypeName = "";
                if (l.HookTypeName == null) l.HookTypeName = "";
                if (l.HookLengthMm < 0) l.HookLengthMm = 0;
                if (l.SpacingMm <= 0) l.SpacingMm = 200;
            }
            Bottom.Enabled = true;
            Bottom.Main.Enabled = true;
            Top.Main.Enabled = true;

            if (CoverBottomMm < 0) CoverBottomMm = 0;
            if (CoverTopMm < 0) CoverTopMm = 0;
            if (CoverEdgeMm < 0) CoverEdgeMm = 0;
            if (ToleranceMm <= 0) ToleranceMm = 2;
            if (MinBarLengthMm < 0) MinBarLengthMm = 0;
            if (string.IsNullOrWhiteSpace(PartitionTemplate)) PartitionTemplate = DefaultPartitionTemplate;
        }

        /// <summary>"long", "short", "x", "y" o "angle"; cualquier otra cosa es "long".</summary>
        public static string NormalizeDirection(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            switch (m)
            {
                case "short": case "corto": return "short";
                case "x": return "x";
                case "y": return "y";
                case "angle": case "angulo": return "angle";
                default: return "long";
            }
        }

        public static string ConfigPath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(dir ?? "", "config.json");
        }

        private static JsonSerializerOptions ReadOptions() => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static JsonSerializerOptions WriteOptions() => new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static AppConfig Load()
        {
            string path = ConfigPath();
            AppConfig cfg = File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), ReadOptions()) ?? new AppConfig()
                : new AppConfig();
            cfg.Normalize();
            return cfg;
        }

        /// <summary>Guarda esta configuracion como config.json junto a la DLL (valores por defecto de la interfaz).</summary>
        public void Save(string path = null)
        {
            File.WriteAllText(path ?? ConfigPath(), JsonSerializer.Serialize(this, WriteOptions()));
        }

        /// <summary>Copia independiente, para que la interfaz edite sin tocar la configuracion cargada.</summary>
        public AppConfig Clone()
        {
            AppConfig c = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, WriteOptions()), ReadOptions())
                          ?? new AppConfig();
            c.Normalize();
            return c;
        }
    }
}
