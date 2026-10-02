using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace FootingRebar
{
    /// <summary>
    /// Resultado del analisis de una zapata seleccionada, antes de armar nada: el contorno
    /// deducido o el motivo del rechazo, mas la eleccion por elemento hecha en la ventana
    /// (direccion propia de las barras principales).
    /// </summary>
    public sealed class HostAnalysis
    {
        public Element Host;
        public string Tag;
        public string Mark = "", TypeName = "", FamilyName = "";

        public FootingOutline Outline;
        public string Error;

        /// <summary>Direccion propia: "" (la general), "long", "short", "x" o "y".</summary>
        public string DirectionOverride = "";

        public bool CanBuild => Error == null && Outline != null;

        public string DirectionMode(AppConfig cfg) =>
            AppConfig.NormalizeDirection(string.IsNullOrWhiteSpace(DirectionOverride) ? cfg.Direction.Mode : DirectionOverride);

        /// <summary>La zapata en el sistema local de la direccion efectiva.</summary>
        public FootingFrame Frame(AppConfig cfg) => Outline?.Frame(DirectionMode(cfg), cfg.Direction.AngleDeg);

        public string Kind => Error != null ? "SIN ARMAR" : "Zapata";

        public string Detail => Error ?? Outline.Describe();

        public string Partition(AppConfig cfg, string setName, string layer)
        {
            return PartitionName.Expand(cfg.PartitionTemplate, new PartitionName.Source
            {
                Mark = Mark, Id = Host.Id.ToString(), TypeName = TypeName, FamilyName = FamilyName,
                SetName = setName, Layer = layer
            });
        }

        public static HostAnalysis Analyze(Document doc, Element host, AppConfig cfg)
        {
            var a = new HostAnalysis { Host = host, Tag = "[" + host.Id + " " + host.Name + "] " };
            try
            {
                a.Mark = host.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                a.TypeName = FootingOutline.TypeNameOf(doc, host) ?? "";
                a.FamilyName = FootingOutline.FamilyNameOf(doc, host) ?? "";

                RebarHostData hd = RebarHostData.GetRebarHostData(host);
                if (hd == null || !hd.IsValidHost())
                {
                    a.Error = "no admite armadura. Revisa que la cimentacion sea estructural y que su material sea hormigon.";
                    return a;
                }

                a.Outline = FootingOutline.Probe(doc, host, cfg);
                if (a.Outline == null)
                    a.Error = "RECHAZADA, " + (FootingOutline.LastError ?? "no se pudo deducir el contorno (motivo desconocido)") +
                              ". No se ha creado ninguna barra.";
            }
            catch (Exception ex)
            {
                a.Error = "ERROR: " + ex.Message;
            }
            return a;
        }
    }
}
