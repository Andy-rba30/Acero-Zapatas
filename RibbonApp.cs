using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arba.Comun;
using Autodesk.Revit.UI;

namespace FootingRebar
{
    /// <summary>
    /// Entrada de la aplicacion de cinta para FootingRebar en Revit.
    /// Anade el boton "Zapatas" al desplegable "Acero" del panel "Acero" en la pestana "ARBA".
    /// La pestana, los paneles y el desplegable los gestiona la clase comun <see cref="ArbaRibbon"/>
    /// (ARBA-comun): todos los add-ins de armado escriben en la misma pestana y el mismo desplegable.
    /// </summary>
    public class RibbonApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                ArbaRibbon.Ensure(app);

                string assembly = Assembly.GetExecutingAssembly().Location;
                var data = new PushButtonData("ARBA_Acero_Zapatas", "Zapatas", assembly, typeof(ArmarZapataCommand).FullName)
                {
                    ToolTip = "Genera el armado de zapatas: parrilla inferior en las dos direcciones con ganchos y, opcional, parrilla superior",
                    LongDescription = "Selecciona una o varias zapatas (cimentaciones estructurales: zapatas aisladas, combinadas, " +
                                      "corridas o losas de cimentacion) y pulsa el boton. Se abre la ventana para elegir la direccion de " +
                                      "las barras principales, los tipos de barra, separaciones, ganchos y recubrimientos, con un " +
                                      "esquema en planta y de la seccion. Si no hay nada seleccionado, el comando pide que elijas las zapatas. " +
                                      "Contrato ARBA " + ArbaContract.Version + ".",
                    LargeImage = IconZapatas(32),
                    Image = IconZapatas(16)
                };

                ArbaRibbon.AddAcero(app, data);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ARBA", "No se pudo anadir el boton Zapatas a la cinta: " + ex.Message +
                                "\nEl comando sigue disponible en Complementos > Herramientas externas.");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        /// <summary>
        /// Icono del boton Zapatas: seccion de una zapata con el arranque de la columna
        /// encima, la parrilla inferior (barra con ganchos hacia arriba en los extremos y las
        /// barras perpendiculares como puntos) y el terreno debajo.
        /// </summary>
        private static BitmapSource IconZapatas(int size)
        {
            double s = size / 32.0;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var concrete = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2 * s);
                var column = new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF));
                var bar = new Pen(new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E)), 1.8 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var dot = new SolidColorBrush(Color.FromRgb(0x7A, 0x3E, 0x9D));
                var soil = new Pen(new SolidColorBrush(Color.FromRgb(0xA8, 0x8A, 0x5A)), 1.0 * s);

                // arranque de la columna
                dc.DrawRectangle(column, edge, new Rect(12 * s, 2 * s, 8 * s, 10 * s));
                // zapata
                dc.DrawRectangle(concrete, edge, new Rect(1.5 * s, 11 * s, 29 * s, 15 * s));
                // parrilla inferior: barra con ganchos hacia arriba
                dc.DrawLine(bar, new Point(5 * s, 22.5 * s), new Point(27 * s, 22.5 * s));
                dc.DrawLine(bar, new Point(5 * s, 22.5 * s), new Point(5 * s, 16 * s));
                dc.DrawLine(bar, new Point(27 * s, 22.5 * s), new Point(27 * s, 16 * s));
                // barras perpendiculares (puntos) encima de la principal
                for (double x = 8.5; x <= 24; x += 5.2)
                    dc.DrawEllipse(dot, null, new Point(x * s, 20.4 * s), 1.1 * s, 1.1 * s);
                // terreno
                for (double x = 2; x < 31; x += 5)
                    dc.DrawLine(soil, new Point(x * s, 30 * s), new Point((x + 3) * s, 27.5 * s));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
