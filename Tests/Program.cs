using System;
using System.Collections.Generic;
using System.Linq;
using Arba.Comun;

namespace FootingRebar.Tests
{
    /// <summary>Pruebas de consola de las clases puras. Imprime OK/FALLO por comprobacion y termina con codigo 1 si algo falla.</summary>
    internal static class Program
    {
        private static int _fail, _ok;
        private const double Ft = 304.8;
        private static double Mm(double mm) => mm / Ft;
        private static double ToMm(double ft) => Math.Round(ft * Ft, 1);

        private static void Check(bool cond, string what)
        {
            if (cond) { _ok++; Console.WriteLine("  OK    " + what); }
            else { _fail++; Console.WriteLine("  FALLO " + what); }
        }

        private static void Near(double a, double bMm, string what, double tolMm = 0.5) =>
            Check(Math.Abs(ToMm(a) - bMm) <= tolMm, what + " = " + ToMm(a) + " mm (esperado " + bMm + ")");

        private static Outline2D Rect(double wMm, double dMm, params List<Pt>[] holes) =>
            new Outline2D(new List<Pt> { new Pt(0, 0), new Pt(Mm(wMm), 0), new Pt(Mm(wMm), Mm(dMm)), new Pt(0, Mm(dMm)) }, holes, Mm(2));

        private static List<Pt> Box(double u1, double v1, double u2, double v2) =>
            new List<Pt> { new Pt(Mm(u1), Mm(v1)), new Pt(Mm(u2), Mm(v1)), new Pt(Mm(u2), Mm(v2)), new Pt(Mm(u1), Mm(v2)) };

        /// <summary>Diametros de 1/2" (12.7 mm) en todas las capas; retranqueo por gancho de 3 d + d (doblado 6 d) = 50.8 mm.</summary>
        private static PlanDiameters Diam(double hookInsetMm = 50.8)
        {
            var d = new PlanDiameters();
            foreach (BarLayer l in Layers.All) d.Set(l, Mm(12.7), Mm(hookInsetMm));
            return d;
        }

        private static int Main()
        {
            Console.WriteLine("== Geometry2D ==");
            Geometry();
            Console.WriteLine("== Zapata aislada 2.0 x 3.0 m ==");
            Isolated();
            Console.WriteLine("== Ganchos ==");
            Hooks();
            Console.WriteLine("== Parrilla superior y zapata escalonada ==");
            TopMesh();
            Console.WriteLine("== Zapata con hueco (pase) ==");
            Hole();
            Console.WriteLine("== Zapata en L (combinada) ==");
            LShape();
            Console.WriteLine("== Zapata corrida 0.6 x 12 m ==");
            Strip();
            Console.WriteLine("== Config y particion ==");
            ConfigAndPartition();
            Console.WriteLine();
            Console.WriteLine(_ok + " comprobaciones correctas, " + _fail + " fallos");
            return _fail == 0 ? 0 : 1;
        }

        private static void Geometry()
        {
            var sq = Box(0, 0, 1000, 1000);
            Check(Geometry2D.SignedArea(sq) > 0, "cuadrado antihorario tiene area positiva");
            Near(Geometry2D.Centroid(sq).U, 500, "centroide u");

            List<double> pos = Geometry2D.Positions(0, Mm(1000), Mm(300), Mm(2));
            Check(pos.Count == 5, "Positions 0..1000 @300 -> 5 posiciones (" + pos.Count + ")");
            Near(pos[1], 250, "paso real 250");

            Outline2D r = Rect(2000, 3000);
            List<Span> sp = r.Cut(true, Mm(1000), Mm(80), Mm(2));
            Check(sp.Count == 1 && !sp[0].HoleA && !sp[0].HoleB, "corte de rectangulo: un tramo exterior");
            Near(sp[0].A, 0, "tramo A"); Near(sp[0].B, 2000, "tramo B");
            Check(r.Cut(true, Mm(50), Mm(80), Mm(2)).Count == 0, "franja fuera del borde -> sin tramo");

            Outline2D h = Rect(2000, 3000, Box(800, 1200, 1200, 1600));
            sp = h.Cut(true, Mm(1400), Mm(30), Mm(2));
            Check(sp.Count == 2, "corte por el hueco: dos tramos (" + sp.Count + ")");
            if (sp.Count == 2)
            {
                Near(sp[0].B, 800, "fin del primer tramo en el hueco"); Check(sp[0].HoleB && !sp[0].HoleA, "marcas de hueco tramo 1");
                Near(sp[1].A, 1200, "inicio del segundo tramo"); Check(sp[1].HoleA && !sp[1].HoleB, "marcas de hueco tramo 2");
            }
            sp = h.Cut(false, Mm(1000), Mm(30), Mm(2));
            Check(sp.Count == 2, "corte u=1000 por el hueco: dos tramos (" + sp.Count + ")");
            Check(!h.Contains(new Pt(Mm(1000), Mm(1400))), "punto en el hueco no esta dentro");
            Check(h.Contains(new Pt(Mm(300), Mm(300))), "punto en la zapata esta dentro");

            var merged = Geometry2D.Merge(new[] { (0.0, 1.0), (0.5, 2.0), (3.0, 4.0) }, 0);
            Check(merged.Count == 2 && Math.Abs(merged[0].b - 2.0) < 1e-9, "Merge une solapes");

            Pt dir = Geometry2D.LongestEdgeDirection(Box(0, 0, 3000, 1000));
            Check(Math.Abs(dir.U - 1) < 1e-9, "borde mas largo en u");
        }

