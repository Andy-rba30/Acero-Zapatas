# Armado automático de zapatas — add-in Revit 2027

Genera la armadura de **zapatas** (aisladas, combinadas, corridas y losas de cimentación) a
partir de la **geometría real del elemento**, sin depender de los nombres de parámetros de tus
familias:

- **Parrilla inferior** en las dos direcciones: barras principales (a lo largo de `u`, la capa
  más baja) y secundarias (a lo largo de `v`, apoyadas encima), cada una con su tipo de barra,
  separación y **gancho estándar** en los dos extremos (dobla hacia arriba).
- **Parrilla superior** opcional (zapatas combinadas, conectadas o de gran canto), con sus dos
  capas y ganchos hacia abajo.
- **Columnas** que apoyan sobre la zapata, detectadas y dibujadas en los esquemas como
  referencia (su armado y arranques los pone el add-in de columnas).

Es el hermano del add-in de losas
([Acero-losas](https://github.com/Andy-rba30/Acero-losas)), del de columnas
([Acero-columnas](https://github.com/Andy-rba30/Acero-columnas)) y del de muros
([Acero-automatico](https://github.com/Andy-rba30/Acero-automatico)): misma base (lectura del
sólido, ventana previa con esquemas y tema oscuro de Revit, red de seguridad que deshace el
elemento entero si una barra queda fuera del hormigón, `config.json`) y el mismo botón en la
pestaña **ARBA** > panel **Acero** > desplegable **Acero** > **Zapatas**.

Desde la versión 1.0.0 del **contrato ARBA** el add-in comparte con los demás el código común
[ARBA-comun](https://github.com/Andy-rba30/ARBA-comun) (submódulo `external/ARBA-comun`): la misma
cinta, el mismo tema oscuro, la **partición** `CIMIENTOS - ZAP-<marca>`, los parámetros compartidos
`ARBA - Origen` / `ARBA - Código` / `Metrado - Elemento`, y con ellos **borrar y rearmar** sin duplicar y la
**migración** de modelos armados con la versión anterior (ver [Contrato ARBA](#contrato-arba-partición-origen-borrar-y-rearmar-migración)).

Antes de crear nada abre una **ventana** en la que se ve qué se ha detectado en cada zapata
seleccionada y se elige el armado: dirección de las barras principales (general y por zapata),
tipos de barra, separaciones, ganchos y recubrimientos, con un esquema en **planta** y de la
**sección transversal** (con la columna encima, como en el plano de detalle).

## Cómo lee la zapata

1. Toma los sólidos del elemento (cimentación estructural: instancia de familia de zapata,
   losa de cimentación o zapata corrida). Puede tener **varios sólidos** (zapata escalonada
   modelada en dos cuerpos); los despreciables (menos del 1 % del mayor) se ignoran.
2. El **canto** va de la cara inferior horizontal más baja a la cara superior horizontal más
   alta. Sin cara inferior horizontal (base inclinada) se rechaza con un mensaje claro y sin
   crear ninguna barra.
3. El **contorno inferior** sale de las caras a la cota inferior (bordes curvos teselados;
   puede tener huecos, por ejemplo un pase). El **contorno superior** sale de las caras a la
   cota superior: en una zapata **escalonada o piramidal** es menor y se marca como tal.
4. Busca las **columnas** (estructurales o arquitectónicas) cuya base cae sobre la zapata, para
   dibujarlas en los esquemas y contarlas en el informe.
5. Elige los **ejes locales**: `u` es la dirección de las barras principales, `v` la
   perpendicular. Por defecto `u` va paralela al **lado largo** de la zapata (las barras del
   lado largo van abajo, con más peralte útil); también puede ir por el lado corto, por los
   ejes X o Y del proyecto o con un ángulo. Se cambia en general y **zapata a zapata** desde la
   lista de la ventana.

## Reglas de armado (`FootingPlan`)

Cada barra es una recta `v = cte` (a lo largo de `u`) o `u = cte` (a lo largo de `v`),
**recortada contra el contorno con sus huecos**: cada tramo interior es una barra. Vale para
cualquier forma (rectangular, en L o T, con huecos, irregular). Además la franja de ancho
`recubrimiento lateral + diámetro` alrededor de la recta tiene que quedar dentro del hormigón,
así la barra guarda el recubrimiento con los bordes paralelos a ella.

- La **separación es un máximo**: `n = techo(L / s)` huecos iguales, con barra en los dos
  extremos al recubrimiento lateral. Las barras iguales y equiespaciadas se crean como **un
  solo conjunto de Revit (array)**, igual que si se modelaran a mano: cada capa de una zapata
  rectangular es un conjunto.
- **Parrilla inferior**: principal a `recubrimiento inferior + medio diámetro` de la cara
  inferior; secundaria apoyada encima (`+ diámetro principal`), repartidas en el contorno
  inferior.
- **Parrilla superior** (opcional): principal a `recubrimiento superior + medio diámetro` de la
  cara superior; secundaria colgada debajo, repartidas en el contorno superior (la plataforma
  en una zapata escalonada). Si las dos parrillas se solapan (canto pequeño) se rechaza.
- **Ganchos**: en los extremos que dan al borde exterior; en los bordes de un hueco la barra
  va recta. Revit dobla el gancho **en el propio extremo de la barra** (el extremo es la esquina
  del doblez y la pata sale de él), así que el tramo recto llega al recubrimiento lateral como una
  barra recta y la cara exterior de la pata queda al recubrimiento. Las **barras extremas de la
  capa perpendicular se meten un diámetro hacia dentro**, para quedar dentro de las patas: los
  ganchos abrazan toda la parrilla. El plugin crea la primera barra de cada capa, lee hacia dónde
  dobla el gancho y si es al revés (abajo en la inferior, arriba en la superior) la borra y la
  vuelve a crear con la otra orientación; si en un proyecto Revit añadiera el gancho más allá del
  extremo (la pata sobresale del tramo recto), deshace lo creado, retranquea el tramo recto el
  radio del doblez y vuelve a armar la zapata, avisándolo en el resumen. La ventana ofrece los ganchos **de estilo
  Estándar** del proyecto (los de *Estribo/Tirante* no: Revit no los admite en estas barras) y,
  si al proyecto le falta el de 90° o el de 180°, el catálogo del plugin los ofrece igualmente
  (`Estandar - 90`, prolongación 12 diámetros; `Estandar - 180`, 4 diámetros): el tipo se crea
  en el proyecto al armar y se avisa en el resumen. La **longitud de gancho** de cada capa (total,
  como la mide Revit) arranca en la ventana con la **predeterminada** que el tipo de barra da a ese
  gancho (su tabla *Longitudes de gancho*) y se actualiza al cambiar el tipo o el gancho; se puede
  acortar o alargar escribiendo otra: esa se aplica a cada barra con *Sobrescribir longitudes de
  gancho* (se activa la sobrescritura, se escribe en el parámetro de longitud de gancho que Revit deja
  modificable en cada extremo y se vuelve a leer para comprobar que cambió), sin tocar el tipo de
  barra. Dejar la predeterminada (o 0) no sobrescribe nada. Una longitud que no deje prolongación
  recta más allá del doblez se rechaza con el mínimo; si el gancho no cabe en el canto, la
  comprobación de la geometría real rechaza la zapata.
- Los tramos más cortos que `minBarLengthMm` (300 mm) se omiten y se cuentan en el aviso.

## Comprobaciones de seguridad

Igual que en losas, columnas y muros: **o se arma la zapata entera y bien, o no se arma**.

1. **Antes de crear cada conjunto** se comprueba que el eje de la barra y cuatro fibras
   desplazadas medio diámetro (en horizontal perpendicular y en vertical) quedan dentro del
   hormigón (`Solid.IntersectWithCurve`) en todas las posiciones del array.
2. **Después de crear y regenerar** se lee la geometría real de cada barra de cada conjunto
   (**ganchos y radios de doblado incluidos**) y se vuelve a comprobar entera: un gancho que
   asome por la cara superior de una zapata delgada hace que se rechace el elemento.
3. Cualquier fallo deshace la subtransacción de ese elemento: no queda ni una barra.

El informe final dice, zapata a zapata, qué se ha creado (barras por capa y conjuntos) y por
qué se ha rechazado lo que no.

## Interfaz gráfica

- **Zapatas seleccionadas**: dimensiones `u × v`, dirección, canto, escalones, huecos,
  columnas encima y el resumen del armado (o, en rojo, el motivo del rechazo). Cada fila
  armable tiene su **dirección** propia. Clic en una fila para verla en los esquemas.
- **Dirección de las barras principales**: lado largo (por defecto), lado corto, X, Y o ángulo.
- **Parrilla inferior**: principal y secundaria (tipo de barra, separación, gancho y longitud de
  gancho, que muestra la predeterminada del tipo de barra y el mínimo que deja el doblez).
- **Parrilla superior**: activar, principal y secundaria.
- La columna de entradas tiene barras de desplazamiento vertical y horizontal cuando no cabe.
- **Recubrimientos, columnas y partición**: recubrimiento inferior (contra el terreno, 75 mm
  por defecto), superior (50) y lateral (75); mostrar las columnas; plantilla del parámetro
  Partición (`{categoria}`, `{prefijo}`, `{marca}`, `{id}`, `{codigo}` o `{capa}`, `{tipo}`,
  `{familia}`, `{conjunto}`), con el ejemplo de la zapata seleccionada y, en rojo, el aviso si la
  plantilla no empieza por `{categoria} - {prefijo}-` (incumple el contrato ARBA). En el pie se ve
  la versión del contrato con la que se compiló el add-in.
- **Planta**: hormigón de la cara inferior con huecos, cara superior a trazos si es menor,
  columnas (gris), cada barra a su grosor y con el color de su capa (rojo oscuro inferior
  principal, morado inferior secundaria, naranja superior principal, azul superior secundaria)
  y marcas de gancho. Rueda: zoom; arrastrar: mover; doble clic: encajar. Al pasar el ratón
  por una barra se ve su capa, diámetro, posición y longitud.
- **Sección transversal** a media luz: el **perfil real** del hormigón (muestreado del
  sólido, con escalones y taludes), el terreno, la columna que apoya encima, cada barra
  principal como un círculo a su diámetro y la secundaria más cercana al corte como una raya
  con sus ganchos (hacia arriba abajo, hacia abajo arriba).
- **Guardar como valores por defecto** escribe `config.json`; **Armar** crea las barras;
  **Cancelar** no toca nada.

Sin tipo de barra elegido los esquemas se dibujan con diámetros orientativos (12.7 mm) y el
botón Armar avisa de qué falta.

## Limitaciones

- Solo zapatas de **base plana horizontal**. Las escalonadas o piramidales se arman en la
  cara inferior (parrilla inferior) y en la plataforma superior (parrilla superior); los
  taludes no llevan armadura propia.
- Las barras se reparten **uniformemente**. No se concentra la armadura corta en la banda
  central de las zapatas rectangulares (ACI 318 13.3.3.3 / E.060) ni se calculan cuantías: la
  separación la da el usuario.
- No se colocan los **arranques de columna** (barras longitudinales con gancho de 90° sobre
  la parrilla y estribos de arranque, el "1@50, 5@100" del plano): eso es armado de la
  columna y lo hace el add-in de columnas. Las columnas solo se dibujan como referencia.
- No se colocan estribos ni barras en U en los bordes de la zapata.
- No se comprueban choques entre barras de capas distintas más allá del apilado de cotas, ni
  con los arranques de la columna.

## config.json

```jsonc
{
  "coverBottomMm": 75, "coverTopMm": 50, "coverEdgeMm": 75,
  "direction": { "mode": "long", "angleDeg": 0 },   // long | short | x | y | angle
  "bottom": {
    "main":      { "barTypeName": "", "spacingMm": 200, "hookTypeName": "", "hookLengthMm": 0 },   // hookLengthMm 0 = la del tipo de barra
    "secondary": { "enabled": true, "barTypeName": "", "spacingMm": 200, "hookTypeName": "", "hookLengthMm": 0 }
  },
  "top": {
    "enabled": false,
    "main":      { "barTypeName": "", "spacingMm": 200, "hookTypeName": "", "hookLengthMm": 0 },
    "secondary": { "enabled": true, "barTypeName": "", "spacingMm": 200, "hookTypeName": "", "hookLengthMm": 0 }
  },
  "detectColumns": true,
  "partitionTemplate": "{categoria} - {prefijo}-{marca}",   // contrato ARBA: "CIMIENTOS - ZAP-Z1"
  "toleranceMm": 2, "minBarLengthMm": 300
}
```

Los nombres de tipo de barra y de gancho pueden ser exactos o un fragmento (`"1/2"`,
`"90"`); sin coincidencia no se arma, nunca se sustituye por otro tipo. Si un fragmento coincide
con varios tipos del proyecto se toma el primero y la ventana marca el desplegable en amarillo
para que lo confirmes.

## Contrato ARBA: partición, origen, borrar y rearmar, migración

El add-in cumple el [contrato ARBA 1.0.0](https://github.com/Andy-rba30/ARBA-comun/blob/main/CONTRATO.md)
compartido por todos los add-ins de armado y el plugin de metrados. El código común está en el
submódulo `external/ARBA-comun` y se compila **dentro** de `FootingRebar.dll` (nunca como DLL aparte).

- **Partición** de cada conjunto: `<CATEGORIA> - <PREFIJO>-{marca}`. La categoría la da el anfitrión
  (una cimentación estructural es `CIMIENTOS`), el prefijo de este add-in es `ZAP` y la marca es el
  parámetro Marca de la zapata (si está vacía, su Id): `CIMIENTOS - ZAP-Z1`, `CIMIENTOS - ZAP-1234`.
  La capa **no** entra en la partición por defecto (una partición por zapata, como antes): queda en
  `ARBA - Código`. Si la quieres en la partición, usa `{categoria} - {prefijo}-{marca}-{capa}`.
  La partición se escribe en el parámetro predefinido, así que también funciona en Revit en español.
- **Parámetros compartidos** (de ejemplar, grupo Datos, GUID fijos del contrato): al pulsar Armar el
  add-in crea o completa en el proyecto `ARBA - Origen`, `ARBA - Código` y `Metrado - Elemento`, sin
  tocar tu archivo de parámetros compartidos (usa uno temporal que borra al terminar). En cada
  conjunto escribe `ARBA - Origen = ZAPATAS`, `ARBA - Código = inferior | inferior-sec | superior |
  superior-sec` y `Metrado - Elemento = CIMIENTOS` (lo que agrupa el plugin de metrados).
- **Borrar y rearmar**: si alguna zapata seleccionada ya tiene conjuntos con `ARBA - Origen = ZAPATAS`,
  antes de armar se pregunta una vez: *Borrar la armadura del add-in y rearmar* (se borran solo esos
  conjuntos y se vuelve a armar: el número de conjuntos no se duplica) o *Conservar y armar encima*.
  Las barras colocadas a mano o por otros add-ins no se tocan nunca.
- **Migración** de modelos armados con la versión anterior (partición `ZAP-Z1`, sin origen): el
  add-in no las reconoce como propias hasta migrarlas, así que ofrece una tercera opción, *Migrar la
  armadura antigua al contrato (sin rearmar)*: la partición pasa a `CIMIENTOS - ZAP-Z1` y se rellenan
  `ARBA - Origen`, `ARBA - Código` (si la partición lo llevaba) y `Metrado - Elemento`, sin crear ni
  borrar barras (Ctrl+Z lo deshace). Un segundo "armar" ya las reconoce y ofrece borrar y rearmar.
  El plugin de metrados trae además el botón **Migrar particiones y origen** para todo el modelo.
- El informe final y el pie de la ventana muestran la versión del contrato (`Contrato ARBA 1.0.0`).

## Compilar e instalar

Requiere el SDK de .NET 10 y Revit 2027.2 (los paquetes `Nice3point.Revit.Api.*` 2027.2
traen las DLL de la API; para Revit 2025/2026 cambia el `TargetFramework` a
`net8.0-windows` y la versión del paquete).

El código común ARBA viene como **submódulo git**: clona con `--recurse-submodules` o, si ya tienes
el repo, inicialízalo antes de compilar (sin él faltan las clases `Arba.Comun.*` y la compilación
falla):

```
git clone --recurse-submodules https://github.com/Andy-rba30/Acero-Zapatas
# o, en un clon existente:
git submodule update --init
dotnet build -c Debug
```

Para subir de versión del contrato: `git -C external/ARBA-comun checkout vX.Y.Z` y commit del
puntero del submódulo. Nunca se edita nada dentro de `external/ARBA-comun` desde este repo (lo que
falte va a `NOTAS-ARBA-COMUN.md`).

En Debug la compilación copia `FootingRebar.dll`, `config.json` y `FootingRebar.addin` a
`%AppData%\Autodesk\Revit\Addins\2027\`. Al abrir Revit aparece la pestaña **ARBA** con el
panel **Acero** y el botón **Zapatas** dentro del desplegable **Acero** (comparte la pestaña y
el desplegable con los add-ins de columnas, muros y losas si están instalados) y el comando
queda también en Complementos > Herramientas externas.

El proyecto lleva `EnableWindowsTargeting`, así que también compila en Linux o macOS para
comprobar el código (la DLL solo sirve en Windows con Revit). Las clases puras se prueban sin
Revit con el programa de consola de `Tests/`:

```
cd Tests && dotnet run
```

## Estructura del código

| Archivo | Qué hace |
|---------|----------|
| `Geometry2D.cs` | Geometría pura: contorno con huecos, recorte scan-line de una recta con franja de recubrimiento, reparto de posiciones, unión de tramos. |
| `FootingPlan.cs` | Armado de la zapata (parrillas inferior y superior, ganchos, barras extremas dentro de las patas, conjuntos). Pura, compartida por ventana y generador. |
| `FootingOutline.cs` | Lectura del sólido de Revit: caras inferior y superior, contornos, columnas encima, perfil de la sección; sistema local por dirección (`FootingFrame`). |
| `HostAnalysis.cs` | Resultado por elemento (contorno o motivo de rechazo) y dirección propia. |
| `RebarGenerator.cs` | Crea los `Rebar` con las dos redes de seguridad y la orientación automática de ganchos. |
| `RebarOptionsWindow.cs`, `PlanPreview.cs`, `SectionPreview.cs` | Ventana y esquemas (WPF en código, sin XAML). |
| `ArmarZapataCommand.cs`, `RibbonApp.cs` | Comando externo (parámetros del contrato, borrar y rearmar, migración) y botón en la cinta ARBA. |
| `AppConfig.cs` | Configuración (`config.json`), con la plantilla de Partición del contrato. |
| `external/ARBA-comun/` | Submódulo con el código común ARBA (contrato, `ArbaPartition`, `PartitionName`, `ArbaOrigin`, `ArbaSharedParams`, `ArbaMigration`, `ArbaRibbon`, `RevitTheme`, `NameMatch`); se compila dentro de la DLL vía `Arba.Comun.props`. |
| `NOTAS-ARBA-COMUN.md` | Lo que le falta o convendría cambiar al código común (no se edita desde aquí). |
| `Tests/` | Pruebas de consola de las clases puras. |
| `PLAN.md` | Plan de trabajo y estado del proyecto. |
| `INSTALADOR.md` | Instrucciones para añadir el plugin al instalador de ARBA. |

Las clases puras (`Geometry2D`, `FootingPlan`, `AppConfig` y, del común, `ArbaContract`,
`ArbaPartition`, `PartitionName`, `NameMatch`) no dependen de Revit y se prueban en el programa de
consola.
