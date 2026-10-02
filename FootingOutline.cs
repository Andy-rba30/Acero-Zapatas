using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace FootingRebar
{
    /// <summary>Columna detectada sobre la zapata: contorno de su base en planta (coordenadas del modelo).</summary>
    public sealed class ColumnInfo
    {
        public Element Column;
        public string Name = "";
        public List<Pt> WorldRing = new List<Pt>();
        /// <summary>Cota de la base de la columna.</summary>
        public double ZBase;
    }

    /// <summary>
    /// La zapata vista en un sistema local concreto: u = direccion de las barras principales
    /// (la capa mas baja), v = perpendicular (Z x u), origen en la esquina minima del contorno
    /// inferior a la cota de la cara inferior. Incluye los contornos inferior y superior en
    /// coordenadas locales y las columnas que apoyan encima.
    /// </summary>
    public sealed class FootingFrame
    {
        public FootingOutline Footing;
        public XYZ Origin, DirU, DirV;
        /// <summary>Contorno de la cara inferior (la parrilla inferior se reparte en el).</summary>
        public Outline2D Outline;
        /// <summary>Contorno de la cara superior (igual que el inferior en una zapata prismatica).</summary>
        public Outline2D TopOutline;
        public List<ColumnFootprint> Columns = new List<ColumnFootprint>();
        public string Mode;
        public double AngleDeg;

        private readonly Dictionary<long, List<(double v, double zTop)>> _profiles = new Dictionary<long, List<(double, double)>>();

        /// <summary>Solidos contra los que se comprueban las barras.</summary>
        public List<Solid> CheckSolids => Footing.Solids;

        public double ZBottom => Footing.ZBottom;
        public double ZTop => Footing.ZTop;
        public double Thickness => Footing.Thickness;
        public double Width => Outline.Width;
        public double Depth => Outline.Depth;
        public bool Stepped => Footing.Stepped;

        public XYZ World(double u, double v, double z) =>
            new XYZ(Origin.X + DirU.X * u + DirV.X * v, Origin.Y + DirU.Y * u + DirV.Y * v, z);

        public Pt Local(XYZ p)
        {
            XYZ d = p - Origin;
            return new Pt(d.X * DirU.X + d.Y * DirU.Y, d.X * DirV.X + d.Y * DirV.Y);
        }

        public string LocalMm(XYZ p)
        {
            Pt l = Local(p);
            return "u=" + FootingOutline.ToMm(l.U) + " v=" + FootingOutline.ToMm(l.V) + " z=" + FootingOutline.ToMm(p.Z - ZBottom);
        }

        /// <summary>Texto de la direccion elegida, con el angulo de u respecto al eje X del proyecto.</summary>
        public string DirectionName
        {
            get
            {
                double deg = Math.Round(Math.Atan2(DirU.Y, DirU.X) * 180 / Math.PI);
                string name;
                switch (Mode)
                {
                    case "short": name = "lado corto"; break;
                    case "x": name = "X del proyecto"; break;
                    case "y": name = "Y del proyecto"; break;
                    case "angle": name = "angulo"; break;
                    default: name = "lado largo"; break;
                }
                return name + " (u a " + deg.ToString(CultureInfo.InvariantCulture) + " grados)";
            }
        }

        public string Describe()
        {
            return "u " + (Width * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " x v " +
                   (Depth * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m, principales segun " + DirectionName +
                   (Columns.Count > 0 ? ", " + Columns.Count + (Columns.Count == 1 ? " columna" : " columnas") : "");
        }

        /// <summary>
        /// Perfil de la seccion transversal (plano v-z) en u = uCut: cota superior del hormigon
        /// (desde la cara inferior) a lo largo de v, muestreada con rectas verticales contra los
        /// solidos reales. Asi la seccion dibuja escalones y taludes tal como estan modelados.
        /// 0 donde no hay hormigon (hueco).
        /// </summary>
        public List<(double v, double zTop)> SectionProfile(double uCut, int samples = 96)
        {
            long key = (long)Math.Round(uCut * 1000);
            if (_profiles.TryGetValue(key, out var cached)) return cached;
            var list = new List<(double, double)>();
            double step = Depth / Math.Max(1, samples);
            for (int i = 0; i <= samples; i++)
            {
                double v = Outline.VMin + i * step;
                // un pelo hacia dentro en los extremos para no muestrear justo en el filo
                double vs = i == 0 ? v + 1e-4 : i == samples ? v - 1e-4 : v;
                list.Add((v, Footing.TopAt(World(uCut, vs, 0)) is double z ? z - ZBottom : 0));
            }
            _profiles[key] = list;
            return list;
        }
    }

    /// <summary>
    /// Zapata deducida de la geometria real del elemento (cimentacion estructural: zapata
    /// aislada como instancia de familia, losa de cimentacion o zapata corrida): el canto va
    /// de la cara inferior horizontal mas baja a la cara superior horizontal mas alta; el
    /// contorno inferior sale de las caras a la cota inferior y el superior de las caras a la
    /// cota superior (en una zapata escalonada o piramidal es menor). Tambien localiza las
    /// columnas que apoyan encima. Todo en pies. Los ejes locales dependen de la direccion
    /// elegida y se calculan con Frame().
    /// </summary>
    public sealed class FootingOutline
    {
        public const double MmPerFt = 304.8;
        public static double Mm(double mm) => mm / MmPerFt;
        public static double ToMm(double ft) => Math.Round(ft * MmPerFt);

        public Element Host;
        /// <summary>Todos los solidos con volumen apreciable del elemento (una zapata escalonada puede tener varios).</summary>
        public List<Solid> Solids = new List<Solid>();
        public double ZTop, ZBottom;
        public double Thickness => ZTop - ZBottom;
        /// <summary>Anillos del contorno inferior en coordenadas del modelo (x, y).</summary>
        public List<List<Pt>> BottomRings = new List<List<Pt>>();
        /// <summary>Anillos del contorno superior en coordenadas del modelo (x, y).</summary>
        public List<List<Pt>> TopRings = new List<List<Pt>>();
        public int HoleCount;
        /// <summary>True si la cara superior es menor que la inferior (zapata escalonada o piramidal).</summary>
        public bool Stepped;
        public List<ColumnInfo> Columns = new List<ColumnInfo>();
        public string Note = "";

        public static string LastError;

        private readonly Dictionary<string, FootingFrame> _frames = new Dictionary<string, FootingFrame>();
        private double _tol;

        public string Describe()
        {
            return "canto " + ToMm(Thickness) + " mm" + (Stepped ? ", escalonada/piramidal (cara superior menor)" : "") +
                   (HoleCount > 0 ? ", " + HoleCount + (HoleCount == 1 ? " hueco" : " huecos") : "") +
                   (Columns.Count > 0 ? ", " + Columns.Count + (Columns.Count == 1 ? " columna encima" : " columnas encima") : "") + Note;
        }

        // ------------------------------------------------------------------
        // Deduccion
        // ------------------------------------------------------------------
        public static FootingOutline Probe(Document doc, Element host, AppConfig cfg)
        {
            LastError = null;
            var s = new FootingOutline { Host = host, _tol = Mm(cfg.ToleranceMm) };
            double tol = s._tol;

            List<Solid> all = AllSolids(host);
            if (all.Count == 0) { LastError = "el elemento no tiene geometria solida"; return null; }
            // se descartan los solidos despreciables (menos del 1 % del mayor)
            s.Solids = all.Where(x => x.Volume >= 0.01 * all[0].Volume).ToList();

            // --- caras horizontales de todos los solidos ---
            var ups = new List<PlanarFace>();
            var downs = new List<PlanarFace>();
            foreach (Solid sol in s.Solids)
                foreach (Face f in sol.Faces)
                {
                    if (!(f is PlanarFace pf)) continue;
                    if (pf.FaceNormal.Z > 0.999) ups.Add(pf);
                    else if (pf.FaceNormal.Z < -0.999) downs.Add(pf);
                }
            if (downs.Count == 0) { LastError = "la zapata no tiene cara inferior horizontal; solo se arman zapatas de base plana"; return null; }
            if (ups.Count == 0) { LastError = "la zapata no tiene ninguna cara superior horizontal"; return null; }
            s.ZBottom = downs.Min(f => f.Origin.Z);
            s.ZTop = ups.Max(f => f.Origin.Z);
            if (s.Thickness < Mm(100)) { LastError = "la zapata es demasiado delgada (" + ToMm(s.Thickness) + " mm)"; return null; }

            List<PlanarFace> bottomFaces = downs.Where(f => Math.Abs(f.Origin.Z - s.ZBottom) <= tol).ToList();
            List<PlanarFace> topFaces = ups.Where(f => Math.Abs(f.Origin.Z - s.ZTop) <= tol).ToList();
            double bottomArea = bottomFaces.Sum(f => f.Area), topArea = topFaces.Sum(f => f.Area);
            double otherBottom = downs.Where(f => !bottomFaces.Contains(f)).Sum(f => f.Area);
            if (otherBottom > 0.02 * bottomArea)
                s.Note += " (hay caras inferiores a distintas cotas: la parrilla inferior se reparte en la cara mas baja)";

            // --- contornos ---
            if (!Rings(bottomFaces, tol, out List<List<Pt>> bottomRings, out string err)) { LastError = "no se pudo leer el contorno inferior (" + err + ")"; return null; }
            if (!Rings(topFaces, tol, out List<List<Pt>> topRings, out err)) { LastError = "no se pudo leer el contorno superior (" + err + ")"; return null; }
            Outline2D bottom, top;
            try { bottom = new Outline2D(bottomRings, tol); top = new Outline2D(topRings, tol); }
            catch (Exception ex) { LastError = "no se pudo clasificar el contorno (" + ex.Message + ")"; return null; }
            s.BottomRings = bottom.Rings().ToList();
            s.TopRings = top.Rings().ToList();
            s.HoleCount = bottom.Holes.Count;
            s.Stepped = topArea < 0.98 * bottomArea;
            if (topArea > 1.02 * bottomArea)
                s.Note += " (la cara superior es mayor que la inferior: zapata invertida o con vuelo; la parrilla inferior se reparte en la cara inferior)";

            // --- columnas encima (se buscan siempre; la configuracion decide si se muestran) ---
            try { s.FindColumns(doc); }
            catch (Exception ex) { s.Note += " (no se pudieron leer las columnas: " + ex.Message + ")"; }
            return s;
        }

        /// <summary>Anillos (x, y) de las caras dadas, con los bordes curvos teselados.</summary>
        private static bool Rings(List<PlanarFace> faces, double tol, out List<List<Pt>> rings, out string error)
        {
            rings = new List<List<Pt>>();
            error = null;
            var loops = new List<CurveLoop>();
            try { foreach (PlanarFace f in faces) loops.AddRange(f.GetEdgesAsCurveLoops()); }
            catch (Exception ex) { error = ex.Message; return false; }
            foreach (CurveLoop loop in loops)
            {
                var pts = new List<Pt>();
                foreach (Curve c in loop)
                {
                    if (c is Line)
                        pts.Add(new Pt(c.GetEndPoint(0).X, c.GetEndPoint(0).Y));
                    else
                    {
                        IList<XYZ> tess = c.Tessellate();
                        for (int i = 0; i + 1 < tess.Count; i++) pts.Add(new Pt(tess[i].X, tess[i].Y));
                        if (tess.Count == 1) pts.Add(new Pt(tess[0].X, tess[0].Y));
                    }
                }
                pts = Geometry2D.Simplify(pts, tol);
                if (pts.Count >= 3 && Math.Abs(Geometry2D.SignedArea(pts)) > tol * tol) rings.Add(pts);
            }
            if (rings.Count == 0) { error = "contorno vacio"; return false; }
            return true;
        }

        /// <summary>
        /// Columnas (estructurales o arquitectonicas) cuya caja envolvente en planta toca la de
        /// la zapata y cuya base queda entre la cara inferior y un poco por encima de la cara
        /// superior (apoyadas encima o empotradas). El contorno de la base sale de la cara
        /// inferior horizontal de su solido; si no la hay, de la caja envolvente.
        /// </summary>
        private void FindColumns(Document doc)
        {
            double tol = _tol;
            BoundingBoxXYZ sb = Host.get_BoundingBox(null);
            if (sb == null) return;
            var filter = new LogicalOrFilter(new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns), new ElementCategoryFilter(BuiltInCategory.OST_Columns));
            var collector = new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType();
            foreach (Element e in collector)
            {
                if (!(e is FamilyInstance)) continue;
                BoundingBoxXYZ bb = e.get_BoundingBox(null);
                if (bb == null) continue;
                if (bb.Max.X < sb.Min.X - tol || bb.Min.X > sb.Max.X + tol || bb.Max.Y < sb.Min.Y - tol || bb.Min.Y > sb.Max.Y + tol) continue;
                // la base de la columna tiene que estar dentro del canto o justo encima
                if (bb.Min.Z < ZBottom - tol || bb.Min.Z > ZTop + Mm(50)) continue;
                if (bb.Max.Z < ZTop + Mm(50)) continue;   // no sube por encima de la zapata: no es una columna apoyada

                var info = new ColumnInfo { Column = e, Name = e.Name + " [" + e.Id + "]", ZBase = bb.Min.Z };
                List<Solid> solids = AllSolids(e);
                PlanarFace baseFace = null;
                foreach (Solid sol in solids)
                    foreach (Face f in sol.Faces)
                        if (f is PlanarFace pf && pf.FaceNormal.Z < -0.999 && (baseFace == null || pf.Origin.Z < baseFace.Origin.Z - 1e-6 || (Math.Abs(pf.Origin.Z - baseFace.Origin.Z) <= 1e-6 && pf.Area > baseFace.Area)))
                            baseFace = pf;
                if (baseFace != null && Rings(new List<PlanarFace> { baseFace }, tol, out List<List<Pt>> rings, out _))
                {
                    List<Pt> ring = rings.OrderByDescending(r => Math.Abs(Geometry2D.SignedArea(r))).First();
                    if (Geometry2D.SignedArea(ring) < 0) ring.Reverse();
                    info.WorldRing = ring;
                    info.ZBase = baseFace.Origin.Z;
                }
                else
                {
                    info.WorldRing = new List<Pt> { new Pt(bb.Min.X, bb.Min.Y), new Pt(bb.Max.X, bb.Min.Y), new Pt(bb.Max.X, bb.Max.Y), new Pt(bb.Min.X, bb.Max.Y) };
                }
                // la base tiene que caer en planta sobre la zapata
                Pt c = Geometry2D.Centroid(info.WorldRing);
                bool over = false;
                foreach (List<Pt> r in BottomRings) if (Geometry2D.PointInRing(r, c)) over = !over;
                if (!over) continue;
                Columns.Add(info);
            }
        }

        /// <summary>Cota superior del hormigon en la vertical del punto (x, y), o null si ahi no hay hormigon.</summary>
        public double? TopAt(XYZ xy)
        {
            XYZ a = new XYZ(xy.X, xy.Y, ZBottom - 1), b = new XYZ(xy.X, xy.Y, ZTop + 1);
            Line probe;
            try { probe = Line.CreateBound(a, b); } catch { return null; }
            double? best = null;
            foreach (Solid sol in Solids)
            {
                try
                {
                    var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                    SolidCurveIntersection ix = sol.IntersectWithCurve(probe, opt);
                    if (ix == null) continue;
                    for (int i = 0; i < ix.SegmentCount; i++)
                    {
                        Curve seg = ix.GetCurveSegment(i);
                        double z = Math.Max(seg.GetEndPoint(0).Z, seg.GetEndPoint(1).Z);
                        if (best == null || z > best.Value) best = z;
                    }
                }
                catch { }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Sistema local
        // ------------------------------------------------------------------

        /// <summary>
        /// La zapata en el sistema local de la direccion pedida ("long", "short", "x", "y" o
        /// "angle" con angleDeg). Se calcula una vez por combinacion.
        /// </summary>
        public FootingFrame Frame(string mode, double angleDeg)
        {
            mode = AppConfig.NormalizeDirection(mode);
            string key = mode == "angle" ? mode + ":" + Math.Round(angleDeg, 3).ToString(CultureInfo.InvariantCulture) : mode;
            if (_frames.TryGetValue(key, out FootingFrame cached)) return cached;

            var bottomWorld = new Outline2D(BottomRings, _tol);
            List<Pt> outerPts = bottomWorld.Outers.SelectMany(r => r).ToList();
            Pt e = Geometry2D.LongestEdgeDirection(bottomWorld.Outers);
            Pt perp = new Pt(-e.V, e.U);
            double le = Extent(outerPts, e), lp = Extent(outerPts, perp);
            Pt dir;
            switch (mode)
            {
                case "x": dir = new Pt(1, 0); break;
                case "y": dir = new Pt(0, 1); break;
                case "angle":
                    double a = angleDeg * Math.PI / 180;
                    dir = new Pt(Math.Cos(a), Math.Sin(a));
                    break;
                case "short": dir = le <= lp ? e : perp; break;
                default: dir = le >= lp ? e : perp; break;   // long: las barras principales van en la dimension mayor
            }
            var f = new FootingFrame { Footing = this, Mode = mode, AngleDeg = angleDeg };
            f.DirU = new XYZ(dir.U, dir.V, 0).Normalize();
            f.DirV = XYZ.BasisZ.CrossProduct(f.DirU).Normalize();

            // contornos en coordenadas locales, con el minimo del inferior en (0, 0)
            List<List<Pt>> local = BottomRings.Select(r => r.Select(p => ToLocal(p, f)).ToList()).ToList();
            double umin = local.SelectMany(r => r).Min(p => p.U), vmin = local.SelectMany(r => r).Min(p => p.V);
            f.Origin = new XYZ(f.DirU.X * umin + f.DirV.X * vmin, f.DirU.Y * umin + f.DirV.Y * vmin, ZBottom);
            f.Outline = new Outline2D(local.Select(r => r.Select(p => new Pt(p.U - umin, p.V - vmin)).ToList()), _tol);
            f.TopOutline = new Outline2D(TopRings.Select(r => r.Select(p => { Pt l = ToLocal(p, f); return new Pt(l.U - umin, l.V - vmin); }).ToList()), _tol);

            foreach (ColumnInfo c in Columns)
                f.Columns.Add(new ColumnFootprint
                {
                    Name = c.Name,
                    Ring = c.WorldRing.Select(p => { Pt l = ToLocal(p, f); return new Pt(l.U - umin, l.V - vmin); }).ToList()
                });
            _frames[key] = f;
            return f;
        }

        private static Pt ToLocal(Pt world, FootingFrame f) =>
            new Pt(world.U * f.DirU.X + world.V * f.DirU.Y, world.U * f.DirV.X + world.V * f.DirV.Y);

        private static double Extent(List<Pt> pts, Pt dir)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (Pt p in pts)
            {
                double t = p.U * dir.U + p.V * dir.V;
                lo = Math.Min(lo, t); hi = Math.Max(hi, t);
            }
            return hi - lo;
        }

        // ------------------------------------------------------------------
        // Solidos
        // ------------------------------------------------------------------
        private static Options GeometryOptions() =>
            new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };

        /// <summary>Todos los solidos con volumen del elemento, de mayor a menor (coordenadas del modelo).</summary>
        public static List<Solid> AllSolids(Element e)
        {
            var list = new List<Solid>();
            GeometryElement ge;
            try { ge = e.get_Geometry(GeometryOptions()); }
            catch { return list; }
            if (ge == null) return list;
            void Scan(IEnumerable<GeometryObject> objs)
            {
                foreach (GeometryObject go in objs)
                {
                    if (go is Solid sol) { if (sol.Volume > 1e-9) list.Add(sol); }
                    else if (go is GeometryInstance gi) Scan(gi.GetInstanceGeometry());
                }
            }
            Scan(ge);
            return list.OrderByDescending(x => x.Volume).ToList();
        }

        internal static string TypeNameOf(Document doc, Element e)
        {
            ElementId tid = e.GetTypeId();
            Element t = (tid != null && tid != ElementId.InvalidElementId) ? doc.GetElement(tid) : null;
            return t?.Name ?? e.Name;
        }

        internal static string FamilyNameOf(Document doc, Element e)
        {
            ElementId tid = e.GetTypeId();
            var t = (tid != null && tid != ElementId.InvalidElementId) ? doc.GetElement(tid) as ElementType : null;
            return t?.FamilyName ?? e.Category?.Name ?? "";
        }
    }
}