        private static AppConfig Cfg()
        {
            var c = new AppConfig();
            c.Normalize();
            c.Bottom.Main.BarTypeName = "1/2"; c.Bottom.Secondary.BarTypeName = "1/2";
            c.Top.Main.BarTypeName = "1/2"; c.Top.Secondary.BarTypeName = "1/2";
            return c;
        }

        private static void Isolated()
        {
            AppConfig c = Cfg();
            // u = lado largo (3.0 m), v = lado corto (2.0 m); canto 600; recubrimientos 75 / 50 / 75
            FootingPlan p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            Check(!p.HasTop, "sin parrilla superior por defecto");
            // principal: repartida en v entre 75 + 6.35 y 2000 - 81.35 -> 1837.3 / 200 -> 10 huecos -> 11 barras
            Check(p.CountOf(BarLayer.BottomMain) == 11, "inferior principal @200 en 2.0 m -> 11 barras (" + p.CountOf(BarLayer.BottomMain) + ")");
            PlannedBar b = p.Bars.First(x => x.Layer == BarLayer.BottomMain);
            Check(b.AlongU, "la principal va a lo largo de u");
            Near(b.Start, 75, "principal empieza al recubrimiento lateral"); Near(b.End, 2925, "y termina al recubrimiento lateral");
            Near(b.Z, 75 + 6.35, "cota de la principal: recubrimiento inferior + medio diametro");
            Near(b.Coord, 81.35, "primera principal al recubrimiento lateral + medio diametro");
            Check(!b.HookStart && !b.HookEnd, "sin gancho (no se ha elegido tipo de gancho)");
            // secundaria: repartida en u entre 81.35 y 2918.65 -> 2837.3 / 200 -> 15 huecos -> 16 barras
            Check(p.CountOf(BarLayer.BottomSecondary) == 16, "inferior secundaria @200 en 3.0 m -> 16 barras (" + p.CountOf(BarLayer.BottomSecondary) + ")");
            PlannedBar s = p.Bars.First(x => x.Layer == BarLayer.BottomSecondary);
            Check(!s.AlongU, "la secundaria va a lo largo de v");
            Near(s.Z, 75 + 12.7 + 6.35, "cota de la secundaria: encima de la principal");
            Near(s.Start, 75, "secundaria empieza al recubrimiento"); Near(s.End, 1925, "y termina al recubrimiento");
            Check(p.Groups.Count == 2, "2 conjuntos (uno por capa) (" + p.Groups.Count + ")");
            BarGroup g = p.Groups.First(x => x.Layer == BarLayer.BottomMain);
            Check(g.Count == 11, "el conjunto principal tiene las 11 barras");
            Near(g.Spacing, 1837.3 / 10, "paso real del conjunto principal", 0.6);

            // separaciones distintas
            c.Bottom.Main.SpacingMm = 150; c.Bottom.Secondary.SpacingMm = 250;
            p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam());
            Check(p.CountOf(BarLayer.BottomMain) == 14, "principal @150 -> 14 (" + p.CountOf(BarLayer.BottomMain) + ")");
            Check(p.CountOf(BarLayer.BottomSecondary) == 13, "secundaria @250 -> 13 (" + p.CountOf(BarLayer.BottomSecondary) + ")");

