using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Arba.Comun;

namespace FootingRebar
{
    /// <summary>
    /// Ventana previa al armado: muestra que se ha detectado en cada zapata seleccionada
    /// (canto, escalones, huecos, columnas encima, o el motivo del rechazo) y deja elegir el
    /// armado: direccion de las barras principales (general y por zapata), parrilla inferior
    /// (principal y secundaria), parrilla superior opcional, ganchos, recubrimientos y
    /// particion, con un esquema en planta y de la seccion que se redibuja con cada cambio.
    /// Los valores iniciales vienen de config.json y se pueden guardar como nuevos valores
    /// por defecto. Construida en codigo (sin XAML).
    /// </summary>
    public sealed class RebarOptionsWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly IList<string> _barTypes;
        private readonly IDictionary<string, double> _diametersMm;
        private readonly IDictionary<string, double> _hookBendMm;
        private readonly Dictionary<string, string> _typeByDisplay = new Dictionary<string, string>();
        private readonly IList<string> _hookTypes;
        private readonly IDictionary<string, double> _hookAngles;
        private readonly IList<HostAnalysis> _items;

        /// <summary>Configuracion final si el usuario pulso "Armar"; null si cancelo.</summary>
        public AppConfig Result { get; private set; }

        // direccion
        private ComboBox _dir;
        private TextBox _angle;
        // parrilla inferior
        private ComboBox _bmType, _bmHook, _bsType, _bsHook;
        private TextBox _bmSp, _bsSp, _bmHl, _bsHl;
        private CheckBox _bsOn;
        // parrilla superior
        private CheckBox _tOn, _tsOn;
        private ComboBox _tmType, _tmHook, _tsType, _tsHook;
        private TextBox _tmSp, _tsSp, _tmHl, _tsHl;
        // general
        private TextBox _coverB, _coverT, _coverE, _partition;
        private CheckBox _columns;
        private TextBlock _message, _partitionPreview, _partitionWarning, _previewCaption;
        /// <summary>Desplegables cuyo nombre configurado (fragmento) coincidia con varios tipos: se tomo el primero y se avisa en amarillo.</summary>
        private readonly HashSet<ComboBox> _ambiguous = new HashSet<ComboBox>();
        private Button _buildButton;
        private PlanPreview _plan;
        private SectionPreview _section;

        private readonly Dictionary<HostAnalysis, (System.Windows.Documents.Run kind, System.Windows.Documents.Run detail)> _itemRuns
            = new Dictionary<HostAnalysis, (System.Windows.Documents.Run, System.Windows.Documents.Run)>();
        private readonly Dictionary<HostAnalysis, Border> _itemRows = new Dictionary<HostAnalysis, Border>();
        private HostAnalysis _selected;
        private bool _building = true;
        private bool _strictTypes;

        private const string NoHook = "(sin gancho, barra recta)";
        private static readonly string[] DirModes = { "long", "short", "x", "y", "angle" };
        private static readonly string[] DirLabels = { "lado largo (lo normal)", "lado corto", "X del proyecto", "Y del proyecto", "angulo (grados)" };
        private static readonly Thickness Pad = new Thickness(4, 2, 4, 2);
        private static readonly Brush SelectedBrush = RevitTheme.Selection;
        private static readonly Brush AmbiguousBrush = Frozen(Color.FromRgb(0xE0, 0xC0, 0x4A));   // amarillo: fragmento ambiguo

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public RebarOptionsWindow(AppConfig cfg, IList<string> barTypes, IDictionary<string, double> diametersMm, IDictionary<string, double> hookBendMm,
                                  IList<string> hookTypes, IDictionary<string, double> hookAngles, IList<HostAnalysis> items)
        {
            _hookAngles = hookAngles ?? new Dictionary<string, double>();
            _cfg = cfg;
            _cfg.Normalize();
            _diametersMm = diametersMm;
            _hookBendMm = hookBendMm ?? new Dictionary<string, double>();
            _barTypes = barTypes.OrderBy(n => diametersMm.TryGetValue(n, out double mm) ? mm : 0)
                                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string n in _barTypes) _typeByDisplay[TypeDisplay(n)] = n;
            _hookTypes = hookTypes;
            _items = items;

            Title = "Armar zapatas";
            Width = 1240;
            Height = 860;
            MinWidth = 1000;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontSize = 12;

