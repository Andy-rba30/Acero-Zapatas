using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Arba.Comun;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace FootingRebar
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArmarZapataCommand : IExternalCommand
    {
        /// <summary>Que hacer con la armadura que ya tiene una zapata (contrato ARBA: lo propio se reconoce por "ARBA - Origen").</summary>
        private enum Existing
        {
            /// <summary>Ninguna zapata tiene armadura del add-in: no se pregunta.</summary>
            None,
            /// <summary>Borrar los conjuntos con ARBA - Origen = ZAPATAS del anfitrion y volver a armarlo.</summary>
            Delete,
            /// <summary>Conservar lo que hay y armar encima (duplica).</summary>
            Keep,
            /// <summary>Solo migrar las barras anteriores al contrato (ZAP-…) al contrato, sin crear barras nuevas.</summary>
            Migrate,
            Cancel
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            AppConfig cfg;
            try { cfg = AppConfig.Load(); }
            catch (Exception ex)
            {
                message = "No se pudo leer config.json (" + AppConfig.ConfigPath() + "): " + ex.Message;
                return Result.Failed;
            }

            IList<Element> hosts;
            try { hosts = GetHosts(uidoc); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (hosts.Count == 0)
            {
                message = "No se selecciono ninguna zapata (cimentacion estructural).";
                return Result.Cancelled;
            }

            var allTypes = RebarGenerator.AllBarTypes(doc);
            List<string> barTypes = allTypes.Select(b => b.Name).ToList();
            var diametersMm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var hookBendMm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RebarBarType bt in allTypes)
            {
                diametersMm[bt.Name] = UnitUtils.ConvertFromInternalUnits(bt.BarNominalDiameter, UnitTypeId.Millimeters);
                double bend = 0;
                try { bend = bt.StandardHookBendDiameter; } catch { }
                if (bend <= 0) { try { bend = bt.StandardBendDiameter; } catch { } }
                hookBendMm[bt.Name] = UnitUtils.ConvertFromInternalUnits(bend, UnitTypeId.Millimeters);
            }
            if (barTypes.Count == 0)
            {
                message = "El proyecto no tiene ningun tipo de barra (RebarBarType). Carga una familia de armadura primero.";
                return Result.Failed;
            }
            var allHooks = RebarGenerator.AllHookTypes(doc);
            List<string> hookTypes = allHooks.Select(h => h.Name).ToList();
            // angulo de cada gancho (grados) para describirlo en los esquemas
            var hookAngles = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RebarHookType h in allHooks)
            {
                double deg = 90;
                try { deg = Math.Round(h.HookAngle * 180 / Math.PI); } catch { }
                hookAngles[h.Name] = deg;
            }
            // catalogo de ganchos estandar: el angulo que el proyecto no tenga se ofrece igualmente y el tipo se crea al armar
            foreach (var e in RebarGenerator.HookCatalog)
                if (!hookAngles.Values.Contains(e.AngleDeg) && !hookTypes.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    hookTypes.Add(e.Name);
                    hookAngles[e.Name] = e.AngleDeg;
                }
            // longitud de gancho predeterminada de cada tipo de barra con cada gancho (y lo que ocupa el doblez): la casilla
            // "Longitud gancho" de la ventana arranca con ella y solo se sobrescribe en las barras lo que se aparte de ella
            var hookLengths = RebarGenerator.HookLengthTable(allTypes, allHooks);

            // --- 1. Analisis geometrico de cada elemento (solo lectura, sin transaccion) ---
            var items = hosts.Select(h => HostAnalysis.Analyze(doc, h, cfg)).ToList();

            // --- 2. Interfaz: el usuario revisa que se ha detectado y elige el armado ---
            var win = new RebarOptionsWindow(cfg.Clone(), barTypes, diametersMm, hookBendMm, hookTypes, hookAngles, hookLengths, items);
            try { new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle; } catch { }
            bool? ok = win.ShowDialog();
            if (ok != true || win.Result == null) return Result.Cancelled;
            cfg = win.Result;

            // --- 3. Armado ---
            var log = new List<string>();
            var avisos = new List<string>();
            int total = 0, armed = 0, rejected = 0, migrated = 0, deletedSets = 0, deletedBars = 0;

            using (Transaction tx = new Transaction(doc, "Armar zapatas"))
            {
                tx.Start();

                // Parametros compartidos del contrato ARBA (GUID fijo, de ejemplar): ARBA - Origen, ARBA - Codigo y
                // Metrado - Elemento. Se crean o completan si faltan; sus avisos van al informe.
                ArbaSharedParams.Ensure(doc, new[] { ArbaContract.Origen, ArbaContract.Codigo, ArbaContract.Elemento }, avisos);
                doc.Regenerate();

                // Zapatas que ya tienen armadura de este add-in (ARBA - Origen = ZAPATAS) o armadura de la version
                // anterior al contrato (particion ZAP-… sin origen). Se pregunta una sola vez para todas.
                var own = new HashSet<ElementId>();
                var legacy = new HashSet<ElementId>();
                foreach (HostAnalysis item in items)
                {
                    if (!item.CanBuild) continue;
                    if (ArbaOrigin.Find(doc, ArbaContract.Zapatas, item.Host).Count > 0) own.Add(item.Host.Id);
                    if (ArbaMigration.HasLegacy(doc, item.Host, ArbaContract.Zapatas)) legacy.Add(item.Host.Id);
                }
                Existing existing = AskExisting(own.Count, legacy.Count);
                if (existing == Existing.Cancel)
                {
                    tx.RollBack();
                    return Result.Cancelled;
                }

                foreach (HostAnalysis item in items)
                {
                    string tag = item.Tag;
                    if (!item.CanBuild)
                    {
                        rejected++;
                        log.Add(tag + "SIN ARMAR -> " + item.Detail);
                        continue;
                    }

                    bool hasOwn = own.Contains(item.Host.Id);
                    bool hasLegacy = legacy.Contains(item.Host.Id);

                    // "Migrar (sin rearmar)": las zapatas que ya tienen armadura solo se migran; no se crea ninguna barra en ellas.
                    if (existing == Existing.Migrate && (hasLegacy || hasOwn))
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (hasLegacy)
                                {
                                    ArbaMigrationResult mr = ArbaMigration.MigrateHost(doc, item.Host, ArbaContract.Zapatas);
                                    sub.Commit();
                                    migrated++;
                                    log.Add(tag + "MIGRADA al contrato ARBA (sin rearmar): " + Indent(mr.Resumen()));
                                }
                                else
                                {
                                    sub.RollBack();
                                    log.Add(tag + "SIN REARMAR -> ya tiene armadura de este add-in (ARBA - Origen = ZAPATAS); se conserva tal cual.");
                                }
                            }
                            catch (Exception ex)
                            {
                                sub.RollBack();
                                rejected++;
                                log.Add(tag + "SIN MIGRAR -> ERROR: " + ex.Message);
                            }
                        }
                        continue;
                    }

                    // Cada elemento se arma dentro de una subtransaccion. Si cualquier barra
                    // queda fuera del hormigon (red de seguridad), se deshace TODO lo creado
                    // para ese elemento: o se arma entero y bien, o no se arma.
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        BuildResult res = null;
                        string error = null;
                        int delSets = 0, delBars = 0;
                        try
                        {
                            if (existing == Existing.Delete && hasOwn)
                            {
                                // Borrar y rearmar: solo lo que creo este add-in en esta zapata (ARBA - Origen = ZAPATAS)
                                delSets = ArbaOrigin.Delete(doc, ArbaContract.Zapatas, item.Host, out delBars);
                                doc.Regenerate();
                            }
                            res = RebarGenerator.Build(doc, item, cfg);
                            if (res.Safe && res.Created.Count > 0)
                            {
                                doc.Regenerate();
                                RebarGenerator.VerifyCreated(doc, item, cfg, res);
                            }
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                        }

                        bool keep = error == null && res != null && res.Safe;
                        string desc = item.Frame(cfg).Describe() + ", " + item.Detail;
                        if (keep)
                        {
                            sub.Commit();
                            armed++;
                            total += res.Created.Count;
                            deletedSets += delSets;
                            deletedBars += delBars;
                            string deleted = delSets > 0 ? "borrados " + delSets + " conjuntos (" + delBars + " barras) del add-in; " : "";
                            string line = tag + desc + "  ->  " + deleted + res.Summary;
                            if (existing == Existing.Keep && hasOwn)
                                line += "  AVISO: conservada la armadura anterior del add-in (quedan conjuntos duplicados)";
                            if (hasLegacy)
                                line += "  AVISO: tiene barras ZAP-… de la version anterior sin ARBA - Origen; no se reconocen como propias " +
                                        "hasta migrarlas (opcion \"Migrar la armadura antigua\" al armar)";
                            if (res.Failed.Count > 0)
                                line += "  INCOMPLETO, no se pudieron crear: " + string.Join(" | ", res.Failed);
                            if (res.Warnings.Count > 0)
                                line += "  AVISOS: " + string.Join(" | ", res.Warnings);
                            log.Add(line);
                        }
                        else
                        {
                            sub.RollBack();
                            rejected++;
                            if (error != null)
                                log.Add(tag + "SIN ARMAR -> ERROR: " + error + ". Se ha deshecho todo lo creado para este elemento" +
                                        (delSets > 0 ? " (la armadura anterior se conserva)" : "") + ".");
                            else
                                log.Add(tag + "SIN ARMAR -> " + desc + ": barras fuera del hormigon, se ha deshecho todo el " +
                                        "elemento (" + res.Rejected.Count + "): " + string.Join(" | ", res.Rejected) +
                                        (delSets > 0 ? ". La armadura anterior se conserva" : ""));
                        }
                    }
                }
                tx.Commit();
            }

            if (avisos.Count > 0)
                log.Insert(0, "Parametros compartidos ARBA: " + string.Join(" | ", avisos));

            string instruction = total + " conjuntos de armadura creados en " + armed + " de " + hosts.Count + " elemento(s).";
            if (deletedSets > 0) instruction += Environment.NewLine + deletedSets + " conjuntos anteriores del add-in (" + deletedBars + " barras) borrados antes de rearmar.";
            if (migrated > 0) instruction += Environment.NewLine + migrated + " elemento(s) migrados al contrato ARBA sin rearmar.";
            var td = new TaskDialog("Armado de zapatas")
            {
                MainInstruction = instruction,
                MainContent = string.Join(Environment.NewLine, log),
                FooterText = "Contrato ARBA " + ArbaContract.Version + ": Particion \"CIMIENTOS - ZAP-marca\", ARBA - Origen = ZAPATAS, " +
                             "ARBA - Codigo = capa, Metrado - Elemento = CIMIENTOS."
            };
            if (rejected > 0)
            {
                td.MainInstruction += Environment.NewLine + "ATENCION: " + rejected +
                                      " elemento(s) SIN ARMAR (ver detalle). No se ha creado ninguna barra en ellos.";
                td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
            }
            td.Show();
            return Result.Succeeded;
        }

        /// <summary>
        /// Pregunta, una sola vez, que hacer con las zapatas que ya tienen armadura: la del add-in (reconocida por
        /// ARBA - Origen = ZAPATAS) se puede borrar y rearmar o conservar; la de la version anterior al contrato
        /// (particion ZAP-… sin origen) se puede migrar sin rearmar. Sin nada que preguntar devuelve None.
        /// </summary>
        private static Existing AskExisting(int ownCount, int legacyCount)
        {
            if (ownCount == 0 && legacyCount == 0) return Existing.None;

            var td = new TaskDialog("Armar zapatas: armadura existente")
            {
                TitleAutoPrefix = false,
                MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                AllowCancellation = true,
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
                FooterText = "Contrato ARBA " + ArbaContract.Version
            };
            var parts = new List<string>();
            if (ownCount > 0) parts.Add(ownCount + " zapata(s) ya tienen armadura creada por este add-in (ARBA - Origen = ZAPATAS)");
            if (legacyCount > 0) parts.Add(legacyCount + " zapata(s) tienen armadura de la version anterior (particion ZAP-… sin ARBA - Origen)");
            td.MainInstruction = string.Join(Environment.NewLine, parts) + ".";
            td.MainContent = "Elige que hacer con esa armadura antes de armar. Solo se toca lo que creo este add-in; " +
                             "las barras colocadas a mano o por otros add-ins no se borran nunca.";

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Borrar la armadura del add-in y rearmar",
                ownCount > 0
                    ? "Se borran los conjuntos con ARBA - Origen = ZAPATAS de esas zapatas y se vuelven a armar con la configuracion elegida."
                    : "No hay armadura con ARBA - Origen = ZAPATAS que borrar: equivale a armar; las barras antiguas sin origen se conservan.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Conservar y armar encima",
                "La armadura existente se mantiene y se anaden los conjuntos nuevos (quedan duplicados).");
            if (legacyCount > 0)
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Migrar la armadura antigua al contrato (sin rearmar)",
                    "Las barras ZAP-… pasan a \"CIMIENTOS - ZAP-…\" con ARBA - Origen = ZAPATAS, ARBA - Codigo y Metrado - Elemento; " +
                    "no se crea ninguna barra en las zapatas que ya tienen armadura. Un segundo \"armar\" ya las reconoce como propias.");

            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: return Existing.Delete;
                case TaskDialogResult.CommandLink2: return Existing.Keep;
                case TaskDialogResult.CommandLink3: return Existing.Migrate;
                default: return Existing.Cancel;
            }
        }

        /// <summary>Resumen de varias lineas sangrado para el informe.</summary>
        private static string Indent(string text) =>
            Environment.NewLine + "      " + (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine + "      ");

        private static IList<Element> GetHosts(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var sel = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(IsCandidate)
                .ToList();
            if (sel.Count > 0) return sel;

            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new HostFilter(),
                "Selecciona las zapatas (cimentaciones estructurales) a armar y pulsa Finalizar");
            return refs.Select(r => doc.GetElement(r)).ToList();
        }

        /// <summary>
        /// Cimentaciones estructurales: zapatas aisladas y combinadas (instancias de familia),
        /// losas de cimentacion (suelos de esa categoria) y zapatas corridas (WallFoundation).
        /// </summary>
        private static bool IsCandidate(Element e)
        {
            if (e == null || e.Category == null) return false;
            try { return e.Category.BuiltInCategory == BuiltInCategory.OST_StructuralFoundation; }
            catch { return false; }
        }

        private class HostFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => IsCandidate(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
