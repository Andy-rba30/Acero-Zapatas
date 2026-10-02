using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FootingRebar
{
    /// <summary>
    /// Esquema de la seccion transversal de la zapata (plano v-z, perpendicular a las barras
    /// principales) cortada a media luz: el perfil real del hormigon (con sus escalones o
    /// taludes, muestreado del solido), la columna que apoya encima, cada barra principal como
    /// un circulo a su diametro y la barra secundaria mas cercana al corte como una raya a su
    /// cota con sus ganchos (hacia arriba en la parrilla inferior, hacia abajo en la superior).
    /// Rueda: zoom; arrastrar: mover; doble clic: encajar.
    /// </summary>
    public sealed class SectionPreview : Canvas
    {
        private FootingFrame _f;
        private FootingPlan _plan;
        private string _message = "Sin elemento armable";

        private double _zoom = 1;
        private Vector _pan;
        private double _x0, _y0;
        private Point _dragStart;
        private Vector _panStart;
        private bool _dragging;

        private const double FtToMm = 304.8;

        public SectionPreview()
        {
            Background = Brushes.White;
            ClipToBounds = true;
            SizeChanged += (s, e) => Redraw();
            MouseWheel += OnWheel;
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseLeave += (s, e) => { _dragging = false; ReleaseMouseCapture(); };
            Cursor = Cursors.Hand;
        }

        public void Show(FootingFrame f, FootingPlan plan)
        {
            bool changed = !ReferenceEquals(_f, f);
            _f = f; _plan = plan;
            if (changed) ResetView(); else Redraw();
        }

        public void Clear(string message)
        {
            _f = null; _plan = null; _message = message;
            Redraw();
        }

        public void ResetView()
        {
            _zoom = 1; _pan = new Vector(0, 0);
            Redraw();
        }

        // --- zoom y desplazamiento ---
        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_f == null) return;
            double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
            double newZoom = Math.Max(0.2, Math.Min(60, _zoom * factor));
            factor = newZoom / _zoom;
            Point m = e.GetPosition(this);
            _pan = new Vector(m.X - _x0 - (m.X - _pan.X - _x0) * factor, m.Y - _y0 - (m.Y - _pan.Y - _y0) * factor);
            _zoom = newZoom;
            Redraw();
            e.Handled = true;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_f == null) return;
            if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
            _dragging = true; _dragStart = e.GetPosition(this); _panStart = _pan;
            CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _pan = _panStart + (e.GetPosition(this) - _dragStart);
            Redraw();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }

        // --- dibujo ---
        private static string Mm(double ft) => Math.Round(ft * FtToMm).ToString(CultureInfo.InvariantCulture);
        private static string Dia(double ft) => (ft * FtToMm).ToString("0.#", CultureInfo.InvariantCulture);

        private void Redraw()
        {
            Children.Clear();
            double W = ActualWidth, H = ActualHeight;
            if (W < 10 || H < 10) return;
            if (_f == null || _plan == null)
            {
                Text(_message, 10, 10, Brushes.Gray, 12);
                return;
            }

            Outline2D o = _f.Outline;
            double t = _plan.Thickness;
            // por encima de la zapata se deja sitio para el arranque de la columna (medio canto)
            double above = 0.5 * t;
            double margin = 40;
            double k = Math.Min((W - 2 * margin) / Math.Max(o.Depth, 1e-6), (H - 2 * margin) / Math.Max(t + above, 1e-6)) * _zoom;
            double vc = 0.5 * (o.VMin + o.VMax);
            // origen sin zoom: el centro de la zapata en el centro del lienzo, cara inferior abajo
            _x0 = 0.5 * W - vc * k / _zoom;
            _y0 = 0.5 * (H + (t + above) * k / _zoom) - above * k / _zoom;
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = v => x0 + v * k;
            Func<double, double> Y = z => y0 - z * k;
            double uCut = 0.5 * (o.UMin + o.UMax);

            // terreno bajo la zapata
            Children.Add(new Line { X1 = 0, Y1 = Y(0), X2 = W, Y2 = Y(0), Stroke = PlanColors.Soil, StrokeThickness = 2 });
            for (double x = 0; x < W; x += 14)
                Children.Add(new Line { X1 = x, Y1 = Y(0) + 2, X2 = x - 6, Y2 = Y(0) + 8, Stroke = PlanColors.Soil, StrokeThickness = 1 });

            // perfil real del hormigon (escalones y taludes incluidos)
            List<(double v, double zTop)> profile = null;
            try { profile = _f.SectionProfile(uCut); } catch { }
            Geometry concrete = profile != null && profile.Any(p => p.zTop > 1e-6) ? ProfileGeometry(profile, X, Y) : null;
            if (concrete == null)
            {
                var slab = new Rectangle { Width = Math.Max(1, o.Depth * k), Height = Math.Max(1, t * k), Fill = PlanColors.Concrete, Stroke = Brushes.DimGray, StrokeThickness = 1.2 };
                SetLeft(slab, X(o.VMin)); SetTop(slab, Y(t));
                Children.Add(slab);
            }
            else
            {
                Children.Add(new Path { Data = concrete, Fill = PlanColors.Concrete, Stroke = Brushes.DimGray, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Miter });
            }

            // columnas que cruzan el corte: arranque esquematico por encima de la zapata
            foreach (ColumnFootprint c in _f.Columns.Where(c => c.UMin - 1e-9 <= uCut && uCut <= c.UMax + 1e-9))
            {
                double zBase = profile != null ? ProfileTop(profile, c.Center.V) : t;
                var col = new Rectangle
                {
                    Width = Math.Max(1, (c.VMax - c.VMin) * k), Height = Math.Max(1, (t + above - zBase) * k),
                    Fill = PlanColors.Column, Stroke = PlanColors.ColumnEdge, StrokeThickness = 1,
                    ToolTip = "Columna encima: " + c.Name + ", ancho en v " + Mm(c.VMax - c.VMin) + " mm"
                };
                SetLeft(col, X(c.VMin)); SetTop(col, Y(t + above));
                Children.Add(col);
                Text(c.Name, X(c.VMin) + 3, Y(t + above) + 2, PlanColors.ColumnEdge, 9);
            }

            // cotas
            Text("canto h = " + Mm(t) + " mm, ancho en v = " + Mm(o.Depth) + " mm" + (_f.Stepped ? " (escalonada/piramidal)" : ""), 8, 6, Brushes.DimGray, 10);
            Text("corte a u = " + (uCut * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m (media luz); recubrimientos inf. " +
                 Mm(_plan.CoverBottom) + ", sup. " + Mm(_plan.CoverTop) + ", lateral " + Mm(_plan.CoverEdge) + " mm", 8, H - 18, Brushes.DimGray, 10);

            if (_plan.Error != null) { Text(_plan.Error, 10, 24, Brushes.Firebrick, 12); return; }

            // barras secundarias (a lo largo de v): la mas cercana al corte de cada capa, como una raya con sus ganchos
            foreach (var layer in _plan.Bars.Where(b => !b.AlongU).GroupBy(b => b.Layer))
            {
                PlannedBar b = layer.OrderBy(x => Math.Abs(x.Coord - uCut)).First();
                Brush brush = PlanColors.Of(b.Layer);
                double th = Math.Max(1.2, b.D * k);
                Children.Add(new Line
                {
                    X1 = X(b.Start), Y1 = Y(b.Z), X2 = X(b.End), Y2 = Y(b.Z), Stroke = brush, StrokeThickness = th,
                    ToolTip = Layers.Name(b.Layer) + " Ø" + Dia(b.D) + " mm a " + Mm(b.Z) + " mm desde abajo (la mas cercana al corte, u=" + Mm(b.Coord) + ")" +
                              (b.HookStart || b.HookEnd ? ", con gancho" : "")
                });
                // ganchos: hacia arriba en la inferior, hacia abajo en la superior; pata esquematica de 12 diametros
                double leg = 12 * b.D, dir = Layers.IsTop(b.Layer) ? -1 : 1;
                if (b.HookStart) HookLeg(X(b.Start), Y(b.Z), -1, dir, leg * k, b.D * k, brush, th);
                if (b.HookEnd) HookLeg(X(b.End), Y(b.Z), 1, dir, leg * k, b.D * k, brush, th);
                Text(Layers.Name(b.Layer) + " Ø" + Dia(b.D), X(o.VMin) + 4, Y(b.Z) + (Layers.IsTop(b.Layer) ? 2 : -14), brush, 9);
            }

            // barras principales (a lo largo de u): circulos (llenos si cruzan el corte)
            foreach (PlannedBar b in _plan.Bars.Where(b => b.AlongU))
            {
                bool crosses = b.Start <= uCut && b.End >= uCut;
                double rr = Math.Max(2.2, 0.5 * b.D * k);
                Brush brush = PlanColors.Of(b.Layer);
                var e = new Ellipse
                {
                    Width = 2 * rr, Height = 2 * rr,
                    Fill = crosses ? brush : Brushes.White, Stroke = crosses ? Brushes.Black : brush, StrokeThickness = crosses ? 0.6 : 1.2,
                    ToolTip = Layers.Name(b.Layer) + " Ø" + Dia(b.D) + " mm en v=" + Mm(b.Coord) + ", cota " + Mm(b.Z) + " mm" +
                              (crosses ? "" : " (no cruza el corte: de u=" + Mm(b.Start) + " a " + Mm(b.End) + ")")
                };
                if (!crosses) e.StrokeDashArray = new DoubleCollection { 2, 1.5 };
                SetLeft(e, X(b.Coord) - rr); SetTop(e, Y(b.Z) - rr);
                Children.Add(e);
            }
        }

        /// <summary>Gancho esquematico: arco corto y pata vertical. side = -1 extremo inicial, +1 final; dir = +1 arriba, -1 abajo.</summary>
        private void HookLeg(double x, double y, int side, double dir, double legPx, double dPx, Brush brush, double th)
        {
            double r = Math.Max(2, 2 * dPx);
            // tramo horizontal corto hacia fuera (radio exterior del doblez) y pata vertical
            double xo = x + side * r;
            Children.Add(new Line { X1 = x, Y1 = y, X2 = xo, Y2 = y, Stroke = brush, StrokeThickness = th });
            Children.Add(new Line { X1 = xo, Y1 = y, X2 = xo, Y2 = y - dir * Math.Max(6, legPx), Stroke = brush, StrokeThickness = th, StrokeEndLineCap = PenLineCap.Round });
        }

        /// <summary>Poligono del perfil: cara inferior plana y cota superior muestreada; los tramos sin hormigon (huecos) bajan a cero.</summary>
        private static Geometry ProfileGeometry(List<(double v, double zTop)> profile, Func<double, double> X, Func<double, double> Y)
        {
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(X(profile[0].v), Y(0)), IsClosed = true, IsFilled = true };
            double prev = double.NaN;
            foreach ((double v, double zTop) in profile)
            {
                // escalon: antes de cambiar de cota se baja/sube en vertical para dibujar el salto recto
                if (!double.IsNaN(prev) && Math.Abs(zTop - prev) > 1e-9) fig.Segments.Add(new LineSegment(new Point(X(v), Y(prev)), true));
                fig.Segments.Add(new LineSegment(new Point(X(v), Y(zTop)), true));
                prev = zTop;
            }
            fig.Segments.Add(new LineSegment(new Point(X(profile[profile.Count - 1].v), Y(0)), true));
            geo.Figures.Add(fig);
            geo.Freeze();
            return geo;
        }

        private static double ProfileTop(List<(double v, double zTop)> profile, double v)
        {
            (double v, double zTop) best = profile[0];
            foreach (var p in profile) if (Math.Abs(p.v - v) < Math.Abs(best.v - v)) best = p;
            return best.zTop;
        }

        private TextBlock Text(string s, double x, double y, Brush brush, double size)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap };
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
            return t;
        }
    }
}
