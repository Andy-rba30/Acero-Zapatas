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
    /// <summary>Colores de los esquemas, compartidos por la planta, la seccion y la leyenda.</summary>
    public static class PlanColors
    {
        public static readonly Brush Concrete = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
        public static readonly Brush ConcreteTop = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));   // escalon superior
        public static readonly Brush BottomMain = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E)));      // inferior principal
        public static readonly Brush BottomSecondary = Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x3E, 0x9D))); // inferior secundaria
        public static readonly Brush TopMain = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x6C, 0x2A)));         // superior principal
        public static readonly Brush TopSecondary = Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x6F, 0xB6)));    // superior secundaria
        public static readonly Brush Column = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)));   // columna encima
        public static readonly Brush ColumnEdge = Freeze(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)));
        public static readonly Brush Soil = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xB9, 0x9A)));            // terreno (seccion)

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }

        public static Brush Of(BarLayer l)
        {
            switch (l)
            {
                case BarLayer.BottomMain: return BottomMain;
                case BarLayer.BottomSecondary: return BottomSecondary;
                case BarLayer.TopMain: return TopMain;
                default: return TopSecondary;
            }
        }
    }

    /// <summary>
    /// Esquema en planta de la zapata con su armado: hormigon de la cara inferior con sus
    /// huecos, cara superior a trazos si es menor (escalonada), columnas encima y cada barra
    /// con el color de su capa (los ganchos con una marca en los extremos). Zoom con la rueda
    /// (centrado en el cursor), desplazamiento arrastrando y doble clic para volver a encajar.
    /// Toda la geometria sale de FootingPlan, la misma clase que usa el generador.
    /// </summary>
    public sealed class PlanPreview : Canvas
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

        public PlanPreview()
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
            double newZoom = Math.Max(1, Math.Min(60, _zoom * factor));
            factor = newZoom / _zoom;
            Point m = e.GetPosition(this);
            _pan = new Vector(m.X - _x0 - (m.X - _pan.X - _x0) * factor, m.Y - _y0 - (m.Y - _pan.Y - _y0) * factor);
            _zoom = newZoom;
            if (_zoom <= 1.0001) _pan = new Vector(0, 0);
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
        private static string M(double ft) => (ft * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m";

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
            double margin = 46;
            double k = Math.Min((W - 2 * margin) / Math.Max(o.Width, 1e-6), (H - 2 * margin) / Math.Max(o.Depth, 1e-6)) * _zoom;
            // origen sin zoom (esquina inferior izquierda de la zapata); el zoom crece desde ahi y el desplazamiento se suma
            _x0 = 0.5 * (W - o.Width * k / _zoom);
            _y0 = 0.5 * (H + o.Depth * k / _zoom);
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = u => x0 + u * k;
            Func<double, double> Y = v => y0 - v * k;

            // hormigon de la cara inferior con huecos (regla par-impar)
            Geometry outlineGeo = OutlineGeometry(o, X, Y);
            Children.Add(new Path { Data = outlineGeo, Fill = PlanColors.Concrete, Stroke = Brushes.DimGray, StrokeThickness = 1.2 });

            // cara superior (escalon) si es distinta
            if (_f.Stepped)
            {
                Geometry topGeo = OutlineGeometry(_f.TopOutline, X, Y);
                Children.Add(new Path
                {
                    Data = topGeo, Fill = PlanColors.ConcreteTop, Stroke = Brushes.DimGray, StrokeThickness = 0.9, StrokeDashArray = new DoubleCollection { 4, 3 },
                    ToolTip = "Cara superior (escalon o plataforma): " + M(_f.TopOutline.Width) + " x " + M(_f.TopOutline.Depth)
                });
            }

            // columnas encima
            foreach (ColumnFootprint c in _f.Columns)
            {
                var fig = new PathFigure { StartPoint = new Point(X(c.Ring[0].U), Y(c.Ring[0].V)), IsClosed = true, IsFilled = true };
                for (int i = 1; i < c.Ring.Count; i++) fig.Segments.Add(new LineSegment(new Point(X(c.Ring[i].U), Y(c.Ring[i].V)), true));
                var geo = new PathGeometry(); geo.Figures.Add(fig);
                Children.Add(new Path
                {
                    Data = geo, Fill = PlanColors.Column, Stroke = PlanColors.ColumnEdge, StrokeThickness = 1,
                    ToolTip = "Columna encima: " + c.Name + ", " + Mm(c.UMax - c.UMin) + " x " + Mm(c.VMax - c.VMin) + " mm, centro u=" + Mm(c.Center.U) + " v=" + Mm(c.Center.V)
                });
                Text(c.Name, X(c.UMin) + 2, Y(c.VMax) + 2, PlanColors.ColumnEdge, 9);
            }

            // cotas generales
            Text(M(o.Width) + "  (u)", X(o.Width / 2) - 30, Y(0) + 20, Brushes.DimGray, 11);
            Text(M(o.Depth) + "  (v)", X(o.Width) + 6, Y(o.Depth / 2) - 8, Brushes.DimGray, 11);
            Arrow(X(0), Y(0) + 34, X(0) + 40, Y(0) + 34, Brushes.DimGray);
            Text("u (barras principales, capa mas baja)", X(0) + 44, Y(0) + 27, Brushes.DimGray, 9);

            if (_plan.Error != null)
            {
                Text(_plan.Error, 10, 10, Brushes.Firebrick, 12);
                return;
            }

            // barras: primero las inferiores, encima las superiores
            foreach (PlannedBar b in _plan.Bars.OrderBy(b => b.Z))
            {
                Brush brush = PlanColors.Of(b.Layer);
                double th = Math.Max(1.1, b.D * k);
                string tip = Layers.Name(b.Layer) + " Ø" + (b.D * FtToMm).ToString("0.#", CultureInfo.InvariantCulture) + " mm, " +
                             (b.AlongU ? "v=" : "u=") + Mm(b.Coord) + " mm, L recta=" + Mm(b.Length) + " mm (de " + Mm(b.Start) + " a " + Mm(b.End) +
                             "), cota " + Mm(b.Z) + " mm desde abajo" + (b.HookStart || b.HookEnd ? ", con gancho " + (Layers.IsTop(b.Layer) ? "hacia abajo" : "hacia arriba") : "");
                Segment(b, b.Start, b.End, X, Y, brush, th, tip);
                // marcas de gancho
                double tick = Math.Max(4, 1.5 * th);
                if (b.HookStart) Tick(b, b.Start, X, Y, brush, th, tick);
                if (b.HookEnd) Tick(b, b.End, X, Y, brush, th, tick);
            }

            // resumen
            Text(_plan.Describe() + " | " + _plan.DescribeLayers(), 8, H - 20, Brushes.DimGray, 11);
            if (_plan.Warnings.Count > 0) Text(string.Join(" | ", _plan.Warnings), 8, H - 36, Brushes.Firebrick, 11);
        }

        private static Geometry OutlineGeometry(Outline2D o, Func<double, double> X, Func<double, double> Y)
        {
            var geo = new PathGeometry { FillRule = FillRule.EvenOdd };
            foreach (List<Pt> ring in o.Rings())
            {
                var fig = new PathFigure { StartPoint = new Point(X(ring[0].U), Y(ring[0].V)), IsClosed = true, IsFilled = true };
                for (int i = 1; i < ring.Count; i++) fig.Segments.Add(new LineSegment(new Point(X(ring[i].U), Y(ring[i].V)), true));
                geo.Figures.Add(fig);
            }
            geo.Freeze();
            return geo;
        }

        private void Segment(PlannedBar b, double a0, double a1, Func<double, double> X, Func<double, double> Y, Brush brush, double th, string tip)
        {
            if (a1 - a0 <= 1e-9) return;
            Children.Add(new Line
            {
                X1 = b.AlongU ? X(a0) : X(b.Coord), Y1 = b.AlongU ? Y(b.Coord) : Y(a0),
                X2 = b.AlongU ? X(a1) : X(b.Coord), Y2 = b.AlongU ? Y(b.Coord) : Y(a1),
                Stroke = brush, StrokeThickness = th, ToolTip = tip, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat
            });
        }

        private void Tick(PlannedBar b, double at, Func<double, double> X, Func<double, double> Y, Brush brush, double th, double len)
        {
            double cx = b.AlongU ? X(at) : X(b.Coord), cy = b.AlongU ? Y(b.Coord) : Y(at);
            Children.Add(new Line
            {
                X1 = b.AlongU ? cx : cx - len, Y1 = b.AlongU ? cy - len : cy,
                X2 = b.AlongU ? cx : cx + len, Y2 = b.AlongU ? cy + len : cy,
                Stroke = brush, StrokeThickness = Math.Max(1, th * 0.8)
            });
        }

        private void Arrow(double x1, double y1, double x2, double y2, Brush brush)
        {
            Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1 });
            Children.Add(new Line { X1 = x2, Y1 = y2, X2 = x2 - 5, Y2 = y2 - 3, Stroke = brush, StrokeThickness = 1 });
            Children.Add(new Line { X1 = x2, Y1 = y2, X2 = x2 - 5, Y2 = y2 + 3, Stroke = brush, StrokeThickness = 1 });
        }

        private TextBlock Text(string s, double x, double y, Brush brush, double size, bool bold = false)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
            return t;
        }
    }
}