            // sin secundaria
            c.Bottom.Secondary.Enabled = false;
            p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam());
            Check(p.CountOf(BarLayer.BottomSecondary) == 0 && p.Groups.Count == 1, "sin secundaria: un solo conjunto");

            // canto insuficiente
            p = FootingPlan.Build(Rect(3000, 2000), null, Mm(130), c, Diam());
            Check(p.Error != null, "canto 130 mm con recubrimientos 75 + 50 rechazado: " + p.Error);
        }

        private static void Hooks()
        {
            AppConfig c = Cfg();
            c.Bottom.Main.HookTypeName = "90"; c.Bottom.Secondary.HookTypeName = "90";
            FootingPlan p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            PlannedBar b = p.Bars.First(x => x.Layer == BarLayer.BottomMain);
            Check(b.HookStart && b.HookEnd, "ganchos en los dos extremos exteriores");
            // tramo recto retranqueado el radio exterior del doblez (50.8) para que el gancho guarde el recubrimiento
            Near(b.Start, 75 + 50.8, "tramo recto empieza a recubrimiento + radio exterior del doblez");
            Near(b.End, 3000 - 75 - 50.8, "y termina igual por el otro lado");
            Check(p.DescribeLayers().Contains("gancho"), "el desglose menciona el gancho");
            // sin retranqueo (tipo sin diametro de doblado conocido -> 0)
            p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam(0));
            Near(p.Bars.First(x => x.Layer == BarLayer.BottomMain).Start, 75, "sin retranqueo el tramo recto empieza al recubrimiento");
            // el gancho no cambia el numero de barras ni los conjuntos
            Check(p.CountOf(BarLayer.BottomMain) == 11 && p.Groups.Count == 2, "mismo numero de barras y conjuntos con gancho");
            // longitud de gancho elegida: llega al plan (esquema de la seccion) solo en las capas con gancho y no mueve las barras
            c.Bottom.Main.HookLengthMm = 250;
            c.Bottom.Secondary.HookTypeName = "";
            c.Bottom.Secondary.HookLengthMm = 300;
            p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam());
            Near(p.HookLength[BarLayer.BottomMain], 250, "longitud de gancho de la inferior principal");
            Check(p.HookLength[BarLayer.BottomSecondary] == 0, "sin gancho elegido no hay longitud de gancho");
            Near(p.Bars.First(x => x.Layer == BarLayer.BottomMain).Start, 75 + 50.8, "la longitud de gancho no cambia el retranqueo");
        }

        private static void TopMesh()
        {
            AppConfig c = Cfg();
            c.Top.Enabled = true;
            c.Top.Main.HookTypeName = "90";
            FootingPlan p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            Check(p.HasTop, "lleva parrilla superior");
            Check(p.CountOf(BarLayer.TopMain) == 11 && p.CountOf(BarLayer.TopSecondary) == 16, "superior: 11 principales y 16 secundarias (" +
                  p.CountOf(BarLayer.TopMain) + ", " + p.CountOf(BarLayer.TopSecondary) + ")");
            PlannedBar t = p.Bars.First(x => x.Layer == BarLayer.TopMain);
            Near(t.Z, 600 - 50 - 6.35, "cota de la superior principal: canto - recubrimiento superior - medio diametro");
            Check(t.HookStart && t.HookEnd, "ganchos en la superior principal");
            PlannedBar ts = p.Bars.First(x => x.Layer == BarLayer.TopSecondary);
            Near(ts.Z, 600 - 50 - 12.7 - 6.35, "cota de la superior secundaria: colgada debajo");
            Check(!ts.HookStart, "superior secundaria recta");
            Check(p.Groups.Count == 4, "4 conjuntos (" + p.Groups.Count + ")");

            // sin secundaria superior
            c.Top.Secondary.Enabled = false;
            p = FootingPlan.Build(Rect(3000, 2000), null, Mm(600), c, Diam());
            Check(p.CountOf(BarLayer.TopSecondary) == 0 && p.CountOf(BarLayer.TopMain) == 11, "superior solo principal");
            c.Top.Secondary.Enabled = true;

            // las dos parrillas se solapan en un canto pequeno
            p = FootingPlan.Build(Rect(3000, 2000), null, Mm(190), c, Diam());
            Check(p.Error != null && p.Error.Contains("solapan"), "canto 190 con dos parrillas rechazado: " + p.Error);

            // zapata escalonada: la parrilla superior se reparte en la plataforma superior (menor)
            Outline2D top = new Outline2D(Box(750, 500, 2250, 1500), null, Mm(2));
            p = FootingPlan.Build(Rect(3000, 2000), top, Mm(800), c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "escalonada sin error: " + p.Error);
            Check(p.CountOf(BarLayer.BottomMain) == 11, "la parrilla inferior sigue en la cara inferior (" + p.CountOf(BarLayer.BottomMain) + ")");
            // superior principal repartida en v entre 500 + 81.35 y 1500 - 81.35 -> 837.3 / 200 -> 5 huecos -> 6 barras
            Check(p.CountOf(BarLayer.TopMain) == 6, "superior principal en la plataforma 1.5 x 1.0: 6 barras (" + p.CountOf(BarLayer.TopMain) + ")");
            t = p.Bars.First(x => x.Layer == BarLayer.TopMain);
            Near(t.Start, 750 + 75 + 50.8, "superior principal empieza en la plataforma + recubrimiento + doblez");
            Near(t.End, 2250 - 75 - 50.8, "y termina en el otro borde de la plataforma");
            Check(p.Bars.Where(x => Layers.IsTop(x.Layer)).All(x => top.Contains(new Pt(x.AlongU ? 0.5 * (x.Start + x.End) : x.Coord, x.AlongU ? x.Coord : 0.5 * (x.Start + x.End)))),
                  "todas las barras superiores dentro de la plataforma");
        }

        private static void Hole()
        {
            AppConfig c = Cfg();
            c.Bottom.Main.HookTypeName = "90"; c.Bottom.Secondary.HookTypeName = "90";
            Outline2D h = Rect(3000, 2000, Box(1300, 800, 1700, 1200));
            FootingPlan p = FootingPlan.Build(h, null, Mm(600), c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            // principales que cruzan el hueco (v entre 800 y 1200, con franja de 81.35): se parten en dos
            List<PlannedBar> cut = p.Bars.Where(x => x.Layer == BarLayer.BottomMain && x.Coord > Mm(700) && x.Coord < Mm(1300)).ToList();
            Check(cut.Count > 0 && cut.Count % 2 == 0, "las principales que cruzan el hueco se parten en dos (" + cut.Count + ")");
            PlannedBar left = cut.OrderBy(x => x.Start).First();
            Near(left.End, 1300 - 75, "la barra para al recubrimiento del hueco, recta");
            Check(!left.HookEnd && left.HookStart, "sin gancho en el hueco, con gancho en el borde exterior");
            PlannedBar right = cut.OrderByDescending(x => x.End).First();
            Near(right.Start, 1700 + 75, "la otra mitad empieza al recubrimiento del hueco");
            Check(p.CountOf(BarLayer.BottomMain) > 11, "mas barras principales que sin hueco (" + p.CountOf(BarLayer.BottomMain) + ")");
            Check(p.Bars.All(b => h.Contains(new Pt(b.AlongU ? 0.5 * (b.Start + b.End) : b.Coord, b.AlongU ? b.Coord : 0.5 * (b.Start + b.End)))), "todas las barras dentro del hormigon");
        }

        private static void LShape()
        {
            AppConfig c = Cfg();
            var outer = new List<Pt>
            {
                new Pt(0, 0), new Pt(Mm(5000), 0), new Pt(Mm(5000), Mm(2000)), new Pt(Mm(2000), Mm(2000)), new Pt(Mm(2000), Mm(5000)), new Pt(0, Mm(5000))
            };
            var o = new Outline2D(outer, null, Mm(2));
            Near(o.Width, 5000, "ancho de la L"); Near(o.Depth, 5000, "fondo de la L");
            FootingPlan p = FootingPlan.Build(o, null, Mm(700), c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            PlannedBar lowV = p.Bars.First(x => x.Layer == BarLayer.BottomMain && x.Coord < Mm(1900));
            PlannedBar highV = p.Bars.First(x => x.Layer == BarLayer.BottomMain && x.Coord > Mm(2100));
            Near(lowV.End, 4925, "barra larga en el ala"); Near(highV.End, 1925, "barra corta en el alma");
            Check(p.GroupsOf(BarLayer.BottomMain) == 2, "dos conjuntos de inferiores principales (" + p.GroupsOf(BarLayer.BottomMain) + ")");
            Check(p.Bars.All(b => o.Contains(new Pt(b.AlongU ? 0.5 * (b.Start + b.End) : b.Coord, b.AlongU ? b.Coord : 0.5 * (b.Start + b.End)))), "todas las barras dentro de la L");
        }

        private static void Strip()
        {
            AppConfig c = Cfg();
            c.Bottom.Main.SpacingMm = 200; c.Bottom.Secondary.SpacingMm = 250;
            // zapata corrida: u = lado largo (12 m), las principales son las longitudinales
            FootingPlan p = FootingPlan.Build(Rect(12000, 600), null, Mm(400), c, Diam());
            Console.WriteLine("  " + p.Describe() + " | " + p.DescribeLayers());
            Check(p.Error == null, "sin error: " + p.Error);
            // longitudinales repartidas en 600 - 2 x 81.35 = 437.3 -> 3 huecos -> 4 barras
            Check(p.CountOf(BarLayer.BottomMain) == 4, "4 barras longitudinales (" + p.CountOf(BarLayer.BottomMain) + ")");
            Near(p.Bars.First(x => x.Layer == BarLayer.BottomMain).Length, 12000 - 150, "longitudinal de 11.85 m");
            // transversales cada 250 en 12 m - 2 x 81.35 -> 48 huecos -> 49 barras
            Check(p.CountOf(BarLayer.BottomSecondary) == 49, "49 transversales @250 (" + p.CountOf(BarLayer.BottomSecondary) + ")");
            Near(p.Bars.First(x => x.Layer == BarLayer.BottomSecondary).Length, 600 - 150, "transversal de 450 mm");
            Check(p.Groups.Count == 2, "2 conjuntos (" + p.Groups.Count + ")");
        }

        private static void ConfigAndPartition()
        {
            var c = new AppConfig();
            c.Normalize();
            Check(c.Direction.Mode == "long" && c.Bottom.Enabled && !c.Top.Enabled, "valores por defecto");
            Check(c.CoverBottomMm == 75 && c.CoverTopMm == 50 && c.CoverEdgeMm == 75, "recubrimientos por defecto 75 / 50 / 75");
            Check(c.Bottom.Main.HookLengthMm == 0, "longitud de gancho por defecto 0 (la del tipo de barra)");
            c.Direction.Mode = "CORTO"; c.Bottom.Main.SpacingMm = -5; c.Bottom.Enabled = false; c.Top.Main.Enabled = false;
            c.Top.Secondary.HookLengthMm = -10;
            c.Normalize();
            Check(c.Direction.Mode == "short", "direccion normalizada");
            Check(c.Bottom.Main.SpacingMm == 200 && c.Bottom.Enabled && c.Top.Main.Enabled, "separacion y capas obligatorias normalizadas");
            Check(c.Top.Secondary.HookLengthMm == 0, "longitud de gancho negativa normalizada a 0");
            Check(AppConfig.NormalizeDirection("xx") == "long", "direccion desconocida -> long");
            string tmp = System.IO.Path.GetTempFileName();
            c.Save(tmp);
            string json = System.IO.File.ReadAllText(tmp);
            Check(json.Contains("\"bottom\"") && json.Contains("\"spacingMm\"") && json.Contains("\"hookTypeName\"") && json.Contains("\"hookLengthMm\""), "json en camelCase");
            AppConfig back = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Check(back != null && back.Direction.Mode == "short" && back.CoverBottomMm == 75, "ida y vuelta por json");
            AppConfig clone = c.Clone();
            clone.CoverTopMm = 99;
            Check(c.CoverTopMm != 99, "Clone es independiente");
            System.IO.File.Delete(tmp);

            string s = PartitionName.Expand("ZAP-{marca}-{capa}", new PartitionName.Source { Mark = "Z-01", Code = "inferior" });
            Check(s == "ZAP-Z-01-inferior", "particion con marca y capa ({capa} es alias de {codigo}): " + s);
            s = PartitionName.Expand("ZAP-{marca}", new PartitionName.Source { Mark = "", Id = "1234" });
            Check(s == "ZAP-1234", "particion sin marca usa el id: " + s);
            s = PartitionName.Expand("ZAP-{conjunto}", new PartitionName.Source());
            Check(s == "ZAP", "comodin vacio sin separador huerfano: " + s);

            // contrato ARBA: "{categoria} - {prefijo}-{marca}" por defecto, sin codigo de capa
            var def = new AppConfig();
            def.Normalize();
            Check(ArbaPartition.TemplateFollowsContract(def.PartitionTemplate), "la plantilla por defecto cumple el contrato ARBA: " + def.PartitionTemplate);
            Check(def.PartitionTemplate == AppConfig.DefaultPartitionTemplate, "la plantilla por defecto es la del add-in");
            def.PartitionTemplate = "   ";
            def.Normalize();
            Check(def.PartitionTemplate == AppConfig.DefaultPartitionTemplate, "plantilla vacia -> la del contrato (" + def.PartitionTemplate + ")");
            Check(!ArbaPartition.TemplateFollowsContract("ZAP-{marca}"), "la plantilla antigua ZAP-{marca} incumple el contrato");
            Check(ArbaContract.Zapatas.Prefix == "ZAP" && ArbaContract.Zapatas.Origin == "ZAPATAS", "prefijo ZAP, origen ZAPATAS");

            s = ArbaPartition.Build("CIMIENTOS", "ZAP", "Z-01", "1");
            Check(s == "CIMIENTOS - ZAP-Z-01", "particion del contrato con marca: " + s);
            s = ArbaPartition.Build("CIMIENTOS", "ZAP", "", "1234");
            Check(s == "CIMIENTOS - ZAP-1234", "particion del contrato sin marca usa el id: " + s);
            s = ArbaPartition.Build(AppConfig.DefaultPartitionTemplate, new PartitionName.Source
            {
                Category = "CIMIENTOS", Prefix = "ZAP", Mark = "Z1", Id = "77", SetName = "inferior principal", Code = "inferior"
            });
            Check(s == "CIMIENTOS - ZAP-Z1", "plantilla del add-in: la capa no entra en la particion: " + s);
            s = ArbaPartition.Build("{categoria} - {prefijo}-{marca}-{capa}", new PartitionName.Source
            {
                Category = "CIMIENTOS", Prefix = "ZAP", Mark = "Z1", Code = "superior-sec"
            });
            Check(s == "CIMIENTOS - ZAP-Z1-superior-sec", "plantilla con {capa}: " + s);

            ArbaPartitionInfo info = ArbaPartition.Parse("CIMIENTOS - ZAP-Z1");
            Check(info.Kind == ArbaPartitionKind.Contract && info.Category == "CIMIENTOS" && info.Prefix == "ZAP" && info.Mark == "Z1" && info.Code == "",
                  "Parse de la particion nueva: " + info);
            info = ArbaPartition.Parse("CIMIENTOS - ZAP-Z1-inferior-sec");
            Check(info.Mark == "Z1" && info.Code == "inferior-sec", "Parse separa la capa conocida de la marca: " + info);
            info = ArbaPartition.Parse("ZAP-Z1");
            Check(info.Kind == ArbaPartitionKind.Legacy && info.Prefix == "ZAP" && info.Mark == "Z1", "la particion antigua ZAP-Z1 se reconoce como legacy: " + info);
            Check(ArbaPartition.Upgrade(info, "CIMIENTOS") == "CIMIENTOS - ZAP-Z1", "migracion ZAP-Z1 -> CIMIENTOS - ZAP-Z1");

            Check(NameMatch.First(new[] { "#3", "#4", "1/2\"" }, "1/2") == "1/2\"", "NameMatch.First: fragmento");
            Check(NameMatch.First(new[] { "#3", "#4" }, "#4") == "#4" && NameMatch.First(new[] { "#3" }, "#9") == null, "NameMatch.First: exacto y sin coincidencia");
            Check(NameMatch.IsAmbiguous(new[] { "Gancho 90", "Gancho 90 sismico", "Gancho 135" }, "90") && NameMatch.Unique(new[] { "Gancho 90", "Gancho 135" }, "90") == "Gancho 90",
                  "NameMatch: ambiguo con dos coincidencias, unico con una");
        }
    }
}
