# Plan de trabajo — add-in ARBA Zapatas (Revit 2027.2)

Este archivo es el plan vivo del proyecto y su estado. Se actualiza en cada commit
para que, si se corta la sesion, se pueda retomar exactamente donde se quedo.

## Objetivo

Add-in de Revit 2027 en C# (.NET 10, WPF en codigo, sin XAML) que arme **zapatas**
(aisladas, combinadas, corridas y losas de cimentacion):

- **Parrilla inferior** en las dos direcciones (principal a lo largo de `u`, la capa mas
  baja; secundaria a lo largo de `v` encima), con gancho estandar en los extremos.
- **Parrilla superior** opcional (dos capas, ganchos hacia abajo).
- Deteccion de las **columnas** que apoyan encima, solo para los esquemas y el informe.

Mismo aspecto visual y misma arquitectura que
[Acero-losas](https://github.com/Andy-rba30/Acero-losas): tema oscuro de Revit
(`RevitTheme`), ventana previa con esquemas (planta + seccion), lista de elementos con
ajustes propios por elemento, `config.json` con valores por defecto, boton en la pestana
**ARBA** > panel **Acero** > desplegable **Acero** > **Zapatas**, red de seguridad que
deshace el elemento entero si una barra queda fuera del hormigon.

## Arquitectura (archivos)

| Archivo | Que hace | Estado |
|---------|----------|--------|
| `FootingRebar.csproj`, `FootingRebar.addin`, `config.json`, `.gitignore` | Proyecto .NET 10 (net10.0-windows), manifiesto, configuracion | hecho |
| `RevitTheme.cs` | Tema oscuro de Revit (copiado del add-in de losas, namespace `FootingRebar`) | hecho |
| `RibbonApp.cs` | Pestana ARBA compartida (`ArbaRibbon`) + boton **Zapatas** con icono propio | hecho |
| `AppConfig.cs` | Configuracion (`config.json`): recubrimientos, direccion, parrillas inferior y superior, columnas, particion | hecho |
| `PartitionName.cs` | Plantilla del parametro Particion (`{marca}`, `{id}`, `{tipo}`, `{familia}`, `{conjunto}`, `{capa}`) | hecho |
| `Geometry2D.cs` | Geometria pura: `Pt`, poligonos con huecos, recorte de una recta contra el contorno (scan-line), borde mas largo | hecho |
| `FootingPlan.cs` | Armado puro: capas de las parrillas, ganchos con retranqueo del doblez, agrupacion en arrays | hecho |
| `FootingOutline.cs` | Lectura del solido de Revit: caras inferior y superior, contornos, columnas encima, perfil de la seccion, `FootingFrame` | hecho |
| `HostAnalysis.cs` | Resultado por zapata (contorno o motivo de rechazo) + direccion propia | hecho |
| `RebarGenerator.cs` | Crea los `Rebar` con las dos redes de seguridad | hecho |
| `RebarOptionsWindow.cs` | Ventana WPF en codigo, mismo aspecto que losas | hecho |
| `PlanPreview.cs` | Esquema en planta (zoom/arrastrar/doble clic) con cara superior y columnas | hecho |
| `SectionPreview.cs` | Esquema de la seccion transversal con el perfil real, terreno, columna, barras y ganchos | hecho |
| `ArmarZapataCommand.cs` | Comando externo: seleccion de cimentaciones, analisis, ventana, transaccion, informe | hecho |
| `README.md`, `INSTALADOR.md` | Documentacion de uso e instalacion | hecho |
| `Tests/` | Programa de consola que prueba las clases puras: `cd Tests && dotnet run` | hecho (93 comprobaciones) |

## Decisiones de diseño

1. **Geometria real del elemento**: se leen todos los solidos (una zapata escalonada puede
   tener varios); canto = cara inferior horizontal mas baja a cara superior horizontal mas
   alta; contorno inferior de las caras a la cota inferior, contorno superior de las caras a
   la cota superior (menor si es escalonada o piramidal). Todos los solidos cuentan al
   comprobar las barras.
2. **Ejes locales**: `u` = direccion de las barras principales (la capa mas baja), `v` =
   perpendicular. Por defecto `u` va por el lado largo (las barras largas van abajo, con mas
   peralte util). Cambiable por zapata.
3. **Recorte scan-line**: cada linea de barra se corta contra el contorno con huecos; cada
   intervalo interior es una barra. Las lineas consecutivas iguales se agrupan en un unico
   conjunto de Revit (array).
4. **Ganchos**: en los extremos exteriores (recta en los huecos). El tramo recto se
   retranquea el radio exterior del doblez (`StandardHookBendDiameter / 2 + d`) para que la
   cara exterior del gancho guarde el recubrimiento lateral, suponiendo que Revit anade el
   gancho mas alla del extremo de la curva. Si Revit doblara hacia dentro, la barra solo
   quedaria unos 50 mm mas corta: no hay riesgo. La orientacion (arriba / abajo) se comprueba
   en la geometria real de la primera barra de cada capa y se invierte si hace falta.
5. **Parrilla superior** en el contorno superior (plataforma en escalonadas), con aviso.
   Rechazo si las dos parrillas se solapan.
6. **Columnas**: solo informativas (planta, seccion e informe). Sus arranques son armado de
   columna (add-in de columnas).
7. **Red de seguridad**: igual que losas. (1) antes de crear, eje + fibras a medio diametro
   dentro del solido en todas las posiciones del array; (2) tras regenerar, geometria real
   completa de cada barra, ganchos incluidos (en una zapata nada puede sobresalir).
   Cualquier fallo deshace la subtransaccion del elemento.
8. **Seccion**: el perfil del hormigon se muestrea del solido con rectas verticales
   (`Solid.IntersectWithCurve`), asi se ven escalones y taludes reales sin modelar nada.

## Progreso

- [x] Analisis del repo de referencia (Acero-losas) y de su estilo visual.
- [x] Plan guardado.
- [x] Proyecto, manifiesto, config, tema, cinta.
- [x] Geometria pura y plan de armado + pruebas de consola (93 OK).
- [x] Lectura del solido de Revit, columnas y perfil de la seccion.
- [x] Generador con redes de seguridad.
- [x] Ventana y esquemas.
- [x] Comando e informe.
- [x] README e INSTALADOR.
- [x] Compilacion (dotnet build -c Release con EnableWindowsTargeting en Linux): 0 errores, 0 avisos.
- [ ] Prueba en Revit 2027.2 por parte del usuario (no hay Revit en el entorno de la sesion).
      En especial: comprobar que el gancho se anade mas alla del extremo (decision 4) y que
      `Category.BuiltInCategory` filtra bien las cimentaciones del proyecto.

## Como retomar

1. `git pull` de la rama `claude/pensive-rubin-nsa1u1`.
2. `dotnet build -c Debug` en Windows con Revit 2027 instalado: copia la DLL, `config.json` y el
   `.addin` a `%AppData%\Autodesk\Revit\Addins\2027\`.
3. En Revit: pestana ARBA > Acero > Zapatas. Seleccionar cimentaciones estructurales de hormigon.
4. Si algo falla en Revit, el informe final y los avisos de la ventana dicen el motivo; las
   clases puras se depuran con `cd Tests && dotnet run`.

## Ideas pendientes (no implementadas)

- Concentracion de la armadura corta en la banda central (zapatas rectangulares, ACI 13.3.3.3)
  con separacion distinta dentro y fuera de la banda, centrada en la columna detectada.
- Arranques de columna (barras con gancho de 90° apoyado en la parrilla + estribos 1@50, 5@100)
  tomando el armado del add-in de columnas.
- Estribos o barras en U en los bordes de zapatas de gran canto.
- Longitudes de anclaje y ganchos calculados por diametro; aviso si el gancho no cabe en el canto
  antes de crear (ahora lo detecta la red de seguridad 2).
- Etiquetas automaticas en planta y numeracion de particion por zapata.

## Entorno de compilacion usado en la sesion

`apt-get install dotnet-sdk-10.0` (Ubuntu 24.04) y `dotnet build` del proyecto principal con
`EnableWindowsTargeting=true`: WPF y los paquetes `Nice3point.Revit.Api.*` 2027.2 compilan
en Linux (solo para comprobar; la DLL se usa en Windows con Revit).
