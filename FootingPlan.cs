using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FootingRebar
{
    /// <summary>Capas de barras que puede llevar una zapata.</summary>
    public enum BarLayer
    {
        /// <summary>Parrilla inferior, direccion principal (a lo largo de u): la capa mas baja.</summary>
        BottomMain,
        /// <summary>Parrilla inferior, direccion secundaria (a lo largo de v), apoyada encima de la principal.</summary>
        BottomSecondary,
        /// <summary>Parrilla superior, direccion principal (a lo largo de u): la capa mas alta.</summary>
        TopMain,
        /// <summary>Parrilla superior, direccion secundaria (a lo largo de v), colgada debajo de la principal.</summary>
        TopSecondary
    }

    public static class Layers
    {
        public static readonly BarLayer[] All = { BarLayer.BottomMain, BarLayer.BottomSecondary, BarLayer.TopMain, BarLayer.TopSecondary };

        public static string Name(BarLayer l)
        {
            switch (l)
            {
                case BarLayer.BottomMain: return "inferior principal";
                case BarLayer.BottomSecondary: return "inferior secundaria";
                case BarLayer.TopMain: return "superior principal";
                default: return "superior secundaria";
            }
        }

        /// <summary>Nombre corto para el comodin {capa} de la Particion.</summary>
        public static string Short(BarLayer l)
        {
            switch (l)
            {
                case BarLayer.BottomMain: return "inferior";
                case BarLayer.BottomSecondary: return "inferior-sec";
                case BarLayer.TopMain: return "superior";
                default: return "superior-sec";
            }
        }

        public static bool IsTop(BarLayer l) => l == BarLayer.TopMain || l == BarLayer.TopSecondary;
        public static bool AlongU(BarLayer l) => l == BarLayer.BottomMain || l == BarLayer.TopMain;
    }

    /// <summary>
    /// Diametros (pies) de cada capa, ya resueltos a partir de los tipos de barra, y el
    /// retranqueo del extremo recto cuando la barra lleva gancho: radio exterior del doblez
    /// (medio diametro de doblado del gancho + un diametro de barra), para que la cara
    /// exterior del gancho guarde el recubrimiento lateral. 0 si la capa va recta.
    /// </summary>
    public sealed class PlanDiameters
    {
        private readonly Dictionary<BarLayer, double> _d = new Dictionary<BarLayer, double>();
        private readonly Dictionary<BarLayer, double> _inset = new Dictionary<BarLayer, double>();

        public double Of(BarLayer l) => _d.TryGetValue(l, out double v) ? v : 0;
        public double HookInsetOf(BarLayer l) => _inset.TryGetValue(l, out double v) ? v : 0;
        public void Set(BarLayer l, double diameterFt, double hookInsetFt = 0)
        {
            _d[l] = diameterFt;
            _inset[l] = hookInsetFt;
        }
    }

    /// <summary>Columna que apoya sobre la zapata, en coordenadas locales (solo para los esquemas y el informe).</summary>
    public sealed class ColumnFootprint
    {
        public string Name = "";
        /// <summary>Contorno de la base de la columna (coordenadas locales u, v).</summary>
        public List<Pt> Ring = new List<Pt>();
        public double UMin => Ring.Min(p => p.U);
        public double UMax => Ring.Max(p => p.U);
        public double VMin => Ring.Min(p => p.V);
        public double VMax => Ring.Max(p => p.V);
        public Pt Center => Geometry2D.Centroid(Ring);
    }

    /// <summary>
    /// Una barra recta planificada, en coordenadas locales de la zapata (pies). Va a lo largo
    /// de u (AlongU, en la recta v = Coord) o de v (en la recta u = Coord), de Start a End,
    /// a la cota Z medida desde la cara inferior. Si lleva gancho en un extremo, el tramo
    /// recto ya esta retranqueado para que el doblez guarde el recubrimiento.
    /// </summary>
    public sealed class PlannedBar
    {
        public BarLayer Layer;
        public bool AlongU;
        public double Coord, Z, Start, End, D;
        public bool HookStart, HookEnd;
        public double Length => End - Start;

        /// <summary>Misma barra (longitud, ganchos, cota) en otra posicion: candidata al mismo conjunto.</summary>
        public bool SameAs(PlannedBar o, double tol) =>
            Layer == o.Layer && AlongU == o.AlongU && Math.Abs(Z - o.Z) <= tol && Math.Abs(D - o.D) <= 1e-9 &&
            Math.Abs(Start - o.Start) <= tol && Math.Abs(End - o.End) <= tol &&
            HookStart == o.HookStart && HookEnd == o.HookEnd;
    }

    /// <summary>Barras iguales y equiespaciadas: un conjunto (array) de Revit.</summary>
    public sealed class BarGroup
    {
        public List<PlannedBar> Bars = new List<PlannedBar>();
        public PlannedBar First => Bars[0];
        public int Count => Bars.Count;
        /// <summary>Separacion entre barras del conjunto (pies); 0 si es una sola.</summary>
        public double Spacing;
        public BarLayer Layer => First.Layer;
        public bool AlongU => First.AlongU;
    }

    /// <summary>
    /// Armado completo de una zapata en coordenadas locales: parrilla inferior (principal
    /// y secundaria) y, opcional, parrilla superior, agrupadas en conjuntos. Pura (sin
    /// Revit): la misma clase la usan la ventana (para dibujar) y el generador (para crear
    /// las barras), asi lo que se ve es lo que se arma.
    /// </summary>
    public sealed class FootingPlan
    {
        public const double MmPerFt = 304.8;
        public static double Mm(double mm) => mm / MmPerFt;
        public static double ToMm(double ft) => Math.Round(ft * MmPerFt);

        /// <summary>Contorno de la cara inferior (la parrilla inferior se reparte en el).</summary>
        public Outline2D Outline;
        /// <summary>Contorno de la cara superior (la parrilla superior se reparte en el; en una zapata prismatica coincide con el inferior).</summary>
        public Outline2D TopOutline;
        public double Thickness;
        public double CoverBottom, CoverTop, CoverEdge;

        public List<PlannedBar> Bars = new List<PlannedBar>();
        public List<BarGroup> Groups = new List<BarGroup>();
        /// <summary>Cota (desde la cara inferior) de cada capa colocada, para el esquema de la seccion.</summary>
        public Dictionary<BarLayer, double> LayerZ = new Dictionary<BarLayer, double>();
        /// <summary>Longitud de gancho elegida de cada capa (pies; 0 = la del tipo de barra), para el esquema de la seccion.</summary>
        public Dictionary<BarLayer, double> HookLength = new Dictionary<BarLayer, double>();
        public List<string> Warnings = new List<string>();
        public string Error;
        /// <summary>Tramos demasiado cortos que se han omitido.</summary>
        public int Skipped;

        private double _tol, _minLen;

        public int CountOf(BarLayer l) => Bars.Count(b => b.Layer == l);
        public int GroupsOf(BarLayer l) => Groups.Count(g => g.Layer == l);
        public IEnumerable<BarLayer> UsedLayers => Bars.Select(b => b.Layer).Distinct().OrderBy(l => (int)l);
        public bool HasTop => Bars.Any(b => Layers.IsTop(b.Layer));

        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>Resumen corto: "parrilla inferior + superior; 43 barras en 4 conjuntos".</summary>
        public string Describe()
        {
            if (Error != null) return "SIN ARMAR: " + Error;
            string head = HasTop ? "parrilla inferior + superior" : "parrilla inferior";
            return head + "; " + Bars.Count + " barras en " + Groups.Count + " conjuntos" +
                   (Skipped > 0 ? " (" + Skipped + " tramos cortos omitidos)" : "");
        }

        /// <summary>Desglose por capa: "inferior principal 11 (Ø12.7 @200), inferior secundaria 16 (Ø12.7 @200)".</summary>
        public string DescribeLayers()
        {
            var parts = new List<string>();
            foreach (BarLayer l in UsedLayers)
            {
                PlannedBar b = Bars.First(x => x.Layer == l);
                BarGroup g = Groups.FirstOrDefault(x => x.Layer == l && x.Count > 1);
                parts.Add(Layers.Name(l) + " " + CountOf(l) + " (Ø" + Num(b.D * MmPerFt) +
                          (g != null ? " @" + ToMm(g.Spacing) : "") + (b.HookStart || b.HookEnd ? ", gancho" : "") + ")");
            }
            return string.Join(", ", parts);
        }

        // =================================================================
        // Construccion
        // =================================================================

        /// <param name="bottom">Contorno de la cara inferior en coordenadas locales (u = direccion de las barras principales).</param>
        /// <param name="top">Contorno de la cara superior en las mismas coordenadas; null = el mismo que el inferior.</param>
        /// <param name="thickness">Canto de la zapata (pies), de la cara inferior a la superior.</param>
        /// <param name="d">Diametros de cada capa (pies) y retranqueo por gancho.</param>
        public static FootingPlan Build(Outline2D bottom, Outline2D top, double thickness, AppConfig cfg, PlanDiameters d)
        {
            var p = new FootingPlan
            {
                Outline = bottom, TopOutline = top ?? bottom, Thickness = thickness,
                CoverBottom = Mm(cfg.CoverBottomMm), CoverTop = Mm(cfg.CoverTopMm), CoverEdge = Mm(cfg.CoverEdgeMm),
                _tol = Mm(cfg.ToleranceMm), _minLen = Mm(cfg.MinBarLengthMm)
            };
            try
            {
                if (thickness <= p.CoverBottom + p.CoverTop + Mm(10))
                {
                    p.Error = "el canto (" + ToMm(thickness) + " mm) no deja sitio entre los recubrimientos";
                    return p;
                }

                // --- parrilla inferior: principal (la mas baja) y secundaria encima ---
                double d1 = d.Of(BarLayer.BottomMain);
                double z1 = p.CoverBottom + 0.5 * d1;
                p.LayerZ[BarLayer.BottomMain] = z1;
                p.Mesh(BarLayer.BottomMain, p.Outline, z1, d1, d.HookInsetOf(BarLayer.BottomMain), cfg.Bottom.Main);
                double bottomTop = p.CoverBottom + d1;
                if (cfg.Bottom.Secondary.Enabled)
                {
                    double d2 = d.Of(BarLayer.BottomSecondary);
                    double z2 = p.CoverBottom + d1 + 0.5 * d2;
                    p.LayerZ[BarLayer.BottomSecondary] = z2;
                    p.Mesh(BarLayer.BottomSecondary, p.Outline, z2, d2, d.HookInsetOf(BarLayer.BottomSecondary), cfg.Bottom.Secondary);
                    bottomTop += d2;
                }

                // --- parrilla superior: principal (la mas alta) y secundaria debajo ---
                if (cfg.Top.Enabled)
                {
                    double dt1 = d.Of(BarLayer.TopMain);
                    double zt1 = thickness - p.CoverTop - 0.5 * dt1;
                    p.LayerZ[BarLayer.TopMain] = zt1;
                    double topBottom = thickness - p.CoverTop - dt1;
                    p.Mesh(BarLayer.TopMain, p.TopOutline, zt1, dt1, d.HookInsetOf(BarLayer.TopMain), cfg.Top.Main);
                    if (cfg.Top.Secondary.Enabled)
                    {
                        double dt2 = d.Of(BarLayer.TopSecondary);
                        double zt2 = thickness - p.CoverTop - dt1 - 0.5 * dt2;
                        p.LayerZ[BarLayer.TopSecondary] = zt2;
                        p.Mesh(BarLayer.TopSecondary, p.TopOutline, zt2, dt2, d.HookInsetOf(BarLayer.TopSecondary), cfg.Top.Secondary);
                        topBottom -= dt2;
                    }
                    if (topBottom < bottomTop + Mm(25))
                    {
                        p.Error = "las parrillas inferior y superior se solapan: quedan " + ToMm(topBottom - bottomTop) +
                                  " mm libres entre ellas (canto " + ToMm(thickness) + " mm)";
                        return p;
                    }
                }

                // cotas dentro del canto
                foreach (PlannedBar b in p.Bars)
                    if (b.Z - 0.5 * b.D < -p._tol || b.Z + 0.5 * b.D > thickness + p._tol)
                    {
                        p.Error = "la capa " + Layers.Name(b.Layer) + " queda fuera del canto de la zapata (cota " + ToMm(b.Z) + " mm)";
                        return p;
                    }
                if (p.Bars.Count == 0) { p.Error = "no se obtiene ninguna barra con esta configuracion"; return p; }
                p.Group();
            }
            catch (Exception ex)
            {
                p.Error = ex.Message;
            }
            return p;
        }

        /// <summary>
        /// Capa de barras corridas equiespaciadas en el contorno dado: a lo largo de u
        /// (repartidas en v) o de v (repartidas en u), con la separacion como maximo y barra en
        /// los dos extremos al recubrimiento lateral. Cada recta se recorta contra el contorno
        /// con sus huecos; cada tramo interior es una barra, con gancho en los extremos que dan
        /// al borde exterior (si la capa lo lleva) y recta en los que dan a un hueco.
        /// </summary>
        private void Mesh(BarLayer layer, Outline2D o, double z, double d, double hookInset, LayerCfg cfg)
        {
            bool alongU = Layers.AlongU(layer);
            double from = (alongU ? o.VMin : o.UMin) + CoverEdge + 0.5 * d;
            double to = (alongU ? o.VMax : o.UMax) - CoverEdge - 0.5 * d;
            if (to <= from) { Warnings.Add("no cabe la capa " + Layers.Name(layer)); return; }
            bool hook = !string.IsNullOrEmpty(cfg.HookTypeName);
            double inset = hook ? Math.Max(0, hookInset) : 0;
            HookLength[layer] = hook ? Mm(Math.Max(0, cfg.HookLengthMm)) : 0;
            foreach (double c in Geometry2D.Positions(from, to, Mm(cfg.SpacingMm), _tol))
                foreach (Span s in o.Cut(alongU, c, CoverEdge + 0.5 * d, _tol))
                {
                    bool hs = hook && !s.HoleA, he = hook && !s.HoleB;
                    double start = s.A + CoverEdge + (hs ? inset : 0);
                    double end = s.B - CoverEdge - (he ? inset : 0);
                    if (end - start < Math.Max(_minLen, _tol)) { Skipped++; continue; }
                    Bars.Add(new PlannedBar
                    {
                        Layer = layer, AlongU = alongU, Coord = c, Z = z, D = d, Start = start, End = end,
                        HookStart = hs, HookEnd = he
                    });
                }
        }

        // -----------------------------------------------------------------
        // Conjuntos (arrays)
        // -----------------------------------------------------------------

        /// <summary>
        /// Agrupa las barras iguales (misma capa, longitud, ganchos y cota) equiespaciadas en
        /// conjuntos, como si se modelaran a mano con un array: una capa de una zapata
        /// rectangular es un solo conjunto.
        /// </summary>
        private void Group()
        {
            Groups.Clear();
            var clusters = new List<List<PlannedBar>>();
            foreach (PlannedBar b in Bars.OrderBy(b => (int)b.Layer).ThenBy(b => b.Coord).ThenBy(b => b.Start))
            {
                List<PlannedBar> c = clusters.FirstOrDefault(x => x[0].SameAs(b, _tol));
                if (c == null) { c = new List<PlannedBar>(); clusters.Add(c); }
                c.Add(b);
            }
            foreach (List<PlannedBar> c in clusters)
                Groups.AddRange(Progressions(c.OrderBy(b => b.Coord).ToList()));
        }

        /// <summary>Conjuntos de paso constante a partir de barras iguales ordenadas por coordenada (voraz).</summary>
        private List<BarGroup> Progressions(List<PlannedBar> sorted)
        {
            var result = new List<BarGroup>();
            BarGroup g = null;
            foreach (PlannedBar b in sorted)
            {
                if (g != null)
                {
                    double step = b.Coord - g.Bars[g.Count - 1].Coord;
                    if (g.Count == 1 && step > _tol) { g.Spacing = step; g.Bars.Add(b); continue; }
                    if (g.Count >= 2 && Math.Abs(step - g.Spacing) <= _tol) { g.Bars.Add(b); continue; }
                }
                g = new BarGroup();
                g.Bars.Add(b);
                result.Add(g);
            }
            return result;
        }
    }
}
