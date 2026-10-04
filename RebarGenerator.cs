using System;
using System.Collections.Generic;
using System.Linq;
using Arba.Comun;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FootingRebar
{
    /// <summary>Un conjunto (elemento Rebar) creado para una zapata.</summary>
    public sealed class CreatedSet
    {
        public ElementId Id;
        public string Name;
        /// <summary>Radio nominal de la barra (pies).</summary>
        public double Radius;
        public bool AlongU;
    }

    /// <summary>Resultado del armado de un elemento.</summary>
    public sealed class BuildResult
    {
        public List<CreatedSet> Created = new List<CreatedSet>();
        /// <summary>Barras que quedarian fuera del hormigon. Si hay alguna, el elemento entero se deshace.</summary>
        public List<string> Rejected = new List<string>();
        /// <summary>Conjuntos que Revit no pudo crear.</summary>
        public List<string> Failed = new List<string>();
        public List<string> Warnings = new List<string>();
        public int Bars;
        public Dictionary<BarLayer, int> ByLayer = new Dictionary<BarLayer, int>();
        public string Summary => Bars + " barras en " + Created.Count + " conjuntos (" +
                                 string.Join(", ", ByLayer.OrderBy(k => (int)k.Key).Select(k => Layers.Name(k.Key) + " " + k.Value)) + ")";
        public bool Safe => Rejected.Count == 0;
    }

    public static class RebarGenerator
    {
        private static double Mm(double mm) => FootingOutline.Mm(mm);
        private static double ToMm(double ft) => FootingOutline.ToMm(ft);
        private const double MinSeg = 0.003;   // ~1 mm en pies
        /// <summary>Longitud de barra que se tolera fuera del solido al comprobar (pies, ~1 mm).</summary>
        private const double InsideTol = 0.0033;

        private sealed class Ctx
        {
            public Document Doc;
            public HostAnalysis Item;
            public FootingFrame F;
            public AppConfig Cfg;
            public BuildResult Result;
            public FootingPlan Plan;
            public double Tol;
            /// <summary>Tipo de barra y gancho de cada capa.</summary>
            public Dictionary<BarLayer, RebarBarType> BarTypes = new Dictionary<BarLayer, RebarBarType>();
            public Dictionary<BarLayer, ElementId> Hooks = new Dictionary<BarLayer, ElementId>();
            /// <summary>Longitud de gancho que se sobrescribe en las barras de cada capa (pies); 0 = se deja la del tipo de barra.</summary>
            public Dictionary<BarLayer, double> HookLengths = new Dictionary<BarLayer, double>();
            /// <summary>Longitud de gancho predeterminada del tipo de barra para el gancho de cada capa (pies; NaN si Revit no la da).</summary>
            public Dictionary<BarLayer, double> HookDefaults = new Dictionary<BarLayer, double>();
            /// <summary>Capas en las que ya se aviso de que Revit deja el gancho en otra longitud que la pedida.</summary>
            public HashSet<BarLayer> HookLengthNoted = new HashSet<BarLayer>();
            /// <summary>Orientacion de los ganchos de cada capa (se invierte sola si el gancho dobla hacia el lado equivocado).</summary>
            public Dictionary<BarLayer, bool> HookLeft = new Dictionary<BarLayer, bool>();
            public HashSet<BarLayer> HookChecked = new HashSet<BarLayer>();
            /// <summary>
            /// True si Revit anade el gancho mas alla del extremo de la curva en este proyecto (detectado en la primera barra
            /// con gancho): entonces el tramo recto se retranquea el radio del doblez. Lo normal es false: Revit dobla en el extremo.
            /// </summary>
            public bool HooksAppended;
            /// <summary>Place lo pone a true cuando detecta que hay que replanificar con retranqueo (Build deshace y repite).</summary>
            public bool Replan;
        }

        // =================================================================
        // Armado de una zapata
        // =================================================================
        public static BuildResult Build(Document doc, HostAnalysis item, AppConfig cfg)
        {
            FootingFrame f = item.Frame(cfg);
            var c = new Ctx { Doc = doc, Item = item, F = f, Cfg = cfg, Result = new BuildResult(), Tol = Mm(cfg.ToleranceMm) };

            // tipos de barra y ganchos de las capas que se van a colocar
            foreach ((BarLayer layer, LayerCfg lc) in LayersFor(cfg))
            {
                RebarBarType bt = FindBarType(doc, lc.BarTypeName, Layers.Name(layer));
                c.BarTypes[layer] = bt;
                ElementId hook = FindHookType(doc, lc.HookTypeName, c.Result.Warnings);
                c.Hooks[layer] = hook;
                bool known = TryHookLength(bt, hook, out double defFt, out double bendFt);
                c.HookDefaults[layer] = known ? defFt : double.NaN;
                c.HookLengths[layer] = hook == ElementId.InvalidElementId ? 0 : Mm(HookLengthFor(layer, lc.HookLengthMm, known, defFt, bendFt));
                c.HookLeft[layer] = true;
            }

            // Revit dobla el gancho en el propio extremo de la curva (la pata sale del extremo), asi que las barras se
            // planifican sin retranqueo y la cara exterior de la pata queda al recubrimiento. Si al crear la primera barra con
            // gancho de una capa el gancho sobresale del extremo (Revit lo ha anadido mas alla), se deshace lo creado, se
            // replanifica con el retranqueo del radio del doblez y se vuelve a armar, una sola vez.
            for (int pass = 0; ; pass++)
            {
                var d = new PlanDiameters();
                foreach ((BarLayer layer, LayerCfg _) in LayersFor(cfg))
                    d.Set(layer, c.BarTypes[layer].BarNominalDiameter, c.HooksAppended ? HookAppendInset(c.BarTypes[layer]) : 0);
                c.Plan = FootingPlan.Build(f.Outline, f.TopOutline, f.Thickness, cfg, d);
                if (c.Plan.Error != null) throw new InvalidOperationException(c.Plan.Error);

                c.Replan = false;
                int n = 0;
                foreach (BarGroup g in c.Plan.Groups)
                {
                    n++;
                    if (!Place(c, g, n)) break;
                }
                if (!c.Replan || pass > 0) break;

                foreach (CreatedSet cs in c.Result.Created) { try { doc.Delete(cs.Id); } catch { } }
                c.Result.Created.Clear(); c.Result.Rejected.Clear(); c.Result.Failed.Clear(); c.Result.ByLayer.Clear();
                c.Result.Bars = 0;
                c.HookChecked.Clear();
                c.HooksAppended = true;
                c.Result.Warnings.Add("Revit anade los ganchos mas alla del extremo de la barra en este proyecto: el tramo recto se retranquea el radio del doblez para que la pata quede al recubrimiento");
            }

            c.Result.Warnings.AddRange(c.Plan.Warnings);
            if (c.Plan.Skipped > 0) c.Result.Warnings.Add(c.Plan.Skipped + " tramo(s) demasiado cortos omitidos");
            if (f.Stepped && cfg.Top.Enabled) c.Result.Warnings.Add("zapata escalonada/piramidal: la parrilla superior se reparte en la cara superior (menor)");
            return c.Result;
        }

        /// <summary>Capas que lleva la zapata con esta configuracion y su configuracion.</summary>
        public static List<(BarLayer, LayerCfg)> LayersFor(AppConfig cfg)
        {
            var list = new List<(BarLayer, LayerCfg)> { (BarLayer.BottomMain, cfg.Bottom.Main) };
            if (cfg.Bottom.Secondary.Enabled) list.Add((BarLayer.BottomSecondary, cfg.Bottom.Secondary));
            if (cfg.Top.Enabled)
            {
                list.Add((BarLayer.TopMain, cfg.Top.Main));
                if (cfg.Top.Secondary.Enabled) list.Add((BarLayer.TopSecondary, cfg.Top.Secondary));
            }
            return list;
        }

        /// <summary>
        /// Retranqueo del extremo recto de una barra con gancho cuando Revit anade el gancho mas alla del extremo de la
        /// curva (Ctx.HooksAppended): radio del doblez al eje (medio diametro de doblado del gancho + medio diametro de
        /// barra), para que la pata caiga donde caeria doblando en el extremo, con su cara exterior al recubrimiento.
        /// Si el tipo no define el diametro de doblado, 4 diametros.
        /// </summary>
        public static double HookAppendInset(RebarBarType bt)
        {
            double d = bt.BarNominalDiameter;
            double bend = 0;
            try { bend = bt.StandardHookBendDiameter; } catch { }
            if (bend <= 0) { try { bend = bt.StandardBendDiameter; } catch { } }
            if (bend <= 0) bend = 4 * d;
            return 0.5 * bend + 0.5 * d;
        }

        /// <summary>
        /// Longitud de gancho predeterminada (mm) que cada tipo de barra da a cada gancho del proyecto (su tabla "Longitudes de
        /// gancho", automatica o escrita a mano) y lo que ocupa el doblez (mm: longitud menos prolongacion recta), por
        /// (tipo de barra, gancho). La ventana la muestra en la casilla de longitud de gancho. Los pares que Revit no resuelve
        /// no entran (p. ej. un gancho que el tipo no admite).
        /// </summary>
        public static Dictionary<(string Bar, string Hook), (double LengthMm, double BendMm)> HookLengthTable(IEnumerable<RebarBarType> bars, IEnumerable<RebarHookType> hooks)
        {
            var table = new Dictionary<(string, string), (double, double)>();
            List<RebarHookType> hookList = hooks.ToList();
            foreach (RebarBarType bt in bars)
                foreach (RebarHookType h in hookList)
                    if (TryHookLength(bt, h.Id, out double len, out double bend))
                        table[(bt.Name, h.Name)] = (ToMm(len), ToMm(bend));
            return table;
        }

        /// <summary>
        /// Longitud de gancho (pies) que el tipo de barra da a ese gancho y lo que ocupa el doblez (longitud menos prolongacion
        /// recta). False si Revit no la resuelve.
        /// </summary>
        private static bool TryHookLength(RebarBarType bt, ElementId hookId, out double len, out double bend)
        {
            len = bend = 0;
            if (hookId == null || hookId == ElementId.InvalidElementId) return false;
            try
            {
                len = bt.GetHookLength(hookId);
                double tangent = 0;
                try { tangent = bt.GetHookTangentLength(hookId); } catch { }
                bend = Math.Max(0, len - tangent);
                return len > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Longitud de gancho (mm) que hay que sobrescribir en las barras de la capa: 0 si no se eligio ninguna o si coincide con
        /// la predeterminada del tipo de barra para ese gancho (Revit usa entonces la del tipo, sin sobrescribir). Lanza si la
        /// elegida no deja prolongacion recta mas alla del doblez (known: se conocen la predeterminada defFt y el doblez bendFt).
        /// </summary>
        private static double HookLengthFor(BarLayer layer, double chosenMm, bool known, double defFt, double bendFt)
        {
            double mm = HookLengthRule.Resolve(chosenMm, known ? ToMm(defFt) : (double?)null, known ? ToMm(bendFt) : (double?)null, out string err);
            if (err != null) throw new InvalidOperationException(Layers.Name(layer) + ": " + err);
            return mm;
        }

        /// <summary>Armado de la zapata con esta configuracion y estos diametros (lo mismo que dibuja la ventana).</summary>
        public static FootingPlan PlanFor(HostAnalysis item, AppConfig cfg, PlanDiameters d)
        {
            FootingFrame f = item.Frame(cfg);
            return FootingPlan.Build(f.Outline, f.TopOutline, f.Thickness, cfg, d);
        }

        // =================================================================
        // Colocacion con red de seguridad
        // =================================================================

        /// <summary>
        /// Crea un conjunto (array de barras iguales). Antes comprueba que cada posicion del
        /// array queda dentro del hormigon. Si la capa lleva gancho, tras crear la primera barra
        /// lee su geometria real y, si el gancho dobla hacia el lado equivocado (abajo en la
        /// parrilla inferior, arriba en la superior), la borra, invierte la orientacion y la
        /// vuelve a crear. False si algo se rechazo.
        /// </summary>
        private static bool Place(Ctx c, BarGroup g, int index)
        {
            PlannedBar b = g.First;
            FootingFrame f = c.F;
            RebarBarType bt = c.BarTypes[b.Layer];
            ElementId hook = c.Hooks.TryGetValue(b.Layer, out ElementId h) ? h : ElementId.InvalidElementId;
            double r = bt.BarNominalDiameter * 0.5;
            double z = f.ZBottom + b.Z;
            XYZ normal = b.AlongU ? f.DirV : f.DirU;
            XYZ p0 = b.AlongU ? f.World(b.Start, b.Coord, z) : f.World(b.Coord, b.Start, z);
            XYZ p1 = b.AlongU ? f.World(b.End, b.Coord, z) : f.World(b.Coord, b.End, z);
            var curves = new List<Curve>();
            AddLine(curves, p0, p1);
            if (curves.Count == 0) { c.Result.Rejected.Add(NameOf(b, g, index) + ": sin longitud"); return false; }

            bool array = g.Count >= 2 && g.Spacing > MinSeg;
            string name = NameOf(b, g, index);

            // --- RED DE SEGURIDAD (1): geometria planificada, antes de crear nada ---
            for (int k = 0; k < (array ? g.Count : 1); k++)
            {
                IList<Curve> moved = curves;
                if (k > 0)
                {
                    Transform t = Transform.CreateTranslation(normal * (k * g.Spacing));
                    moved = curves.Select(cv => cv.CreateTransformed(t)).ToList();
                }
                if (!BarInside(f, f.CheckSolids, moved, r, b.AlongU, out string why))
                {
                    c.Result.Rejected.Add(name + (k > 0 ? " (posicion " + (k + 1) + " del array)" : "") + ": " + why);
                    return false;
                }
            }

            bool hasHook = hook != ElementId.InvalidElementId && (b.HookStart || b.HookEnd);
            bool checkHook = hasHook && !c.HookChecked.Contains(b.Layer);
            bool wantUp = !Layers.IsTop(b.Layer);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool left = c.HookLeft[b.Layer];
                Rebar rb = Create(c.Doc, c.Item.Host, bt, normal, curves,
                                  b.HookStart ? hook : ElementId.InvalidElementId, b.HookEnd ? hook : ElementId.InvalidElementId, left, out string err);
                if (rb == null) { c.Result.Failed.Add(name + ": Revit no pudo crear la barra (" + err + ")"); return true; }

                // longitud de gancho elegida (distinta de la predeterminada del tipo): se sobrescribe en la barra antes de las
                // comprobaciones, que leen la geometria real con ella
                double hookLen = c.HookLengths.TryGetValue(b.Layer, out double hl) ? hl : 0;
                if (hasHook && hookLen > 0 && !ApplyHookLength(c, rb, b.Layer, b.HookStart, b.HookEnd, hookLen, out string hookErr))
                {
                    c.Doc.Delete(rb.Id);
                    c.Result.Rejected.Add(name + ": no se pudo fijar la longitud de gancho de " + ToMm(hookLen) + " mm (" + hookErr + ")");
                    return false;
                }

                if (checkHook)
                {
                    // RED DE SEGURIDAD (1b): el gancho solo existe en la geometria real
                    c.Doc.Regenerate();
                    int dir = HookDirection(rb, z, r, c.Tol);
                    if (dir != 0 && (dir > 0) != wantUp)
                    {
                        c.Doc.Delete(rb.Id);
                        if (attempt == 0)
                        {
                            c.HookLeft[b.Layer] = !left;
                            c.Result.Warnings.Add(Layers.Name(b.Layer) + ": los ganchos doblaban hacia " + (dir > 0 ? "arriba" : "abajo") + ", se ha invertido su orientacion");
                            continue;
                        }
                        c.Result.Rejected.Add(name + ": los ganchos doblan hacia el lado equivocado con las dos orientaciones");
                        return false;
                    }
                    // RED DE SEGURIDAD (1c): la pata tiene que salir del extremo de la barra. Si el gancho sobresale del
                    // extremo (Revit lo ha anadido mas alla), la zapata se replanifica con retranqueo (Build).
                    if (!c.HooksAppended && HookOverhang(rb, p0, p1, b.HookStart, b.HookEnd) > Math.Max(c.Tol, Mm(2)))
                    {
                        c.Doc.Delete(rb.Id);
                        c.Replan = true;
                        return false;
                    }
                    c.HookChecked.Add(b.Layer);
                }

                if (array) rb.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(g.Count, (g.Count - 1) * g.Spacing, true, true, true);
                else rb.GetShapeDrivenAccessor().SetLayoutAsSingle();

                Finish(c.Doc, rb, c.Item.Host, c.Item.Partition(c.Cfg, SetName(b), Layers.Short(b.Layer)), b.Layer);
                c.Result.Created.Add(new CreatedSet { Id = rb.Id, Name = name, Radius = r, AlongU = b.AlongU });
                c.Result.Bars += g.Count;
                c.Result.ByLayer[b.Layer] = (c.Result.ByLayer.TryGetValue(b.Layer, out int prev) ? prev : 0) + g.Count;
                return true;
            }
            return false;
        }

        private static string SetName(PlannedBar b) => Layers.Name(b.Layer);

        private static string NameOf(PlannedBar b, BarGroup g, int index)
        {
            string pos = b.AlongU ? "v=" + ToMm(b.Coord) : "u=" + ToMm(b.Coord);
            return "conjunto " + index + " " + Layers.Name(b.Layer) + " " + pos + " mm, L=" + ToMm(b.Length) + " mm" +
                   (g.Count > 1 ? " (" + g.Count + " barras cada " + ToMm(g.Spacing) + " mm)" : "");
        }

        /// <summary>
        /// Hacia donde dobla el gancho de la barra real: +1 arriba, -1 abajo, 0 sin desviacion
        /// vertical apreciable (sin gancho o gancho en el plano horizontal).
        /// </summary>
        private static int HookDirection(Rebar rb, double zBar, double r, double tol)
        {
            try
            {
                IList<Curve> cl = rb.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                double zMin = double.MaxValue, zMax = double.MinValue;
                foreach (Curve cv in cl)
                    foreach (XYZ p in cv.Tessellate()) { zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z); }
                double limit = Math.Max(2 * r, tol) + 1e-6;
                if (zMax > zBar + limit) return 1;
                if (zMin < zBar - limit) return -1;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Cuanto sobresale la geometria real de la barra (eje, ganchos incluidos) mas alla de los extremos p0 / p1 del
        /// tramo recto, en la direccion de la barra y solo por los extremos con gancho (pies). Si Revit dobla el gancho en
        /// el extremo no sobresale nada (la pata sale del extremo); si lo anade mas alla, sobresale el radio del doblez.
        /// NaN si no se puede leer.
        /// </summary>
        private static double HookOverhang(Rebar rb, XYZ p0, XYZ p1, bool hookStart, bool hookEnd)
        {
            try
            {
                XYZ dir = (p1 - p0).Normalize();
                double over = double.NegativeInfinity;
                IList<Curve> cl = rb.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                foreach (Curve cv in cl)
                    foreach (XYZ p in cv.Tessellate())
                    {
                        if (hookEnd) over = Math.Max(over, (p - p1).DotProduct(dir));
                        if (hookStart) over = Math.Max(over, (p0 - p).DotProduct(dir));
                    }
                return over;
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// RED DE SEGURIDAD (2): tras crear y regenerar, se lee la geometria REAL de cada
        /// barra de cada conjunto tal y como la ha colocado Revit (radios de doblado, ganchos
        /// completos y todas las posiciones del array) y se comprueba entera contra el
        /// hormigon de la zapata.
        /// </summary>
        public static void VerifyCreated(Document doc, HostAnalysis item, AppConfig cfg, BuildResult res)
        {
            FootingFrame f = item.Frame(cfg);
            foreach (CreatedSet cs in res.Created)
            {
                var rb = doc.GetElement(cs.Id) as Rebar;
                if (rb == null) { res.Rejected.Add(cs.Name + ": el conjunto no existe tras regenerar"); continue; }
                if (!RealInside(f, f.CheckSolids, rb, cs, out string why)) res.Rejected.Add(cs.Name + ": " + why);
            }
        }

        private static bool RealInside(FootingFrame f, List<Solid> solids, Rebar rb, CreatedSet cs, out string why)
        {
            why = null;
            int n;
            try { n = rb.NumberOfBarPositions; }
            catch (Exception ex) { why = "no se pudo leer el conjunto (" + ex.Message + ")"; return false; }
            for (int k = 0; k < n; k++)
            {
                IList<Curve> cl;
                try
                {
                    if (!rb.DoesBarExistAtPosition(k)) continue;
                    cl = rb.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, k);
                }
                catch (Exception ex) { why = "barra " + (k + 1) + " de " + n + ": no se pudo leer su geometria (" + ex.Message + ")"; return false; }
                if (cl == null || cl.Count == 0) { why = "barra " + (k + 1) + " de " + n + ": sin geometria"; return false; }
                if (!BarInside(f, solids, cl, cs.Radius, cs.AlongU, out string w)) { why = "barra " + (k + 1) + " de " + n + ": " + w; return false; }
            }
            return true;
        }

        /// <summary>
        /// True si toda la barra (tramo recto, dobleces y ganchos) esta dentro del hormigon.
        /// Ademas del eje se comprueban cuatro fibras extremas (eje desplazado +-r en horizontal
        /// perpendicular y en vertical), asi una barra tangente a una cara o con medio diametro
        /// fuera tambien falla.
        /// </summary>
        private static bool BarInside(FootingFrame f, List<Solid> solids, IList<Curve> curves, double r, bool alongU, out string why)
        {
            why = null;
            XYZ side = alongU ? f.DirV : f.DirU;
            var shifts = new List<XYZ> { XYZ.Zero, side * r, side * -r, XYZ.BasisZ * r, XYZ.BasisZ * -r };
            foreach (Curve cv in curves)
            {
                if (cv.Length < MinSeg) continue;
                foreach (XYZ sh in shifts)
                {
                    Curve probe = sh.IsZeroLength() ? cv : cv.CreateTransformed(Transform.CreateTranslation(sh));
                    if (!CurveInside(solids, probe, out double outside))
                    {
                        why = "queda fuera del hormigon (" + ToMm(outside) + " mm de barra fuera; segmento de " +
                              f.LocalMm(cv.GetEndPoint(0)) + " a " + f.LocalMm(cv.GetEndPoint(1)) + ")";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Longitud de la curva que queda fuera del hormigon: se unen los tramos interiores de
        /// todos los solidos (por sus parametros, para no contar dos veces un solapamiento
        /// entre solidos) y se resta de la longitud total. No verificable cuenta como fuera.
        /// </summary>
        private static bool CurveInside(List<Solid> solids, Curve cv, out double outsideLen)
        {
            outsideLen = cv.Length;
            double p0 = cv.GetEndParameter(0), p1 = cv.GetEndParameter(1);
            if (p1 - p0 < 1e-12) { outsideLen = 0; return true; }
            var parts = new List<(double a, double b)>();
            bool any = false;
            foreach (Solid solid in solids)
            {
                try
                {
                    var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                    SolidCurveIntersection ix = solid.IntersectWithCurve(cv, opt);
                    any = true;
                    if (ix == null) continue;
                    for (int i = 0; i < ix.SegmentCount; i++)
                    {
                        CurveExtents ex = ix.GetCurveSegmentExtents(i);
                        parts.Add((Math.Min(ex.StartParameter, ex.EndParameter), Math.Max(ex.StartParameter, ex.EndParameter)));
                    }
                }
                catch { }
            }
            if (!any) return false;
            double inside = Geometry2D.Merge(parts, 0).Sum(p => p.b - p.a) / (p1 - p0) * cv.Length;
            outsideLen = Math.Max(0, cv.Length - inside);
            return outsideLen <= InsideTol;
        }

        // =================================================================
        // Utilidades
        // =================================================================
        private static void AddLine(List<Curve> list, XYZ a, XYZ b)
        {
            if (a.DistanceTo(b) > MinSeg) list.Add(Line.CreateBound(a, b));
        }

        private static Rebar Create(Document doc, Element host, RebarBarType bt, XYZ normal, IList<Curve> curves,
                                    ElementId hookStart, ElementId hookEnd, bool hookLeft, out string err)
        {
            err = null;
            try
            {
                // Revit 2027: ganchos y tratamientos de extremo van agrupados en BarTerminationsData.
                using (BarTerminationsData term = new BarTerminationsData(doc))
                {
                    if (hookStart != null && hookStart != ElementId.InvalidElementId) term.HookTypeIdAtStart = hookStart;
                    if (hookEnd != null && hookEnd != ElementId.InvalidElementId) term.HookTypeIdAtEnd = hookEnd;
                    RebarTerminationOrientation o = hookLeft ? RebarTerminationOrientation.Left : RebarTerminationOrientation.Right;
                    term.TerminationOrientationAtStart = o;
                    term.TerminationOrientationAtEnd = o;
                    return Rebar.CreateFromCurves(doc, RebarStyle.Standard, bt, host, normal.Normalize(), curves, term, true, true);
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Sobrescribe la longitud total de los ganchos de esta barra (Revit: "Sobrescribir longitudes de gancho"), sin tocar
        /// la tabla de longitudes del tipo de barra. Al activar la sobrescritura Revit deja modificables los parametros de
        /// longitud de gancho de la barra: los de formula que su forma asocia a cada extremo (GetOverridableHookParameters) y
        /// los predefinidos "Longitud de gancho inicial/final". Se escribe en el primero que lo admita de cada extremo y
        /// despues se regenera y se vuelve a leer la longitud del gancho de la barra para comprobar que ha cambiado lo que
        /// debia respecto a la predeterminada. False, con el motivo, si Revit no deja ninguno modificable, rechaza el valor o
        /// no cambia nada.
        /// </summary>
        private static bool ApplyHookLength(Ctx c, Rebar rb, BarLayer layer, bool start, bool end, double len, out string err)
        {
            err = null;
            Document doc = c.Doc;
            double same = Mm(HookLengthRule.SameMm);
            double def = c.HookDefaults.TryGetValue(layer, out double d0) ? d0 : double.NaN;
            try
            {
                rb.EnableHookLengthOverride(true);
                doc.Regenerate();

                ISet<ElementId> startIds = null, endIds = null;
                try { rb.GetOverridableHookParameters(out startIds, out _, out endIds, out _); } catch { }

                var ends = new List<(int Index, ISet<ElementId> ShapeIds, BuiltInParameter Builtin)>();
                if (start) ends.Add((0, startIds, BuiltInParameter.REBAR_SHAPE_START_HOOK_LENGTH));
                if (end) ends.Add((1, endIds, BuiltInParameter.REBAR_SHAPE_END_HOOK_LENGTH));

                var before = new Dictionary<int, double>();
                foreach (var e in ends)
                {
                    before[e.Index] = HookLengthAt(rb, e.Index);
                    var tried = new List<string>();
                    if (!WriteHookLength(doc, rb, e.ShapeIds, e.Builtin, len, tried))
                    {
                        err = "Revit no deja modificable ningun parametro de longitud de gancho en el extremo " + EndName(e.Index) + " de esta barra " +
                              (tried.Count > 0 ? "(" + string.Join("; ", tried) + ")" : "(su forma de barra no define ninguno)");
                        return false;
                    }
                }

                doc.Regenerate();
                foreach (var e in ends)
                {
                    // Se compara el cambio (despues - antes) con el esperado (pedida - predeterminada): asi da igual si Revit
                    // reporta la longitud total del gancho o solo su prolongacion recta.
                    double after = HookLengthAt(rb, e.Index), was = before[e.Index];
                    if (double.IsNaN(after) || double.IsNaN(was)) continue;   // Revit no la deja leer: no se puede comprobar
                    double delta = after - was;
                    double expected = double.IsNaN(def) ? len - was : len - def;
                    if (Math.Abs(delta) < same && Math.Abs(expected) >= same)
                    {
                        err = "la longitud del gancho " + EndName(e.Index) + " no cambio al sobrescribirla (sigue en " + ToMm(was) + " mm)";
                        return false;
                    }
                    if (Math.Abs(delta - expected) >= same && c.HookLengthNoted.Add(layer))
                        c.Result.Warnings.Add(Layers.Name(layer) + ": longitud de gancho pedida " + ToMm(len) + " mm; el gancho ha cambiado " + ToMm(delta) +
                                              " mm respecto a la predeterminada en vez de " + ToMm(expected) + " mm (redondeo de armadura del proyecto)");
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        private static string EndName(int end) => end == 0 ? "inicial" : "final";

        /// <summary>
        /// Escribe la longitud en el primer parametro modificable de los dados: los de la forma de barra y, si no, el
        /// predefinido. Anota en "tried" los que no lo admitieron y por que.
        /// </summary>
        private static bool WriteHookLength(Document doc, Rebar rb, ISet<ElementId> shapeIds, BuiltInParameter builtin, double len, List<string> tried)
        {
            var candidates = new List<Parameter>();
            if (shapeIds != null) foreach (ElementId id in shapeIds) candidates.Add(ParamOf(doc, rb, id));
            try { candidates.Add(rb.get_Parameter(builtin)); } catch { }
            foreach (Parameter p in candidates)
            {
                if (p == null) continue;
                string pname = p.Definition?.Name ?? "?";
                if (p.IsReadOnly) { tried.Add(pname + ": solo lectura"); continue; }
                try
                {
                    if (p.Set(len)) return true;
                    tried.Add(pname + ": Revit rechazo el valor");
                }
                catch (Exception ex) { tried.Add(pname + ": " + ex.Message); }
            }
            return false;
        }

        /// <summary>Parametro de la barra con ese id: predefinido (id negativo) o de proyecto/compartido (ParameterElement).</summary>
        private static Parameter ParamOf(Document doc, Rebar rb, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return null;
            try
            {
                if (id.Value < 0) return rb.get_Parameter((BuiltInParameter)id.Value);
                var pe = doc.GetElement(id) as ParameterElement;
                Definition def = pe?.GetDefinition();
                return def == null ? null : rb.get_Parameter(def);
            }
            catch { return null; }
        }

        /// <summary>
        /// Longitud del gancho (pies) en el extremo dado (0 inicial, 1 final) tal como la lleva la barra: el parametro predefinido
        /// "Longitud de gancho inicial/final" o, si no lo tiene, la de sus datos de doblado. NaN si Revit no la da.
        /// </summary>
        private static double HookLengthAt(Rebar rb, int end)
        {
            try
            {
                Parameter p = rb.get_Parameter(end == 0 ? BuiltInParameter.REBAR_SHAPE_START_HOOK_LENGTH : BuiltInParameter.REBAR_SHAPE_END_HOOK_LENGTH);
                if (p != null && p.StorageType == StorageType.Double && p.HasValue) return p.AsDouble();
            }
            catch { }
            try
            {
                RebarBendData bd = rb.GetBendData();
                return end == 0 ? bd.HookLength0 : bd.HookLength1;
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// Marca un conjunto recien creado segun el contrato ARBA: Particion ("CIMIENTOS - ZAP-Z1", escrita en el
        /// parametro predefinido con respaldo por nombre en espanol e ingles), "ARBA - Origen" = ZAPATAS,
        /// "ARBA - Codigo" = capa (inferior, inferior-sec, superior, superior-sec) y "Metrado - Elemento" = la
        /// categoria del anfitrion. Con el origen el add-in reconoce lo suyo al rearmar (borrar y rearmar).
        /// Los parametros compartidos los asegura el comando antes de armar (ArbaSharedParams.Ensure).
        /// </summary>
        private static void Finish(Document doc, Rebar r, Element host, string partition, BarLayer layer)
        {
            ArbaPartition.Write(r, partition);
            ArbaOrigin.WriteFor(r, host, ArbaContract.Zapatas, Layers.Short(layer));
            try { r.SetUnobscuredInView(doc.ActiveView, true); } catch { }
        }

        public static RebarBarType FindBarType(Document doc, string name, string use)
        {
            var all = AllBarTypes(doc);
            if (all.Count == 0)
                throw new InvalidOperationException("El proyecto no tiene ningun tipo de barra (RebarBarType). Carga una familia de armadura primero.");
            string match = NameMatch.First(all.Select(b => b.Name), name);
            if (match == null)
                throw new InvalidOperationException("el tipo de barra de " + use + " \"" + name + "\" no existe en este proyecto; elige uno de los cargados en la ventana");
            return all.First(b => b.Name == match);
        }

        /// <summary>
        /// Catalogo de ganchos estandar para barras longitudinales (90 grados con prolongacion de 12 diametros y
        /// 180 grados con 4). La ventana ofrece los de este catalogo cuyo angulo no tenga el proyecto entre sus
        /// ganchos de estilo Estandar, y el tipo se crea en el proyecto al armar (FindHookType).
        /// </summary>
        public static readonly (string Name, double AngleDeg, double Multiplier)[] HookCatalog =
        {
            ("Estandar - 90", 90, 12),
            ("Estandar - 180", 180, 4),
        };

        /// <summary>
        /// Id del tipo de gancho, o InvalidElementId si el nombre esta vacio. Si el proyecto no lo tiene pero es
        /// del catalogo, lo crea (se llama dentro de la transaccion del armado) y lo anota en "notes". Lanza si
        /// el nombre no existe.
        /// </summary>
        public static ElementId FindHookType(Document doc, string name, List<string> notes = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            var all = AllHookTypes(doc);
            string match = NameMatch.First(all.Select(h => h.Name), name);
            if (match != null) return all.First(h => h.Name == match).Id;

            string entry = NameMatch.First(HookCatalog.Select(e => e.Name), name);
            if (entry != null) return CreateCatalogHook(doc, HookCatalog.First(e => e.Name == entry), notes);

            // Puede que el nombre exista pero sea un gancho de estribo/tirante: Revit no lo admite
            // en barras de estilo Estandar (falla con "internal error" al crear la barra), asi que se avisa claro.
            string other = NameMatch.First(HookTypesOf(doc).Select(h => h.Name), name);
            if (other != null)
                throw new InvalidOperationException("el tipo de gancho \"" + other + "\" es de estilo Estribo/Tirante y Revit no lo admite en barras longitudinales de zapata (estilo Estandar); elige un gancho de estilo Estandar (p. ej. \"Estandar - 90\") o deja el gancho vacio");
            throw new InvalidOperationException("el tipo de gancho \"" + name + "\" no existe en este proyecto; elige uno de los cargados en la ventana o deja el gancho vacio");
        }

        /// <summary>Crea en el proyecto el gancho Estandar del catalogo, admitido por todos los tipos de barra.</summary>
        private static ElementId CreateCatalogHook(Document doc, (string Name, double AngleDeg, double Multiplier) e, List<string> notes)
        {
            RebarHookType h = RebarHookType.Create(doc, e.AngleDeg * Math.PI / 180, e.Multiplier);
            try { h.Style = RebarStyle.Standard; } catch { }
            try { h.Name = e.Name; } catch { }
            foreach (RebarBarType bt in AllBarTypes(doc))
                try { if (!bt.GetHookPermission(h.Id)) bt.SetHookPermission(h.Id, true); } catch { }
            notes?.Add("creado en el proyecto el tipo de gancho \"" + h.Name + "\" (Estandar, " + e.AngleDeg + " grados, prolongacion " + e.Multiplier + " diametros)");
            return h.Id;
        }

        // La coincidencia de nombres de tipo (exacta, si no el primero que contiene el fragmento; nunca se sustituye
        // por otro tipo) es ahora NameMatch.First del codigo comun ARBA; la ventana avisa con NameMatch.IsAmbiguous
        // cuando un fragmento coincide con varios tipos.

        public static List<RebarBarType> AllBarTypes(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Tipos de gancho de estilo Estandar, los unicos que Revit admite en las barras de zapata
        /// (todas se crean con RebarStyle.Standard). Los de estilo Estribo/Tirante se excluyen:
        /// si se asignan a una barra Estandar, Revit falla al crearla ("An internal error has occurred").
        /// </summary>
        public static List<RebarHookType> AllHookTypes(Document doc) =>
            HookTypesOf(doc).Where(IsStandardHook)
                .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Todos los tipos de gancho del proyecto, de cualquier estilo.</summary>
        private static List<RebarHookType> HookTypesOf(Document doc)
        {
            var list = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();
            // respaldo: recorrer los tipos del proyecto por si el filtro por clase no los devuelve
            if (list.Count == 0) list = new FilteredElementCollector(doc).WhereElementIsElementType().OfType<RebarHookType>().ToList();
            return list;
        }

        /// <summary>True si el gancho es de estilo Estandar (si la API no lo dice, se admite).</summary>
        public static bool IsStandardHook(RebarHookType h)
        {
            try { return h.Style == RebarStyle.Standard; } catch { return true; }
        }
    }
}