            RevitTheme.Apply(this);
            Content = BuildRoot();
            _selected = _items.FirstOrDefault(i => i.CanBuild);
            if (_selected != null) SelectItem(_selected);
            _building = false;
            Refresh();
        }

        // ------------------------------------------------------------------
        // Construccion de la interfaz
        // ------------------------------------------------------------------
        private UIElement BuildRoot()
        {
            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement elements = BuildElements();
            Grid.SetRow(elements, 0);
            root.Children.Add(elements);

            var body = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

            var left = new StackPanel();
            left.Children.Add(BuildDirection());
            left.Children.Add(BuildBottom());
            left.Children.Add(BuildTop());
            left.Children.Add(BuildGeneral());
            var scroll = new ScrollViewer
            {
                // entradas: barras de desplazamiento vertical y horizontal cuando no caben (nada queda recortado)
                Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(scroll, 0);
            body.Children.Add(scroll);

            UIElement previews = BuildPreviews();
            Grid.SetColumn(previews, 1);
            body.Children.Add(previews);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            UIElement buttons = BuildButtons();
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            return root;
        }

        private UIElement BuildElements()
        {
            int ok = _items.Count(i => i.CanBuild);
            var group = new GroupBox
            {
                Header = "Zapatas seleccionadas: " + _items.Count + " (" + ok + " armables). Haz clic en una para verla en el esquema. " +
                         "A la derecha, la direccion propia de las barras principales de cada una (general = la elegida abajo).",
                Padding = new Thickness(4)
            };
            var panel = new StackPanel();
            foreach (HostAnalysis item in _items)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                text.Inlines.Add(new System.Windows.Documents.Run(item.Tag) { FontWeight = FontWeights.Bold });
                var kindRun = new System.Windows.Documents.Run(item.Kind + ": ")
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = item.CanBuild ? RevitTheme.Ok : RevitTheme.Error
                };
                var detailRun = new System.Windows.Documents.Run(item.Detail);
                text.Inlines.Add(kindRun);
                text.Inlines.Add(detailRun);
                _itemRuns[item] = (kindRun, detailRun);
                Grid.SetColumn(text, 0);
                row.Children.Add(text);

                if (item.CanBuild)
                {
                    var side = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    HostAnalysis captured = item;
                    side.Children.Add(new TextBlock { Text = "Direccion:", Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    var dir = new ComboBox
                    {
                        Width = 170, ToolTip = "Direccion de las barras principales (la capa mas baja) de este elemento. General = la elegida abajo."
                    };
                    dir.Items.Add("(general)");
                    for (int i = 0; i < 4; i++) dir.Items.Add(DirLabels[i]);
                    int di = Array.IndexOf(DirModes, item.DirectionOverride);
                    dir.SelectedIndex = di >= 0 && di < 4 ? di + 1 : 0;
                    dir.SelectionChanged += (s, e) => { captured.DirectionOverride = dir.SelectedIndex <= 0 ? "" : DirModes[dir.SelectedIndex - 1]; Refresh(); };
                    side.Children.Add(dir);
                    Grid.SetColumn(side, 1);
                    row.Children.Add(side);
                }

                var border = new Border { Child = row, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(3) };
                if (item.CanBuild)
                {
                    border.Cursor = Cursors.Hand;
                    HostAnalysis captured = item;
                    border.MouseLeftButtonDown += (s, e) => { SelectItem(captured); Refresh(); };
                }
                _itemRows[item] = border;
                panel.Children.Add(border);
            }
            group.Content = new ScrollViewer
            {
                Content = panel, MaxHeight = 150,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return group;
        }

        private void SelectItem(HostAnalysis item)
        {
            _selected = item;
            foreach (var kv in _itemRows)
                kv.Value.Background = kv.Key == item ? SelectedBrush : Brushes.Transparent;
        }

        private UIElement BuildDirection()
        {
            var group = new GroupBox { Header = "Direccion de las barras principales", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _dir = new ComboBox { Margin = Pad };
            foreach (string l in DirLabels) _dir.Items.Add(l);
            _dir.SelectedIndex = Math.Max(0, Array.IndexOf(DirModes, _cfg.Direction.Mode));
            AddRow(grid, r++, "Direccion (u):", _dir,
                   "Direccion de las barras principales, que son la capa mas baja de la parrilla inferior (y la mas alta de la superior): " +
                   "paralelas al lado largo de la zapata (lo normal: las barras del lado largo van abajo, con mas peralte util), al lado corto, " +
                   "a los ejes X o Y del proyecto, o un angulo. Las secundarias (v) van perpendiculares. Cambiable zapata a zapata en la lista de arriba.");
            _angle = NumBox(_cfg.Direction.AngleDeg);
            AddRow(grid, r++, "Angulo (grados):", _angle, "Angulo de la direccion u respecto al eje X del proyecto, solo con la opcion \"angulo\".");
            group.Content = grid;
            return group;
        }

        private UIElement BuildBottom()
        {
            MeshCfg m = _cfg.Bottom;
            var group = new GroupBox { Header = "Parrilla inferior (obligatoria)", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _bmType = TypeCombo(m.Main.BarTypeName);
            AddRow(grid, r++, "Principal (u), tipo:", _bmType, "Tipo de barra de la capa inferior principal, a lo largo de u (la capa mas baja, apoyada en los separadores).");
            var bm = new StackPanel { Orientation = Orientation.Horizontal };
            _bmSp = NumBox(m.Main.SpacingMm); Hook(_bmSp);
            bm.Children.Add(_bmSp);
            bm.Children.Add(new TextBlock { Text = "separacion (mm), gancho:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _bmHook = HookCombo(m.Main.HookTypeName); _bmHook.Width = 220; Hook(_bmHook);
            bm.Children.Add(_bmHook);
            AddRow(grid, r++, "Principal:", bm,
                   "Separacion maxima entre barras (se reparten por igual sin superarla, con barra en los dos extremos al recubrimiento lateral) y " +
                   "gancho en los dos extremos de cada barra (dobla hacia arriba). Con gancho, el tramo recto se retranquea el radio exterior del " +
                   "doblez para que la cara exterior del gancho guarde el recubrimiento. En los bordes de un hueco la barra va recta.");
            _bmHl = HookLengthRow(grid, r++, m.Main.HookLengthMm, "inferior principal");

            _bsOn = new CheckBox { Content = "Colocar secundaria (v), encima de la principal", IsChecked = m.Secondary.Enabled, Margin = Pad };
            AddRow(grid, r++, "", _bsOn, "Barras inferiores perpendiculares, apoyadas sobre la capa principal. En una zapata siempre se colocan las dos direcciones.");
            _bsType = TypeCombo(m.Secondary.BarTypeName);
            AddRow(grid, r++, "Secundaria, tipo:", _bsType, "Tipo de barra de la capa inferior secundaria.");
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            _bsSp = NumBox(m.Secondary.SpacingMm); Hook(_bsSp);
            bs.Children.Add(_bsSp);
            bs.Children.Add(new TextBlock { Text = "separacion (mm), gancho:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _bsHook = HookCombo(m.Secondary.HookTypeName); _bsHook.Width = 220; Hook(_bsHook);
            bs.Children.Add(_bsHook);
            AddRow(grid, r++, "Secundaria:", bs, "Separacion maxima y gancho en los extremos (hacia arriba) de la capa inferior secundaria.");
            _bsHl = HookLengthRow(grid, r++, m.Secondary.HookLengthMm, "inferior secundaria");
            group.Content = grid;
            return group;
        }

        private UIElement BuildTop()
        {
            MeshCfg m = _cfg.Top;
            var group = new GroupBox { Header = "Parrilla superior (opcional)", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _tOn = new CheckBox { Content = "Colocar parrilla superior", IsChecked = m.Enabled, Margin = Pad };
            AddRow(grid, r++, "", _tOn,
                   "Parrilla superior para zapatas combinadas, conectadas o de gran canto. Se reparte en la cara superior (en una zapata " +
                   "escalonada o piramidal, en la plataforma superior, que es menor). Los ganchos doblan hacia abajo.");
            _tmType = TypeCombo(m.Main.BarTypeName);
            AddRow(grid, r++, "Principal (u), tipo:", _tmType, "Tipo de barra de la capa superior principal, a lo largo de u (la capa mas alta).");
            var tm = new StackPanel { Orientation = Orientation.Horizontal };
            _tmSp = NumBox(m.Main.SpacingMm); Hook(_tmSp);
            tm.Children.Add(_tmSp);
            tm.Children.Add(new TextBlock { Text = "separacion (mm), gancho:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _tmHook = HookCombo(m.Main.HookTypeName); _tmHook.Width = 220; Hook(_tmHook);
            tm.Children.Add(_tmHook);
            AddRow(grid, r++, "Principal:", tm, "Separacion maxima y gancho en los extremos (hacia abajo) de la capa superior principal.");
            _tmHl = HookLengthRow(grid, r++, m.Main.HookLengthMm, "superior principal");

            _tsOn = new CheckBox { Content = "Colocar secundaria (v), debajo de la principal", IsChecked = m.Secondary.Enabled, Margin = Pad };
            AddRow(grid, r++, "", _tsOn, "Barras superiores perpendiculares, colgadas bajo la capa superior principal.");
            _tsType = TypeCombo(m.Secondary.BarTypeName);
            AddRow(grid, r++, "Secundaria, tipo:", _tsType, "Tipo de barra de la capa superior secundaria.");
            var ts = new StackPanel { Orientation = Orientation.Horizontal };
            _tsSp = NumBox(m.Secondary.SpacingMm); Hook(_tsSp);
            ts.Children.Add(_tsSp);
            ts.Children.Add(new TextBlock { Text = "separacion (mm), gancho:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _tsHook = HookCombo(m.Secondary.HookTypeName); _tsHook.Width = 220; Hook(_tsHook);
            ts.Children.Add(_tsHook);
            AddRow(grid, r++, "Secundaria:", ts, "Separacion maxima y gancho en los extremos (hacia abajo) de la capa superior secundaria.");
            _tsHl = HookLengthRow(grid, r++, m.Secondary.HookLengthMm, "superior secundaria");
            group.Content = grid;
            return group;
        }

        private UIElement BuildGeneral()
        {
            var group = new GroupBox { Header = "Recubrimientos, columnas y particion", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            var cov = new StackPanel { Orientation = Orientation.Horizontal };
            _coverB = NumBox(_cfg.CoverBottomMm); _coverT = NumBox(_cfg.CoverTopMm); _coverE = NumBox(_cfg.CoverEdgeMm);
            Hook(_coverB); Hook(_coverT); Hook(_coverE);
            cov.Children.Add(_coverB);
            cov.Children.Add(new TextBlock { Text = "inferior (terreno),", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            cov.Children.Add(_coverT);
            cov.Children.Add(new TextBlock { Text = "superior,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            cov.Children.Add(_coverE);
            cov.Children.Add(new TextBlock { Text = "lateral y huecos", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            AddRow(grid, r++, "Recubrimientos (mm):", cov,
                   "Distancia de la cara inferior (hormigon contra el terreno: 75 mm es lo habitual), de la cara superior y de los bordes " +
                   "laterales (y de huecos) a la cara de la barra mas proxima.");
            _columns = new CheckBox { Content = "Mostrar las columnas que apoyan sobre la zapata en los esquemas", IsChecked = _cfg.DetectColumns, Margin = Pad };
            AddRow(grid, r++, "", _columns,
                   "Las columnas (estructurales o arquitectonicas) cuya base cae sobre la zapata se dibujan en la planta y en la seccion, solo " +
                   "como referencia: el armado de la columna y sus arranques los coloca el add-in de columnas.");
            _partition = new TextBox { Text = _cfg.PartitionTemplate, Margin = Pad };
            AddRow(grid, r++, "Particion:", _partition, "Plantilla del parametro Particion de cada barra. Comodines: " + PartitionName.Help);
            _partitionPreview = new TextBlock { Foreground = RevitTheme.Muted, Margin = Pad, TextWrapping = TextWrapping.Wrap };
            AddRow(grid, r++, "", _partitionPreview, null);
            _partitionWarning = new TextBlock
            {
                Foreground = RevitTheme.Error, Margin = Pad, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
                ToolTip = "El contrato ARBA (" + ArbaContract.Version + ") exige que la particion empiece por la categoria del anfitrion y el prefijo " +
                          "del add-in: \"{categoria} - {prefijo}-...\" (p. ej. \"" + AppConfig.DefaultPartitionTemplate + "\" da \"CIMIENTOS - ZAP-Z1\"). " +
                          "Asi el plugin de metrados agrupa el acero por categoria y cada add-in reconoce lo suyo."
            };
            AddRow(grid, r++, "", _partitionWarning, null);
            group.Content = grid;
            return group;
        }

        private UIElement BuildPreviews()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.5, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var planGroup = new GroupBox { Header = "Planta (rueda: zoom, arrastrar: mover, doble clic: encajar)", Padding = new Thickness(4) };
            var planPanel = new DockPanel();
            _previewCaption = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(_previewCaption, Dock.Top);
            planPanel.Children.Add(_previewCaption);
            var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            LegendItem(legend, PlanColors.BottomMain, "inferior principal (u)");
            LegendItem(legend, PlanColors.BottomSecondary, "inferior secundaria (v)");
            LegendItem(legend, PlanColors.TopMain, "superior principal (u)");
            LegendItem(legend, PlanColors.TopSecondary, "superior secundaria (v)");
            LegendItem(legend, PlanColors.ConcreteTop, "cara superior (escalon)");
            LegendItem(legend, PlanColors.ColumnEdge, "columna encima");
            DockPanel.SetDock(legend, Dock.Bottom);
            planPanel.Children.Add(legend);
            _plan = new PlanPreview { MinHeight = 220 };
            planPanel.Children.Add(new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _plan });
            planGroup.Content = planPanel;
            Grid.SetRow(planGroup, 0);
            grid.Children.Add(planGroup);

            var secGroup = new GroupBox { Header = "Seccion transversal a media luz (rueda: zoom, arrastrar: mover, doble clic: encajar)", Padding = new Thickness(4), Margin = new Thickness(0, 6, 0, 0) };
            _section = new SectionPreview { MinHeight = 160 };
            secGroup.Content = new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _section };
            Grid.SetRow(secGroup, 1);
            grid.Children.Add(secGroup);
            return grid;
        }

        private static void LegendItem(Panel panel, Brush brush, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            sp.Children.Add(new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Fill = brush, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(sp);
        }

        private UIElement BuildButtons()
        {
            var panel = new DockPanel();
            _message = new TextBlock { Foreground = RevitTheme.Error, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Right);

            var save = new Button { Content = "Guardar como valores por defecto", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0) };
            save.ToolTip = "Guarda lo elegido en config.json (" + AppConfig.ConfigPath() + ") para las proximas veces.";
            save.Click += (s, e) =>
            {
                AppConfig c = ReadConfig(out string err);
                if (err != null) { _message.Text = err; return; }
                try { c.Save(); _message.Foreground = RevitTheme.Ok; _message.Text = "Guardado en " + AppConfig.ConfigPath(); }
                catch (Exception ex) { _message.Foreground = RevitTheme.Error; _message.Text = "No se pudo guardar: " + ex.Message; }
            };
            buttons.Children.Add(save);

            _buildButton = new Button { Content = "Armar", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(4, 0, 4, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
            _buildButton.Click += (s, e) => OnBuild();
            buttons.Children.Add(_buildButton);

            var cancel = new Button { Content = "Cancelar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(cancel);

            var version = new TextBlock
            {
                Text = "Contrato ARBA " + ArbaContract.Version, Foreground = RevitTheme.Muted, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Version del contrato ARBA-comun con la que se compilo el add-in: particion \"CATEGORIA - PREFIJO-marca\", " +
                          "parametros compartidos ARBA - Origen (ZAPATAS), ARBA - Codigo (capa) y Metrado - Elemento (CIMIENTOS)."
            };
            DockPanel.SetDock(version, Dock.Left);

            panel.Children.Add(buttons);
            panel.Children.Add(version);
            panel.Children.Add(_message);
            return panel;
        }

        // ------------------------------------------------------------------
        // Controles auxiliares
        // ------------------------------------------------------------------
        private static Grid FormGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private void AddRow(Grid grid, int row, string label, FrameworkElement control, string tip)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lb = new TextBlock { Text = label, Margin = Pad, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(lb, row); Grid.SetColumn(lb, 0);
            grid.Children.Add(lb);
            if (tip != null) { control.ToolTip = tip; lb.ToolTip = tip; }
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, row); Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            Hook(control);
        }

        private readonly HashSet<FrameworkElement> _hooked = new HashSet<FrameworkElement>();

        private void Hook(FrameworkElement c)
        {
            if (!_hooked.Add(c)) return;   // cada control se engancha una sola vez
            if (c is TextBox tb) tb.TextChanged += (s, e) => Refresh();
            else if (c is ComboBox cb) cb.SelectionChanged += (s, e) => Refresh();
            else if (c is CheckBox ck) { ck.Checked += (s, e) => Refresh(); ck.Unchecked += (s, e) => Refresh(); }
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static TextBox NumBox(double v) => new TextBox { Text = Num(v), Width = 70, HorizontalAlignment = HorizontalAlignment.Left, Margin = Pad };

        private string TypeDisplay(string name) =>
            _diametersMm.TryGetValue(name, out double mm) ? name + " (" + Num(mm) + " mm)" : name;

        private ComboBox TypeCombo(string current)
        {
            var cb = new ComboBox { Margin = Pad };
            foreach (string n in _barTypes) cb.Items.Add(TypeDisplay(n));
            string match = NameMatch.First(_barTypes, current);
            cb.SelectedIndex = match == null ? -1 : _barTypes.IndexOf(match);
            if (NameMatch.IsAmbiguous(_barTypes, current)) MarkAmbiguous(cb, "tipo de barra", current, NameMatch.Candidates(_barTypes, current));
            return cb;
        }

        /// <summary>
        /// El nombre de config.json es un fragmento que coincide con varios tipos: se ha tomado el primero (regla
        /// antigua, NameMatch.First) y se marca el desplegable en amarillo hasta que el usuario elija uno.
        /// </summary>
        private void MarkAmbiguous(ComboBox cb, string what, string fragment, List<string> candidates)
        {
            _ambiguous.Add(cb);
            cb.ToolTip = "El " + what + " \"" + fragment + "\" de la configuracion coincide con varios tipos del proyecto (" +
                         string.Join(", ", candidates) + "). Se ha tomado el primero: comprueba que es el que quieres o elige otro.";
            cb.BorderBrush = AmbiguousBrush;
            cb.BorderThickness = new Thickness(2);
            cb.SelectionChanged += (s, e) =>
            {
                if (!_ambiguous.Remove(cb)) return;
                cb.ClearValue(FrameworkElement.ToolTipProperty);
                cb.ClearValue(Control.BorderBrushProperty);
                cb.ClearValue(Control.BorderThicknessProperty);
            };
        }

        private string TypeOf(ComboBox cb) =>
            cb.SelectedItem is string d && _typeByDisplay.TryGetValue(d, out string n) ? n : "";

        private ComboBox HookCombo(string current)
        {
            var cb = new ComboBox { Margin = Pad };
            cb.Items.Add(NoHook);
            foreach (string n in _hookTypes) cb.Items.Add(HookDisplay(n));
            string match = NameMatch.First(_hookTypes, current);
            cb.SelectedIndex = match == null ? 0 : _hookTypes.IndexOf(match) + 1;
            if (NameMatch.IsAmbiguous(_hookTypes, current)) MarkAmbiguous(cb, "tipo de gancho", current, NameMatch.Candidates(_hookTypes, current));
            return cb;
        }

        /// <summary>Fila "Longitud gancho": casilla en mm (0 = la del tipo de barra) de la capa dada.</summary>
        private TextBox HookLengthRow(Grid grid, int row, double mm, string layer)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            TextBox tb = NumBox(mm); Hook(tb);
            panel.Children.Add(tb);
            panel.Children.Add(new TextBlock { Text = "mm (0 = la del tipo de barra)", Margin = Pad, VerticalAlignment = VerticalAlignment.Center, Foreground = RevitTheme.Muted });
            AddRow(grid, row, "Longitud gancho:", panel,
                   "Longitud de los ganchos de la capa " + layer + " (la \"Longitud de gancho\" de Revit). Al armar se usa una copia del gancho " +
                   "elegido (p. ej. \"Estandar - 90 - L250\") con esa longitud fija en la tabla de longitudes de gancho del tipo de barra: el gancho " +
                   "elegido y el resto del proyecto no cambian. 0 = la que da el tipo de barra para ese gancho. " +
                   "Si el gancho no cabe en el canto, la comprobacion de la geometria real rechaza la zapata.");
            return tb;
        }

        private string HookDisplay(string name) =>
            _hookAngles.TryGetValue(name, out double deg) && name.IndexOf(Num(deg), StringComparison.Ordinal) < 0 ? name + " (" + Num(deg) + " grados)" : name;

        private string HookOf(ComboBox cb) => cb.SelectedIndex <= 0 ? "" : _hookTypes[cb.SelectedIndex - 1];

        private double DiameterFt(string typeName)
        {
            string match = NameMatch.First(_barTypes, typeName);
            return match != null && _diametersMm.TryGetValue(match, out double mm) ? FootingPlan.Mm(mm) : 0;
        }

        private double HookInsetFt(string typeName, double diameterFt)
        {
            string match = NameMatch.First(_barTypes, typeName);
            double bend = match != null && _hookBendMm.TryGetValue(match, out double mm) ? mm : 0;
            return FootingPlan.Mm(RebarGenerator.HookInsetMm(diameterFt * FootingPlan.MmPerFt, bend));
        }

        // ------------------------------------------------------------------
        // Lectura de la configuracion desde los controles
        // ------------------------------------------------------------------
        private AppConfig ReadConfig(out string error)
        {
            var errors = new List<string>();
            AppConfig c = _cfg.Clone();

            c.Direction.Mode = DirModes[Math.Max(0, _dir.SelectedIndex)];
            c.Direction.AngleDeg = ReadNum(_angle, "angulo", -360, errors);

            MeshCfg b = c.Bottom;
            b.Enabled = true;
            b.Main.BarTypeName = TypeOf(_bmType);
            b.Main.SpacingMm = ReadNum(_bmSp, "separacion inferior principal", 1, errors);
            b.Main.HookTypeName = HookOf(_bmHook);
            b.Main.HookLengthMm = ReadNum(_bmHl, "longitud de gancho inferior principal", 0, errors);
            b.Secondary.Enabled = _bsOn.IsChecked == true;
            b.Secondary.BarTypeName = TypeOf(_bsType);
            b.Secondary.SpacingMm = ReadNum(_bsSp, "separacion inferior secundaria", 1, errors);
            b.Secondary.HookTypeName = HookOf(_bsHook);
            b.Secondary.HookLengthMm = ReadNum(_bsHl, "longitud de gancho inferior secundaria", 0, errors);

            MeshCfg t = c.Top;
            t.Enabled = _tOn.IsChecked == true;
            t.Main.BarTypeName = TypeOf(_tmType);
            t.Main.SpacingMm = ReadNum(_tmSp, "separacion superior principal", 1, errors);
            t.Main.HookTypeName = HookOf(_tmHook);
            t.Main.HookLengthMm = ReadNum(_tmHl, "longitud de gancho superior principal", 0, errors);
            t.Secondary.Enabled = _tsOn.IsChecked == true;
            t.Secondary.BarTypeName = TypeOf(_tsType);
            t.Secondary.SpacingMm = ReadNum(_tsSp, "separacion superior secundaria", 1, errors);
            t.Secondary.HookTypeName = HookOf(_tsHook);
            t.Secondary.HookLengthMm = ReadNum(_tsHl, "longitud de gancho superior secundaria", 0, errors);

            c.CoverBottomMm = ReadNum(_coverB, "recubrimiento inferior", 0, errors);
            c.CoverTopMm = ReadNum(_coverT, "recubrimiento superior", 0, errors);
            c.CoverEdgeMm = ReadNum(_coverE, "recubrimiento lateral", 0, errors);
            c.DetectColumns = _columns.IsChecked == true;
            c.PartitionTemplate = _partition.Text.Trim();
            c.Normalize();

            error = errors.Count == 0 ? null : string.Join(" | ", errors);
            return c;
        }

        private static bool TryNumber(string s, out double v)
        {
            s = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static double ReadNum(TextBox tb, string label, double min, List<string> errors)
        {
            if (!TryNumber(tb.Text, out double v) || v < min)
            {
                errors.Add(label + ": numero no valido" + (min > 0 ? " (minimo " + Num(min) + ")" : ""));
                tb.BorderBrush = RevitTheme.Error;
                return Math.Max(min, 0);
            }
            tb.ClearValue(Control.BorderBrushProperty);
            return v;
        }

        /// <summary>Diametros de cada capa con esta configuracion; sin tipo elegido, uno orientativo (1/2") para poder ver el esquema.</summary>
        private PlanDiameters Diameters(AppConfig c, out bool allChosen)
        {
            allChosen = true;
            var d = new PlanDiameters();
            foreach ((BarLayer layer, LayerCfg lc) in new[]
            {
                (BarLayer.BottomMain, c.Bottom.Main), (BarLayer.BottomSecondary, c.Bottom.Secondary),
                (BarLayer.TopMain, c.Top.Main), (BarLayer.TopSecondary, c.Top.Secondary)
            })
            {
                double ft = DiameterFt(lc.BarTypeName);
                if (ft <= 0) { ft = FootingPlan.Mm(12.7); allChosen = false; }
                d.Set(layer, ft, HookInsetFt(lc.BarTypeName, ft));
            }
            return d;
        }

        /// <summary>Tipos de barra que faltan para las capas que se van a colocar.</summary>
        private static List<string> MissingTypes(AppConfig c)
        {
            var missing = new List<string>();
            foreach ((BarLayer layer, LayerCfg lc) in RebarGenerator.LayersFor(c))
                if (string.IsNullOrEmpty(lc.BarTypeName)) missing.Add(Layers.Name(layer));
            return missing;
        }

        private void MarkTypes(AppConfig c)
        {
            var needed = new HashSet<BarLayer>(RebarGenerator.LayersFor(c).Select(x => x.Item1));
            foreach ((ComboBox cb, BarLayer layer) in new[]
            {
                (_bmType, BarLayer.BottomMain), (_bsType, BarLayer.BottomSecondary), (_tmType, BarLayer.TopMain), (_tsType, BarLayer.TopSecondary)
            })
            {
                if (_strictTypes && needed.Contains(layer) && cb.SelectedIndex < 0) { cb.BorderBrush = RevitTheme.Error; cb.BorderThickness = new Thickness(2); }
                else if (_ambiguous.Contains(cb)) { cb.BorderBrush = AmbiguousBrush; cb.BorderThickness = new Thickness(2); }
                else { cb.ClearValue(Control.BorderBrushProperty); cb.ClearValue(Control.BorderThicknessProperty); }
            }
        }

        // ------------------------------------------------------------------
        // Actualizacion
        // ------------------------------------------------------------------
        private void Refresh()
        {
            if (_building) return;
            AppConfig scratch = ReadConfig(out string error);
            MarkTypes(scratch);

            // contrato ARBA: la plantilla tiene que empezar por "{categoria} - {prefijo}-"
            bool follows = ArbaPartition.TemplateFollowsContract(scratch.PartitionTemplate);
            _partitionWarning.Text = follows ? "" : "La plantilla no empieza por {categoria} - {prefijo}-: incumple el contrato ARBA";
            _partitionWarning.Visibility = follows ? Visibility.Collapsed : Visibility.Visible;

            // controles que dependen de otros
            _angle.IsEnabled = scratch.Direction.Mode == "angle";
            bool bs = scratch.Bottom.Secondary.Enabled;
            foreach (FrameworkElement fe in new FrameworkElement[] { _bsType, _bsSp, _bsHook }) fe.IsEnabled = bs;
            bool top = scratch.Top.Enabled;
            foreach (FrameworkElement fe in new FrameworkElement[] { _tmType, _tmSp, _tmHook, _tsOn }) fe.IsEnabled = top;
            bool ts = top && scratch.Top.Secondary.Enabled;
            foreach (FrameworkElement fe in new FrameworkElement[] { _tsType, _tsSp, _tsHook }) fe.IsEnabled = ts;
            _bmHl.IsEnabled = scratch.Bottom.Main.HookTypeName != "";
            _bsHl.IsEnabled = bs && scratch.Bottom.Secondary.HookTypeName != "";
            _tmHl.IsEnabled = top && scratch.Top.Main.HookTypeName != "";
            _tsHl.IsEnabled = ts && scratch.Top.Secondary.HookTypeName != "";

            PlanDiameters d = Diameters(scratch, out bool allChosen);

            // estado de cada zapata con esta configuracion
            int ok = 0;
            foreach (HostAnalysis item in _items)
            {
                bool good = ItemStatus(item, scratch, d, out string text, out _);
                if (good) ok++;
                if (_itemRuns.TryGetValue(item, out var runs))
                {
                    runs.kind.Foreground = good ? RevitTheme.Ok : RevitTheme.Error;
                    runs.detail.Text = text;
                }
            }
            _buildButton.Content = "Armar " + ok + " elemento(s)";
            _buildButton.IsEnabled = ok > 0 && error == null;

            // esquema del elemento seleccionado
            if (_selected != null && _selected.CanBuild)
            {
                ItemStatus(_selected, scratch, d, out string text, out FootingPlan plan);
                FootingFrame frame = _selected.Frame(scratch);
                _previewCaption.Text = _selected.Tag + frame.Describe() + ", " + _selected.Outline.Describe() +
                                       (allChosen ? "" : "  (hay capas sin tipo de barra elegido: diametros orientativos de 12.7 mm)");
                if (plan != null) { _plan.Show(frame, plan); _section.Show(frame, plan); }
                else { _plan.Clear(text); _section.Clear(text); }
                _partitionPreview.Text = "Ejemplo: " + _selected.Partition(scratch, "inferior principal", "inferior");
            }
            else
            {
                _previewCaption.Text = "";
                _plan.Clear("Sin elemento armable");
                _section.Clear("");
                _partitionPreview.Text = "";
            }

            if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; }
            else if (_message.Foreground == RevitTheme.Error) _message.Text = "";
        }

        /// <summary>Estado de una zapata con la configuracion dada: true si se puede armar, y el texto para su fila.</summary>
        private static bool ItemStatus(HostAnalysis item, AppConfig cfg, PlanDiameters d, out string text, out FootingPlan plan)
        {
            plan = null;
            if (!item.CanBuild) { text = item.Error; return false; }
            try
            {
                FootingFrame frame = item.Frame(cfg);
                plan = RebarGenerator.PlanFor(item, cfg, d);
                string head = frame.Describe() + ", " + item.Outline.Describe();
                if (plan.Error != null) { text = head + " -> SIN ARMAR: " + plan.Error; return false; }
                text = head + "; " + plan.Describe() + (plan.Warnings.Count > 0 ? " (" + string.Join("; ", plan.Warnings) + ")" : "");
                return plan.Bars.Count > 0;
            }
            catch (Exception ex)
            {
                text = item.Outline.Describe() + " -> SIN ARMAR: " + ex.Message;
                return false;
            }
        }

        private void OnBuild()
        {
            AppConfig c = ReadConfig(out string error);
            if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; return; }
            List<string> missing = MissingTypes(c);
            if (missing.Count > 0)
            {
                _strictTypes = true;
                MarkTypes(c);
                _message.Foreground = RevitTheme.Error;
                _message.Text = "Elige el tipo de barra de: " + string.Join(", ", missing) + ".";
                return;
            }
            Result = c;
            DialogResult = true;
            Close();
        }
    }
}
